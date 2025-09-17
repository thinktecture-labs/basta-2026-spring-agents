using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.Memory;
using BastaAgent.Tools;
using BastaAgent.UI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using AgentCore = BastaAgent.Agent.Agent;

namespace BastaAgent.Tests;

public class AgentResetTests
{
    private class NoopLLM : ILLMClient
    {
        public Task<BastaAgent.LLM.Models.ChatResponse> CompleteAsync(
            BastaAgent.LLM.Models.ChatRequest request,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new BastaAgent.LLM.Models.ChatResponse());

        public async IAsyncEnumerable<BastaAgent.LLM.Models.StreamingChatResponse> StreamAsync(
            BastaAgent.LLM.Models.ChatRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default
        )
        {
            // Ensure this async iterator contains an await to avoid CS1998
            await Task.Yield();
            yield break;
        }

        public string GetModelForPurpose(BastaAgent.LLM.ModelPurpose purpose) => "mock";
    }

    [Fact]
    public async Task ResetAsync_ShouldClearSessionApprovals()
    {
        var llm = new NoopLLM();
        var streaming = new StreamingHandler(NullLogger<StreamingHandler>.Instance);
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        var memory = new MemoryManager(NullLogger<MemoryManager>.Instance);
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var approval = new ToolApprovalManager(NullLogger<ToolApprovalManager>.Instance, console);
        approval.ApproveForSession("X");
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

        await agent.ResetAsync();
        Assert.Null(approval.GetApprovalStatus("X"));
    }
}
