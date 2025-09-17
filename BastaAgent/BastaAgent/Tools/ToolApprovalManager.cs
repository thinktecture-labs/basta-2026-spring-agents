using System.Collections.Concurrent;
using System.Text.Json;
using BastaAgent.UI;
using Microsoft.Extensions.Logging;

namespace BastaAgent.Tools;

/// <summary>
/// Manages tool approval requests and per-session approval memory.
/// Allows users to approve tools once for the entire session or approve each time.
///
/// <para><b>Conference Note - Security Through User Consent:</b></para>
/// <para>This is a critical security component that ensures the AI agent never performs</para>
/// <para>actions without explicit user permission. Key features:</para>
/// <list type="bullet">
/// <item><b>Per-Tool Approval:</b> Each tool call requires user consent</item>
/// <item><b>Session Memory:</b> Users can approve "always for this session"</item>
/// <item><b>Denial with Feedback:</b> Users can deny and explain why</item>
/// <item><b>Parameter Visibility:</b> Users see exactly what the tool will do</item>
/// </list>
///
/// <para><b>Three-Tier Approval System:</b></para>
/// <list type="number">
/// <item><b>Always Approved:</b> Safe tools that never need approval</item>
/// <item><b>Session Approved:</b> User approved for current session</item>
/// <item><b>Per-Call Approval:</b> Requires approval every time</item>
/// </list>
///
/// <para><b>Security Best Practice:</b></para>
/// <para>Never let an AI system perform actions without human oversight!</para>
/// </summary>
/// <remarks>
/// Initialize the tool approval manager
/// </remarks>
public class ToolApprovalManager(ILogger<ToolApprovalManager> logger, InteractiveConsole console)
    : IToolApprovalManager
{
    private readonly ILogger<ToolApprovalManager> _logger = logger;
    private readonly InteractiveConsole _console = console;
    private readonly IInputReader _inputReader = new ConsoleInputReader(console);

    // Track approvals for this session
    private readonly ConcurrentDictionary<string, ApprovalStatus> _sessionApprovals = new(
        StringComparer.OrdinalIgnoreCase
    );

    // Track tools that are always approved (no prompt needed)
    private readonly ConcurrentDictionary<string, bool> _alwaysApprovedTools = new(
        StringComparer.OrdinalIgnoreCase
    );

    // Track tools that are always denied
    private readonly ConcurrentDictionary<string, bool> _alwaysDeniedTools = new(
        StringComparer.OrdinalIgnoreCase
    );

    // Internal overload for tests to inject input reader
    internal ToolApprovalManager(
        ILogger<ToolApprovalManager> logger,
        InteractiveConsole console,
        IInputReader inputReader
    )
        : this(logger, console)
    {
        _inputReader = inputReader;
    }

    /// <summary>
    /// Request approval to execute a tool
    /// </summary>
    public async Task<ToolApprovalResult> RequestApprovalAsync(
        string toolName,
        string toolDescription,
        string parameters,
        CancellationToken cancellationToken = default
    )
    {
        // Check if tool is always denied
        if (_alwaysDeniedTools.ContainsKey(toolName))
        {
            _logger.LogInformation("Tool {ToolName} is always denied", toolName);
            return new ToolApprovalResult
            {
                Approved = false,
                Reason = "Tool is configured to always be denied",
                RememberChoice = true,
            };
        }

        // Check if tool is always approved
        if (_alwaysApprovedTools.ContainsKey(toolName))
        {
            _logger.LogDebug("Tool {ToolName} is always approved", toolName);
            return new ToolApprovalResult { Approved = true, RememberChoice = true };
        }

        // Check session approvals
        if (_sessionApprovals.TryGetValue(toolName, out var status))
        {
            if (status.AlwaysApprove)
            {
                _logger.LogDebug("Tool {ToolName} is approved for this session", toolName);
                return new ToolApprovalResult { Approved = true, RememberChoice = true };
            }
            else if (status.AlwaysDeny)
            {
                _logger.LogDebug("Tool {ToolName} is denied for this session", toolName);
                return new ToolApprovalResult
                {
                    Approved = false,
                    Reason = status.DenyReason ?? "Denied by user for this session",
                    RememberChoice = true,
                };
            }
        }

        // Need to ask user for approval
        return await PromptUserForApprovalAsync(
            toolName,
            toolDescription,
            parameters,
            cancellationToken
        );
    }

    /// <summary>
    /// Prompt the user for tool approval
    /// </summary>
    private async Task<ToolApprovalResult> PromptUserForApprovalAsync(
        string toolName,
        string toolDescription,
        string parameters,
        CancellationToken cancellationToken
    )
    {
        // Display tool information
        _console.WriteLine();
        _console.WriteLine(
            "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━",
            ConsoleMessageType.Warning
        );
        _console.WriteLine("🔧 Tool Approval Required", ConsoleMessageType.Warning);
        _console.WriteLine(
            "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━",
            ConsoleMessageType.Warning
        );
        _console.WriteLine($"Tool: {toolName}", ConsoleMessageType.Info);
        _console.WriteLine($"Description: {toolDescription}", ConsoleMessageType.Info);

        // Format and display parameters (robust to OpenAI-style JSON-in-string and streaming fragments)
        _console.WriteLine("Parameters:", ConsoleMessageType.Info);
        var paramText = (parameters ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(paramText))
        {
            _console.WriteLine("  (none)");
        }
        else
        {
            // Try parse as JSON object/array
            if (TryPrettyJson(paramText, out var pretty))
            {
                foreach (var line in pretty.Split('\n'))
                    _console.WriteLine($"  {line}");
            }
            else
            {
                // Some providers stream arguments as a JSON string containing JSON
                if (
                    TryDeserializeJsonString(paramText, out var inner)
                    && TryPrettyJson(inner, out var prettyInner)
                )
                {
                    foreach (var line in prettyInner.Split('\n'))
                        _console.WriteLine($"  {line}");
                }
                else
                {
                    // Fallback: raw text
                    foreach (var line in paramText.Split('\n'))
                        _console.WriteLine($"  {line}");
                }
            }
        }

        _console.WriteLine();
        _console.WriteLine("Choose an option:", ConsoleMessageType.Warning);
        _console.WriteLine("  [Y] Approve once");
        _console.WriteLine("  [A] Always approve this tool (this session)");
        _console.WriteLine("  [N] Deny once");
        _console.WriteLine("  [D] Always deny this tool (this session)");
        _console.WriteLine("  [?] More information");
        _console.Write("Your choice (Y/A/N/D/?): ", ConsoleMessageType.Warning);

        // Wait for user input
        while (!cancellationToken.IsCancellationRequested)
        {
            var input = await _inputReader.WaitForInputAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(input))
                continue;

            var choice = input.Trim().ToUpperInvariant();

            switch (choice)
            {
                case "Y":
                case "YES":
                    _logger.LogInformation("User approved tool {ToolName} (once)", toolName);
                    return new ToolApprovalResult { Approved = true, RememberChoice = false };

                case "A":
                case "ALWAYS":
                    _logger.LogInformation(
                        "User approved tool {ToolName} (always for session)",
                        toolName
                    );
                    _sessionApprovals[toolName] = new ApprovalStatus
                    {
                        AlwaysApprove = true,
                        Timestamp = DateTime.UtcNow,
                    };
                    return new ToolApprovalResult { Approved = true, RememberChoice = true };

                case "N":
                case "NO":
                    _logger.LogInformation("User denied tool {ToolName} (once)", toolName);

                    // Ask for reason
                    _console.Write("Reason for denial (optional, press Enter to skip): ");
                    var reason = await _inputReader.WaitForInputAsync(cancellationToken);

                    return new ToolApprovalResult
                    {
                        Approved = false,
                        Reason = string.IsNullOrWhiteSpace(reason) ? "Denied by user" : reason,
                        RememberChoice = false,
                    };

                case "D":
                case "DENY":
                    _logger.LogInformation(
                        "User denied tool {ToolName} (always for session)",
                        toolName
                    );

                    // Ask for reason
                    _console.Write("Reason for denial (optional, press Enter to skip): ");
                    var denyReason = await _inputReader.WaitForInputAsync(cancellationToken);

                    _sessionApprovals[toolName] = new ApprovalStatus
                    {
                        AlwaysDeny = true,
                        DenyReason = denyReason,
                        Timestamp = DateTime.UtcNow,
                    };

                    return new ToolApprovalResult
                    {
                        Approved = false,
                        Reason = string.IsNullOrWhiteSpace(denyReason)
                            ? "Denied by user for session"
                            : denyReason,
                        RememberChoice = true,
                    };

                case "?":
                case "HELP":
                    ShowToolHelp(toolName);
                    _console.Write("Your choice (Y/A/N/D): ", ConsoleMessageType.Warning);
                    break;

                default:
                    _console.WriteLine(
                        "Invalid choice. Please enter Y, A, N, D, or ?",
                        ConsoleMessageType.Error
                    );
                    _console.Write("Your choice (Y/A/N/D/?): ", ConsoleMessageType.Warning);
                    break;
            }
        }

        // Cancelled
        return new ToolApprovalResult
        {
            Approved = false,
            Reason = "Approval cancelled",
            RememberChoice = false,
        };
    }

    private static bool TryPrettyJson(string text, out string pretty)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            pretty = JsonSerializer.Serialize(
                doc,
                new JsonSerializerOptions { WriteIndented = true }
            );
            return true;
        }
        catch
        {
            pretty = string.Empty;
            return false;
        }
    }

    private static bool TryDeserializeJsonString(string text, out string value)
    {
        try
        {
            value = JsonSerializer.Deserialize<string>(text) ?? string.Empty;
            return !string.IsNullOrEmpty(value);
        }
        catch
        {
            value = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Show detailed help about a tool
    /// </summary>
    private void ShowToolHelp(string toolName)
    {
        _console.WriteLine();
        _console.WriteLine("ℹ️ Tool Information:", ConsoleMessageType.Info);

        // Tool-specific help
        var help = toolName switch
        {
            "Web.Request" =>
                "Makes HTTP requests to external URLs. Use caution with sensitive endpoints.",
            "FileSystem.Write" =>
                "Writes or modifies files on your system. Review the path carefully.",
            "FileSystem.Read" => "Reads files from your system. May access sensitive data.",
            "Directory.List" =>
                "Lists directory contents. Generally safe but reveals file structure.",
            "Web.Search" => "Searches the web. Currently returns mock data for demo purposes.",
            _ => "This tool performs operations that may affect your system or data.",
        };

        _console.WriteLine(help);
        _console.WriteLine();
        _console.WriteLine("Security Tips:", ConsoleMessageType.Warning);
        _console.WriteLine("• Review parameters carefully before approving");
        _console.WriteLine("• Use 'Approve once' for sensitive operations");
        _console.WriteLine("• Use 'Always approve' only for trusted, safe tools");
        _console.WriteLine("• Provide a reason when denying to help the AI understand");
        _console.WriteLine();
    }

    /// <summary>
    /// Pre-approve a tool (no prompts will be shown)
    /// </summary>
    public void PreApprove(string toolName)
    {
        _alwaysApprovedTools.TryAdd(toolName, true);
        _alwaysDeniedTools.TryRemove(toolName, out _);
        _sessionApprovals.TryRemove(toolName, out _);
        _logger.LogInformation($"Tool {toolName} pre-approved");
    }

    /// <summary>
    /// Pre-deny a tool (will always be denied)
    /// </summary>
    public void PreDeny(string toolName)
    {
        _alwaysDeniedTools.TryAdd(toolName, true);
        _alwaysApprovedTools.TryRemove(toolName, out _);
        _sessionApprovals.TryRemove(toolName, out _);
        _logger.LogInformation($"Tool {toolName} pre-denied");
    }

    /// <summary>
    /// Clear all session approvals
    /// </summary>
    public void ClearSessionApprovals()
    {
        _sessionApprovals.Clear();
        _logger.LogInformation("Cleared all session approvals");
    }

    /// <summary>
    /// Approve a tool for the current session
    /// </summary>
    public void ApproveForSession(string toolName)
    {
        _sessionApprovals[toolName] = new ApprovalStatus { AlwaysApprove = true };
        _logger.LogInformation($"Tool {toolName} approved for session");
    }

    /// <summary>
    /// Deny a tool for the current session
    /// </summary>
    public void DenyForSession(string toolName, string reason)
    {
        _sessionApprovals[toolName] = new ApprovalStatus { AlwaysDeny = true, DenyReason = reason };
        _logger.LogInformation($"Tool {toolName} denied for session: {reason}");
    }

    /// <summary>
    /// Get current approval status for a tool
    /// </summary>
    public ApprovalStatus? GetApprovalStatus(string toolName)
    {
        if (_alwaysApprovedTools.ContainsKey(toolName))
            return new ApprovalStatus { AlwaysApprove = true };

        if (_alwaysDeniedTools.ContainsKey(toolName))
            return new ApprovalStatus { AlwaysDeny = true };

        return _sessionApprovals.TryGetValue(toolName, out var status) ? status : null;
    }

    /// <summary>
    /// Get all approved tools for this session
    /// </summary>
    public IEnumerable<string> GetApprovedTools()
    {
        var approved = new HashSet<string>(_alwaysApprovedTools.Keys);

        foreach (var kvp in _sessionApprovals)
        {
            if (kvp.Value.AlwaysApprove)
                approved.Add(kvp.Key);
        }

        return approved;
    }

    /// <summary>
    /// Get all denied tools for this session
    /// </summary>
    public IEnumerable<string> GetDeniedTools()
    {
        var denied = new HashSet<string>(_alwaysDeniedTools.Keys);

        foreach (var kvp in _sessionApprovals)
        {
            if (kvp.Value.AlwaysDeny)
                denied.Add(kvp.Key);
        }

        return denied;
    }
}

/// <summary>
/// Abstraction for reading user input (testable)
/// </summary>
internal interface IInputReader
{
    Task<string?> WaitForInputAsync(CancellationToken cancellationToken);
}

internal class ConsoleInputReader(InteractiveConsole console) : IInputReader
{
    public Task<string?> WaitForInputAsync(CancellationToken cancellationToken) =>
        console.WaitForInputAsync(cancellationToken);
}

/// <summary>
/// Interface for tool approval manager
/// </summary>
public interface IToolApprovalManager
{
    /// <summary>
    /// Request approval to execute a tool
    /// </summary>
    Task<ToolApprovalResult> RequestApprovalAsync(
        string toolName,
        string toolDescription,
        string parameters,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Pre-approve a tool
    /// </summary>
    void PreApprove(string toolName);

    /// <summary>
    /// Pre-deny a tool
    /// </summary>
    void PreDeny(string toolName);

    /// <summary>
    /// Clear all session approvals
    /// </summary>
    void ClearSessionApprovals();

    /// <summary>
    /// Get approval status for a tool
    /// </summary>
    ApprovalStatus? GetApprovalStatus(string toolName);

    /// <summary>
    /// Get all approved tools
    /// </summary>
    IEnumerable<string> GetApprovedTools();

    /// <summary>
    /// Get all denied tools
    /// </summary>
    IEnumerable<string> GetDeniedTools();

    /// <summary>
    /// Approve a tool for the current session
    /// </summary>
    void ApproveForSession(string toolName);

    /// <summary>
    /// Deny a tool for the current session
    /// </summary>
    void DenyForSession(string toolName, string reason);
}

/// <summary>
/// Result of a tool approval request
/// </summary>
public class ToolApprovalResult
{
    /// <summary>
    /// Whether the tool was approved
    /// </summary>
    public bool Approved { get; set; }

    /// <summary>
    /// Reason for denial (if not approved)
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Whether to remember this choice for the session
    /// </summary>
    public bool RememberChoice { get; set; }

    /// <summary>
    /// Optional alternative action suggested by user
    /// </summary>
    public string? AlternativeAction { get; set; }
}

/// <summary>
/// Approval status for a tool
/// </summary>
public class ApprovalStatus
{
    /// <summary>
    /// Tool is always approved for this session
    /// </summary>
    public bool AlwaysApprove { get; set; }

    /// <summary>
    /// Tool is always denied for this session
    /// </summary>
    public bool AlwaysDeny { get; set; }

    /// <summary>
    /// Reason for denial
    /// </summary>
    public string? DenyReason { get; set; }

    /// <summary>
    /// When the approval/denial was made
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Number of times this tool has been used
    /// </summary>
    public int UsageCount { get; set; }
}
