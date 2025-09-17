using System.Runtime.CompilerServices;
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

public class AgentToolFlowTests
{
    private class DummyTool : ITool
    {
        public string Name => "Demo.Tool";
        public string Description => "Demo tool";
        public string ParametersSchema => JsonSerializer.Serialize(new { type = "object" });

        public Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(ToolResult.Ok("RESULT"));
    }

    private class FakeLLMClient : ILLMClient
    {
        public bool LinkingVerified { get; private set; }

        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default
        )
        {
            // Verify assistant tool_calls and tool tool_call_id linking
            var msgs = request.Messages ?? new List<Message>();
            var assistantWithCalls = msgs.LastOrDefault(m =>
                m.Role == "assistant" && m.ToolCalls is not null
            );
            var toolMsg = msgs.LastOrDefault(m => m.Role == "tool");

            if (assistantWithCalls is not null && toolMsg is not null)
            {
                var id = assistantWithCalls.ToolCalls![0].Id;
                if (
                    !string.IsNullOrEmpty(id)
                    && toolMsg.ToolCallId == id
                    && (toolMsg.Content?.Contains("RESULT") ?? false)
                )
                {
                    LinkingVerified = true;
                }
            }

            return Task.FromResult(
                new ChatResponse
                {
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Message = new Message { Role = "assistant", Content = "done" },
                            Index = 0,
                            FinishReason = "stop",
                        },
                    },
                }
            );
        }

        public async IAsyncEnumerable<StreamingChatResponse> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            // Stream a single delta that requests a tool call to Demo_Tool
            yield return new StreamingChatResponse
            {
                Choices = new List<Choice>
                {
                    new Choice
                    {
                        Delta = new Message
                        {
                            ToolCalls = new List<ToolCall>
                            {
                                new ToolCall
                                {
                                    Id = "call_1",
                                    Type = "function",
                                    Function = new FunctionCall
                                    {
                                        Name = "Demo_Tool",
                                        Arguments = "{}",
                                    },
                                },
                            },
                        },
                        Index = 0,
                    },
                },
            };
            await Task.CompletedTask;
        }

        public string GetModelForPurpose(ModelPurpose purpose) => "mock";
    }

    [Fact]
    public async Task Agent_ShouldLinkToolCallsAndResults_ByToolCallId()
    {
        var llm = new FakeLLMClient();
        var streaming = new StreamingHandler(NullLogger<StreamingHandler>.Instance);
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.RegisterTool(new DummyTool());
        var memory = new MemoryManager(NullLogger<MemoryManager>.Instance);
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var approval = new ToolApprovalManager(NullLogger<ToolApprovalManager>.Instance, console);
        // Approve the API function name
        approval.ApproveForSession("Demo_Tool");

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

        var response = await agent.ProcessMessageAsync("run demo tool");

        Assert.Equal("done", response);
        Assert.True(llm.LinkingVerified);
    }
}
