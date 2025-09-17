using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.v3;

namespace BastaAgent.Tests;

/// <summary>
/// Unit tests for the StreamingHandler
/// </summary>
public class StreamingHandlerTests
{
    private readonly StreamingHandler _handler;
    private readonly List<ReasoningStepEventArgs> _reasoningSteps;
    private readonly List<ContentEventArgs> _contentEvents;

    public StreamingHandlerTests()
    {
        var logger = new TestLogger<StreamingHandler>();
        _handler = new StreamingHandler(logger);
        _reasoningSteps = [];
        _contentEvents = [];

        // Subscribe to events
        _handler.ReasoningStepDetected += (sender, args) => _reasoningSteps.Add(args);
        _handler.ContentReceived += (sender, args) => _contentEvents.Add(args);
    }

    [Fact]
    public async Task ProcessStreamAsync_ExtractsSimpleReasoningBlock()
    {
        // Arrange
        var stream = CreateStreamFromContent(
            "<reasoning type=\"planning\">",
            "I need to analyze the user's request.",
            "<step type=\"analysis\">Understanding the requirements</step>",
            "</reasoning>",
            "Here's the answer to your question."
        );

        // Act
        var chunks = await _handler
            .ProcessStreamAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        chunks.Should().Contain(c => c.Type == ChunkType.ReasoningStart);
        chunks
            .Should()
            .Contain(c => c.Type == ChunkType.ReasoningStep && c.Content.Contains("[analysis]"));
        chunks.Should().Contain(c => c.Type == ChunkType.ReasoningComplete);
        chunks
            .Should()
            .Contain(c => c.Type == ChunkType.Content && c.Content.Contains("Here's the answer"));

        _reasoningSteps.Should().HaveCount(1);
        _reasoningSteps[0].Type.Should().Be("planning");
    }

    [Fact]
    public async Task ProcessStreamAsync_HandlesMultipleReasoningSteps()
    {
        // Arrange
        var stream = CreateStreamFromContent(
            "<reasoning type=\"tool_selection\">",
            "<step type=\"planning\">Need to read a file</step>",
            "<step type=\"tool_selection\">Using FileSystemTool</step>",
            "<step type=\"execution\">Reading content from file.txt</step>",
            "</reasoning>"
        );

        // Act
        var chunks = await _handler
            .ProcessStreamAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        var reasoningSteps = chunks.Where(c => c.Type == ChunkType.ReasoningStep).ToList();
        reasoningSteps.Should().HaveCount(3);
        reasoningSteps.Should().Contain(s => s.Content.Contains("[planning]"));
        reasoningSteps.Should().Contain(s => s.Content.Contains("[tool_selection]"));
        reasoningSteps.Should().Contain(s => s.Content.Contains("[execution]"));
    }

    [Fact]
    public async Task ProcessStreamAsync_SummarizesToolSelection()
    {
        // Arrange
        var stream = CreateStreamFromContent(
            "<reasoning type=\"tool_use\">",
            "<step type=\"tool_selection\">I will use the WebRequestTool to fetch data</step>",
            "</reasoning>"
        );

        // Act
        var chunks = await _handler
            .ProcessStreamAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        var completeChunk = chunks.FirstOrDefault(c => c.Type == ChunkType.ReasoningComplete);
        completeChunk.Should().NotBeNull();
        completeChunk!.Content.Should().Contain("WebRequestTool");
    }

    [Fact]
    public async Task ProcessStreamAsync_HandlesStreamedContent()
    {
        // Arrange
        // Simulate content coming in small chunks
        var stream = CreateStreamFromContent(
            "<rea",
            "soning",
            " type=\"ana",
            "lysis\">",
            "Thinking about",
            " the problem",
            "</reasoning>"
        );

        // Act
        var chunks = await _handler
            .ProcessStreamAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        chunks.Should().Contain(c => c.Type == ChunkType.ReasoningStart);
        chunks.Should().Contain(c => c.Type == ChunkType.ReasoningComplete);
    }

    [Fact]
    public async Task ProcessStreamAsync_HandlesToolCalls()
    {
        // Arrange
        var toolCall = new ToolCall
        {
            Id = "call_123",
            Type = "function",
            Function = new FunctionCall
            {
                Name = "FileSystemTool.Read",
                Arguments = "{\"path\": \"test.txt\"}",
            },
        };

        var stream = CreateStreamWithToolCall(toolCall);

        // Act
        var chunks = await _handler
            .ProcessStreamAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        var toolChunk = chunks.FirstOrDefault(c => c.Type == ChunkType.ToolCall);
        toolChunk.Should().NotBeNull();
        toolChunk!.ToolCalls.Should().NotBeNull();
        toolChunk.ToolCalls.Should().HaveCount(1);
        toolChunk.ToolCalls![0].Function?.Name.Should().Be("FileSystemTool.Read");
    }

    [Fact]
    public async Task ProcessStreamAsync_PreservesNonReasoningContent()
    {
        // Arrange
        var stream = CreateStreamFromContent(
            "This is regular content.",
            " It should be preserved as-is.",
            " No reasoning blocks here."
        );

        // Act
        var chunks = await _handler
            .ProcessStreamAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        chunks.Should().AllSatisfy(c => c.Type.Should().Be(ChunkType.Content));
        var fullContent = string.Join("", chunks.Select(c => c.Content));
        fullContent
            .Should()
            .Be("This is regular content. It should be preserved as-is. No reasoning blocks here.");
    }

    [Fact]
    public async Task ProcessStreamAsync_HandlesNestedReasoningCorrectly()
    {
        // Arrange
        var stream = CreateStreamFromContent(
            "Let me think about this.",
            "<reasoning type=\"analysis\">",
            "First, I need to understand the problem.",
            "<step type=\"decision\">I'll use a systematic approach</step>",
            "This requires careful consideration.",
            "</reasoning>",
            "Based on my analysis, here's what I found."
        );

        // Act
        var chunks = await _handler
            .ProcessStreamAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        // Verify content before reasoning
        chunks
            .Should()
            .Contain(c => c.Type == ChunkType.Content && c.Content.Contains("Let me think"));

        // Verify reasoning
        chunks.Should().Contain(c => c.Type == ChunkType.ReasoningStart);
        chunks.Should().Contain(c => c.Type == ChunkType.ReasoningStep);
        chunks.Should().Contain(c => c.Type == ChunkType.ReasoningComplete);

        // Verify content after reasoning
        chunks
            .Should()
            .Contain(c =>
                c.Type == ChunkType.Content && c.Content.Contains("Based on my analysis")
            );
    }

    [Fact]
    public async Task GetFullContent_ReturnsAccumulatedContent()
    {
        // Arrange
        var stream = CreateStreamFromContent(
            "Hello ",
            "<reasoning>thinking</reasoning>",
            " World!"
        );

        // Act
        _ = await _handler
            .ProcessStreamAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);
        var fullContent = _handler.GetFullContent();

        // Assert
        fullContent.Should().Be("Hello <reasoning>thinking</reasoning> World!");
    }

    [Fact]
    public async Task Reset_ClearsState()
    {
        // Arrange
        var stream = CreateStreamFromContent("Some content");
        _ = await _handler
            .ProcessStreamAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Act
        _handler.Reset();
        var fullContent = _handler.GetFullContent();

        // Assert
        fullContent.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessStreamAsync_TruncatesLongReasoningSteps()
    {
        // Arrange
        var longContent = new string('x', 200); // Very long content
        var stream = CreateStreamFromContent(
            "<reasoning>",
            $"<step type=\"analysis\">{longContent}</step>",
            "</reasoning>"
        );

        // Act
        var chunks = await _handler
            .ProcessStreamAsync(stream, TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        var stepChunk = chunks.FirstOrDefault(c => c.Type == ChunkType.ReasoningStep);
        stepChunk.Should().NotBeNull();
        stepChunk!.Content.Length.Should().BeLessThan(100); // Should be truncated
        stepChunk.Content.Should().EndWith("...");
    }

    // Helper methods

    private async IAsyncEnumerable<StreamingChatResponse> CreateStreamFromContent(
        params string[] contentParts
    )
    {
        foreach (var content in contentParts)
        {
            yield return new StreamingChatResponse
            {
                Choices = [new Choice { Delta = new Message { Content = content } }],
            };
            await Task.Yield(); // Simulate async behavior
        }
    }

    private async IAsyncEnumerable<StreamingChatResponse> CreateStreamWithToolCall(
        ToolCall toolCall
    )
    {
        yield return new StreamingChatResponse
        {
            Choices = [new Choice { Delta = new Message { ToolCalls = [toolCall] } }],
        };
        await Task.Yield();
    }

    // Test logger implementation
    private class TestLogger<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) { }
    }
}
