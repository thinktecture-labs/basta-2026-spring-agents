using System.Diagnostics;

namespace BastaAgent.Utilities;

/// <summary>
/// Provides progress indication for long-running operations in the console.
///
/// <para><b>Conference Note - User Experience Patterns:</b></para>
/// <para>This class demonstrates important UX principles for CLI applications:</para>
/// <list type="bullet">
/// <item><b>Visual Feedback:</b> Users need to know the system is working</item>
/// <item><b>Non-Blocking:</b> Progress updates shouldn't interfere with operations</item>
/// <item><b>Contextual Information:</b> Show what's happening, not just a spinner</item>
/// <item><b>Graceful Degradation:</b> Handle console limitations elegantly</item>
/// </list>
/// </summary>
public class ProgressIndicator : IDisposable
{
    private readonly string _message;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly Task _animationTask;
    private readonly int _startColumn;
    private readonly int _startRow;
    private readonly Stopwatch _stopwatch;
    private readonly bool _showElapsedTime;
    private readonly string[] _frames;
    private bool _disposed;

    /// <summary>
    /// Different spinner animation styles
    /// </summary>
    public enum SpinnerStyle
    {
        /// <summary>
        /// Classic rotating dots: ⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏
        /// </summary>
        Dots,

        /// <summary>
        /// Simple ASCII spinner: | / - \
        /// </summary>
        Simple,

        /// <summary>
        /// Growing dots: .  .. ...
        /// </summary>
        Growing,

        /// <summary>
        /// Arrows: ← ↖ ↑ ↗ → ↘ ↓ ↙
        /// </summary>
        Arrows,

        /// <summary>
        /// Loading blocks: ▁ ▃ ▄ ▅ ▆ ▇ █ ▇ ▆ ▅ ▄ ▃
        /// </summary>
        Blocks,
    }

    /// <summary>
    /// Start a new progress indicator
    /// </summary>
    /// <param name="message">The message to display</param>
    /// <param name="style">The spinner animation style</param>
    /// <param name="showElapsedTime">Whether to show elapsed time</param>
    public ProgressIndicator(
        string message,
        SpinnerStyle style = SpinnerStyle.Dots,
        bool showElapsedTime = true
    )
    {
        _message = message;
        _showElapsedTime = showElapsedTime;
        _stopwatch = Stopwatch.StartNew();
        _frames = GetFramesForStyle(style);

        // Capture current cursor position
        if (Console.IsOutputRedirected)
        {
            // If output is redirected (e.g., to a file), just print the message once
            Console.WriteLine($"[WORKING] {_message}");
            _cancellationTokenSource = new CancellationTokenSource();
            _animationTask = Task.CompletedTask;
            _startColumn = 0;
            _startRow = 0;
            return;
        }

        try
        {
            _startColumn = Console.CursorLeft;
            _startRow = Console.CursorTop;
        }
        catch
        {
            // Fallback if cursor position can't be determined
            _startColumn = 0;
            _startRow = 0;
        }

        _cancellationTokenSource = new CancellationTokenSource();
        _animationTask = RunAnimation(_cancellationTokenSource.Token);
    }

    /// <summary>
    /// Get animation frames for the specified style
    /// </summary>
    private static string[] GetFramesForStyle(SpinnerStyle style) =>
        style switch
        {
            SpinnerStyle.Dots => new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" },
            SpinnerStyle.Simple => new[] { "|", "/", "-", "\\" },
            SpinnerStyle.Growing => new[] { ".  ", ".. ", "..." },
            SpinnerStyle.Arrows => new[] { "←", "↖", "↑", "↗", "→", "↘", "↓", "↙" },
            SpinnerStyle.Blocks => new[]
            {
                "▁",
                "▃",
                "▄",
                "▅",
                "▆",
                "▇",
                "█",
                "▇",
                "▆",
                "▅",
                "▄",
                "▃",
            },
            _ => new[] { ".", "..", "..." },
        };

    /// <summary>
    /// Run the spinner animation
    /// </summary>
    private async Task RunAnimation(CancellationToken cancellationToken)
    {
        int frameIndex = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Move cursor to start position
                Console.SetCursorPosition(_startColumn, _startRow);

                // Build the display string
                var display = $"{_frames[frameIndex]} {_message}";

                if (_showElapsedTime)
                {
                    var elapsed = _stopwatch.Elapsed;
                    var timeStr =
                        elapsed.TotalSeconds < 60
                            ? $"{elapsed.TotalSeconds:F1}s"
                            : $"{elapsed.Minutes}m {elapsed.Seconds}s";
                    display += $" ({timeStr})";
                }

                // Clear the line and write the new content
                Console.Write(display.PadRight(Console.WindowWidth - 1));

                frameIndex = (frameIndex + 1) % _frames.Length;
                await Task.Delay(100, cancellationToken);
            }
            catch (Exception)
            {
                // Ignore exceptions in animation (e.g., console resizing)
                break;
            }
        }
    }

    /// <summary>
    /// Update the progress message
    /// </summary>
    public void UpdateMessage(string newMessage)
    {
        lock (this)
        {
            if (!_disposed)
            {
                // Clear current line
                try
                {
                    Console.SetCursorPosition(_startColumn, _startRow);
                    Console.Write(new string(' ', Console.WindowWidth - 1));
                }
                catch
                {
                    // Ignore if we can't update
                }
            }
        }
    }

    /// <summary>
    /// Complete the progress indicator with a final message
    /// </summary>
    public void Complete(string? completionMessage = null, bool success = true)
    {
        if (_disposed)
            return;

        Stop();

        if (Console.IsOutputRedirected)
        {
            var status = success ? "DONE" : "FAILED";
            Console.WriteLine($"[{status}] {completionMessage ?? _message}");
            return;
        }

        try
        {
            Console.SetCursorPosition(_startColumn, _startRow);

            var icon = success ? "✓" : "✗";
            var color = success ? ConsoleColor.Green : ConsoleColor.Red;
            var elapsed = _stopwatch.Elapsed;
            var timeStr =
                elapsed.TotalSeconds < 60
                    ? $"{elapsed.TotalSeconds:F1}s"
                    : $"{elapsed.Minutes}m {elapsed.Seconds}s";

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.Write(icon);
            Console.ForegroundColor = originalColor;

            var message = completionMessage ?? _message;
            Console.WriteLine($" {message} ({timeStr})".PadRight(Console.WindowWidth - 2));
        }
        catch
        {
            // Fallback to simple output
            Console.WriteLine($"{(success ? "✓" : "✗")} {completionMessage ?? _message}");
        }
    }

    /// <summary>
    /// Stop the progress indicator without a completion message
    /// </summary>
    public void Stop()
    {
        if (_disposed)
            return;

        _cancellationTokenSource.Cancel();
        try
        {
            _animationTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore cancellation exceptions
        }
        _stopwatch.Stop();
    }

    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        Stop();
        _cancellationTokenSource.Dispose();
        _disposed = true;
    }

    /// <summary>
    /// Create a simple progress indicator for a task
    /// </summary>
    public static async Task<T> RunWithProgress<T>(
        string message,
        Func<CancellationToken, Task<T>> operation,
        SpinnerStyle style = SpinnerStyle.Dots
    )
    {
        using var progress = new ProgressIndicator(message, style);
        try
        {
            var result = await operation(CancellationToken.None);
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
    /// Create a simple progress indicator for a void task
    /// </summary>
    public static async Task RunWithProgress(
        string message,
        Func<CancellationToken, Task> operation,
        SpinnerStyle style = SpinnerStyle.Dots
    )
    {
        using var progress = new ProgressIndicator(message, style);
        try
        {
            await operation(CancellationToken.None);
            progress.Complete(success: true);
        }
        catch
        {
            progress.Complete(success: false);
            throw;
        }
    }
}

/// <summary>
/// Provides a simpler progress bar for operations with known total count.
///
/// <para><b>Conference Note - Progress Tracking:</b></para>
/// <para>This demonstrates deterministic progress tracking for operations where we know the total work:</para>
/// <list type="bullet">
/// <item>File processing with known file count</item>
/// <item>Batch operations with fixed item count</item>
/// <item>Multi-step procedures with defined stages</item>
/// </list>
/// </summary>
public class ProgressBar : IDisposable
{
    private readonly int _total;
    private readonly string _description;
    private readonly int _barWidth;
    private readonly Stopwatch _stopwatch;
    private int _current;
    private readonly int _startRow;
    private bool _disposed;

    /// <summary>
    /// Create a new progress bar
    /// </summary>
    public ProgressBar(int total, string description = "", int barWidth = 40)
    {
        _total = total;
        _description = description;
        _barWidth = barWidth;
        _stopwatch = Stopwatch.StartNew();
        _current = 0;

        if (!Console.IsOutputRedirected)
        {
            _startRow = Console.CursorTop;
            Draw();
        }
        else
        {
            _startRow = 0;
            Console.WriteLine($"[PROGRESS] {description} (0/{total})");
        }
    }

    /// <summary>
    /// Update the progress
    /// </summary>
    public void Update(int current, string? statusMessage = null)
    {
        _current = Math.Min(current, _total);

        if (Console.IsOutputRedirected)
        {
            if (_total > 0 && (_current == _total || _current % Math.Max(1, _total / 10) == 0))
            {
                var percent = (_current * 100) / _total;
                Console.WriteLine($"[PROGRESS] {_description} ({_current}/{_total}) {percent}%");
            }
            else if (_total == 0)
            {
                Console.WriteLine($"[PROGRESS] {_description} (0/0) 0%");
            }
            return;
        }

        Draw(statusMessage);
    }

    /// <summary>
    /// Increment the progress by one
    /// </summary>
    public void Increment(string? statusMessage = null)
    {
        Update(_current + 1, statusMessage);
    }

    /// <summary>
    /// Draw the progress bar
    /// </summary>
    private void Draw(string? statusMessage = null)
    {
        try
        {
            Console.SetCursorPosition(0, _startRow);

            var percent = _total > 0 ? (_current * 100.0) / _total : 0;
            var filled = (int)((percent / 100.0) * _barWidth);

            // Build the bar
            var bar = new string('█', filled) + new string('░', _barWidth - filled);

            // Calculate ETA
            var elapsed = _stopwatch.Elapsed;
            var eta = TimeSpan.Zero;
            if (_current > 0 && _current < _total)
            {
                var rate = elapsed.TotalSeconds / _current;
                var remaining = (_total - _current) * rate;
                eta = TimeSpan.FromSeconds(remaining);
            }

            // Format the output
            var line = $"{_description} [{bar}] {percent:F1}% ({_current}/{_total})";

            if (_current < _total && eta.TotalSeconds > 0)
            {
                var etaStr =
                    eta.TotalSeconds < 60
                        ? $"{eta.TotalSeconds:F0}s"
                        : $"{eta.Minutes}m {eta.Seconds}s";
                line += $" ETA: {etaStr}";
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                line += $" - {statusMessage}";
            }

            Console.Write(line.PadRight(Console.WindowWidth - 1));

            if (_current >= _total)
            {
                var totalTime =
                    elapsed.TotalSeconds < 60
                        ? $"{elapsed.TotalSeconds:F1}s"
                        : $"{elapsed.Minutes}m {elapsed.Seconds}s";
                Console.WriteLine($"\n✓ Completed in {totalTime}");
            }
        }
        catch
        {
            // Ignore drawing errors
        }
    }

    /// <summary>
    /// Complete the progress bar
    /// </summary>
    public void Complete()
    {
        Update(_total);
    }

    /// <summary>
    /// Dispose of resources
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        if (_current < _total)
        {
            Complete();
        }

        _stopwatch.Stop();
        _disposed = true;
    }
}
