using System.Runtime.CompilerServices;
using System.Text;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using BastaAgent.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

/// <summary>
/// Tests for real-time reasoning display functionality
/// Verifies that reasoning steps are properly extracted and displayed on console
/// </summary>
public class ReasoningDisplayTests : IDisposable
{
    private readonly StreamingHandler _streamingHandler;
    private readonly InteractiveConsole _console;
    private readonly List<string> _capturedOutput;

    public ReasoningDisplayTests()
    {
        _streamingHandler = new StreamingHandler(NullLogger<StreamingHandler>.Instance);
        _console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        _capturedOutput = [];
    }

    public void Dispose()
    {
        _console?.Dispose();
    }

    /// <summary>
    /// Test that reasoning blocks are properly detected
    /// </summary>
    [Fact]
    public async Task StreamingHandler_ShouldDetectReasoningBlocks()
    {
        // Arrange
        var chunks = CreateTestStream(
            new[]
            {
                "Let me think about this. ",
                "<reasoning type=\"analysis\">",
                "First, I need to understand the request.",
                "The user is asking about file operations.",
                "</reasoning>",
                "I can help you with that.",
            }
        );

        var processedChunks = new List<ProcessedChunk>();

        // Act
        await foreach (var chunk in _streamingHandler.ProcessStreamAsync(chunks))
        {
            processedChunks.Add(chunk);
        }

        // Assert
        Assert.Contains(processedChunks, c => c.Type == ChunkType.ReasoningStart);
        Assert.Contains(processedChunks, c => c.Type == ChunkType.ReasoningComplete);
        Assert.Contains(
            processedChunks,
            c => c.Type == ChunkType.Content && c.Content.Contains("Let me think")
        );
        Assert.Contains(
            processedChunks,
            c => c.Type == ChunkType.Content && c.Content.Contains("I can help")
        );
    }

    /// <summary>
    /// Test that reasoning steps are extracted correctly
    /// </summary>
    [Fact]
    public async Task StreamingHandler_ShouldExtractReasoningSteps()
    {
        // Arrange
        var chunks = CreateTestStream(
            new[]
            {
                "<reasoning type=\"planning\">",
                "<step type=\"analysis\">Analyzing the user's request for file operations</step>",
                "<step type=\"tool_selection\">Selecting FileSystemTool for this task</step>",
                "<step type=\"execution\">Preparing to execute the file read operation</step>",
                "</reasoning>",
            }
        );

        var processedChunks = new List<ProcessedChunk>();

        // Act
        await foreach (var chunk in _streamingHandler.ProcessStreamAsync(chunks))
        {
            processedChunks.Add(chunk);
        }

        // Assert
        var reasoningSteps = processedChunks.Where(c => c.Type == ChunkType.ReasoningStep).ToList();
        Assert.Equal(3, reasoningSteps.Count);
        Assert.Contains(reasoningSteps, s => s.Content.Contains("[analysis]"));
        Assert.Contains(reasoningSteps, s => s.Content.Contains("[tool_selection]"));
        Assert.Contains(reasoningSteps, s => s.Content.Contains("[execution]"));
    }

    /// <summary>
    /// Test that reasoning type is properly identified
    /// </summary>
    [Theory]
    [InlineData("planning", "planning")]
    [InlineData("analysis", "analysis")]
    [InlineData("decision", "decision")]
    [InlineData(null, "general")]
    public async Task StreamingHandler_ShouldIdentifyReasoningType(
        string? typeAttribute,
        string expectedType
    )
    {
        // Arrange
        var reasoningTag = typeAttribute is not null
            ? $"<reasoning type=\"{typeAttribute}\">"
            : "<reasoning>";

        var chunks = CreateTestStream(
            new[] { reasoningTag, "Some reasoning content here", "</reasoning>" }
        );

        ProcessedChunk? reasoningStart = null;

        // Act
        await foreach (var chunk in _streamingHandler.ProcessStreamAsync(chunks))
        {
            if (chunk.Type == ChunkType.ReasoningStart)
            {
                reasoningStart = chunk;
                break;
            }
        }

        // Assert
        Assert.NotNull(reasoningStart);
        Assert.Equal(expectedType, reasoningStart.ReasoningType);
    }

    /// <summary>
    /// Test that content and reasoning are properly separated
    /// </summary>
    [Fact]
    public async Task StreamingHandler_ShouldSeparateContentAndReasoning()
    {
        // Arrange
        var chunks = CreateTestStream(
            new[]
            {
                "Here's my response. ",
                "<reasoning>Internal thinking that shouldn't be shown directly</reasoning>",
                "The answer is 42.",
            }
        );

        var contentChunks = new List<string>();
        var reasoningChunks = new List<string>();

        // Act
        await foreach (var chunk in _streamingHandler.ProcessStreamAsync(chunks))
        {
            if (chunk.Type == ChunkType.Content)
            {
                contentChunks.Add(chunk.Content);
            }
            else if (chunk.Type == ChunkType.ReasoningComplete)
            {
                reasoningChunks.Add(chunk.Content);
            }
        }

        // Assert
        Assert.Equal(2, contentChunks.Count);
        Assert.Contains("Here's my response. ", contentChunks);
        Assert.Contains("The answer is 42.", contentChunks);
        Assert.Single(reasoningChunks);
    }

    /// <summary>
    /// Test that incomplete reasoning blocks are handled gracefully
    /// </summary>
    [Fact]
    public async Task StreamingHandler_ShouldHandleIncompleteReasoningBlocks()
    {
        // Arrange
        var chunks = CreateTestStream(
            new[]
            {
                "<reasoning type=\"analysis\">",
                "This is some incomplete reasoning that never closes properly",
                // Note: No closing tag
            }
        );

        var processedChunks = new List<ProcessedChunk>();

        // Act
        await foreach (var chunk in _streamingHandler.ProcessStreamAsync(chunks))
        {
            processedChunks.Add(chunk);
        }

        // Assert
        Assert.Contains(processedChunks, c => c.Type == ChunkType.ReasoningStart);
        // Should still produce a reasoning complete chunk when stream ends
        Assert.Contains(processedChunks, c => c.Type == ChunkType.ReasoningComplete);
    }

    /// <summary>
    /// Test that nested reasoning blocks are handled
    /// </summary>
    [Fact]
    public async Task StreamingHandler_ShouldHandleNestedContent()
    {
        // Arrange
        var chunks = CreateTestStream(
            new[]
            {
                "Start ",
                "<reasoning>",
                "Thinking about <tool>FileSystem</tool> usage",
                "</reasoning>",
                " End",
            }
        );

        var processedChunks = new List<ProcessedChunk>();

        // Act
        await foreach (var chunk in _streamingHandler.ProcessStreamAsync(chunks))
        {
            processedChunks.Add(chunk);
        }

        // Assert
        var contentChunks = processedChunks.Where(c => c.Type == ChunkType.Content).ToList();
        Assert.Equal(2, contentChunks.Count); // "Start " and " End"
    }

    /// <summary>
    /// Test that reasoning events are raised
    /// </summary>
    [Fact]
    public async Task StreamingHandler_ShouldRaiseReasoningEvents()
    {
        // Arrange
        var chunks = CreateTestStream(
            new[] { "<reasoning type=\"planning\">", "Planning the approach", "</reasoning>" }
        );

        ReasoningStepEventArgs? capturedEvent = null;
        _streamingHandler.ReasoningStepDetected += (sender, args) => capturedEvent = args;

        // Act
        await foreach (var _ in _streamingHandler.ProcessStreamAsync(chunks))
        {
            // Process all chunks
        }

        // Assert
        Assert.NotNull(capturedEvent);
        Assert.Equal("planning", capturedEvent.Type);
        Assert.Contains("Planning the approach", capturedEvent.FullContent);
    }

    /// <summary>
    /// Test that console displays reasoning with correct formatting
    /// </summary>
    [Fact]
    public void InteractiveConsole_ShouldDisplayReasoningWithCorrectColor()
    {
        // Arrange & Act
        using var consoleCapture = new ConsoleOutputCapture();

        _console.WriteLine("🤔 Reasoning...", ConsoleMessageType.Reasoning);
        _console.WriteLine("  → Analyzing request", ConsoleMessageType.Reasoning);
        _console.WriteLine("  ✓ Complete", ConsoleMessageType.Reasoning);

        var output = consoleCapture.GetOutput();

        // Assert
        Assert.Contains("Reasoning", output);
        Assert.Contains("Analyzing request", output);
        Assert.Contains("Complete", output);
    }

    /// <summary>
    /// Test streaming handler reset functionality
    /// </summary>
    [Fact]
    public async Task StreamingHandler_ShouldResetProperly()
    {
        // Arrange - First stream
        var firstStream = CreateTestStream(new[] { "First message" });
        await foreach (var _ in _streamingHandler.ProcessStreamAsync(firstStream)) { }

        var firstContent = _streamingHandler.GetFullContent();

        // Act - Reset and process second stream
        _streamingHandler.Reset();
        var secondStream = CreateTestStream(new[] { "Second message" });
        await foreach (var _ in _streamingHandler.ProcessStreamAsync(secondStream)) { }

        var secondContent = _streamingHandler.GetFullContent();

        // Assert
        Assert.Equal("First message", firstContent);
        Assert.Equal("Second message", secondContent);
        Assert.DoesNotContain("First", secondContent);
    }

    /// <summary>
    /// Test that multiple reasoning blocks in sequence are handled
    /// </summary>
    [Fact]
    public async Task StreamingHandler_ShouldHandleMultipleReasoningBlocks()
    {
        // Arrange
        var chunks = CreateTestStream(
            new[]
            {
                "Starting. ",
                "<reasoning type=\"analysis\">First reasoning</reasoning>",
                " Middle content. ",
                "<reasoning type=\"planning\">Second reasoning</reasoning>",
                " End.",
            }
        );

        var reasoningBlocks = new List<ProcessedChunk>();

        // Act
        await foreach (var chunk in _streamingHandler.ProcessStreamAsync(chunks))
        {
            if (chunk.Type == ChunkType.ReasoningComplete)
            {
                reasoningBlocks.Add(chunk);
            }
        }

        // Assert
        Assert.Equal(2, reasoningBlocks.Count);
    }

    /// <summary>
    /// Helper to create a test stream from string chunks
    /// </summary>
    private async IAsyncEnumerable<StreamingChatResponse> CreateTestStream(
        string[] chunks,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (var chunk in chunks)
        {
            yield return new StreamingChatResponse
            {
                Choices = [new() { Delta = new Message { Content = chunk } }],
            };

            // Small delay to simulate streaming
            await Task.Delay(1, cancellationToken);
        }
    }

    /// <summary>
    /// Helper class to capture console output for testing
    /// </summary>
    private class ConsoleOutputCapture : IDisposable
    {
        private readonly StringWriter _stringWriter;
        private readonly TextWriter _originalOutput;

        public ConsoleOutputCapture()
        {
            _stringWriter = new StringWriter();
            _originalOutput = Console.Out;
            Console.SetOut(_stringWriter);
        }

        public string GetOutput() => _stringWriter.ToString();

        public void Dispose()
        {
            Console.SetOut(_originalOutput);
            _stringWriter.Dispose();
        }
    }
}
