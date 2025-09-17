using System.Text.Json;
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

public class AgentLoadStateTests : IDisposable
{
    private readonly string _stateDir;

    public AgentLoadStateTests()
    {
        _stateDir = Path.Combine(Path.GetTempPath(), $"AgentLoad_{Guid.NewGuid()}");
        Directory.CreateDirectory(Path.Combine(_stateDir, "state"));
        Environment.CurrentDirectory = _stateDir;
    }

    [Fact]
    public async Task LoadStateAsync_ShouldRestoreApprovals_FromSavedFile()
    {
        var llm = new DummyLLM();
        var streaming = new StreamingHandler(NullLogger<StreamingHandler>.Instance);
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        var memory = new MemoryManager(NullLogger<MemoryManager>.Instance);
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var approval1 = new ToolApprovalManager(NullLogger<ToolApprovalManager>.Instance, console);
        var cfg = Options.Create(
            new AgentConfiguration
            {
                Models = new ModelConfiguration { Reasoning = "mock", Execution = "mock" },
                API = new ApiConfiguration { BaseUrl = "http://localhost/", ApiKey = "none" },
            }
        );

        // Set approvals and save via Agent
        approval1.ApproveForSession("Demo.Read");
        approval1.DenyForSession("Demo.Write", "test");
        var agent1 = new AgentCore(
            NullLogger<AgentCore>.Instance,
            llm,
            streaming,
            registry,
            memory,
            approval1,
            console,
            cfg
        );
        await agent1.SaveStateAsync();

        // New agent with fresh manager; load state to restore approvals
        var approval2 = new ToolApprovalManager(NullLogger<ToolApprovalManager>.Instance, console);
        var agent2 = new AgentCore(
            NullLogger<AgentCore>.Instance,
            llm,
            streaming,
            registry,
            new MemoryManager(NullLogger<MemoryManager>.Instance),
            approval2,
            console,
            cfg
        );
        await agent2.LoadStateAsync();

        Assert.NotNull(approval2.GetApprovalStatus("Demo.Read"));
        Assert.True(approval2.GetApprovalStatus("Demo.Read")!.AlwaysApprove);
        Assert.NotNull(approval2.GetApprovalStatus("Demo.Write"));
        Assert.True(approval2.GetApprovalStatus("Demo.Write")!.AlwaysDeny);
    }

    private class DummyLLM : ILLMClient
    {
        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                new ChatResponse
                {
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Index = 0,
                            Message = new Message { Role = "assistant", Content = "ok" },
                        },
                    },
                }
            );

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

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_stateDir))
                Directory.Delete(_stateDir, true);
        }
        catch { }
    }
}
