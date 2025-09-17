namespace BastaAgent.Tools;

/// <summary>
/// Attribute to mark a class as a tool that can be discovered by the agent
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class ToolAttribute : Attribute
{
    /// <summary>
    /// Category of the tool (e.g., "FileSystem", "Web", "Math")
    /// </summary>
    public string Category { get; set; } = "General";

    /// <summary>
    /// Whether this tool requires user approval before execution
    /// </summary>
    public bool RequiresApproval { get; set; } = false;

    /// <summary>
    /// Priority for tool selection (higher = preferred)
    /// </summary>
    public int Priority { get; set; } = 0;

    /// <summary>
    /// Timeout for tool execution in seconds (0 = use default)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 0;
}

/// <summary>
/// Attribute to mark tool parameters
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public class ToolParameterAttribute : Attribute
{
    /// <summary>
    /// Description of the parameter
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Whether this parameter is required
    /// </summary>
    public bool Required { get; set; } = false;

    /// <summary>
    /// Example value for the parameter
    /// </summary>
    public string? Example { get; set; }
}
