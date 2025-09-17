using System.Text.Json.Serialization;

namespace BastaAgent.LLM.Models;

/// <summary>
/// Response model from chat completions API
/// </summary>
public class ChatResponse
{
    /// <summary>
    /// Unique identifier for the completion
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Object type (always "chat.completion" for non-streaming)
    /// </summary>
    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    /// <summary>
    /// Unix timestamp of when the completion was created
    /// </summary>
    [JsonPropertyName("created")]
    public long Created { get; set; }

    /// <summary>
    /// The model used for completion
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// List of completion choices
    /// </summary>
    [JsonPropertyName("choices")]
    public List<Choice> Choices { get; set; } = [];

    /// <summary>
    /// Token usage statistics
    /// </summary>
    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }

    /// <summary>
    /// System fingerprint (optional)
    /// </summary>
    [JsonPropertyName("system_fingerprint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SystemFingerprint { get; set; }

    /// <summary>
    /// Get the first message from the response
    /// </summary>
    public Message? GetMessage()
    {
        return Choices?.Count > 0 ? Choices[0].Message : null;
    }

    /// <summary>
    /// Check if the response contains tool calls
    /// </summary>
    public bool HasToolCalls()
    {
        var message = GetMessage();
        return message?.ToolCalls?.Count > 0;
    }
}

/// <summary>
/// Represents a completion choice
/// </summary>
public class Choice
{
    /// <summary>
    /// Index of this choice
    /// </summary>
    [JsonPropertyName("index")]
    public int Index { get; set; }

    /// <summary>
    /// The message generated
    /// </summary>
    [JsonPropertyName("message")]
    public Message? Message { get; set; }

    /// <summary>
    /// Delta for streaming responses
    /// </summary>
    [JsonPropertyName("delta")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Message? Delta { get; set; }

    /// <summary>
    /// Reason for completion finishing
    /// </summary>
    [JsonPropertyName("finish_reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FinishReason { get; set; }
}

/// <summary>
/// Token usage information
/// </summary>
public class Usage
{
    /// <summary>
    /// Number of tokens in the prompt
    /// </summary>
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    /// <summary>
    /// Number of tokens in the completion
    /// </summary>
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    /// <summary>
    /// Total tokens used (prompt + completion)
    /// </summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }

    /// <summary>
    /// Cache-related information (for providers that support caching)
    /// </summary>
    [JsonPropertyName("cache_creation_input_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CacheCreationInputTokens { get; set; }

    /// <summary>
    /// Cached tokens that were read
    /// </summary>
    [JsonPropertyName("cache_read_input_tokens")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CacheReadInputTokens { get; set; }
}

/// <summary>
/// Streaming response chunk
/// </summary>
public class StreamingChatResponse : ChatResponse
{
    /// <summary>
    /// For streaming, the object type is "chat.completion.chunk"
    /// </summary>
    public StreamingChatResponse()
    {
        Object = "chat.completion.chunk";
    }
}

/// <summary>
/// Error response from the API
/// </summary>
public class ErrorResponse
{
    /// <summary>
    /// Error details
    /// </summary>
    [JsonPropertyName("error")]
    public ErrorDetail? Error { get; set; }
}

/// <summary>
/// Error detail information
/// </summary>
public class ErrorDetail
{
    /// <summary>
    /// Error message
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Error type
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Error code
    /// </summary>
    [JsonPropertyName("code")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Code { get; set; }

    /// <summary>
    /// Parameter that caused the error
    /// </summary>
    [JsonPropertyName("param")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Param { get; set; }
}
