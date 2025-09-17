using System.Text.Json;
using System.Text.Json.Serialization;

namespace BastaAgent.LLM.Models;

/// <summary>
/// Represents a tool call made by the assistant
/// </summary>
public class ToolCall
{
    /// <summary>
    /// Position index within the tool_calls array (streaming deltas)
    /// </summary>
    [JsonPropertyName("index")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Index { get; set; }

    /// <summary>
    /// Unique identifier for the tool call
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Type of tool call (always "function" for now)
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    /// <summary>
    /// The function to be called
    /// </summary>
    [JsonPropertyName("function")]
    public FunctionCall? Function { get; set; }
}

/// <summary>
/// Represents a function call within a tool call
/// </summary>
public class FunctionCall
{
    /// <summary>
    /// Name of the function to call
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// JSON string of the function arguments
    /// </summary>
    [JsonPropertyName("arguments")]
    public string Arguments { get; set; } = "{}";

    /// <summary>
    /// Some providers (e.g., Anthropic tool_use) send structured input instead of string arguments
    /// </summary>
    [JsonPropertyName("input")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Input { get; set; }
}

/// <summary>
/// Tool definition for the API
/// </summary>
public class Tool
{
    /// <summary>
    /// Type of tool (always "function" for now)
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "function";

    /// <summary>
    /// Function definition
    /// </summary>
    [JsonPropertyName("function")]
    public FunctionDefinition? Function { get; set; }
}

/// <summary>
/// Function definition for tools
/// </summary>
public class FunctionDefinition
{
    /// <summary>
    /// Name of the function
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what the function does
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// JSON schema for the function parameters
    /// </summary>
    [JsonPropertyName("parameters")]
    public object? Parameters { get; set; }

    /// <summary>
    /// Whether the function requires confirmation (extension)
    /// </summary>
    [JsonIgnore]
    public bool RequiresConfirmation { get; set; } = true;
}
