using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BastaAgent.Tools;

/// <summary>
/// Base class for tools with common functionality
/// </summary>
public abstract class BaseTool(ILogger? logger = null) : ITool
{
    protected readonly ILogger? _logger = logger;

    /// <summary>
    /// Name of the tool
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Description of the tool
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// JSON schema for parameters
    /// </summary>
    public abstract string ParametersSchema { get; }

    /// <summary>
    /// Execute the tool
    /// </summary>
    public async Task<ToolResult> ExecuteAsync(
        string parameters,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            _logger?.LogDebug(
                "Executing tool {ToolName} with parameters: {Parameters}",
                Name,
                parameters
            );

            // Validate parameters against schema if needed
            var validationResult = ValidateParameters(parameters);
            if (!validationResult.Success)
            {
                return validationResult;
            }

            // Execute the tool logic
            var result = await ExecuteInternalAsync(parameters, cancellationToken);

            _logger?.LogDebug(
                "Tool {ToolName} execution completed: {Success}",
                Name,
                result.Success
            );
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("Tool {ToolName} execution was cancelled", Name);
            return ToolResult.Error("Operation was cancelled");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error executing tool {ToolName}", Name);
            return ToolResult.Error($"Tool execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Internal execution logic to be implemented by derived classes
    /// </summary>
    protected abstract Task<ToolResult> ExecuteInternalAsync(
        string parameters,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Validate parameters against the schema
    /// </summary>
    protected virtual ToolResult ValidateParameters(string parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return ToolResult.Error("Parameters cannot be empty");
        }

        try
        {
            // Basic JSON validation
            JsonDocument.Parse(parameters);
            return ToolResult.Ok("Valid");
        }
        catch (JsonException ex)
        {
            return ToolResult.Error($"Invalid JSON parameters: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse parameters into a typed object
    /// </summary>
    protected T? ParseParameters<T>(string parameters)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(
                parameters,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to parse parameters for tool {ToolName}", Name);
            return null;
        }
    }
}
