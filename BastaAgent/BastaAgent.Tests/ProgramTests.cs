using BastaAgent.Core;
using BastaAgent.Tools;
using BastaAgent.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

public class ProgramTests
{
    private class FakeAgent : IAgent
    {
        public bool Saved { get; private set; }
        public bool Reset { get; private set; }

        public Task<string> ProcessMessageAsync(
            string userMessage,
            CancellationToken cancellationToken = default
        )
        {
            ProcessingStarted?.Invoke(this, new AgentEventArgs(userMessage));
            ProcessingCompleted?.Invoke(this, new AgentEventArgs("processed"));
            return Task.FromResult("processed");
        }

        public Task RunAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveStateAsync()
        {
            Saved = true;
            ProcessingCompleted?.Invoke(this, new AgentEventArgs("save"));
            return Task.CompletedTask;
        }

        public Task LoadStateAsync()
        {
            // Use the error event once to satisfy analyzer
            ErrorOccurred?.Invoke(this, new AgentErrorEventArgs("load", new Exception("sim")));
            return Task.CompletedTask;
        }

        public Task ResetAsync()
        {
            Reset = true;
            ProcessingCompleted?.Invoke(this, new AgentEventArgs("reset"));
            return Task.CompletedTask;
        }

        public event EventHandler<AgentEventArgs>? ProcessingStarted;
        public event EventHandler<AgentEventArgs>? ProcessingCompleted;
        public event EventHandler<AgentErrorEventArgs>? ErrorOccurred;
    }

    [Fact]
    public async Task HandleCommand_Help_ReturnsTrue()
    {
        var agent = new FakeAgent();
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var cfg = new AgentConfiguration();
        var handled = await BastaAgent.Program.HandleCommand(
            "/help",
            agent,
            console,
            NullLogger.Instance,
            cfg,
            new ToolRegistry(NullLogger<ToolRegistry>.Instance)
        );
        Assert.True(handled);
    }

    [Fact]
    public async Task HandleCommand_SaveAndReset_InvokeAgent_ReturnsTrue()
    {
        var agent = new FakeAgent();
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var cfg = new AgentConfiguration();
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);

        var save = await BastaAgent.Program.HandleCommand(
            "/save",
            agent,
            console,
            NullLogger.Instance,
            cfg,
            registry
        );
        var reset = await BastaAgent.Program.HandleCommand(
            "/reset",
            agent,
            console,
            NullLogger.Instance,
            cfg,
            registry
        );

        Assert.True(save);
        Assert.True(reset);
        Assert.True(agent.Saved);
        Assert.True(agent.Reset);
    }

    [Fact]
    public async Task HandleCommand_Exit_ReturnsFalse()
    {
        var agent = new FakeAgent();
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var cfg = new AgentConfiguration();
        var handled = await BastaAgent.Program.HandleCommand(
            "/exit",
            agent,
            console,
            NullLogger.Instance,
            cfg,
            new ToolRegistry(NullLogger<ToolRegistry>.Instance)
        );
        Assert.False(handled);
    }

    [Fact]
    public void ShowConfig_And_ShowTools_DoNotThrow()
    {
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var cfg = new AgentConfiguration
        {
            API = new ApiConfiguration { BaseUrl = "http://localhost/", ApiKey = "none" },
            Models = new ModelConfiguration { Reasoning = "mock" },
        };
        BastaAgent.Program.ShowConfig(console, cfg);
        BastaAgent.Program.ShowTools(console, new ToolRegistry(NullLogger<ToolRegistry>.Instance));
        Assert.True(true);
    }
}
