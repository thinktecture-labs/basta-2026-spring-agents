using System.Runtime.CompilerServices;
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

public class AgentFollowUpLoopTests
{
    private class DummyTool : ITool
    {
        public string Name => "Demo.Tool";
        public string Description => "Demo";
        public string ParametersSchema => "{ \"type\": \"object\" }";

        public Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(ToolResult.Ok("OK"));
    }

    private class FollowUpLLM : ILLMClient
    {
        public int CompleteCalls { get; private set; }

        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default
        )
        {
            CompleteCalls++;
            if (CompleteCalls == 1)
            {
                // First call returns empty content and no tool_calls
                return Task.FromResult(
                    new ChatResponse
                    {
                        Choices = new List<Choice>
                        {
                            new Choice
                            {
                                Index = 0,
                                Message = new Message
                                {
                                    Role = "assistant",
                                    Content = string.Empty,
                                },
                                FinishReason = "stop",
                            },
                        },
                    }
                );
            }
            // Second call returns final content
            return Task.FromResult(
                new ChatResponse
                {
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Index = 0,
                            Message = new Message { Role = "assistant", Content = "final" },
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
            // Stream a single tool call to trigger execution path
            yield return new StreamingChatResponse
            {
                Choices = new List<Choice>
                {
                    new Choice
                    {
                        Index = 0,
                        Delta = new Message
                        {
                            ToolCalls = new List<ToolCall>
                            {
                                new ToolCall
                                {
                                    Id = "c1",
                                    Type = "function",
                                    Function = new FunctionCall
                                    {
                                        Name = "Demo_Tool",
                                        Arguments = "{}",
                                    },
                                },
                            },
                        },
                    },
                },
            };
            await Task.CompletedTask;
        }

        public string GetModelForPurpose(ModelPurpose purpose) => "mock";
    }

    [Fact]
    public async Task Agent_ShouldPerformFollowUp_WhenFirstCompletionEmpty()
    {
        var llm = new FollowUpLLM();
        var streaming = new StreamingHandler(NullLogger<StreamingHandler>.Instance);
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.RegisterTool(new DummyTool());
        var memory = new MemoryManager(NullLogger<MemoryManager>.Instance);
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var approval = new ToolApprovalManager(NullLogger<ToolApprovalManager>.Instance, console);
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

        var response = await agent.ProcessMessageAsync("run tool and follow-up");

        Assert.Equal("final", response);
        Assert.Equal(2, llm.CompleteCalls);
    }
}
