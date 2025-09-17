using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using BastaAgent.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

/// <summary>
/// Integration tests for memory compaction with real LLM summarization simulation.
///
/// <para><b>Conference Note - Testing Memory Compaction:</b></para>
/// <para>These tests verify the complete memory compaction workflow:</para>
/// <list type="bullet">
/// <item>Automatic triggering when memory exceeds threshold</item>
/// <item>LLM-based summarization of old conversations</item>
/// <item>Preservation of key information in summaries</item>
/// <item>Maintaining recent messages for immediate context</item>
/// </list>
/// </summary>
public class MemoryCompactionIntegrationTests : IDisposable
{
    private readonly string _testDir;
    private readonly ILogger<MemoryManager> _logger;

    public MemoryCompactionIntegrationTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MemoryCompactionIntTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
        _logger = NullLogger<MemoryManager>.Instance;
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>
    /// Test complete memory compaction workflow with simulated LLM
    /// </summary>
    [Fact]
    public async Task CompleteCompactionWorkflow_WithLLM_ShouldSummarizeIntelligently()
    {
        // Conference Note: This test simulates the full compaction workflow
        // with a mock LLM client that returns realistic summaries

        // Arrange - Create mock LLM client
        var mockLLM = new MockLLMClient(generateSmartSummaries: true);
        var manager = new MemoryManager(
            _logger,
            mockLLM,
            maxTokens: 5000,
            compactionThreshold: 4000
        );

        // Add a complete conversation that will trigger compaction
        var conversation = GenerateRealisticConversation(100); // 100 message exchanges

        foreach (var entry in conversation)
        {
            manager.AddMemory(entry);
        }

        // Act - Wait for background compaction to complete
        await Task.Delay(1000); // Give time for async compaction

        // Assert - Verify intelligent summarization occurred
        var memories = manager.GetRecentMemories(5000);

        // Should have summaries
        var summaries = memories.Where(m => m.Type == MemoryType.Summary).ToList();
        Assert.NotEmpty(summaries);

        // Summaries should contain key information
        foreach (var summary in summaries)
        {
            Assert.NotNull(summary.Content);
            Assert.Contains("conversation", summary.Content.ToLower());

            // Check metadata
            Assert.NotNull(summary.Metadata);
            Assert.True(summary.Metadata.ContainsKey("originalCount"));
            Assert.True(summary.Metadata.ContainsKey("compactionMethod"));
            Assert.Equal("llm", summary.Metadata["compactionMethod"]);
        }

        // Recent messages should be preserved as-is
        var recentMessages = memories
            .Where(m => m.Type == MemoryType.User || m.Type == MemoryType.Assistant)
            .ToList();
        Assert.True(recentMessages.Count >= 10); // At least 10 recent messages preserved

        // Total tokens should be under limit
        var totalTokens = manager.EstimateTotalTokens();
        Assert.True(totalTokens < 5000, $"Tokens {totalTokens} should be under 5000");
    }

    /// <summary>
    /// Test that compaction preserves important context
    /// </summary>
    [Fact]
    public async Task Compaction_ShouldPreserveImportantContext()
    {
        // Conference Note: Verifies that critical information like
        // tool calls, decisions, and action items are preserved in summaries

        // Arrange
        var mockLLM = new MockLLMClient(generateSmartSummaries: true);
        var manager = new MemoryManager(
            _logger,
            mockLLM,
            maxTokens: 3000,
            compactionThreshold: 2500
        );

        // Add conversation with important decisions and tool calls
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.User,
                Content = "I need to analyze the sales data from Q3 2024",
            }
        );
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Assistant,
                Content = "I'll help you analyze the Q3 2024 sales data. Let me fetch the reports.",
            }
        );
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Tool,
                Content = "Fetched sales report: Total revenue $2.5M, 15% growth YoY",
            }
        );
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Assistant,
                Content =
                    "Key findings: Q3 revenue was $2.5M with 15% YoY growth. Top product: Widget-X",
            }
        );

        // Add more messages to trigger compaction
        for (int i = 0; i < 80; i++)
        {
            manager.AddMemory(
                new MemoryEntry
                {
                    Type = i % 2 == 0 ? MemoryType.User : MemoryType.Assistant,
                    Content = $"Discussion point {i}: Various analysis details and questions",
                }
            );
        }

        // Act - Trigger manual compaction
        await manager.CompactAsync();

        // Assert - Important context should be in summaries
        var context = manager.BuildContext(3000);

        // Key information should be preserved
        Assert.Contains("Q3", context);
        Assert.Contains("2.5M", context);
        Assert.Contains("15%", context);

        // Summary should indicate tool usage
        var memories = manager.GetRecentMemories(3000);
        var summaries = memories.Where(m => m.Type == MemoryType.Summary).ToList();
        Assert.NotEmpty(summaries);
    }

    /// <summary>
    /// Test compaction with various conversation patterns
    /// </summary>
    [Fact]
    public async Task Compaction_ShouldHandleVariousConversationPatterns()
    {
        // Conference Note: Tests different conversation patterns:
        // - Simple Q&A
        // - Multi-turn discussions
        // - Tool interactions
        // - Error recovery

        // Arrange
        var mockLLM = new MockLLMClient(generateSmartSummaries: true);
        var manager = new MemoryManager(
            _logger,
            mockLLM,
            maxTokens: 4000,
            compactionThreshold: 3000
        );

        // Pattern 1: Simple Q&A
        manager.AddMemory(new MemoryEntry { Type = MemoryType.User, Content = "What is Python?" });
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Assistant,
                Content = "Python is a high-level programming language known for its simplicity.",
            }
        );

        // Pattern 2: Multi-turn discussion
        manager.AddMemory(
            new MemoryEntry { Type = MemoryType.User, Content = "Tell me more about its uses" }
        );
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Assistant,
                Content = "Python is used in web development, data science, AI, and automation.",
            }
        );
        manager.AddMemory(
            new MemoryEntry { Type = MemoryType.User, Content = "What about performance?" }
        );
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Assistant,
                Content =
                    "Python trades some performance for ease of use, but libraries like NumPy are fast.",
            }
        );

        // Pattern 3: Tool interaction
        manager.AddMemory(
            new MemoryEntry { Type = MemoryType.User, Content = "Check the weather in NYC" }
        );
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Assistant,
                Content = "I'll check the current weather in New York City for you.",
            }
        );
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Tool,
                Content = "Weather API: NYC - 72°F, Partly cloudy, Humidity: 65%",
            }
        );
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Assistant,
                Content = "Currently in NYC: 72°F with partly cloudy skies and 65% humidity.",
            }
        );

        // Add bulk messages to trigger compaction
        for (int i = 0; i < 60; i++)
        {
            manager.AddMemory(
                new MemoryEntry
                {
                    Type = i % 2 == 0 ? MemoryType.User : MemoryType.Assistant,
                    Content = $"Extended conversation message {i}",
                }
            );
        }

        // Act
        await manager.CompactAsync();

        // Assert
        var memories = manager.GetRecentMemories(4000);
        var summaries = memories.Where(m => m.Type == MemoryType.Summary).ToList();

        Assert.NotEmpty(summaries);

        // Summaries should capture different patterns
        var allSummaryContent = string.Join(" ", summaries.Select(s => s.Content));
        Assert.Contains("Python", allSummaryContent); // Technical discussion preserved
        Assert.Contains("weather", allSummaryContent.ToLower()); // Tool usage preserved
    }

    /// <summary>
    /// Test compaction performance with large conversations
    /// </summary>
    [Fact]
    public async Task Compaction_ShouldHandleLargeConversationsEfficiently()
    {
        // Conference Note: Performance test ensuring compaction
        // completes in reasonable time even with large histories

        // Arrange
        var mockLLM = new MockLLMClient(generateSmartSummaries: true, simulateDelay: false);
        var manager = new MemoryManager(
            _logger,
            mockLLM,
            maxTokens: 100000,
            compactionThreshold: 80000
        );

        // Add large conversation (2000 messages to stay under threshold)
        // Conference Note: We use fewer messages to avoid triggering automatic
        // background compaction which could interfere with our manual compaction test
        var largeConversation = GenerateRealisticConversation(1000); // 1000 pairs = 2000 messages

        var addStart = DateTime.UtcNow;
        foreach (var entry in largeConversation)
        {
            manager.AddMemory(entry);
        }
        var addTime = DateTime.UtcNow - addStart;

        // Wait a moment to ensure any background operations complete
        await Task.Delay(100);

        // Act - Measure compaction time
        var compactStart = DateTime.UtcNow;
        await manager.CompactAsync();
        var compactTime = DateTime.UtcNow - compactStart;

        // Assert
        // Should complete quickly
        Assert.True(addTime.TotalSeconds < 5, $"Adding messages took {addTime.TotalSeconds}s");
        Assert.True(compactTime.TotalSeconds < 10, $"Compaction took {compactTime.TotalSeconds}s");

        // Should have reduced memory footprint
        var memories = manager.GetRecentMemories(100000);
        var summaryCount = memories.Count(m => m.Type == MemoryType.Summary);
        var messageCount = memories.Count(m => m.Type != MemoryType.Summary);

        Assert.True(summaryCount > 0, "Should have created summaries");
        Assert.True(messageCount < 1000, "Should have fewer raw messages after compaction");

        // Token count should be significantly reduced
        var finalTokens = manager.EstimateTotalTokens();
        Assert.True(finalTokens < 100000, $"Final tokens {finalTokens} should be under limit");
    }

    /// <summary>
    /// Test fallback summarization when LLM is unavailable
    /// </summary>
    [Fact]
    public async Task Compaction_ShouldUseFallbackWhenLLMUnavailable()
    {
        // Conference Note: Tests graceful degradation when LLM
        // is unavailable or returns errors

        // Arrange - No LLM client provided
        var manager = new MemoryManager(
            _logger,
            llmClient: null, // No LLM
            maxTokens: 2000,
            compactionThreshold: 1500
        );

        // Add conversation
        for (int i = 0; i < 50; i++)
        {
            manager.AddMemory(
                new MemoryEntry
                {
                    Type = i % 2 == 0 ? MemoryType.User : MemoryType.Assistant,
                    Content = $"Message {i}: This is a test message with content",
                    Timestamp = DateTime.UtcNow.AddMinutes(i),
                }
            );
        }

        // Act
        await manager.CompactAsync();

        // Assert - Should use fallback summarization
        var memories = manager.GetRecentMemories(2000);
        var summaries = memories.Where(m => m.Type == MemoryType.Summary).ToList();

        Assert.NotEmpty(summaries);

        // Check fallback summary format
        foreach (var summary in summaries)
        {
            Assert.Contains("Conversation from", summary.Content);
            Assert.Contains("User discussed:", summary.Content);
            Assert.Contains("Assistant provided:", summary.Content);

            // Metadata should indicate fallback method
            Assert.Equal("fallback", summary.Metadata?["compactionMethod"]);
        }
    }

    /// <summary>
    /// Test concurrent memory operations during compaction
    /// </summary>
    [Fact]
    public async Task Compaction_ShouldHandleConcurrentOperations()
    {
        // Conference Note: Ensures thread safety during compaction
        // while new messages are being added

        // Arrange
        var mockLLM = new MockLLMClient(generateSmartSummaries: true);
        var manager = new MemoryManager(
            _logger,
            mockLLM,
            maxTokens: 3000,
            compactionThreshold: 2000
        );

        // Start with some messages
        for (int i = 0; i < 30; i++)
        {
            manager.AddMemory(
                new MemoryEntry { Type = MemoryType.User, Content = $"Initial message {i}" }
            );
        }

        // Act - Add messages concurrently while compacting
        var tasks = new List<Task>
        {
            // Task 1: Trigger compaction
            manager.CompactAsync(),
            // Task 2: Add more messages during compaction
            Task.Run(async () =>
            {
                await Task.Delay(50); // Small delay to ensure compaction starts
                for (int i = 0; i < 20; i++)
                {
                    manager.AddMemory(
                        new MemoryEntry
                        {
                            Type = MemoryType.User,
                            Content = $"Concurrent message {i}",
                        }
                    );
                    await Task.Delay(10);
                }
            }),
        };

        await Task.WhenAll(tasks);

        // Assert - System should remain consistent
        var memories = manager.GetRecentMemories(3000);
        Assert.NotNull(memories);
        Assert.NotEmpty(memories);

        // Should have both summaries and recent messages
        Assert.Contains(memories, m => m.Type == MemoryType.Summary);
        Assert.Contains(memories, m => m.Content.Contains("Concurrent"));
    }

    #region Helper Methods

    /// <summary>
    /// Generate realistic conversation for testing
    /// </summary>
    private List<MemoryEntry> GenerateRealisticConversation(int pairs)
    {
        var conversation = new List<MemoryEntry>();
        var topics = new[] { "programming", "weather", "history", "science", "travel" };
        var random = new Random(42); // Fixed seed for reproducibility

        for (int i = 0; i < pairs; i++)
        {
            var topic = topics[random.Next(topics.Length)];

            // User question
            conversation.Add(
                new MemoryEntry
                {
                    Type = MemoryType.User,
                    Content =
                        $"Question {i} about {topic}: Can you explain something related to this?",
                    Timestamp = DateTime.UtcNow.AddMinutes(i * 2),
                }
            );

            // Assistant response
            conversation.Add(
                new MemoryEntry
                {
                    Type = MemoryType.Assistant,
                    Content =
                        $"Response {i}: Here's information about {topic}. "
                        + $"This is a detailed explanation with multiple points. "
                        + $"Point 1: Important detail. Point 2: Another detail. "
                        + $"Conclusion: This covers the main aspects of {topic}.",
                    Timestamp = DateTime.UtcNow.AddMinutes(i * 2 + 1),
                }
            );

            // Occasionally add tool calls
            if (i % 10 == 0 && i > 0)
            {
                conversation.Add(
                    new MemoryEntry
                    {
                        Type = MemoryType.Tool,
                        Content = $"Tool result for {topic}: Data retrieved successfully",
                        Timestamp = DateTime.UtcNow.AddMinutes(i * 2 + 0.5),
                    }
                );
            }
        }

        return conversation;
    }

    #endregion
}

/// <summary>
/// Mock LLM client for testing memory compaction
/// </summary>
public class MockLLMClient(bool generateSmartSummaries = true, bool simulateDelay = true)
    : ILLMClient
{
    private readonly bool _generateSmartSummaries = generateSmartSummaries;
    private readonly bool _simulateDelay = simulateDelay;

    public async Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (_simulateDelay)
        {
            await Task.Delay(100, cancellationToken); // Simulate network delay
        }

        // Generate smart summary based on request
        var summary = _generateSmartSummaries
            ? GenerateSmartSummary(request)
            : "Basic summary of conversation.";

        return new ChatResponse
        {
            Id = Guid.NewGuid().ToString(),
            Object = "chat.completion",
            Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            Model = request.Model ?? "mock-model",
            Choices =
            [
                new Choice
                {
                    Index = 0,
                    Message = new Message { Role = "assistant", Content = summary },
                    FinishReason = "stop",
                },
            ],
            Usage = new Usage
            {
                PromptTokens = 100,
                CompletionTokens = 50,
                TotalTokens = 150,
            },
        };
    }

    public async IAsyncEnumerable<StreamingChatResponse> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        // Not needed for compaction tests
        yield return new StreamingChatResponse();
        await Task.CompletedTask;
    }

    public string GetModelForPurpose(ModelPurpose purpose)
    {
        return purpose switch
        {
            ModelPurpose.Summarization => "mock-summarization-model",
            _ => "mock-model",
        };
    }

    private string GenerateSmartSummary(ChatRequest request)
    {
        // Extract conversation content from request
        var userMessage = request.Messages?.LastOrDefault(m => m.Role == "user");
        if (userMessage is null)
            return "Summary: Conversation processed.";

        var content = userMessage.Content ?? "";

        // Generate contextual summary
        var summary = new StringBuilder();
        summary.AppendLine("Summary of conversation:");

        // Detect patterns in content
        if (content.Contains("User discussed:"))
        {
            summary.AppendLine("- Users asked various questions about topics");
            summary.AppendLine("- Assistant provided detailed explanations");
        }

        if (content.Contains("Tool"))
        {
            summary.AppendLine("- Tool calls were made to fetch external data");
        }

        if (content.Contains("Q3") || content.Contains("sales"))
        {
            summary.AppendLine("- Discussed Q3 2024 sales data: $2.5M revenue with 15% YoY growth");
            summary.AppendLine("- Key product identified: Widget-X");
        }

        if (content.Contains("Python"))
        {
            summary.AppendLine("- Discussed Python programming language and its applications");
        }

        if (content.Contains("weather"))
        {
            summary.AppendLine("- Weather information was requested and provided");
        }

        summary.AppendLine("- Key decisions and action items were noted");

        return summary.ToString();
    }
}
