using System.Text.Json.Serialization;

namespace BastaAgent.LLM.Models;

/// <summary>
/// Represents a message in a conversation
/// </summary>
public class Message
{
    /// <summary>
    /// Role of the message sender (system, user, assistant, tool)
    /// </summary>
    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Content of the message
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// Name of the sender (optional, used for tool messages)
    /// </summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    /// <summary>
    /// Tool calls made by the assistant (optional)
    /// </summary>
    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<ToolCall>? ToolCalls { get; set; }

    /// <summary>
    /// ID of the tool call this message is responding to (for tool role)
    /// </summary>
    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; set; }

    /// <summary>
    /// Cache control for prompt caching (Anthropic Claude models)
    /// </summary>
    [JsonPropertyName("cache_control")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CacheControl? CacheControl { get; set; }

    /// <summary>
    /// Timestamp when the message was created
    /// </summary>
    [JsonIgnore]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Estimated token count for this message
    /// </summary>
    [JsonIgnore]
    public int TokenCount { get; set; }

    /// <summary>
    /// Create a system message
    /// </summary>
    public static Message System(string content)
    {
        return new Message { Role = "system", Content = content };
    }

    /// <summary>
    /// Create a user message
    /// </summary>
    public static Message User(string content)
    {
        return new Message { Role = "user", Content = content };
    }

    /// <summary>
    /// Create an assistant message
    /// </summary>
    public static Message Assistant(string content)
    {
        return new Message { Role = "assistant", Content = content };
    }

    /// <summary>
    /// Create a tool response message
    /// </summary>
    public static Message Tool(string content, string toolCallId)
    {
        return new Message
        {
            Role = "tool",
            Content = content,
            ToolCallId = toolCallId,
        };
    }

    /// <summary>
    /// Estimate the token count for this message
    /// Simple approximation: 1 token ≈ 4 characters
    /// </summary>
    public void EstimateTokens()
    {
        int count = 0;

        // Count content tokens
        if (!string.IsNullOrEmpty(Content))
        {
            count += Content.Length / 4;
        }

        // Count tool call tokens
        if (ToolCalls is not null)
        {
            foreach (var call in ToolCalls)
            {
                if (call.Function?.Arguments is not null)
                {
                    count += call.Function.Arguments.Length / 4;
                }
                count += 10; // Overhead for structure
            }
        }

        // Add overhead for role and structure
        count += 5;

        TokenCount = count;
    }

    /// <summary>
    /// Clone this message
    /// </summary>
    public Message Clone()
    {
        return new Message
        {
            Role = Role,
            Content = Content,
            Name = Name,
            ToolCalls = ToolCalls,
            ToolCallId = ToolCallId,
            CacheControl = CacheControl is not null
                ? new CacheControl { Type = CacheControl.Type }
                : null,
            Timestamp = Timestamp,
            TokenCount = TokenCount,
        };
    }
}

/// <summary>
/// Message roles
/// </summary>
public static class MessageRole
{
    public const string System = "system";
    public const string User = "user";
    public const string Assistant = "assistant";
    public const string Tool = "tool";
}

/// <summary>
/// Cache control for prompt caching (Anthropic API)
/// </summary>
public class CacheControl
{
    /// <summary>
    /// Type of cache control (e.g., "ephemeral")
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "ephemeral";
}
