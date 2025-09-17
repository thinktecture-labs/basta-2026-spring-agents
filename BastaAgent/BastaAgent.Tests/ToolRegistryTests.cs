using System.Reflection;
using System.Text.Json;
using BastaAgent.LLM.Models;
using BastaAgent.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

/// <summary>
/// Comprehensive unit tests for the ToolRegistry class
/// Tests tool discovery, registration, and OpenAI function definition generation
/// </summary>
public class ToolRegistryTests
{
    private readonly ILogger<ToolRegistry> _logger;
    private readonly ToolRegistry _registry;

    public ToolRegistryTests()
    {
        // Use NullLogger for testing (part of standard .NET)
        _logger = NullLogger<ToolRegistry>.Instance;
        _registry = new ToolRegistry(_logger);
    }

    /// <summary>
    /// Test that tools can be manually registered
    /// </summary>
    [Fact]
    public void RegisterTool_ShouldAddToolToRegistry()
    {
        // Arrange
        var testTool = new TestTool(
            "TestTool",
            "A test tool",
            @"{
                ""type"": ""object"",
                ""properties"": {
                    ""input"": {
                        ""type"": ""string"",
                        ""description"": ""The input value""
                    }
                },
                ""required"": [""input""]
            }"
        );

        // Act
        _registry.RegisterTool(testTool);

        // Assert
        var tool = _registry.GetTool("TestTool");
        Assert.NotNull(tool);
        Assert.Equal("TestTool", tool.Name);
        Assert.Equal("A test tool", tool.Description);
    }

    /// <summary>
    /// Test that GetTool returns null for non-existent tools
    /// </summary>
    [Fact]
    public void GetTool_ShouldReturnNullForNonExistentTool()
    {
        // Act
        var tool = _registry.GetTool("NonExistentTool");

        // Assert
        Assert.Null(tool);
    }

    /// <summary>
    /// Test that GetAllTools returns all registered tools
    /// </summary>
    [Fact]
    public void GetAllTools_ShouldReturnAllRegisteredTools()
    {
        // Arrange
        var tool1 = new TestTool("Tool1", "First tool");
        var tool2 = new TestTool("Tool2", "Second tool");

        _registry.RegisterTool(tool1);
        _registry.RegisterTool(tool2);

        // Act
        var tools = _registry.GetAllTools().ToList();

        // Assert
        Assert.Equal(2, tools.Count);
        Assert.Contains(tools, t => t.Name == "Tool1");
        Assert.Contains(tools, t => t.Name == "Tool2");
    }

    /// <summary>
    /// Test that tools can be retrieved by category
    /// </summary>
    [Fact]
    public void GetToolsByCategory_ShouldReturnToolsInCategory()
    {
        // Arrange
        // The TestToolWithCategory class has Category = "Test" in its attribute
        var tool1 = new TestToolWithCategory("Tool1", "First tool");
        var tool2 = new TestToolWithCategory("Tool2", "Second tool");
        var tool3 = new TestTool("Tool3", "Third tool"); // Different type, no category

        _registry.RegisterTool(tool1);
        _registry.RegisterTool(tool2);
        _registry.RegisterTool(tool3);

        // Act
        var testTools = _registry.GetToolsByCategory("Test").ToList();
        var generalTools = _registry.GetToolsByCategory("General").ToList();

        // Assert
        Assert.Equal(2, testTools.Count);
        Assert.Single(generalTools); // Tool3 should have General category by default
        Assert.All(testTools, t => Assert.Contains("Tool", t.Name));
    }

    /// <summary>
    /// Test that tool metadata is properly stored and retrieved
    /// </summary>
    [Fact]
    public void GetToolMetadata_ShouldReturnCorrectMetadata()
    {
        // Arrange
        // TestToolWithCategory has Category = "Test" in its Tool attribute
        var tool = new TestToolWithCategory("MetaTool", "Tool with metadata");
        _registry.RegisterTool(tool);

        // Act
        var metadata = _registry.GetToolMetadata("MetaTool");

        // Assert
        Assert.NotNull(metadata);
        Assert.Equal("MetaTool", metadata.Name);
        Assert.Equal("Tool with metadata", metadata.Description);
        Assert.Equal("Test", metadata.Category); // Category comes from attribute
        Assert.True(metadata.RequiresApproval);
    }

    /// <summary>
    /// Test that GenerateToolDefinitions creates proper OpenAI format
    /// </summary>
    [Fact]
    public void GenerateToolDefinitions_ShouldCreateOpenAIFormat()
    {
        // Arrange
        var tool = new TestTool(
            "WebSearch",
            "Search the web for information",
            @"{
                ""type"": ""object"",
                ""properties"": {
                    ""query"": {
                        ""type"": ""string"",
                        ""description"": ""The search query""
                    },
                    ""maxResults"": {
                        ""type"": ""integer"",
                        ""description"": ""Maximum number of results"",
                        ""default"": 5
                    }
                },
                ""required"": [""query""]
            }"
        );
        _registry.RegisterTool(tool);

        // Act
        var definitions = _registry.GenerateToolDefinitions();

        // Assert
        Assert.Single(definitions);

        var toolDef = definitions[0];
        Assert.NotNull(toolDef);
        Assert.Equal("function", toolDef.Type);
        Assert.NotNull(toolDef.Function);
        Assert.Equal("WebSearch", toolDef.Function.Name);
        Assert.Equal("Search the web for information", toolDef.Function.Description);

        // Verify the parameters are properly formatted
        Assert.NotNull(toolDef.Function.Parameters);
        var parametersJson = JsonSerializer.Serialize(toolDef.Function.Parameters);
        Assert.Contains("\"query\"", parametersJson);
        Assert.Contains("\"maxResults\"", parametersJson);
    }

    /// <summary>
    /// Test that GenerateToolDefinitions handles multiple tools with priority ordering
    /// </summary>
    [Fact]
    public void GenerateToolDefinitions_ShouldOrderByPriority()
    {
        // Arrange
        var tool1 = new LowPriorityTool("LowPriority", "Low priority tool");
        var tool2 = new HighPriorityTool("HighPriority", "High priority tool");
        var tool3 = new MediumPriorityTool("MediumPriority", "Medium priority tool");

        _registry.RegisterTool(tool1);
        _registry.RegisterTool(tool2);
        _registry.RegisterTool(tool3);

        // Act
        var definitions = _registry.GenerateToolDefinitions();

        // Assert
        Assert.Equal(3, definitions.Count);

        var toolDefs = definitions;
        Assert.Equal("HighPriority", toolDefs[0].Function?.Name);
        Assert.Equal("MediumPriority", toolDefs[1].Function?.Name);
        Assert.Equal("LowPriority", toolDefs[2].Function?.Name);
    }

    /// <summary>
    /// Test that GenerateToolDefinitions handles invalid JSON schema gracefully
    /// </summary>
    [Fact]
    public void GenerateToolDefinitions_ShouldHandleInvalidSchema()
    {
        // Arrange
        var invalidTool = new TestTool(
            "InvalidTool",
            "Tool with invalid schema",
            "{ invalid json }"
        );

        _registry.RegisterTool(invalidTool);

        // Act
        var definitions = _registry.GenerateToolDefinitions();

        // Assert
        Assert.Empty(definitions); // Invalid tools should be skipped
    }

    /// <summary>
    /// Test that Clear removes all tools
    /// </summary>
    [Fact]
    public void Clear_ShouldRemoveAllTools()
    {
        // Arrange
        var tool1 = new TestTool("Tool1", "First tool");
        var tool2 = new TestTool("Tool2", "Second tool");

        _registry.RegisterTool(tool1);
        _registry.RegisterTool(tool2);

        // Act
        _registry.Clear();

        // Assert
        Assert.Empty(_registry.GetAllTools());
        Assert.Null(_registry.GetTool("Tool1"));
        Assert.Null(_registry.GetTool("Tool2"));
    }

    /// <summary>
    /// Test tool discovery from assembly with proper attribute
    /// </summary>
    [Fact]
    public void DiscoverTools_ShouldFindToolsWithAttribute()
    {
        // Arrange
        var assembly = Assembly.GetExecutingAssembly();

        // Act
        _registry.DiscoverTools(assembly);

        // Assert
        var discoveredTool = _registry.GetTool("DiscoverableTestTool");
        Assert.NotNull(discoveredTool);
        Assert.Equal("A discoverable test tool", discoveredTool.Description);
    }

    // Test helper class - Simple ITool implementation for testing
    private class TestTool(string name, string description, string? schema = null) : ITool
    {
        public string Name { get; } = name;
        public string Description { get; } = description;
        public string ParametersSchema { get; } =
            schema ?? @"{""type"": ""object"", ""properties"": {}}";

        public Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(ToolResult.Ok($"Executed {Name} with parameters: {parameters}"));
        }
    }

    // Test helper classes
    [Tool(Category = "Test", RequiresApproval = true)]
    private class TestToolWithCategory(string name, string description) : ITool
    {
        public string Name { get; } = name;
        public string Description { get; } = description;
        public string ParametersSchema => @"{""type"": ""object"", ""properties"": {}}";

        public Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(ToolResult.Ok("Success"));
        }
    }

    // Create separate classes with different priorities for testing
    [Tool(Priority = 1)]
    private class LowPriorityTool(string name, string description) : ITool
    {
        public string Name { get; } = name;
        public string Description { get; } = description;
        public string ParametersSchema => @"{""type"": ""object"", ""properties"": {}}";

        public Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(ToolResult.Ok("Success"));
        }
    }

    [Tool(Priority = 10)]
    private class HighPriorityTool(string name, string description) : ITool
    {
        public string Name { get; } = name;
        public string Description { get; } = description;
        public string ParametersSchema => @"{""type"": ""object"", ""properties"": {}}";

        public Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(ToolResult.Ok("Success"));
        }
    }

    [Tool(Priority = 5)]
    private class MediumPriorityTool(string name, string description) : ITool
    {
        public string Name { get; } = name;
        public string Description { get; } = description;
        public string ParametersSchema => @"{""type"": ""object"", ""properties"": {}}";

        public Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(ToolResult.Ok("Success"));
        }
    }
}

/// <summary>
/// Test tool for discovery testing
/// </summary>
[Tool(Category = "Test", RequiresApproval = false)]
public class DiscoverableTestTool : ITool
{
    public string Name => "DiscoverableTestTool";
    public string Description => "A discoverable test tool";
    public string ParametersSchema => @"{""type"": ""object"", ""properties"": {}}";

    public Task<ToolResult> ExecuteAsync(
        string parameters,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(ToolResult.Ok("Discovered and executed"));
    }
}
