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

public class AgentMultiToolFollowUpFlowTests
{
    private class ReadTool : ITool
    {
        public string Name => "Demo.Read";
        public string Description => "Read op";
        public string ParametersSchema => "{ \"type\": \"object\" }";

        public Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(ToolResult.Ok("READ_OK"));
    }

    private class WriteTool : ITool
    {
        public string Name => "Demo.Write";
        public string Description => "Write op";
        public string ParametersSchema => "{ \"type\": \"object\" }";

        public Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(ToolResult.Ok("WRITE_OK"));
    }

    private class MultiToolLLM : ILLMClient
    {
        private int _completeCount;
        public List<string> SeenToolNames { get; } = new();

        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default
        )
        {
            _completeCount++;
            var msgs = request.Messages ?? new List<Message>();
            // Capture tool result sequence by reading last tool message name (not present, but content identifies)
            var lastTool = msgs.LastOrDefault(m => m.Role == "tool");
            if (lastTool is not null)
            {
                SeenToolNames.Add(
                    lastTool.Content!.Contains("READ")
                        ? "Read"
                        : (lastTool.Content!.Contains("WRITE") ? "Write" : "?")
                );
            }

            if (_completeCount == 1)
            {
                // After first tool (Read), request Write tool
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
                                    ToolCalls = new List<ToolCall>
                                    {
                                        new ToolCall
                                        {
                                            Id = "w1",
                                            Type = "function",
                                            Function = new FunctionCall
                                            {
                                                Name = "Demo_Write",
                                                Arguments = "{}",
                                            },
                                        },
                                    },
                                },
                                FinishReason = "tool_calls",
                            },
                        },
                    }
                );
            }

            return Task.FromResult(
                new ChatResponse
                {
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Index = 0,
                            Message = new Message { Role = "assistant", Content = "ok" },
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
            // Stream initial Read
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
                                    Id = "r1",
                                    Type = "function",
                                    Function = new FunctionCall
                                    {
                                        Name = "Demo_Read",
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
    public async Task Agent_ShouldExecuteReadThenWrite_InFollowUpFlow()
    {
        var llm = new MultiToolLLM();
        var streaming = new StreamingHandler(NullLogger<StreamingHandler>.Instance);
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        registry.RegisterTool(new ReadTool());
        registry.RegisterTool(new WriteTool());
        var memory = new MemoryManager(NullLogger<MemoryManager>.Instance);
        var console = new InteractiveConsole(NullLogger<InteractiveConsole>.Instance);
        var approval = new ToolApprovalManager(NullLogger<ToolApprovalManager>.Instance, console);
        approval.ApproveForSession("Demo_Read");
        approval.ApproveForSession("Demo_Write");

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

        var response = await agent.ProcessMessageAsync("read then write");
        Assert.Equal("ok", response);
        Assert.Contains("Read", llm.SeenToolNames);
        Assert.Contains("Write", llm.SeenToolNames);
        // Read should appear before Write in captured sequence
        Assert.True(llm.SeenToolNames.IndexOf("Read") < llm.SeenToolNames.IndexOf("Write"));
    }
}
