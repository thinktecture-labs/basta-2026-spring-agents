using BastaAgent.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

public class InteractiveConsoleRunWithProgressTests
{
    [Fact]
    public async Task RunWithProgressAsync_ReturnsResult_OnSuccess()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var result = await console.RunWithProgressAsync<int>(
            "work",
            async ct =>
            {
                await Task.Delay(10, ct);
                return 42;
            }
        );
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunWithProgressAsync_PropagatesException_OnFailure()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await console.RunWithProgressAsync<object>(
                "work",
                async ct =>
                {
                    await Task.Delay(5, ct);
                    throw new InvalidOperationException("x");
                }
            )
        );
    }
}
