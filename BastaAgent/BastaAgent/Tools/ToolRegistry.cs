using System.Reflection;
using System.Text.Json;
using BastaAgent.LLM.Models;
using Microsoft.Extensions.Logging;

namespace BastaAgent.Tools;

/// <summary>
/// Registry for discovering and managing tools via reflection.
///
/// <para><b>Conference Note - Reflection-Based Tool Discovery:</b></para>
/// <para>This demonstrates a powerful pattern for extensibility - tools are discovered automatically at runtime!</para>
///
/// <para><b>How It Works:</b></para>
/// <list type="number">
/// <item>Scan assemblies for classes with [Tool] attribute</item>
/// <item>Check if they implement ITool interface</item>
/// <item>Create instances via reflection</item>
/// <item>Register them for the agent to use</item>
/// </list>
///
/// <para><b>Benefits:</b></para>
/// <list type="bullet">
/// <item>Add new tools without modifying core code</item>
/// <item>Tools can be in separate assemblies/plugins</item>
/// <item>Metadata drives behavior (approval, timeouts, etc.)</item>
/// </list>
/// </summary>
public class ToolRegistry(ILogger<ToolRegistry> logger) : IToolRegistry
{
    private readonly ILogger<ToolRegistry> _logger = logger;
    private readonly Dictionary<string, ITool> _tools = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ITool> _toolsByApiName = new(
        StringComparer.OrdinalIgnoreCase
    );
    private readonly Dictionary<string, ToolMetadata> _metadata = new(
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Discover and register all tools in the specified assembly.
    ///
    /// <para><b>Conference Note - The Magic of Reflection:</b></para>
    /// <para>This method demonstrates .NET reflection to dynamically find and load tools:</para>
    /// <list type="bullet">
    /// <item>GetTypes() - Gets all types defined in the assembly</item>
    /// <item>GetCustomAttribute&lt;ToolAttribute&gt;() - Checks for our [Tool] attribute</item>
    /// <item>IsAssignableFrom() - Verifies the ITool interface is implemented</item>
    /// <item>Activator.CreateInstance() - Creates instances at runtime</item>
    /// </list>
    /// </summary>
    /// <param name="assembly">The assembly to scan for tools</param>
    public void DiscoverTools(Assembly assembly)
    {
        _logger.LogInformation(
            "Discovering tools in assembly: {AssemblyName}",
            assembly.GetName().Name
        );

        // Conference Note: This LINQ chain demonstrates reflection-based discovery:
        // 1. GetTypes() - Gets ALL types (classes, interfaces, etc.) in the assembly
        // 2. Filter for [Tool] attribute - Only types decorated with our custom attribute
        // 3. Check ITool implementation - Must implement our tool interface
        // 4. Exclude abstract/interface - We need concrete classes we can instantiate
        var toolTypes = assembly
            .GetTypes() // Get all types in the assembly
            .Where(t => t.GetCustomAttribute<ToolAttribute>() is not null) // Has [Tool] attribute
            .Where(t => typeof(ITool).IsAssignableFrom(t)) // Implements ITool interface
            .Where(t => !t.IsAbstract && !t.IsInterface); // Is a concrete class

        // Conference Note: We wrap each registration in try-catch so one bad tool
        // doesn't break the entire discovery process. This makes the system resilient.
        foreach (var toolType in toolTypes)
        {
            try
            {
                RegisterToolType(toolType);
            }
            catch (Exception ex)
            {
                // Log and continue - one bad tool shouldn't stop others from loading
                _logger.LogError(ex, "Failed to register tool type: {ToolType}", toolType.Name);
            }
        }

        _logger.LogInformation("Discovered {ToolCount} tools", _tools.Count);
    }

    /// <summary>
    /// Register a specific tool type
    /// </summary>
    private void RegisterToolType(Type toolType)
    {
        var attribute = toolType.GetCustomAttribute<ToolAttribute>();
        if (attribute is null)
            return;

        // Create instance of the tool. Prefer parameterless; fallback to single-arg (ILogger<T>?).
        ITool? tool = null;
        try
        {
            tool = Activator.CreateInstance(toolType) as ITool;
        }
        catch
        {
            // ignore and try overloads
        }
        if (tool is null)
        {
            try
            {
                // Many tools expose a ctor(ILogger<T>?). Passing null is acceptable.
                tool = Activator.CreateInstance(toolType, new object?[] { null }) as ITool;
            }
            catch
            {
                // ignore
            }
        }
        if (tool is null)
        {
            _logger.LogWarning(
                "Failed to create instance of tool: {ToolTypeFullName}",
                toolType.FullName
            );
            return;
        }

        // Conference Note: We store tools by name (case-insensitive) for easy lookup.
        // The dictionary uses StringComparer.OrdinalIgnoreCase for case-insensitive keys.
        // Store tool and metadata
        var apiName = SanitizeFunctionName(tool.Name);

        _tools[tool.Name] = tool;
        _toolsByApiName[apiName] = tool;
        _metadata[tool.Name] = new ToolMetadata
        {
            Name = tool.Name,
            ApiName = apiName,
            Description = tool.Description,
            Category = attribute.Category,
            RequiresApproval = attribute.RequiresApproval,
            Priority = attribute.Priority,
            Timeout =
                attribute.TimeoutSeconds > 0
                    ? TimeSpan.FromSeconds(attribute.TimeoutSeconds)
                    : null,
            Type = toolType,
        };

        _logger.LogDebug(
            "Registered tool: {ToolName} (Category: {Category})",
            tool.Name,
            attribute.Category
        );
    }

    /// <summary>
    /// Register a tool instance directly
    /// </summary>
    public void RegisterTool(ITool tool)
    {
        _tools[tool.Name] = tool;

        var toolType = tool.GetType();
        var attribute = toolType.GetCustomAttribute<ToolAttribute>() ?? new ToolAttribute();

        var apiName = SanitizeFunctionName(tool.Name);
        _toolsByApiName[apiName] = tool;

        _metadata[tool.Name] = new ToolMetadata
        {
            Name = tool.Name,
            ApiName = apiName,
            Description = tool.Description,
            Category = attribute.Category,
            RequiresApproval = attribute.RequiresApproval,
            Priority = attribute.Priority,
            Timeout =
                attribute.TimeoutSeconds > 0
                    ? TimeSpan.FromSeconds(attribute.TimeoutSeconds)
                    : null,
            Type = toolType,
        };

        _logger.LogInformation("Manually registered tool: {ToolName}", tool.Name);
    }

    /// <summary>
    /// Get a tool by name
    /// </summary>
    public ITool? GetTool(string name)
    {
        if (_tools.TryGetValue(name, out var tool))
            return tool;
        if (_toolsByApiName.TryGetValue(name, out var toolByApi))
            return toolByApi;
        return null;
    }

    /// <summary>
    /// Get all registered tools
    /// </summary>
    public IEnumerable<ITool> GetAllTools()
    {
        return _tools.Values;
    }

    /// <summary>
    /// Get tools by category
    /// </summary>
    public IEnumerable<ITool> GetToolsByCategory(string category)
    {
        return _tools.Values.Where(tool =>
        {
            if (_metadata.TryGetValue(tool.Name, out var meta))
            {
                return string.Equals(meta.Category, category, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        });
    }

    /// <summary>
    /// Get metadata for a tool
    /// </summary>
    public ToolMetadata? GetToolMetadata(string name)
    {
        return _metadata.TryGetValue(name, out var meta) ? meta : null;
    }

    /// <summary>
    /// Generate OpenAI-compatible function definitions for all tools
    /// </summary>
    public List<Tool> GenerateToolDefinitions()
    {
        var tools = new List<Tool>();

        // Order tools by priority (highest first)
        foreach (
            var tool in _tools.Values.OrderByDescending(t =>
                _metadata.TryGetValue(t.Name, out var m) ? m.Priority : 0
            )
        )
        {
            try
            {
                // Parse the JSON schema to ensure it's valid
                var schemaElement = JsonSerializer.Deserialize<JsonElement>(tool.ParametersSchema);

                // Create the Tool object in OpenAI format
                var toolDefinition = new Tool
                {
                    Type = "function",
                    Function = new FunctionDefinition
                    {
                        Name = _metadata.TryGetValue(tool.Name, out var md)
                            ? md.ApiName
                            : SanitizeFunctionName(tool.Name),
                        Description = tool.Description,
                        Parameters = schemaElement,
                    },
                };

                // Add metadata for approval requirements
                if (_metadata.TryGetValue(tool.Name, out var metadata))
                {
                    toolDefinition.Function.RequiresConfirmation = metadata.RequiresApproval;
                }

                tools.Add(toolDefinition);

                _logger.LogDebug("Generated function definition for tool: {ToolName}", tool.Name);
            }
            catch (JsonException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to parse JSON schema for tool: {ToolName}. Schema: {Schema}",
                    tool.Name,
                    tool.ParametersSchema
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to generate function definition for tool: {ToolName}",
                    tool.Name
                );
            }
        }

        _logger.LogInformation("Generated {ToolDefinitionCount} tool definitions", tools.Count);
        return tools;
    }

    /// <summary>
    /// Generate OpenAI-compatible function definitions for all tools
    /// This is a compatibility method that returns List<object> for backward compatibility
    /// </summary>
    [Obsolete("Use GenerateToolDefinitions() which returns List<Tool> instead")]
    public List<object> GenerateFunctionDefinitions()
    {
        return GenerateToolDefinitions().Cast<object>().ToList();
    }

    /// <summary>
    /// Clear all registered tools
    /// </summary>
    public void Clear()
    {
        _tools.Clear();
        _toolsByApiName.Clear();
        _metadata.Clear();
        _logger.LogInformation("Cleared all registered tools");
    }

    private static string SanitizeFunctionName(string name)
    {
        // Allow only A-Z, a-z, 0-9, underscore and hyphen. Replace others with underscore.
        var chars = name.Select(c => char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_')
            .ToArray();
        var cleaned = new string(chars);
        // Collapse multiple underscores
        while (cleaned.Contains("__"))
            cleaned = cleaned.Replace("__", "_");
        // Trim to max length 128
        if (cleaned.Length > 128)
            cleaned = cleaned.Substring(0, 128);
        // Ensure non-empty
        if (string.IsNullOrWhiteSpace(cleaned))
            cleaned = "tool";
        return cleaned;
    }
}

/// <summary>
/// Interface for tool registry
/// </summary>
public interface IToolRegistry
{
    void DiscoverTools(Assembly assembly);
    void RegisterTool(ITool tool);
    ITool? GetTool(string name);
    IEnumerable<ITool> GetAllTools();
    IEnumerable<ITool> GetToolsByCategory(string category);
    ToolMetadata? GetToolMetadata(string name);
    List<Tool> GenerateToolDefinitions();

    [Obsolete("Use GenerateToolDefinitions() which returns List<Tool> instead")]
    List<object> GenerateFunctionDefinitions();
    void Clear();
}

/// <summary>
/// Metadata about a registered tool
/// </summary>
public class ToolMetadata
{
    public string Name { get; set; } = string.Empty;
    public string ApiName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public bool RequiresApproval { get; set; }
    public int Priority { get; set; }
    public TimeSpan? Timeout { get; set; }
    public Type? Type { get; set; }
}
