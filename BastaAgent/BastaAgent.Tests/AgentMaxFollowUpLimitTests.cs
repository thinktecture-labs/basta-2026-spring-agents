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

public class AgentMaxFollowUpLimitTests
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

    private class InfiniteToolCallsLLM : ILLMClient
    {
        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default
        )
        {
            // Always return another tool call and no content
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
                                        Id = Guid.NewGuid().ToString("N"),
                                        Type = "function",
                                        Function = new FunctionCall
                                        {
                                            Name = "Demo_Tool",
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

        public async IAsyncEnumerable<StreamingChatResponse> StreamAsync(
            ChatRequest request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            // Initial stream triggers first tool call
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
                                    Id = "first",
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
    public async Task Agent_ShouldStopAfterMaxFollowUps_AndReturnNoResponse()
    {
        var llm = new InfiniteToolCallsLLM();
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

        var response = await agent.ProcessMessageAsync("infinite tools");
        Assert.Equal("No response", response);
    }
}
