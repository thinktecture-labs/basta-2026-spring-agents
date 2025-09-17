using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using BastaAgent.Memory;
using BastaAgent.Tools;
using BastaAgent.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using AgentCore = BastaAgent.Agent.Agent;

namespace BastaAgent.Tests;

public class AgentErrorEventTests
{
    private class ThrowingLLM : ILLMClient
    {
        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("boom");

        public async IAsyncEnumerable<StreamingChatResponse> StreamAsync(
            ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();
            yield break;
        }

        public string GetModelForPurpose(ModelPurpose purpose) => "mock";
    }

    [Fact]
    public async Task Agent_ShouldRaiseErrorOccurred_WhenLLMThrows()
    {
        var llm = new ThrowingLLM();
        var streaming = new StreamingHandler(NullLogger<StreamingHandler>.Instance);
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        var memory = new MemoryManager(NullLogger<MemoryManager>.Instance);
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var approval = new ToolApprovalManager(NullLogger<ToolApprovalManager>.Instance, console);
        var cfg = Options.Create(
            new AgentConfiguration
            {
                Models = new ModelConfiguration { Reasoning = "mock", Execution = "mock" },
                API = new ApiConfiguration { BaseUrl = "http://localhost/", ApiKey = "none" },
            }
        );

        var agent = new AgentCore(
            NullLogger<AgentCore>.Instance,
            llm,
            streaming,
            registry,
            memory,
            approval,
            console,
            cfg
        );
        AgentErrorEventArgs? captured = null;
        agent.ErrorOccurred += (s, e) => captured = e;

        await Assert.ThrowsAsync<InvalidOperationException>(() => agent.ProcessMessageAsync("hi"));
        Assert.NotNull(captured);
        Assert.Equal("hi", captured!.Message);
        Assert.IsType<InvalidOperationException>(captured.Exception);
    }
}
