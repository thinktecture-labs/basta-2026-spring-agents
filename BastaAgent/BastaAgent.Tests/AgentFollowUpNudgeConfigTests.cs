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

public class AgentFollowUpNudgeConfigTests
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

    private class CapturingLLM : ILLMClient
    {
        public ChatRequest? SecondCallRequest { get; private set; }
        private int _calls;

        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default
        )
        {
            _calls++;
            if (_calls == 1)
            {
                // First call: no content, no tool_calls
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
            SecondCallRequest = request;
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
            // Trigger one tool call via stream to enter execution path
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
    public async Task Agent_ShouldUseConfiguredFollowUpNudgeMessage()
    {
        var llm = new CapturingLLM();
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
                Conversation = new ConversationConfiguration
                {
                    FollowUpNudgeSystemMessage = "PLEASE NUDGE",
                },
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

        var response = await agent.ProcessMessageAsync("trigger nudge");
        Assert.Equal("ok", response);
        Assert.NotNull(llm.SecondCallRequest);
        Assert.Contains(
            llm.SecondCallRequest!.Messages!,
            m => m.Role == "system" && m.Content == "PLEASE NUDGE"
        );
    }
}
