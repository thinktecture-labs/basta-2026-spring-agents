using System.Text.Json;
using System.Text.Json.Serialization;
using BastaAgent.LLM.Models;

namespace BastaAgent.Core;

/// <summary>
/// Manages the persistent state of the agent.
///
/// <para><b>Conference Note - State Persistence for Resilience:</b></para>
/// <para>This class demonstrates how to make an AI agent resilient to crashes and restarts.</para>
/// <para>All conversation context and decisions are saved to disk after every interaction.</para>
///
/// <para><b>What Gets Persisted:</b></para>
/// <list type="bullet">
/// <item><b>Conversation History:</b> Full message history for context</item>
/// <item><b>Tool Approvals:</b> Remember user's security decisions</item>
/// <item><b>Session Metrics:</b> Token usage, message counts, etc.</item>
/// <item><b>Reasoning Steps:</b> For debugging and analysis</item>
/// <item><b>Tool State:</b> Custom state from individual tools</item>
/// </list>
///
/// <para><b>Auto-Save Strategy:</b></para>
/// <list type="bullet">
/// <item>Save after every user message</item>
/// <item>Save after every LLM response</item>
/// <item>Save after every tool execution</item>
/// <item>Atomic writes prevent corruption</item>
/// </list>
/// </summary>
public class AgentState
{
    /// <summary>
    /// Unique session identifier
    /// </summary>
    public string SessionId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Conversation history
    /// </summary>
    public List<Message> ConversationHistory { get; set; } = [];

    /// <summary>
    /// Current plan being executed
    /// </summary>
    public string? CurrentPlan { get; set; }

    /// <summary>
    /// Tool-specific state data
    /// </summary>
    public Dictionary<string, JsonElement> ToolState { get; set; } = [];

    /// <summary>
    /// Tool approval decisions for this session
    /// </summary>
    public Dictionary<string, ToolApproval> ToolApprovals { get; set; } = [];

    /// <summary>
    /// Timestamp of last state save
    /// </summary>
    public DateTime LastSaved { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp of session start
    /// </summary>
    public DateTime SessionStarted { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Total tokens used in this session
    /// </summary>
    public int TotalTokensUsed { get; set; }

    /// <summary>
    /// Number of messages processed
    /// </summary>
    public int MessageCount { get; set; }

    /// <summary>
    /// Number of tool calls made
    /// </summary>
    public int ToolCallCount { get; set; }

    /// <summary>
    /// Last reasoning steps (for analysis and debugging)
    /// </summary>
    public List<ReasoningStep> LastReasoningSteps { get; set; } = [];

    /// <summary>
    /// Save the state to a JSON file
    /// </summary>
    /// <param name="filePath">Path to save the state file</param>
    public async Task SaveAsync(string filePath)
    {
        try
        {
            // Conference Note: Create directory if it doesn't exist
            // This ensures saves work even in new directories
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Update last saved timestamp
            LastSaved = DateTime.UtcNow;

            // Serialize with indented formatting for readability
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Converters = { new JsonStringEnumConverter() },
            };

            var json = JsonSerializer.Serialize(this, options);

            // Conference Note: Atomic write pattern - write to temp file first
            // then rename to prevent corruption if process crashes during write
            // Use unique temp file name to avoid conflicts in concurrent scenarios
            var tempFile = $"{filePath}.{Guid.NewGuid():N}.tmp";
            // Ensure directory still exists (tests may change working dir in parallel)
            var targetDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }
            try
            {
                await File.WriteAllTextAsync(tempFile, json);
            }
            catch (DirectoryNotFoundException)
            {
                // Recreate directory and retry once
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }
                await File.WriteAllTextAsync(tempFile, json);
            }

            // Move temp file to actual file (atomic operation)
            File.Move(tempFile, filePath, true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to save agent state to {filePath}", ex);
        }
    }

    /// <summary>
    /// Load state from a JSON file
    /// </summary>
    /// <param name="filePath">Path to the state file</param>
    /// <returns>Loaded agent state</returns>
    public static async Task<AgentState> LoadAsync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                // Return new state if file doesn't exist
                return new AgentState();
            }

            var json = await File.ReadAllTextAsync(filePath);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() },
            };

            var state = JsonSerializer.Deserialize<AgentState>(json, options);

            if (state is null)
            {
                throw new InvalidOperationException("Deserialized state was null");
            }

            return state;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to load agent state from {filePath}", ex);
        }
    }

    /// <summary>
    /// Create a backup of the current state
    /// </summary>
    /// <param name="filePath">Base path for the state file</param>
    public async Task BackupAsync(string filePath)
    {
        // Conference Note: Include milliseconds to ensure unique filenames
        // even when called in rapid succession during tests
        var backupPath = $"{filePath}.backup_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}";
        await SaveAsync(backupPath);
    }

    /// <summary>
    /// Reset the state to initial values
    /// </summary>
    public void Reset()
    {
        SessionId = Guid.NewGuid().ToString();
        ConversationHistory.Clear();
        CurrentPlan = null;
        ToolState.Clear();
        ToolApprovals.Clear();
        SessionStarted = DateTime.UtcNow;
        LastSaved = DateTime.UtcNow;
        TotalTokensUsed = 0;
        MessageCount = 0;
        ToolCallCount = 0;
    }

    /// <summary>
    /// Add a message to the conversation history
    /// </summary>
    /// <param name="message">Message to add</param>
    public void AddMessage(Message message)
    {
        ConversationHistory.Add(message);
        MessageCount++;
    }

    /// <summary>
    /// Get a summary of the current state
    /// </summary>
    public string GetSummary()
    {
        var duration = DateTime.UtcNow - SessionStarted;
        return $"Session: {SessionId}\n"
            + $"Duration: {duration:hh\\:mm\\:ss}\n"
            + $"Messages: {MessageCount}\n"
            + $"Tool Calls: {ToolCallCount}\n"
            + $"Tokens Used: {TotalTokensUsed:N0}";
    }
}

/// <summary>
/// Represents a tool approval decision
/// </summary>
public class ToolApproval
{
    /// <summary>
    /// Whether the tool is approved
    /// </summary>
    public bool IsApproved { get; set; }

    /// <summary>
    /// Whether approval applies to all future calls in this session
    /// </summary>
    public bool ApplyToSession { get; set; }

    /// <summary>
    /// Timestamp of the approval decision
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional feedback from user when declining
    /// </summary>
    public string? DeclineFeedback { get; set; }
}

/// <summary>
/// Represents a reasoning step taken by the agent
/// </summary>
public class ReasoningStep
{
    /// <summary>
    /// Type of reasoning step
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Full content of the reasoning
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Summary of the reasoning step
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// When the reasoning occurred
    /// </summary>
    public DateTime Timestamp { get; set; }
}
