using BastaAgent.Tools;
using BastaAgent.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

public class ToolApprovalManagerInteractiveTests
{
    private class FakeInputReader : IInputReader
    {
        private readonly Queue<string> _inputs;

        public FakeInputReader(params string[] inputs)
        {
            _inputs = new Queue<string>(inputs);
        }

        public Task<string?> WaitForInputAsync(CancellationToken cancellationToken) =>
            Task.FromResult<string?>(_inputs.Count > 0 ? _inputs.Dequeue() : string.Empty);
    }

    [Fact]
    public async Task Interactive_ApproveOnce_Y()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(
            NullLogger<ToolApprovalManager>.Instance,
            console,
            new FakeInputReader("Y")
        );
        var res = await mgr.RequestApprovalAsync("Demo.Tool", "desc", "{}", CancellationToken.None);
        Assert.True(res.Approved);
        Assert.False(res.RememberChoice);
    }

    [Fact]
    public async Task Interactive_AlwaysApprove_A()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(
            NullLogger<ToolApprovalManager>.Instance,
            console,
            new FakeInputReader("A")
        );
        var res = await mgr.RequestApprovalAsync("Demo.Tool", "desc", "{}", CancellationToken.None);
        Assert.True(res.Approved);
        Assert.True(res.RememberChoice);
        Assert.True(mgr.GetApprovalStatus("Demo.Tool")!.AlwaysApprove);
    }

    [Fact]
    public async Task Interactive_DenyOnce_N_WithReason()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(
            NullLogger<ToolApprovalManager>.Instance,
            console,
            new FakeInputReader("N", "Busy")
        );
        var res = await mgr.RequestApprovalAsync("Demo.Tool", "desc", "{}", CancellationToken.None);
        Assert.False(res.Approved);
        Assert.Equal("Busy", res.Reason);
        Assert.False(res.RememberChoice);
    }

    [Fact]
    public async Task Interactive_AlwaysDeny_D_NoReason()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(
            NullLogger<ToolApprovalManager>.Instance,
            console,
            new FakeInputReader("D", "")
        );
        var res = await mgr.RequestApprovalAsync("Demo.Tool", "desc", "{}", CancellationToken.None);
        Assert.False(res.Approved);
        Assert.True(res.RememberChoice);
        Assert.True(mgr.GetApprovalStatus("Demo.Tool")!.AlwaysDeny);
    }

    [Fact]
    public async Task Interactive_Help_ThenApprove()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(
            NullLogger<ToolApprovalManager>.Instance,
            console,
            new FakeInputReader("?", "Y")
        );
        var res = await mgr.RequestApprovalAsync(
            "FileSystem.Read",
            "read",
            "{}",
            CancellationToken.None
        );
        Assert.True(res.Approved);
    }

    [Fact]
    public async Task Interactive_InvalidChoice_ThenApprove()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        // First invalid 'X', then valid 'Y'
        var mgr = new ToolApprovalManager(
            NullLogger<ToolApprovalManager>.Instance,
            console,
            new FakeInputReader("X", "Y")
        );
        var res = await mgr.RequestApprovalAsync("Demo.Tool", "desc", "{}", CancellationToken.None);
        Assert.True(res.Approved);
    }

    [Fact]
    public async Task Interactive_AlwaysApprove_SkipsNextPrompt()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var mgr = new ToolApprovalManager(
            NullLogger<ToolApprovalManager>.Instance,
            console,
            new FakeInputReader("A")
        );
        var first = await mgr.RequestApprovalAsync(
            "Demo.Tool",
            "desc",
            "{}",
            CancellationToken.None
        );
        Assert.True(first.Approved);

        // No inputs provided for second call; should be auto-approved due to session state
        var mgr2 = new ToolApprovalManager(
            NullLogger<ToolApprovalManager>.Instance,
            console,
            new FakeInputReader()
        );
        // Copy session state by reusing the same instance would be natural, but to validate logic, re-check status
        // Simulate same manager to ensure auto-approval path is exercised
        var second = await mgr.RequestApprovalAsync(
            "Demo.Tool",
            "desc",
            "{}",
            CancellationToken.None
        );
        Assert.True(second.Approved);
        Assert.True(mgr.GetApprovalStatus("Demo.Tool")!.AlwaysApprove);
    }
}
