namespace BastaAgent.Tools;

/// <summary>
/// Base interface for all agent tools
/// Tools are discovered via reflection and can be executed by the agent
/// </summary>
public interface ITool
{
    /// <summary>
    /// Name of the tool (used for identification in prompts)
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Description of what the tool does (included in system prompts)
    /// </summary>
    string Description { get; }

    /// <summary>
    /// JSON schema describing the tool's parameters
    /// This is used to generate the function definition for the LLM
    /// </summary>
    string ParametersSchema { get; }

    /// <summary>
    /// Execute the tool with the given parameters
    /// </summary>
    /// <param name="parameters">JSON string containing the tool parameters</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>Result of the tool execution as a string</returns>
    Task<ToolResult> ExecuteAsync(string parameters, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result from tool execution
/// </summary>
public class ToolResult
{
    /// <summary>
    /// Whether the tool execution was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// The result content (on success) or error message (on failure)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Optional metadata about the execution
    /// </summary>
    public Dictionary<string, object>? Metadata { get; set; }

    /// <summary>
    /// Create a successful result
    /// </summary>
    public static ToolResult Ok(string content, Dictionary<string, object>? metadata = null)
    {
        return new ToolResult
        {
            Success = true,
            Content = content,
            Metadata = metadata,
        };
    }

    /// <summary>
    /// Create a failure result
    /// </summary>
    public static ToolResult Error(string error)
    {
        return new ToolResult { Success = false, Content = error };
    }
}
