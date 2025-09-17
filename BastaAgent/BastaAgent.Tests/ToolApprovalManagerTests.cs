using BastaAgent.Tools;
using BastaAgent.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

public class ToolApprovalManagerTests
{
    [Fact]
    public async Task RequestApproval_PreApprovedTool_BypassesPrompt()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(NullLogger<ToolApprovalManager>.Instance, console);

        mgr.PreApprove("FileSystem.Read");

        var result = await mgr.RequestApprovalAsync(
            toolName: "FileSystem.Read",
            toolDescription: "Read file",
            parameters: "{}"
        );

        Assert.True(result.Approved);
        Assert.True(result.RememberChoice);
    }

    [Fact]
    public async Task RequestApproval_AlwaysDeniedTool_ReturnsDenied()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(NullLogger<ToolApprovalManager>.Instance, console);

        mgr.PreDeny("Web.Request");

        var result = await mgr.RequestApprovalAsync(
            toolName: "Web.Request",
            toolDescription: "HTTP request",
            parameters: "{}"
        );

        Assert.False(result.Approved);
        Assert.True(result.RememberChoice);
        Assert.NotNull(result.Reason);
    }
}
