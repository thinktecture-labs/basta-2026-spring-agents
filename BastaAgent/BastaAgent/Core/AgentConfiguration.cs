namespace BastaAgent.Core;

/// <summary>
/// Root configuration class for the agent system
/// </summary>
public class AgentConfiguration
{
    /// <summary>
    /// Configuration for different model types
    /// </summary>
    public ModelConfiguration Models { get; set; } = new();

    /// <summary>
    /// API configuration for LLM interactions
    /// </summary>
    public ApiConfiguration API { get; set; } = new();

    /// <summary>
    /// Memory management configuration
    /// </summary>
    public MemoryConfiguration Memory { get; set; } = new();

    /// <summary>
    /// User interface configuration
    /// </summary>
    public UIConfiguration UI { get; set; } = new();

    /// <summary>
    /// Tool system configuration
    /// </summary>
    public ToolConfiguration Tools { get; set; } = new();

    /// <summary>
    /// Conversation/agent behavior configuration
    /// </summary>
    public ConversationConfiguration Conversation { get; set; } = new();
}

/// <summary>
/// Configuration for model selection
/// </summary>
public class ModelConfiguration
{
    /// <summary>
    /// Model used for planning and reasoning about tasks
    /// </summary>
    public string Reasoning { get; set; } = string.Empty;

    /// <summary>
    /// Model used for executing tasks
    /// </summary>
    public string Execution { get; set; } = string.Empty;

    /// <summary>
    /// Model used for summarizing conversation history
    /// </summary>
    public string Summarization { get; set; } = string.Empty;
}

/// <summary>
/// API configuration for LLM endpoints
/// </summary>
public class ApiConfiguration
{
    /// <summary>
    /// Base URL for the OpenAI-compatible API
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// API key for authentication
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Request timeout in seconds
    /// </summary>
    public int Timeout { get; set; } = 120;

    /// <summary>
    /// Maximum number of retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Initial delay between retries in milliseconds
    /// </summary>
    public int RetryDelayMilliseconds { get; set; } = 1000;
}

/// <summary>
/// Memory management configuration
/// </summary>
public class MemoryConfiguration
{
    /// <summary>
    /// Maximum tokens to keep in memory
    /// </summary>
    public int MaxTokens { get; set; } = 8000;

    /// <summary>
    /// Threshold at which to trigger memory compaction
    /// </summary>
    public int CompactionThreshold { get; set; } = 6000;

    /// <summary>
    /// File path for persisting agent state
    /// </summary>
    public string StateFile { get; set; } = "agent_state.json";

    /// <summary>
    /// Auto-save interval in seconds
    /// </summary>
    public int AutoSaveInterval { get; set; } = 30;
}

/// <summary>
/// User interface configuration
/// </summary>
public class UIConfiguration
{
    /// <summary>
    /// Delay between streaming chunks in milliseconds
    /// </summary>
    public int StreamingDelayMilliseconds { get; set; } = 50;

    /// <summary>
    /// Whether to show token count in UI
    /// </summary>
    public bool ShowTokenCount { get; set; } = true;

    /// <summary>
    /// Whether to enable markdown formatting
    /// </summary>
    public bool EnableMarkdown { get; set; } = true;

    /// <summary>
    /// Whether to show timestamps with messages
    /// </summary>
    public bool ShowTimestamps { get; set; } = true;
}

/// <summary>
/// Tool system configuration
/// </summary>
public class ToolConfiguration
{
    /// <summary>
    /// Whether tools require user approval before execution
    /// </summary>
    public bool RequireApproval { get; set; } = true;

    /// <summary>
    /// Default timeout for tool execution in seconds
    /// </summary>
    public int DefaultTimeout { get; set; } = 30;
}

/// <summary>
/// Agent conversation behavior configuration
/// </summary>
public class ConversationConfiguration
{
    /// <summary>
    /// Maximum number of follow-up cycles after tool execution
    /// </summary>
    public int MaxFollowUps { get; set; } = 3;

    /// <summary>
    /// System message used to nudge the model when a follow-up completion returns no content
    /// </summary>
    public string? FollowUpNudgeSystemMessage { get; set; } =
        "Continue and complete the remaining requested changes. If additional file updates are required (e.g., adjusting project assignments), issue tool calls now; otherwise provide the final summary.";
}
