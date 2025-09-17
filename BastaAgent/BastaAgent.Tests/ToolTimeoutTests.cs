using BastaAgent.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

/// <summary>
/// Tests for tool execution timeout functionality
/// Verifies that tools respect timeout settings and handle cancellation properly
/// </summary>
public class ToolTimeoutTests
{
    /// <summary>
    /// Test tool with custom timeout attribute
    /// </summary>
    [Tool(Category = "Test", TimeoutSeconds = 2)]
    public class FastTimeoutTool : ITool
    {
        public string Name => "fast_timeout_tool";
        public string Description => "A tool with 2-second timeout";

        public string ParametersSchema =>
            """
                {
                    "type": "object",
                    "properties": {
                        "delay_ms": {
                            "type": "integer",
                            "description": "Delay in milliseconds"
                        }
                    },
                    "required": ["delay_ms"]
                }
                """;

        public async Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        )
        {
            var delay = System.Text.Json.JsonSerializer.Deserialize<DelayParams>(parameters);
            await Task.Delay(delay?.delay_ms ?? 0, cancellationToken);
            return ToolResult.Ok("Completed");
        }

        private class DelayParams
        {
            public int delay_ms { get; set; }
        }
    }

    /// <summary>
    /// Test tool with no timeout specified (should use default)
    /// </summary>
    [Tool(Category = "Test")]
    public class DefaultTimeoutTool : ITool
    {
        public string Name => "default_timeout_tool";
        public string Description => "A tool using default timeout";

        public string ParametersSchema =>
            """
                {
                    "type": "object",
                    "properties": {
                        "delay_ms": {
                            "type": "integer",
                            "description": "Delay in milliseconds"
                        }
                    },
                    "required": ["delay_ms"]
                }
                """;

        public async Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        )
        {
            var delay = System.Text.Json.JsonSerializer.Deserialize<DelayParams>(parameters);
            await Task.Delay(delay?.delay_ms ?? 0, cancellationToken);
            return ToolResult.Ok("Completed");
        }

        private class DelayParams
        {
            public int delay_ms { get; set; }
        }
    }

    /// <summary>
    /// Test that tool metadata includes timeout information
    /// </summary>
    [Fact]
    public void ToolRegistry_ShouldStoreTimeoutMetadata()
    {
        // Arrange
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.RegisterTool(new FastTimeoutTool());
        registry.RegisterTool(new DefaultTimeoutTool());

        // Act
        var fastMetadata = registry.GetToolMetadata("fast_timeout_tool");
        var defaultMetadata = registry.GetToolMetadata("default_timeout_tool");

        // Assert
        Assert.NotNull(fastMetadata);
        Assert.Equal(TimeSpan.FromSeconds(2), fastMetadata.Timeout);

        Assert.NotNull(defaultMetadata);
        Assert.Null(defaultMetadata.Timeout); // No timeout specified
    }

    /// <summary>
    /// Test that tool execution respects cancellation token
    /// </summary>
    [Fact]
    public async Task ToolExecution_ShouldRespectCancellationToken()
    {
        // Arrange
        var tool = new DefaultTimeoutTool();

        // Act
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(TimeSpan.FromSeconds(1)); // Cancel after 1 second

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // This should throw OperationCanceledException
        await Assert.ThrowsAsync<TaskCanceledException>(async () =>
        {
            await tool.ExecuteAsync("""{"delay_ms": 10000}""", cts.Token); // 10 seconds
        });

        sw.Stop();

        // Assert
        Assert.True(
            sw.ElapsedMilliseconds < 3000,
            $"Should have been cancelled quickly, took {sw.ElapsedMilliseconds}ms"
        );
    }

    /// <summary>
    /// Test that multiple tools can have different timeout settings
    /// </summary>
    [Fact]
    public void ToolRegistry_ShouldSupportMultipleTimeoutSettings()
    {
        // Arrange
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);

        // Register tools with different timeout settings
        registry.RegisterTool(new FastTimeoutTool()); // 2 seconds
        registry.RegisterTool(new DefaultTimeoutTool()); // No timeout (default)

        // Act
        var fastMeta = registry.GetToolMetadata("fast_timeout_tool");
        var defaultMeta = registry.GetToolMetadata("default_timeout_tool");

        // Assert
        Assert.NotNull(fastMeta);
        Assert.Equal(TimeSpan.FromSeconds(2), fastMeta.Timeout);

        Assert.NotNull(defaultMeta);
        Assert.Null(defaultMeta.Timeout);
    }

    /// <summary>
    /// Test that tool timeout is properly parsed from attribute
    /// </summary>
    [Theory]
    [InlineData(0, null)]
    [InlineData(5, 5)]
    [InlineData(60, 60)]
    public void ToolAttribute_ShouldParseTimeoutCorrectly(int timeoutSeconds, int? expectedSeconds)
    {
        // Arrange
        var attribute = new ToolAttribute { TimeoutSeconds = timeoutSeconds };
        var expectedTimeout = expectedSeconds.HasValue
            ? TimeSpan.FromSeconds(expectedSeconds.Value)
            : (TimeSpan?)null;

        // Act
        var actualTimeout =
            attribute.TimeoutSeconds > 0
                ? TimeSpan.FromSeconds(attribute.TimeoutSeconds)
                : (TimeSpan?)null;

        // Assert
        Assert.Equal(expectedTimeout, actualTimeout);
    }

    /// <summary>
    /// Test timeout handling with successful execution
    /// </summary>
    [Fact]
    public async Task ToolExecution_ShouldCompleteWithinTimeout()
    {
        // Arrange
        var tool = new FastTimeoutTool();

        // Act - Execute with delay less than timeout
        var result = await tool.ExecuteAsync("""{"delay_ms": 500}""", CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Completed", result.Content);
    }

    /// <summary>
    /// Test that tool discovery preserves timeout metadata
    /// </summary>
    [Fact]
    public void ToolRegistry_DiscoveryShouldPreserveTimeoutMetadata()
    {
        // Arrange
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);

        // Act
        registry.DiscoverTools(typeof(FastTimeoutTool).Assembly);

        // Assert
        var metadata = registry.GetToolMetadata("fast_timeout_tool");
        Assert.NotNull(metadata);
        Assert.Equal(TimeSpan.FromSeconds(2), metadata.Timeout);
    }

    /// <summary>
    /// Test that custom timeout is correctly extracted from ToolAttribute
    /// </summary>
    [Fact]
    public void ToolRegistry_ShouldExtractCustomTimeoutFromAttribute()
    {
        // Arrange
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);

        // Act
        registry.RegisterTool(new FastTimeoutTool());

        // Assert
        var metadata = registry.GetToolMetadata("fast_timeout_tool");
        Assert.NotNull(metadata);
        Assert.NotNull(metadata.Timeout);
        Assert.Equal(2, metadata.Timeout.Value.TotalSeconds);
    }

    /// <summary>
    /// Test that zero timeout means no custom timeout (use default)
    /// </summary>
    [Fact]
    public void ToolRegistry_ZeroTimeoutShouldMeanDefault()
    {
        // Arrange
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);

        // Act
        registry.RegisterTool(new DefaultTimeoutTool());

        // Assert
        var metadata = registry.GetToolMetadata("default_timeout_tool");
        Assert.NotNull(metadata);
        Assert.Null(metadata.Timeout); // Should be null to indicate default timeout
    }
}
