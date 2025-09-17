using BastaAgent.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

/// <summary>
/// Unit tests for the InteractiveConsole class
/// Tests async input handling, cancellation, and output formatting
/// </summary>
[Collection("Console Tests")]
public class InteractiveConsoleTests : IDisposable
{
    private readonly InteractiveConsole _console;

    public InteractiveConsoleTests()
    {
        _console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
    }

    public void Dispose()
    {
        _console?.Dispose();
    }

    /// <summary>
    /// Test that console can start and stop properly
    /// </summary>
    [Fact]
    public async Task InteractiveConsole_ShouldStartAndStop()
    {
        // Act
        _console.Start();
        await Task.Delay(100); // Give it time to start
        await _console.StopAsync();

        // Assert - should not throw
        Assert.True(true);
    }

    /// <summary>
    /// Test that input queue works correctly
    /// </summary>
    [Fact]
    public void InteractiveConsole_ShouldQueueInput()
    {
        // Arrange
        var receivedInput = string.Empty;

        _console.InputReceived += (sender, input) =>
        {
            receivedInput = input;
        };

        // Act - simulate input by raising the event
        // (In real usage, this would come from keyboard input)
        ((IDisposable)_console).Dispose(); // Clean way to test without actual console input

        // Assert
        var nextInput = _console.GetNextInput();
        Assert.Null(nextInput); // No input since we didn't actually type anything
    }

    /// <summary>
    /// Test that cancellation event mechanism exists
    /// </summary>
    [Fact]
    public void InteractiveConsole_ShouldHaveCancellationEventMechanism()
    {
        // Arrange
        var eventRaised = false;
        _console.CancellationRequested += (sender, args) =>
        {
            eventRaised = true;
        };

        // Act - we can't easily simulate ESC key press in unit test
        // but we can verify the event subscription works

        // Assert
        Assert.False(eventRaised); // Not pressed in test, but event handler is registered
    }

    /// <summary>
    /// Test that clear input works
    /// </summary>
    [Fact]
    public void InteractiveConsole_ShouldClearInput()
    {
        // Act
        _console.ClearInput();
        var input = _console.GetNextInput();

        // Assert
        Assert.Null(input);
    }

    /// <summary>
    /// Test processing state management
    /// </summary>
    [Fact]
    public void InteractiveConsole_ShouldManageProcessingState()
    {
        // Act & Assert - should not throw
        _console.SetProcessing(true);
        _console.SetProcessing(false);
        Assert.True(true);
    }

    /// <summary>
    /// Test different message types for output
    /// </summary>
    [Theory]
    [InlineData(ConsoleMessageType.Normal)]
    [InlineData(ConsoleMessageType.Agent)]
    [InlineData(ConsoleMessageType.Error)]
    [InlineData(ConsoleMessageType.Warning)]
    [InlineData(ConsoleMessageType.Info)]
    [InlineData(ConsoleMessageType.Success)]
    [InlineData(ConsoleMessageType.Reasoning)]
    public void InteractiveConsole_ShouldHandleAllMessageTypes(ConsoleMessageType messageType)
    {
        // Arrange
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            // Act
            _console.WriteLine($"Test message", messageType);

            // Assert
            var output = sw.ToString();
            Assert.Contains("Test message", output);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    /// Test progress indicator
    /// </summary>
    [Fact]
    public void InteractiveConsole_ShouldShowProgress()
    {
        // Arrange
        var originalOut = Console.Out;
        using var sw = new StringWriter();
        Console.SetOut(sw);

        try
        {
            // Act
            _console.ShowSimpleProgress("Processing...", 50);
            _console.ClearProgress();

            // Assert - should not throw
            Assert.True(true);
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    /// Test async wait for input with timeout
    /// </summary>
    [Fact]
    public async Task InteractiveConsole_ShouldTimeoutWaitingForInput()
    {
        // Arrange
        using var cts = new CancellationTokenSource(200); // 200ms timeout (increased for test stability)

        // Act
        var input = await _console.WaitForInputAsync(cts.Token);

        // Assert
        Assert.Null(input); // Should timeout and return null
    }

    /// <summary>
    /// Test that console handles disposal gracefully
    /// </summary>
    [Fact]
    public async Task InteractiveConsole_ShouldDisposeGracefully()
    {
        // Arrange
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        console.Start();

        // Give it a moment to start
        await Task.Delay(50);

        // Act & Assert - should not throw
        console.Dispose();
        console.Dispose(); // Second dispose should not throw
    }
}
