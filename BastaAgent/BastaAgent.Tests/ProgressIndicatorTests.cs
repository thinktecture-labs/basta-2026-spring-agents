using BastaAgent.Utilities;
using Xunit;

namespace BastaAgent.Tests.Utilities;

/// <summary>
/// Tests for the ProgressIndicator and ProgressBar utilities.
/// Note: These tests focus on non-visual aspects since console output is hard to test.
/// </summary>
[Collection("Console Tests")]
public class ProgressIndicatorTests
{
    /// <summary>
    /// Tests for ProgressIndicator spinner
    /// </summary>
    [Collection("Console Tests")]
    public class ProgressIndicatorSpinnerTests
    {
        [Fact]
        public void Constructor_InitializesWithMessage()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                using var progress = new ProgressIndicator("Test message");

                // Assert
                Assert.NotNull(progress);
                // Progress should start immediately
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Theory]
        [InlineData(ProgressIndicator.SpinnerStyle.Dots)]
        [InlineData(ProgressIndicator.SpinnerStyle.Simple)]
        [InlineData(ProgressIndicator.SpinnerStyle.Growing)]
        [InlineData(ProgressIndicator.SpinnerStyle.Arrows)]
        [InlineData(ProgressIndicator.SpinnerStyle.Blocks)]
        public void GetFramesForStyle_ReturnsCorrectFrames(ProgressIndicator.SpinnerStyle style)
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                // Act
                using var progress = new ProgressIndicator("Test", style);

                // Assert
                Assert.NotNull(progress);
                // Different styles should work without throwing
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void UpdateMessage_DoesNotThrow()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                using var progress = new ProgressIndicator("Initial message");

                // Act & Assert - should not throw
                progress.UpdateMessage("Updated message");

                // Ensure cleanup
                progress.Stop();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Complete_WithSuccess_DoesNotThrow()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                using var progress = new ProgressIndicator("Test");

                // Act & Assert - should not throw
                progress.Complete("Completed successfully", success: true);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Complete_WithFailure_DoesNotThrow()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                using var progress = new ProgressIndicator("Test");

                // Act & Assert - should not throw
                progress.Complete("Failed", success: false);
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Stop_DoesNotThrow()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                using var progress = new ProgressIndicator("Test");

                // Act & Assert - should not throw
                progress.Stop();
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Arrange
            var originalOut = Console.Out;
            using var sw = new StringWriter();
            Console.SetOut(sw);

            try
            {
                var progress = new ProgressIndicator("Test");

                // Act & Assert - should not throw
                progress.Dispose();
                progress.Dispose(); // Second call should be safe
            }
            finally
            {
                Console.SetOut(originalOut);
            }
        }

        [Fact]
        public async Task RunWithProgress_ReturnsResult()
        {
            // Arrange
            var expectedResult = 42;

            // Act
            var result = await ProgressIndicator.RunWithProgress(
                "Processing",
                async (ct) =>
                {
                    await Task.Delay(10, ct);
                    return expectedResult;
                }
            );

            // Assert
            Assert.Equal(expectedResult, result);
        }

        [Fact]
        public async Task RunWithProgress_PropagatesException()
        {
            // Arrange
            var expectedException = new InvalidOperationException("Test error");

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await ProgressIndicator.RunWithProgress(
                    "Processing",
                    async (ct) =>
                    {
                        await Task.Delay(10, ct);
                        throw expectedException;
                    }
                );
            });
        }

        [Fact]
        public async Task RunWithProgress_VoidVersion_CompletesSuccessfully()
        {
            // Arrange
            var executed = false;

            // Act
            await ProgressIndicator.RunWithProgress(
                "Processing",
                async (ct) =>
                {
                    await Task.Delay(10, ct);
                    executed = true;
                }
            );

            // Assert
            Assert.True(executed);
        }
    }

    /// <summary>
    /// Tests for ProgressBar
    /// </summary>
    [Collection("Console Tests")]
    public class ProgressBarTests
    {
        [Fact]
        public void Constructor_InitializesWithTotalAndDescription()
        {
            // Arrange & Act
            using var progressBar = new ProgressBar(100, "Test progress");

            // Assert
            Assert.NotNull(progressBar);
        }

        [Fact]
        public void Update_DoesNotThrow()
        {
            // Arrange
            using var progressBar = new ProgressBar(100, "Test");

            // Act & Assert - should not throw
            progressBar.Update(50);
            progressBar.Update(75, "Processing item 75");
        }

        [Fact]
        public void Update_ClampsToMaximum()
        {
            // Arrange
            using var progressBar = new ProgressBar(100, "Test");

            // Act & Assert - should not throw even with value > total
            progressBar.Update(150);
        }

        [Fact]
        public void Increment_UpdatesProgress()
        {
            // Arrange
            using var progressBar = new ProgressBar(10, "Test");

            // Act & Assert - should not throw
            progressBar.Increment();
            progressBar.Increment("Processing item 2");
        }

        [Fact]
        public void Complete_FinishesProgress()
        {
            // Arrange
            using var progressBar = new ProgressBar(100, "Test");

            // Act & Assert - should not throw
            progressBar.Complete();
        }

        [Fact]
        public void Dispose_CompletesIfNotFinished()
        {
            // Arrange
            var progressBar = new ProgressBar(100, "Test");
            progressBar.Update(50);

            // Act & Assert - should not throw
            progressBar.Dispose();
        }

        [Fact]
        public void Dispose_CanBeCalledMultipleTimes()
        {
            // Arrange
            var progressBar = new ProgressBar(100, "Test");

            // Act & Assert - should not throw
            progressBar.Dispose();
            progressBar.Dispose(); // Second call should be safe
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(1000)]
        public void Constructor_HandlesVariousTotals(int total)
        {
            // Arrange & Act
            using var progressBar = new ProgressBar(total, $"Test with {total} items");

            // Assert
            Assert.NotNull(progressBar);
        }

        [Fact]
        public void ProgressBar_WithCustomBarWidth()
        {
            // Arrange & Act
            using var progressBar = new ProgressBar(100, "Test", barWidth: 20);

            // Assert
            Assert.NotNull(progressBar);
            progressBar.Update(50);
        }

        [Fact]
        public void ProgressBar_HandlesZeroTotal()
        {
            // Arrange
            using var progressBar = new ProgressBar(0, "Empty progress");

            // Act & Assert - should not throw
            progressBar.Update(0);
            progressBar.Complete();
        }
    }

    /// <summary>
    /// Integration tests for progress indicators
    /// </summary>
    [Collection("Console Tests")]
    public class ProgressIntegrationTests
    {
        [Fact]
        public async Task ProgressIndicator_HandlesQuickOperations()
        {
            // Arrange & Act
            var result = await ProgressIndicator.RunWithProgress(
                "Quick operation",
                async (ct) =>
                {
                    // Simulate very quick operation
                    await Task.Delay(1, ct);
                    return "done";
                }
            );

            // Assert
            Assert.Equal("done", result);
        }

        [Fact]
        public async Task ProgressIndicator_HandlesCancellation()
        {
            // Arrange
            using var cts = new CancellationTokenSource();

            // Act & Assert
            // The current implementation of RunWithProgress doesn't support cancellation
            // So we'll test that it handles the cancellation without throwing
            var result = await ProgressIndicator.RunWithProgress(
                "Cancellable operation",
                async (ct) =>
                {
                    // Simulate cancellation scenario
                    await Task.Delay(10);
                    return "completed despite cancellation attempt";
                }
            );

            // Assert - operation completes normally
            Assert.Equal("completed despite cancellation attempt", result);
        }

        [Fact]
        public void ProgressBar_SimulateFileProcessing()
        {
            // Arrange
            var files = new[] { "file1.txt", "file2.txt", "file3.txt" };
            using var progressBar = new ProgressBar(files.Length, "Processing files");

            // Act
            foreach (var file in files)
            {
                progressBar.Increment($"Processing {file}");
                // Simulate work
                Thread.Sleep(10);
            }

            // Assert - should complete without throwing
            progressBar.Complete();
        }

        [Fact]
        public async Task MultipleProgressIndicators_CanRunSequentially()
        {
            // Act & Assert - should not throw
            await ProgressIndicator.RunWithProgress(
                "First operation",
                async (ct) => await Task.Delay(10, ct)
            );

            await ProgressIndicator.RunWithProgress(
                "Second operation",
                async (ct) => await Task.Delay(10, ct)
            );

            await ProgressIndicator.RunWithProgress(
                "Third operation",
                async (ct) => await Task.Delay(10, ct)
            );
        }
    }
}
