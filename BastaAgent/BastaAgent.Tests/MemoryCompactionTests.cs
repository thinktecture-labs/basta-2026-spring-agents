using System.Text.Json;
using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using BastaAgent.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

/// <summary>
/// Tests for memory compaction and summarization functionality
/// Verifies that memory manager properly compacts old conversations using LLM
/// </summary>
public class MemoryCompactionTests : IDisposable
{
    private readonly string _testDir;

    public MemoryCompactionTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"MemoryCompactionTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
        {
            Directory.Delete(_testDir, true);
        }
    }

    /// <summary>
    /// Test that memory manager triggers compaction when threshold is exceeded
    /// </summary>
    [Fact]
    public async Task MemoryManager_ShouldTriggerCompactionAtThreshold()
    {
        // Arrange
        var manager = new MemoryManager(
            NullLogger<MemoryManager>.Instance,
            llmClient: null, // Use fallback summarization
            maxTokens: 1000,
            compactionThreshold: 800
        );

        // Act - Add memories until we exceed threshold
        for (int i = 0; i < 50; i++)
        {
            manager.AddMemory(
                new MemoryEntry
                {
                    Type = i % 2 == 0 ? MemoryType.User : MemoryType.Assistant,
                    Content = $"This is message number {i} with some content to make it longer",
                }
            );
        }

        // Wait for async compaction to complete
        await Task.Delay(500);

        // Assert - Check that compaction occurred
        var memories = manager.GetRecentMemories(1000);

        // Should have summaries and recent messages
        Assert.Contains(memories, m => m.Type == MemoryType.Summary);

        // Total token count should be under threshold
        var totalTokens = manager.EstimateTotalTokens();
        Assert.True(totalTokens < 1000, $"Expected tokens < 1000, got {totalTokens}");
    }

    /// <summary>
    /// Test that summaries preserve metadata
    /// </summary>
    [Fact]
    public async Task MemoryCompaction_ShouldPreserveMetadata()
    {
        // Arrange
        var manager = new MemoryManager(
            NullLogger<MemoryManager>.Instance,
            llmClient: null,
            maxTokens: 500,
            compactionThreshold: 400
        );

        var startTime = DateTime.UtcNow;

        // Add memories
        for (int i = 0; i < 20; i++)
        {
            manager.AddMemory(
                new MemoryEntry
                {
                    Type = i % 2 == 0 ? MemoryType.User : MemoryType.Assistant,
                    Content = $"Message {i}",
                    Timestamp = startTime.AddMinutes(i),
                }
            );
        }

        // Trigger compaction
        await manager.CompactAsync();

        // Assert
        var memories = manager.GetRecentMemories(500);
        var summary = memories.FirstOrDefault(m => m.Type == MemoryType.Summary);

        Assert.NotNull(summary);
        Assert.NotNull(summary.Metadata);
        Assert.Contains("originalCount", summary.Metadata.Keys);
        Assert.Contains("startTime", summary.Metadata.Keys);
        Assert.Contains("endTime", summary.Metadata.Keys);
        Assert.Contains("compactionMethod", summary.Metadata.Keys);
    }

    /// <summary>
    /// Test that recent messages are preserved during compaction
    /// </summary>
    [Fact]
    public async Task MemoryCompaction_ShouldKeepRecentMessages()
    {
        // Arrange
        var manager = new MemoryManager(
            NullLogger<MemoryManager>.Instance,
            llmClient: null,
            maxTokens: 1000,
            compactionThreshold: 800
        );

        // Add old messages
        for (int i = 0; i < 20; i++)
        {
            manager.AddMemory(
                new MemoryEntry { Type = MemoryType.User, Content = $"Old message {i}" }
            );
        }

        // Add recent messages
        var recentMessages = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var content = $"Recent message {i}";
            recentMessages.Add(content);
            manager.AddMemory(new MemoryEntry { Type = MemoryType.User, Content = content });
        }

        // Act
        await manager.CompactAsync();

        // Assert - Recent messages should be preserved
        var memories = manager.GetRecentMemories(1000);
        foreach (var recentMsg in recentMessages)
        {
            Assert.Contains(memories, m => m.Content == recentMsg);
        }
    }

    /// <summary>
    /// Test memory save and load with summaries
    /// </summary>
    [Fact]
    public async Task MemoryManager_ShouldSaveAndLoadSummaries()
    {
        // Arrange
        var filePath = Path.Combine(_testDir, "memory.json");
        var manager = new MemoryManager(NullLogger<MemoryManager>.Instance, llmClient: null);

        // Add a summary
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Summary,
                Content = "Summary of previous conversation",
                Metadata = new Dictionary<string, object>
                {
                    ["originalCount"] = 10,
                    ["compactionMethod"] = "fallback",
                },
            }
        );

        // Add regular messages
        manager.AddMemory(new MemoryEntry { Type = MemoryType.User, Content = "User message" });

        // Act
        await manager.SaveToFileAsync(filePath);

        var newManager = new MemoryManager(NullLogger<MemoryManager>.Instance, llmClient: null);
        await newManager.LoadFromFileAsync(filePath);

        // Assert
        var memories = newManager.GetRecentMemories(1000);
        Assert.Equal(2, memories.Count);

        var summary = memories.First(m => m.Type == MemoryType.Summary);
        Assert.Equal("Summary of previous conversation", summary.Content);
        Assert.NotNull(summary.Metadata);
    }

    /// <summary>
    /// Test that BuildContext includes summaries
    /// </summary>
    [Fact]
    public void BuildContext_ShouldIncludeSummaries()
    {
        // Arrange
        var manager = new MemoryManager(NullLogger<MemoryManager>.Instance, llmClient: null);

        manager.AddMemory(new MemoryEntry { Type = MemoryType.System, Content = "System prompt" });

        manager.AddMemory(
            new MemoryEntry { Type = MemoryType.Summary, Content = "Previous conversation summary" }
        );

        manager.AddMemory(new MemoryEntry { Type = MemoryType.User, Content = "Current question" });

        // Act
        var context = manager.BuildContext(1000);

        // Assert
        Assert.Contains("[System] System prompt", context);
        Assert.Contains("[Summary] Previous conversation summary", context);
        Assert.Contains("[User] Current question", context);

        // Verify order: System -> Summary -> User/Assistant
        var lines = context.Split('\n');
        var systemIndex = Array.FindIndex(lines, l => l.Contains("[System]"));
        var summaryIndex = Array.FindIndex(lines, l => l.Contains("[Summary]"));
        var userIndex = Array.FindIndex(lines, l => l.Contains("[User]"));

        Assert.True(systemIndex < summaryIndex);
        Assert.True(summaryIndex < userIndex);
    }

    /// <summary>
    /// Test that compaction doesn't happen during compaction (avoid recursion)
    /// </summary>
    [Fact]
    public async Task MemoryCompaction_ShouldNotRecurse()
    {
        // Arrange
        var manager = new MemoryManager(
            NullLogger<MemoryManager>.Instance,
            llmClient: null,
            maxTokens: 200,
            compactionThreshold: 150
        );

        // Add memories to trigger compaction
        for (int i = 0; i < 20; i++)
        {
            manager.AddMemory(new MemoryEntry { Type = MemoryType.User, Content = "Message " + i });
        }

        // Act - Trigger multiple compactions simultaneously
        var tasks = new List<Task>();
        for (int i = 0; i < 5; i++)
        {
            tasks.Add(manager.CompactAsync());
        }

        // Should complete without deadlock or stack overflow
        await Task.WhenAll(tasks);

        // Assert - Should have completed successfully
        var memories = manager.GetRecentMemories(200);
        Assert.NotEmpty(memories);
    }

    /// <summary>
    /// Test fallback summarization format
    /// </summary>
    [Fact]
    public async Task FallbackSummarization_ShouldCreateReadableSummary()
    {
        // Arrange
        var manager = new MemoryManager(
            NullLogger<MemoryManager>.Instance,
            llmClient: null, // Force fallback
            maxTokens: 500,
            compactionThreshold: 400
        );

        var baseTime = DateTime.UtcNow;

        // Add conversation
        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.User,
                Content = "What is the weather?",
                Timestamp = baseTime,
            }
        );

        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Assistant,
                Content = "I cannot check the weather",
                Timestamp = baseTime.AddSeconds(5),
            }
        );

        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.User,
                Content = "Can you help with code?",
                Timestamp = baseTime.AddSeconds(10),
            }
        );

        manager.AddMemory(
            new MemoryEntry
            {
                Type = MemoryType.Assistant,
                Content = "Yes, I can help with code",
                Timestamp = baseTime.AddSeconds(15),
            }
        );

        // Add many more to trigger compaction
        for (int i = 0; i < 20; i++)
        {
            manager.AddMemory(
                new MemoryEntry
                {
                    Type = MemoryType.User,
                    Content = $"Question {i}",
                    Timestamp = baseTime.AddSeconds(20 + i * 5),
                }
            );
        }

        // Act
        await manager.CompactAsync();

        // Assert
        var memories = manager.GetRecentMemories(500);
        var summary = memories.FirstOrDefault(m => m.Type == MemoryType.Summary);

        Assert.NotNull(summary);
        Assert.Contains("Conversation from", summary.Content);
        Assert.Contains("User discussed:", summary.Content);
        Assert.Contains("Assistant provided:", summary.Content);
    }

    /// <summary>
    /// Test token estimation accuracy
    /// </summary>
    [Theory]
    [InlineData("Short text", 5, 20)] // ~3 words -> ~3-7 tokens with overhead
    [InlineData("This is a medium length text with several words", 10, 30)] // ~9 words -> ~12-20 tokens with overhead
    [InlineData(
        "This is a much longer text that contains many more words and should result in a higher token count estimation for our memory manager",
        35,
        60
    )] // ~22 words -> ~40-50 tokens with overhead
    public void EstimateTokens_ShouldBeReasonablyAccurate(
        string content,
        int minExpected,
        int maxExpected
    )
    {
        // Arrange
        var manager = new MemoryManager(NullLogger<MemoryManager>.Instance, llmClient: null);

        manager.AddMemory(new MemoryEntry { Type = MemoryType.User, Content = content });

        // Act
        var totalTokens = manager.EstimateTotalTokens();

        // Assert - Token count should be in reasonable range
        Assert.InRange(totalTokens, minExpected, maxExpected);
    }
}
