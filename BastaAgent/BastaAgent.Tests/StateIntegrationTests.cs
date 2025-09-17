using System.Text.Json;
using BastaAgent.Agent;
using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using BastaAgent.Memory;
using BastaAgent.Tools;
using BastaAgent.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BastaAgent.Tests;

/// <summary>
/// Integration tests for complete state persistence and loading workflow.
///
/// <para><b>Conference Note - State Persistence Testing:</b></para>
/// <para>These tests verify the complete state management lifecycle:</para>
/// <list type="bullet">
/// <item>Saving complete agent state including conversations, tools, and metadata</item>
/// <item>Loading state and resuming conversations seamlessly</item>
/// <item>Handling corrupted state files gracefully</item>
/// <item>Atomic save operations to prevent data loss</item>
/// </list>
/// </summary>
public class StateIntegrationTests : IDisposable
{
    private readonly string _testStateDir;
    private readonly ILogger<AgentState> _logger;

    public StateIntegrationTests()
    {
        // Conference Note: Each test gets its own temporary directory
        // to ensure tests don't interfere with each other
        _testStateDir = Path.Combine(Path.GetTempPath(), $"BastaAgentStateTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testStateDir);
        Environment.CurrentDirectory = _testStateDir;
        _logger = NullLogger<AgentState>.Instance;
    }

    public void Dispose()
    {
        // Clean up test directory after each test
        if (Directory.Exists(_testStateDir))
        {
            try
            {
                Directory.Delete(_testStateDir, true);
            }
            catch
            {
                // Best effort cleanup - ignore errors
            }
        }
    }

    /// <summary>
    /// Test complete state save and load cycle with all components
    /// </summary>
    [Fact]
    public async Task CompleteStateCycle_ShouldPreserveAllData()
    {
        // Conference Note: This test verifies the complete state lifecycle:
        // 1. Create state with all components
        // 2. Save to disk
        // 3. Load from disk
        // 4. Verify all data is preserved

        // Arrange - Create a complete state
        var originalState = new AgentState
        {
            SessionId = "test-session-123",
            // MessageCount will be incremented by AddMessage
            ToolCallCount = 5,
            TotalTokensUsed = 2500,
            SessionStarted = DateTime.UtcNow.AddMinutes(-30),
        };

        // Add conversation history
        originalState.AddMessage(
            new Message { Role = "system", Content = "You are a helpful assistant." }
        );
        originalState.AddMessage(new Message { Role = "user", Content = "What is the weather?" });
        originalState.AddMessage(
            new Message { Role = "assistant", Content = "I'll check the weather for you." }
        );
        originalState.AddMessage(
            new Message
            {
                Role = "assistant",
                Content = null,
                ToolCalls =
                [
                    new ToolCall
                    {
                        Id = "call_123",
                        Type = "function",
                        Function = new FunctionCall
                        {
                            Name = "get_weather",
                            Arguments = "{\"location\":\"New York\"}",
                        },
                    },
                ],
            }
        );
        originalState.AddMessage(
            new Message
            {
                Role = "tool",
                Content = "Temperature: 72°F, Sunny",
                ToolCallId = "call_123",
            }
        );

        // Add reasoning steps
        originalState.LastReasoningSteps.Add(
            new ReasoningStep
            {
                Type = "planning",
                Content = "User asked about weather. I need to use the weather tool.",
                Summary = "Planning: Use weather tool",
                Timestamp = DateTime.UtcNow.AddMinutes(-5),
            }
        );
        originalState.LastReasoningSteps.Add(
            new ReasoningStep
            {
                Type = "execution",
                Content = "Calling get_weather function with location parameter",
                Summary = "Executing: Weather API call",
                Timestamp = DateTime.UtcNow.AddMinutes(-4),
            }
        );

        // Add tool state (using JsonElement for custom data)
        originalState.ToolState["user_preference"] = JsonSerializer.SerializeToElement("metric");
        originalState.ToolState["session_type"] = JsonSerializer.SerializeToElement(
            "weather_query"
        );
        originalState.ToolState["api_version"] = JsonSerializer.SerializeToElement("1.0");

        var stateFile = Path.Combine(_testStateDir, "state", "complete_state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);

        // Act - Save and load
        await originalState.SaveAsync(stateFile);
        var loadedState = await AgentState.LoadAsync(stateFile);

        // Assert - Verify all data is preserved
        Assert.NotNull(loadedState);

        // Basic properties
        Assert.Equal(originalState.SessionId, loadedState.SessionId);
        Assert.Equal(5, loadedState.MessageCount); // We added 5 messages
        Assert.Equal(originalState.ToolCallCount, loadedState.ToolCallCount);
        Assert.Equal(originalState.TotalTokensUsed, loadedState.TotalTokensUsed);
        // Session timing should be preserved
        Assert.Equal(
            originalState.SessionStarted,
            loadedState.SessionStarted,
            TimeSpan.FromSeconds(1)
        );

        // Conversation history
        Assert.Equal(5, loadedState.ConversationHistory.Count);
        Assert.Equal("system", loadedState.ConversationHistory[0].Role);
        Assert.Equal("You are a helpful assistant.", loadedState.ConversationHistory[0].Content);

        // Tool calls
        var toolCallMessage = loadedState.ConversationHistory[3];
        Assert.NotNull(toolCallMessage.ToolCalls);
        Assert.Single(toolCallMessage.ToolCalls);
        Assert.NotNull(toolCallMessage.ToolCalls[0].Function);
        Assert.Equal("get_weather", toolCallMessage.ToolCalls[0].Function!.Name);

        // Tool response
        var toolResponse = loadedState.ConversationHistory[4];
        Assert.Equal("tool", toolResponse.Role);
        Assert.Equal("call_123", toolResponse.ToolCallId);

        // Reasoning steps
        Assert.Equal(2, loadedState.LastReasoningSteps.Count);
        Assert.Equal("planning", loadedState.LastReasoningSteps[0].Type);
        Assert.Contains("weather tool", loadedState.LastReasoningSteps[0].Summary);

        // Tool state
        Assert.Equal(3, loadedState.ToolState.Count);
        Assert.Equal("metric", loadedState.ToolState["user_preference"].GetString());
        Assert.Equal("weather_query", loadedState.ToolState["session_type"].GetString());
        Assert.Equal("1.0", loadedState.ToolState["api_version"].GetString());
    }

    /// <summary>
    /// Test that atomic save prevents data corruption
    /// </summary>
    [Fact]
    public async Task AtomicSave_ShouldPreventCorruption()
    {
        // Conference Note: Atomic saves write to a temp file first,
        // then rename it to the target. This prevents corruption
        // if the process crashes during writing.

        // Arrange
        var state = new AgentState { SessionId = "atomic-test" };
        state.AddMessage(new Message { Role = "user", Content = "Test message" });

        var stateFile = Path.Combine(_testStateDir, "state", "atomic_state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);

        // Create an existing valid state file
        var existingState = new AgentState { SessionId = "existing-session" };
        await existingState.SaveAsync(stateFile);

        // Act - Save should be atomic (temp file + rename)
        await state.SaveAsync(stateFile);

        // Assert - New state should have replaced old one completely
        var loadedState = await AgentState.LoadAsync(stateFile);
        Assert.Equal("atomic-test", loadedState.SessionId);
        Assert.Single(loadedState.ConversationHistory);

        // Verify temp files are cleaned up
        var tempFiles = Directory.GetFiles(Path.GetDirectoryName(stateFile)!, "*.tmp");
        Assert.Empty(tempFiles);
    }

    /// <summary>
    /// Test loading from corrupted state file
    /// </summary>
    [Fact]
    public async Task LoadCorruptedState_ShouldHandleGracefully()
    {
        // Conference Note: The system should handle corrupted state files
        // gracefully, either by returning null or throwing a clear error

        // Arrange - Create corrupted JSON file
        var stateFile = Path.Combine(_testStateDir, "state", "corrupted_state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
        await File.WriteAllTextAsync(stateFile, "{ invalid json content ]");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await AgentState.LoadAsync(stateFile)
        );

        Assert.Contains("Failed to load agent state", exception.Message);
    }

    /// <summary>
    /// Test incremental state updates during conversation
    /// </summary>
    [Fact]
    public async Task IncrementalUpdates_ShouldPersistCorrectly()
    {
        // Conference Note: This simulates a real conversation with
        // incremental saves after each interaction

        // Arrange
        var stateFile = Path.Combine(_testStateDir, "state", "incremental_state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);

        var state = new AgentState { SessionId = "incremental-test" };

        // Simulate conversation with saves after each turn
        // Turn 1: User message
        state.AddMessage(new Message { Role = "user", Content = "Hello" });
        await state.SaveAsync(stateFile);

        // Turn 2: Assistant response
        state.AddMessage(new Message { Role = "assistant", Content = "Hello! How can I help?" });
        state.TotalTokensUsed += 50;
        await state.SaveAsync(stateFile);

        // Turn 3: User follow-up
        state.AddMessage(new Message { Role = "user", Content = "What's 2+2?" });
        await state.SaveAsync(stateFile);

        // Turn 4: Assistant with reasoning
        state.LastReasoningSteps.Clear();
        state.LastReasoningSteps.Add(
            new ReasoningStep
            {
                Type = "calculation",
                Content = "Simple arithmetic: 2+2=4",
                Summary = "Calculating: 2+2",
                Timestamp = DateTime.UtcNow,
            }
        );
        state.AddMessage(new Message { Role = "assistant", Content = "2+2 equals 4" });
        state.TotalTokensUsed += 45;
        await state.SaveAsync(stateFile);

        // Load and verify final state
        var loadedState = await AgentState.LoadAsync(stateFile);

        // Assert
        Assert.Equal(4, loadedState.ConversationHistory.Count);
        Assert.Equal(4, loadedState.MessageCount);
        Assert.Equal(95, loadedState.TotalTokensUsed);
        Assert.Single(loadedState.LastReasoningSteps);
        Assert.Equal("calculation", loadedState.LastReasoningSteps[0].Type);
    }

    /// <summary>
    /// Test state backup functionality
    /// </summary>
    [Fact]
    public async Task StateBackup_ShouldCreateVersionedBackups()
    {
        // Conference Note: Backups are important for recovery.
        // Each backup should have a unique timestamp.

        // Arrange
        var state = new AgentState { SessionId = "backup-test" };
        state.AddMessage(new Message { Role = "user", Content = "Message 1" });

        var stateFile = Path.Combine(_testStateDir, "state", "main_state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);

        // Act - Create multiple backups
        await state.BackupAsync(stateFile);
        await Task.Delay(10); // Small delay to ensure different timestamps

        state.AddMessage(new Message { Role = "assistant", Content = "Response 1" });
        await state.BackupAsync(stateFile);
        await Task.Delay(10);

        state.AddMessage(new Message { Role = "user", Content = "Message 2" });
        await state.BackupAsync(stateFile);

        // Assert - Should have 3 backup files with different timestamps
        var backupFiles = Directory
            .GetFiles(Path.GetDirectoryName(stateFile)!, "main_state.json.backup_*")
            .OrderBy(f => f)
            .ToArray();

        Assert.Equal(3, backupFiles.Length);

        // Verify each backup has different content
        var backup1 = await AgentState.LoadAsync(backupFiles[0]);
        var backup2 = await AgentState.LoadAsync(backupFiles[1]);
        var backup3 = await AgentState.LoadAsync(backupFiles[2]);

        Assert.Single(backup1.ConversationHistory);
        Assert.Equal(2, backup2.ConversationHistory.Count);
        Assert.Equal(3, backup3.ConversationHistory.Count);
    }

    /// <summary>
    /// Test state file permissions and directory creation
    /// </summary>
    [Fact]
    public async Task StateSave_ShouldCreateDirectoriesAsNeeded()
    {
        // Conference Note: The save operation should create any
        // missing directories in the path

        // Arrange
        var state = new AgentState { SessionId = "directory-test" };
        var deepPath = Path.Combine(_testStateDir, "deep", "nested", "directories", "state.json");

        // Act
        await state.SaveAsync(deepPath);

        // Assert
        Assert.True(File.Exists(deepPath));
        Assert.True(Directory.Exists(Path.GetDirectoryName(deepPath)));

        // Verify we can load it back
        var loadedState = await AgentState.LoadAsync(deepPath);
        Assert.Equal("directory-test", loadedState.SessionId);
    }

    /// <summary>
    /// Test concurrent state operations
    /// </summary>
    [Fact]
    public async Task ConcurrentStateOperations_ShouldHandleSafely()
    {
        // Conference Note: Multiple threads might try to save state
        // simultaneously. The system should handle this safely.

        // Arrange
        var stateFile = Path.Combine(_testStateDir, "state", "concurrent_state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);

        var tasks = new List<Task>();

        // Act - Multiple concurrent saves with error handling
        for (int i = 0; i < 10; i++)
        {
            var taskId = i;
            tasks.Add(
                Task.Run(async () =>
                {
                    // Add random delay to reduce file contention
                    await Task.Delay(Random.Shared.Next(0, 50));

                    var state = new AgentState { SessionId = $"session-{taskId}" };
                    state.AddMessage(new Message { Role = "user", Content = $"Message {taskId}" });

                    // Retry logic for concurrent file access
                    int retries = 3;
                    while (retries > 0)
                    {
                        try
                        {
                            await state.SaveAsync(stateFile);
                            break;
                        }
                        catch (IOException) when (retries > 1)
                        {
                            // File might be locked by another thread, retry
                            retries--;
                            await Task.Delay(50);
                        }
                    }
                })
            );
        }

        await Task.WhenAll(tasks);

        // Assert - Should have one valid state file (last write wins)
        var finalState = await AgentState.LoadAsync(stateFile);
        Assert.NotNull(finalState);
        Assert.StartsWith("session-", finalState.SessionId);
        Assert.Single(finalState.ConversationHistory);
    }

    /// <summary>
    /// Test state migration from older versions
    /// </summary>
    [Fact]
    public async Task StateMigration_ShouldHandleOlderVersions()
    {
        // Conference Note: As the state format evolves, we need to
        // handle loading older state files gracefully

        // Arrange - Create a minimal state file (simulating older version)
        var stateFile = Path.Combine(_testStateDir, "state", "old_state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);

        var oldStateJson =
            @"{
            ""SessionId"": ""old-session"",
            ""ConversationHistory"": [
                {""Role"": ""user"", ""Content"": ""Hello""}
            ]
        }";

        await File.WriteAllTextAsync(stateFile, oldStateJson);

        // Act
        var loadedState = await AgentState.LoadAsync(stateFile);

        // Assert - Should load with defaults for missing fields
        Assert.NotNull(loadedState);
        Assert.Equal("old-session", loadedState.SessionId);
        Assert.Single(loadedState.ConversationHistory);
        Assert.Equal(0, loadedState.TotalTokensUsed); // Default value
        Assert.Equal(0, loadedState.ToolCallCount); // Default value
        Assert.Empty(loadedState.LastReasoningSteps); // Default empty list
    }

    /// <summary>
    /// Test state size limits and performance
    /// </summary>
    [Fact]
    public async Task LargeState_ShouldHandleEfficiently()
    {
        // Conference Note: Test that large conversation histories
        // can be saved and loaded efficiently

        // Arrange - Create a large state
        var state = new AgentState { SessionId = "large-state" };

        // Add 1000 messages (simulating long conversation)
        for (int i = 0; i < 1000; i++)
        {
            state.AddMessage(
                new Message
                {
                    Role = i % 2 == 0 ? "user" : "assistant",
                    Content =
                        $"This is message number {i} with some content to make it realistic. "
                        + $"The content should be long enough to simulate real messages. "
                        + $"Random value: {Guid.NewGuid()}",
                }
            );
        }

        // Add tool state
        for (int i = 0; i < 100; i++)
        {
            state.ToolState[$"key_{i}"] = JsonSerializer.SerializeToElement(
                $"value_{i}_{Guid.NewGuid()}"
            );
        }

        var stateFile = Path.Combine(_testStateDir, "state", "large_state.json");
        Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);

        // Act - Measure save and load times
        var saveStart = DateTime.UtcNow;
        await state.SaveAsync(stateFile);
        var saveTime = DateTime.UtcNow - saveStart;

        var loadStart = DateTime.UtcNow;
        var loadedState = await AgentState.LoadAsync(stateFile);
        var loadTime = DateTime.UtcNow - loadStart;

        // Assert
        Assert.Equal(1000, loadedState.ConversationHistory.Count);
        Assert.Equal(1000, loadedState.MessageCount);
        Assert.Equal(100, loadedState.ToolState.Count);

        // Performance assertions (should be fast even with large state)
        Assert.True(
            saveTime.TotalSeconds < 5,
            $"Save took {saveTime.TotalSeconds}s, expected < 5s"
        );
        Assert.True(
            loadTime.TotalSeconds < 5,
            $"Load took {loadTime.TotalSeconds}s, expected < 5s"
        );

        // Verify file size is reasonable
        var fileInfo = new FileInfo(stateFile);
        Assert.True(fileInfo.Length > 0, "State file should not be empty");
        Assert.True(fileInfo.Length < 10_000_000, "State file should be less than 10MB");
    }
}
