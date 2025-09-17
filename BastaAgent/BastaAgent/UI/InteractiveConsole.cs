using System.Collections.Concurrent;
using System.Text;
using BastaAgent.Utilities;
using Microsoft.Extensions.Logging;

namespace BastaAgent.UI;

/// <summary>
/// Interactive console UI that allows input while the model is processing
/// Supports async operations and cancellation via ESC key
/// </summary>
public class InteractiveConsole : IDisposable
{
    private readonly ILogger<InteractiveConsole> _logger;
    private readonly ConcurrentQueue<string> _inputQueue;
    private readonly CancellationTokenSource _consoleCts;
    private readonly object _outputLock = new();
    private Task? _inputTask;
    private volatile bool _isProcessing;
    private volatile bool _isDisposed;
    private int _currentLine;
    private string _currentPrompt = "You> ";
    private ConsoleColor _promptColor = ConsoleColor.Green;
    private ConsoleColor _agentColor = ConsoleColor.Cyan;
    private ConsoleColor _errorColor = ConsoleColor.Red;
    private ConsoleColor _warningColor = ConsoleColor.Yellow;
    private ConsoleColor _infoColor = ConsoleColor.Blue;
    private ProgressIndicator? _currentProgress;

    /// <summary>
    /// Event raised when user provides input
    /// </summary>
    public event EventHandler<string>? InputReceived;

    /// <summary>
    /// Event raised when user presses ESC to cancel
    /// </summary>
    public event EventHandler? CancellationRequested;

    /// <summary>
    /// Initialize the interactive console
    /// </summary>
    public InteractiveConsole(ILogger<InteractiveConsole> logger)
    {
        _logger = logger;
        _inputQueue = new ConcurrentQueue<string>();
        _consoleCts = new CancellationTokenSource();
        Console.CancelKeyPress += OnCancelKeyPress;
    }

    /// <summary>
    /// Start the interactive console
    /// </summary>
    public void Start()
    {
        if (_inputTask is not null)
        {
            _logger.LogWarning("Console already started");
            return;
        }

        _logger.LogDebug("Starting interactive console");
        _inputTask = Task.Run(() => InputLoop(_consoleCts.Token), _consoleCts.Token);
    }

    /// <summary>
    /// Stop the interactive console
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogDebug("Stopping interactive console");
        _consoleCts.Cancel();

        if (_inputTask is not null)
        {
            try
            {
                await _inputTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
        }
    }

    /// <summary>
    /// Get the next input from the queue (non-blocking)
    /// </summary>
    public string? GetNextInput()
    {
        return _inputQueue.TryDequeue(out var input) ? input : null;
    }

    /// <summary>
    /// Wait for the next input (blocking)
    /// </summary>
    public async Task<string?> WaitForInputAsync(CancellationToken cancellationToken = default)
    {
        while (
            !cancellationToken.IsCancellationRequested && !_consoleCts.Token.IsCancellationRequested
        )
        {
            if (_inputQueue.TryDequeue(out var input))
            {
                return input;
            }

            try
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                // Expected when cancellation is requested
                break;
            }
        }

        return null;
    }

    /// <summary>
    /// Clear all pending input
    /// </summary>
    public void ClearInput()
    {
        while (_inputQueue.TryDequeue(out _))
        {
            // Clear the queue
        }
    }

    /// <summary>
    /// Write output to the console (thread-safe)
    /// </summary>
    public void WriteLine(string message = "", ConsoleMessageType type = ConsoleMessageType.Normal)
    {
        lock (_outputLock)
        {
            // Save current cursor position
            var (left, top) = (Console.CursorLeft, Console.CursorTop);

            // Clear current line if we're in the middle of input
            if (_isProcessing && Console.CursorTop == _currentLine)
            {
                Console.SetCursorPosition(0, _currentLine);
                Console.Write(new string(' ', Console.WindowWidth));
                Console.SetCursorPosition(0, _currentLine);
            }

            // Set color based on message type
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = type switch
            {
                ConsoleMessageType.Agent => _agentColor,
                ConsoleMessageType.Error => _errorColor,
                ConsoleMessageType.Warning => _warningColor,
                ConsoleMessageType.Info => _infoColor,
                ConsoleMessageType.Success => ConsoleColor.Green,
                ConsoleMessageType.Reasoning => ConsoleColor.DarkGray,
                _ => originalColor,
            };

            // Write the message
            Console.WriteLine(message);

            // Reset color
            Console.ForegroundColor = originalColor;

            // Update current line
            _currentLine = Console.CursorTop;

            // Restore prompt if we're still accepting input
            if (!_isProcessing)
            {
                ShowPrompt();
            }
        }
    }

    /// <summary>
    /// Write partial output without newline (for streaming)
    /// </summary>
    public void Write(string message, ConsoleMessageType type = ConsoleMessageType.Normal)
    {
        lock (_outputLock)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = type switch
            {
                ConsoleMessageType.Agent => _agentColor,
                ConsoleMessageType.Error => _errorColor,
                ConsoleMessageType.Warning => _warningColor,
                ConsoleMessageType.Info => _infoColor,
                ConsoleMessageType.Success => ConsoleColor.Green,
                ConsoleMessageType.Reasoning => ConsoleColor.DarkGray,
                _ => originalColor,
            };

            Console.Write(message);
            Console.ForegroundColor = originalColor;
        }
    }

    /// <summary>
    /// Show a simple progress message (deprecated - use ShowProgress with ProgressIndicator instead)
    /// </summary>
    public void ShowSimpleProgress(string message, int percentage = -1)
    {
        lock (_outputLock)
        {
            Console.SetCursorPosition(0, _currentLine);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, _currentLine);

            if (percentage >= 0)
            {
                Console.Write($"[{percentage, 3}%] {message}");
            }
            else
            {
                Console.Write($"⏳ {message}");
            }
        }
    }

    /// <summary>
    /// Clear the progress indicator
    /// </summary>
    public void ClearProgress()
    {
        lock (_outputLock)
        {
            Console.SetCursorPosition(0, _currentLine);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, _currentLine);
        }
    }

    /// <summary>
    /// Set whether the agent is currently processing
    /// </summary>
    public void SetProcessing(bool isProcessing)
    {
        _isProcessing = isProcessing;

        if (!isProcessing)
        {
            lock (_outputLock)
            {
                ShowPrompt();
            }
        }
    }

    /// <summary>
    /// Show the input prompt
    /// </summary>
    private void ShowPrompt()
    {
        Console.ForegroundColor = _promptColor;
        Console.Write(_currentPrompt);
        Console.ResetColor();
    }

    /// <summary>
    /// Main input loop that runs in background
    /// </summary>
    private async Task InputLoop(CancellationToken cancellationToken)
    {
        var inputBuffer = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Check for available key
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(intercept: true);

                    // Handle ESC for cancellation
                    if (key.Key == ConsoleKey.Escape)
                    {
                        _logger.LogDebug("ESC pressed - requesting cancellation");
                        CancellationRequested?.Invoke(this, EventArgs.Empty);

                        lock (_outputLock)
                        {
                            Console.WriteLine();
                            WriteLine("⚠️ Cancellation requested...", ConsoleMessageType.Warning);
                        }
                        continue;
                    }

                    // Handle Enter to submit input
                    if (key.Key == ConsoleKey.Enter)
                    {
                        var input = inputBuffer.ToString();
                        inputBuffer.Clear();

                        if (!string.IsNullOrWhiteSpace(input))
                        {
                            _inputQueue.Enqueue(input);
                            InputReceived?.Invoke(this, input);

                            lock (_outputLock)
                            {
                                // Move to next line after input
                                Console.WriteLine();
                                _currentLine = Console.CursorTop;
                            }
                        }
                        else
                        {
                            lock (_outputLock)
                            {
                                Console.WriteLine();
                                ShowPrompt();
                            }
                        }
                        continue;
                    }

                    // Handle Backspace
                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (inputBuffer.Length > 0)
                        {
                            inputBuffer.Length--;
                            lock (_outputLock)
                            {
                                // Move cursor back, write space, move back again
                                Console.Write("\b \b");
                            }
                        }
                        continue;
                    }

                    // Handle regular character input
                    if (!char.IsControl(key.KeyChar))
                    {
                        inputBuffer.Append(key.KeyChar);
                        lock (_outputLock)
                        {
                            Console.Write(key.KeyChar);
                        }
                    }
                }
                else
                {
                    // Small delay to prevent CPU spinning
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in input loop");
            }
        }

        _logger.LogDebug("Input loop terminated");
    }

    /// <summary>
    /// Handle Ctrl+C
    /// </summary>
    private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true; // Prevent immediate termination
        CancellationRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Show a progress indicator for a long-running operation
    /// </summary>
    /// <param name="message">The progress message to display</param>
    /// <param name="style">The spinner style to use</param>
    /// <returns>A progress indicator that should be disposed when complete</returns>
    public ProgressIndicator ShowProgress(
        string message,
        ProgressIndicator.SpinnerStyle style = ProgressIndicator.SpinnerStyle.Dots
    )
    {
        lock (_outputLock)
        {
            // Stop any existing progress
            _currentProgress?.Dispose();

            // Create new progress indicator
            _currentProgress = new ProgressIndicator(message, style);
            return _currentProgress;
        }
    }

    /// <summary>
    /// Stop the current progress indicator
    /// </summary>
    public void StopProgress(bool success = true, string? completionMessage = null)
    {
        lock (_outputLock)
        {
            if (_currentProgress is not null)
            {
                _currentProgress.Complete(completionMessage, success);
                _currentProgress.Dispose();
                _currentProgress = null;
            }
        }
    }

    /// <summary>
    /// Run an async operation with a progress indicator
    /// </summary>
    public async Task<T> RunWithProgressAsync<T>(
        string message,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default
    )
    {
        using var progress = ShowProgress(message);
        try
        {
            var result = await operation(cancellationToken);
            progress.Complete(success: true);
            return result;
        }
        catch
        {
            progress.Complete(success: false);
            throw;
        }
    }

    /// <summary>
    /// Dispose resources
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        // Dispose current progress if any
        _currentProgress?.Dispose();
        _currentProgress = null;

        Console.CancelKeyPress -= OnCancelKeyPress;
        _consoleCts.Cancel();

        try
        {
            _inputTask?.Wait(1000);
        }
        catch (AggregateException)
        {
            // Expected when task is cancelled
        }

        _consoleCts.Dispose();
    }
}

/// <summary>
/// Types of console messages for formatting
/// </summary>
public enum ConsoleMessageType
{
    /// <summary>
    /// Normal text
    /// </summary>
    Normal,

    /// <summary>
    /// Agent response
    /// </summary>
    Agent,

    /// <summary>
    /// Error message
    /// </summary>
    Error,

    /// <summary>
    /// Warning message
    /// </summary>
    Warning,

    /// <summary>
    /// Information message
    /// </summary>
    Info,

    /// <summary>
    /// Success message
    /// </summary>
    Success,

    /// <summary>
    /// Reasoning/thinking message
    /// </summary>
    Reasoning,
}
