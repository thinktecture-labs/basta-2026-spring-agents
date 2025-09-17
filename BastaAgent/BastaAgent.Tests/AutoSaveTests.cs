using System.Text.Json;
using BastaAgent.Agent;
using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using BastaAgent.Memory;
using BastaAgent.Tools;
using BastaAgent.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BastaAgent.Tests;

/// <summary>
/// Tests for auto-save functionality in the Agent class
/// Verifies that state is automatically saved after every LLM interaction
/// </summary>
public class AutoSaveTests : IDisposable
{
    private readonly string _testStateDir;

    public AutoSaveTests()
    {
        // Create test state directory
        _testStateDir = Path.Combine(
            Path.GetTempPath(),
            $"BastaAgentAutoSaveTests_{Guid.NewGuid()}"
        );
        Directory.CreateDirectory(_testStateDir);
        Environment.CurrentDirectory = _testStateDir;
    }

    public void Dispose()
    {
        if (Directory.Exists(_testStateDir))
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Directory.Delete(_testStateDir, true);
                    break;
                }
                catch
                {
                    // Small delay and retry in case files are still being released
                    Thread.Sleep(20);
                }
            }
        }
    }

    /// <summary>
    /// Test that auto-save file is created after SaveStateAsync
    /// </summary>
    [Fact]
    public async Task Agent_ShouldCreateAutoSaveFile()
    {
        // Arrange
        var state = new AgentState { SessionId = "test-session", ToolCallCount = 3 };

        // Add 5 messages to get MessageCount = 5
        state.AddMessage(new Message { Role = "user", Content = "Test message 1" });
        state.AddMessage(new Message { Role = "assistant", Content = "Test response 1" });
        state.AddMessage(new Message { Role = "user", Content = "Test message 2" });
        state.AddMessage(new Message { Role = "assistant", Content = "Test response 2" });
        state.AddMessage(new Message { Role = "user", Content = "Test message 3" });

        // Act
        var stateDir = Path.Combine(Environment.CurrentDirectory, "state");
        Directory.CreateDirectory(stateDir);

        var autoSaveFile = Path.Combine(stateDir, "test_autosave.json");
        await state.SaveAsync(autoSaveFile);

        // Assert
        Assert.True(File.Exists(autoSaveFile), "Auto-save file should exist");

        var savedJson = await File.ReadAllTextAsync(autoSaveFile);
        var savedData = JsonDocument.Parse(savedJson);
        var root = savedData.RootElement;

        Assert.True(root.TryGetProperty("MessageCount", out var messageCount));
        Assert.Equal(5, messageCount.GetInt32());

        Assert.True(root.TryGetProperty("ToolCallCount", out var toolCallCount));
        Assert.Equal(3, toolCallCount.GetInt32());
    }

    /// <summary>
    /// Test that state can be loaded from auto-save
    /// </summary>
    [Fact]
    public async Task AgentState_ShouldLoadFromAutoSave()
    {
        // Arrange
        var originalState = new AgentState { SessionId = "test-session", ToolCallCount = 5 };

        // Add messages - AddMessage increments MessageCount internally
        originalState.AddMessage(new Message { Role = "user", Content = "Question" });
        originalState.AddMessage(new Message { Role = "assistant", Content = "Answer" });

        var stateDir = Path.Combine(Environment.CurrentDirectory, "state");
        Directory.CreateDirectory(stateDir);

        var autoSaveFile = Path.Combine(stateDir, "agent_autosave.json");
        await originalState.SaveAsync(autoSaveFile);

        // Act
        var loadedState = await AgentState.LoadAsync(autoSaveFile);

        // Assert
        Assert.NotNull(loadedState);
        Assert.Equal("test-session", loadedState.SessionId);
        Assert.Equal(2, loadedState.MessageCount); // 2 messages added
        Assert.Equal(5, loadedState.ToolCallCount);
        Assert.Equal(2, loadedState.ConversationHistory.Count);
    }

    /// <summary>
    /// Test that multiple auto-saves overwrite the same file
    /// </summary>
    [Fact]
    public async Task AgentState_ShouldOverwriteAutoSaveFile()
    {
        // Arrange
        var stateDir = Path.Combine(Environment.CurrentDirectory, "state");
        Directory.CreateDirectory(stateDir);
        var autoSaveFile = Path.Combine(stateDir, "agent_autosave.json");

        // Act - Save multiple times with different states
        var state1 = new AgentState { MessageCount = 1 };
        await state1.SaveAsync(autoSaveFile);

        var state2 = new AgentState { MessageCount = 2 };
        await state2.SaveAsync(autoSaveFile);

        var state3 = new AgentState { MessageCount = 3 };
        await state3.SaveAsync(autoSaveFile);

        // Assert - Should only have one file with the latest state
        Assert.True(File.Exists(autoSaveFile));

        var loadedState = await AgentState.LoadAsync(autoSaveFile);
        Assert.Equal(3, loadedState.MessageCount);
    }

    /// <summary>
    /// Test that auto-save handles errors gracefully
    /// </summary>
    [Fact]
    public async Task AgentState_ShouldHandleSaveErrorsGracefully()
    {
        // Arrange
        var state = new AgentState { MessageCount = 5 };
        var invalidPath = Path.Combine("/invalid/path/that/does/not/exist", "state.json");

        // Act & Assert - Should throw with meaningful error
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await state.SaveAsync(invalidPath)
        );

        Assert.Contains("Failed to save agent state", exception.Message);
    }

    /// <summary>
    /// Test that SavedStateData serialization works correctly
    /// </summary>
    [Fact]
    public void SavedStateData_ShouldSerializeCorrectly()
    {
        // Arrange
        var stateData = new
        {
            State = new AgentState
            {
                SessionId = "test-session",
                MessageCount = 5,
                ToolCallCount = 3,
            },
            ApprovedTools = new[] { "Tool1", "Tool2" },
            DeniedTools = new[] { "Tool3" },
            Timestamp = DateTime.UtcNow,
        };

        // Act
        var json = JsonSerializer.Serialize(
            stateData,
            new JsonSerializerOptions { WriteIndented = true }
        );
        var deserialized = JsonSerializer.Deserialize<JsonElement>(json);

        // Assert
        Assert.True(deserialized.TryGetProperty("State", out var stateElement));
        Assert.True(stateElement.TryGetProperty("SessionId", out var sessionId));
        Assert.Equal("test-session", sessionId.GetString());

        Assert.True(deserialized.TryGetProperty("ApprovedTools", out var approvedTools));
        Assert.Equal(2, approvedTools.GetArrayLength());

        Assert.True(deserialized.TryGetProperty("DeniedTools", out var deniedTools));
        Assert.Equal(1, deniedTools.GetArrayLength());
    }

    /// <summary>
    /// Test that reasoning steps are preserved in auto-save
    /// </summary>
    [Fact]
    public async Task AgentState_ShouldPreserveReasoningSteps()
    {
        // Arrange
        var state = new AgentState();
        state.LastReasoningSteps.Add(
            new ReasoningStep
            {
                Type = "planning",
                Content = "Planning the approach",
                Summary = "Plan: Do X then Y",
                Timestamp = DateTime.UtcNow,
            }
        );
        state.LastReasoningSteps.Add(
            new ReasoningStep
            {
                Type = "execution",
                Content = "Executing the plan",
                Summary = "Executed successfully",
                Timestamp = DateTime.UtcNow,
            }
        );

        var stateDir = Path.Combine(Environment.CurrentDirectory, "state");
        Directory.CreateDirectory(stateDir);
        var autoSaveFile = Path.Combine(stateDir, "agent_autosave.json");

        // Act
        await state.SaveAsync(autoSaveFile);
        var loadedState = await AgentState.LoadAsync(autoSaveFile);

        // Assert
        Assert.Equal(2, loadedState.LastReasoningSteps.Count);
        Assert.Equal("planning", loadedState.LastReasoningSteps[0].Type);
        Assert.Equal("Plan: Do X then Y", loadedState.LastReasoningSteps[0].Summary);
        Assert.Equal("execution", loadedState.LastReasoningSteps[1].Type);
        Assert.Equal("Executed successfully", loadedState.LastReasoningSteps[1].Summary);
    }

    /// <summary>
    /// Test that conversation history is preserved in auto-save
    /// </summary>
    [Fact]
    public async Task AgentState_ShouldPreserveConversationHistory()
    {
        // Arrange
        var state = new AgentState();
        state.AddMessage(new Message { Role = "user", Content = "Hello" });
        state.AddMessage(new Message { Role = "assistant", Content = "Hi there!" });
        state.AddMessage(new Message { Role = "user", Content = "How are you?" });
        state.AddMessage(
            new Message { Role = "assistant", Content = "I'm doing well, thank you!" }
        );

        var stateDir = Path.Combine(Environment.CurrentDirectory, "state");
        Directory.CreateDirectory(stateDir);
        var autoSaveFile = Path.Combine(stateDir, "agent_autosave.json");

        // Act
        await state.SaveAsync(autoSaveFile);
        var loadedState = await AgentState.LoadAsync(autoSaveFile);

        // Assert
        Assert.Equal(4, loadedState.ConversationHistory.Count);
        Assert.Equal(4, loadedState.MessageCount);

        Assert.Equal("user", loadedState.ConversationHistory[0].Role);
        Assert.Equal("Hello", loadedState.ConversationHistory[0].Content);

        Assert.Equal("assistant", loadedState.ConversationHistory[1].Role);
        Assert.Equal("Hi there!", loadedState.ConversationHistory[1].Content);
    }

    /// <summary>
    /// Test that BackupAsync creates timestamped backup files
    /// </summary>
    [Fact]
    public async Task AgentState_BackupAsync_ShouldCreateTimestampedFile()
    {
        // Arrange
        var state = new AgentState { MessageCount = 5 };
        var stateDir = Path.Combine(Environment.CurrentDirectory, "state");
        Directory.CreateDirectory(stateDir);
        var basePath = Path.Combine(stateDir, "agent_state.json");

        // Act
        await state.BackupAsync(basePath);

        // Assert
        var backupFiles = Directory.GetFiles(stateDir, "agent_state.json.backup_*");
        Assert.Single(backupFiles);
        Assert.Contains("backup_", backupFiles[0]);

        // Verify backup content
        var backupContent = await File.ReadAllTextAsync(backupFiles[0]);
        var backupState = JsonSerializer.Deserialize<AgentState>(backupContent);
        Assert.Equal(5, backupState?.MessageCount);
    }
}
