using BastaAgent.Tools;
using BastaAgent.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

public class ToolApprovalManagerSessionTests
{
    [Fact]
    public async Task RequestApproval_ApproveForSession_SkipsSubsequentPrompts()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(NullLogger<ToolApprovalManager>.Instance, console);

        mgr.ApproveForSession("Demo.Tool");

        var result1 = await mgr.RequestApprovalAsync("Demo.Tool", "desc", "{}");
        var result2 = await mgr.RequestApprovalAsync("Demo.Tool", "desc", "{}");

        Assert.True(result1.Approved);
        Assert.True(result2.Approved);
    }

    [Fact]
    public async Task RequestApproval_DenyForSession_BlocksSubsequentCalls()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(NullLogger<ToolApprovalManager>.Instance, console);

        mgr.DenyForSession("Danger.Tool", "testing");

        var result = await mgr.RequestApprovalAsync("Danger.Tool", "desc", "{}");

        Assert.False(result.Approved);
        Assert.True(result.RememberChoice);
        Assert.NotNull(result.Reason);
    }
}
