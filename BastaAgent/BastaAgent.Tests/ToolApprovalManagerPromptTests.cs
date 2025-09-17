using BastaAgent.Tools;
using BastaAgent.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

public class ToolApprovalManagerPromptTests
{
    private class FakeInputReader : BastaAgent.Tools.IInputReader
    {
        private readonly Queue<string> _inputs;

        public FakeInputReader(IEnumerable<string> inputs)
        {
            _inputs = new Queue<string>(inputs);
        }

        public Task<string?> WaitForInputAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(_inputs.Count > 0 ? _inputs.Dequeue() : string.Empty);
    }

    [Fact]
    public async Task RequestApproval_CancelledDuringPrompt_ReturnsCancelled()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(
            NullLogger<ToolApprovalManager>.Instance,
            console,
            new FakeInputReader(Array.Empty<string>())
        );

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await mgr.RequestApprovalAsync(
            toolName: "Demo.Tool",
            toolDescription: "desc",
            parameters: "{ }",
            cancellationToken: cts.Token
        );

        Assert.False(result.Approved);
        Assert.Equal("Approval cancelled", result.Reason);
        Assert.False(result.RememberChoice);
    }

    [Fact]
    public void GetApprovedAndDeniedTools_AfterSessionDecisions_ReturnsSets()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(
            NullLogger<ToolApprovalManager>.Instance,
            console,
            new FakeInputReader(Array.Empty<string>())
        );

        mgr.ApproveForSession("A");
        mgr.DenyForSession("B", "reason");

        var approved = mgr.GetApprovedTools().ToList();
        var denied = mgr.GetDeniedTools().ToList();

        Assert.Contains("A", approved);
        Assert.Contains("B", denied);
        Assert.Null(mgr.GetApprovalStatus("Z"));
    }
}
