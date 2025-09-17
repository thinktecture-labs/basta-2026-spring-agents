using System.Text.Json.Serialization;

namespace BastaAgent.LLM.Models;

/// <summary>
/// Request model for chat completions API.
///
/// <para><b>Conference Note - OpenAI API Format:</b></para>
/// <para>This follows the OpenAI Chat Completions API format, making our agent compatible with:</para>
/// <list type="bullet">
/// <item>OpenAI (GPT-3.5, GPT-4, etc.)</item>
/// <item>Ollama (local models)</item>
/// <item>OpenRouter (multiple providers)</item>
/// <item>Any OpenAI-compatible endpoint</item>
/// </list>
///
/// <para><b>Key Concepts:</b></para>
/// <list type="bullet">
/// <item><b>Messages:</b> The conversation history sent to the model</item>
/// <item><b>Tools:</b> Functions the model can call (function calling)</item>
/// <item><b>Temperature:</b> Controls randomness (0=deterministic, 2=very random)</item>
/// <item><b>Streaming:</b> Get responses token-by-token for real-time display</item>
/// </list>
/// </summary>
public class ChatRequest
{
    /// <summary>
    /// The model to use for completion.
    /// Examples: "gpt-4", "gpt-3.5-turbo", "mistral-small3.2:24b", "claude-3-5-sonnet-20241022"
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The messages in the conversation.
    /// This includes the full conversation history: system prompts, user messages,
    /// assistant responses, and tool results. Order matters!
    /// </summary>
    [JsonPropertyName("messages")]
    public List<Message> Messages { get; set; } = [];

    /// <summary>
    /// Available tools for the model to use (function calling).
    /// Each tool describes a function the model can invoke, like reading files,
    /// making web requests, or executing commands.
    /// </summary>
    [JsonPropertyName("tools")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<Tool>? Tools { get; set; }

    /// <summary>
    /// How the model should use tools.
    /// - "auto": Model decides when to use tools
    /// - "none": Disable tool use
    /// - {"type": "function", "function": {"name": "tool_name"}}: Force specific tool
    /// </summary>
    [JsonPropertyName("tool_choice")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? ToolChoice { get; set; }

    /// <summary>
    /// Temperature for sampling (0.0 to 2.0).
    /// Lower values (0.0-0.3) = more focused and deterministic
    /// Higher values (0.7-1.0) = more creative and random
    /// Conference tip: Use low temperature for tool use, high for creative tasks
    /// </summary>
    [JsonPropertyName("temperature")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? Temperature { get; set; }

    /// <summary>
    /// Maximum tokens to generate.
    /// Limits response length. 1 token ≈ 0.75 words.
    /// Example: 1000 tokens ≈ 750 words
    /// </summary>
    [JsonPropertyName("max_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxTokens { get; set; }

    /// <summary>
    /// Whether to stream the response.
    /// When true, response comes as Server-Sent Events (SSE),
    /// allowing real-time display of generation progress.
    /// </summary>
    [JsonPropertyName("stream")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Stream { get; set; }

    /// <summary>
    /// Top-p sampling parameter
    /// </summary>
    [JsonPropertyName("top_p")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? TopP { get; set; }

    /// <summary>
    /// Frequency penalty (-2.0 to 2.0)
    /// </summary>
    [JsonPropertyName("frequency_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? FrequencyPenalty { get; set; }

    /// <summary>
    /// Presence penalty (-2.0 to 2.0)
    /// </summary>
    [JsonPropertyName("presence_penalty")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? PresencePenalty { get; set; }

    /// <summary>
    /// Stop sequences
    /// </summary>
    [JsonPropertyName("stop")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Stop { get; set; }

    /// <summary>
    /// User identifier for tracking
    /// </summary>
    [JsonPropertyName("user")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? User { get; set; }

    /// <summary>
    /// Create a simple chat request
    /// </summary>
    public static ChatRequest Create(string model, List<Message> messages)
    {
        return new ChatRequest
        {
            Model = model,
            Messages = messages,
            Temperature = 0.7,
        };
    }

    /// <summary>
    /// Create a chat request with tools
    /// </summary>
    public static ChatRequest CreateWithTools(
        string model,
        List<Message> messages,
        List<Tool> tools
    )
    {
        return new ChatRequest
        {
            Model = model,
            Messages = messages,
            Tools = tools,
            ToolChoice = "auto",
            Temperature = 0.7,
        };
    }
}
