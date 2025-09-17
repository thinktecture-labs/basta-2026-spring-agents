namespace BastaAgent.Core;

/// <summary>
/// Main interface for the AI agent system
/// </summary>
public interface IAgent
{
    /// <summary>
    /// Process a user message and generate a response
    /// </summary>
    /// <param name="userMessage">The message from the user</param>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    /// <returns>The agent's response</returns>
    Task<string> ProcessMessageAsync(
        string userMessage,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Start the agent's main loop
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the operation</param>
    Task RunAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save the current agent state to disk
    /// </summary>
    Task SaveStateAsync();

    /// <summary>
    /// Load agent state from disk
    /// </summary>
    Task LoadStateAsync();

    /// <summary>
    /// Clear the agent's memory and reset to initial state
    /// </summary>
    Task ResetAsync();

    /// <summary>
    /// Event raised when the agent starts processing
    /// </summary>
    event EventHandler<AgentEventArgs>? ProcessingStarted;

    /// <summary>
    /// Event raised when the agent completes processing
    /// </summary>
    event EventHandler<AgentEventArgs>? ProcessingCompleted;

    /// <summary>
    /// Event raised when the agent encounters an error
    /// </summary>
    event EventHandler<AgentErrorEventArgs>? ErrorOccurred;
}

/// <summary>
/// Event arguments for agent events
/// </summary>
/// <remarks>
/// Create new agent event arguments
/// </remarks>
public class AgentEventArgs(string message) : EventArgs
{
    /// <summary>
    /// The message being processed
    /// </summary>
    public string Message { get; } = message;

    /// <summary>
    /// Timestamp of the event
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}

/// <summary>
/// Event arguments for agent error events
/// </summary>
/// <remarks>
/// Create new agent error event arguments
/// </remarks>
public class AgentErrorEventArgs(string message, Exception exception) : AgentEventArgs(message)
{
    /// <summary>
    /// The exception that occurred
    /// </summary>
    public Exception Exception { get; } = exception;
}
