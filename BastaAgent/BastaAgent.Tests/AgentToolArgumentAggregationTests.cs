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

public class AgentToolArgumentAggregationTests
{
    private class CapturingTool : ITool
    {
        public string Name => "Demo.Tool";
        public string Description => "Captures parameters";
        public string ParametersSchema => JsonSerializer.Serialize(new { type = "object" });
        public string? LastParameters { get; private set; }

        public Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        )
        {
            LastParameters = parameters;
            return Task.FromResult(ToolResult.Ok("OK"));
        }
    }

    private class FragmentedToolCallLLM : ILLMClient
    {
        public Task<ChatResponse> CompleteAsync(
            ChatRequest request,
            CancellationToken cancellationToken = default
        )
        {
            // Return simple assistant content to finish
            return Task.FromResult(
                new ChatResponse
                {
                    Choices = new List<Choice>
                    {
                        new Choice
                        {
                            Index = 0,
                            Message = new Message { Role = "assistant", Content = "done" },
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
            // 1) First delta: index=0, arguments fragment 1
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
                                    Index = 0,
                                    Type = "function",
                                    Function = new FunctionCall
                                    {
                                        Name = string.Empty,
                                        Arguments = "{\"url\":\"https://example",
                                    },
                                },
                            },
                        },
                    },
                },
            };

            // 2) Second delta: index=0, arguments fragment 2
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
                                    Index = 0,
                                    Type = "function",
                                    Function = new FunctionCall
                                    {
                                        Name = string.Empty,
                                        Arguments = ".com/models\"}",
                                    },
                                },
                            },
                        },
                    },
                },
            };

            // 3) Third delta: now provide id and function name
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
                                    Id = "call_1",
                                    Index = 0,
                                    Type = "function",
                                    Function = new FunctionCall
                                    {
                                        Name = "Demo_Tool",
                                        Arguments = string.Empty,
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
    public async Task Agent_ShouldAggregateArgumentFragments_BeforeToolExecution()
    {
        var llm = new FragmentedToolCallLLM();
        var streaming = new StreamingHandler(NullLogger<StreamingHandler>.Instance);
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);
        var tool = new CapturingTool();
        registry.RegisterTool(tool);
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

        var response = await agent.ProcessMessageAsync("please run tool");

        Assert.Equal("done", response);
        Assert.NotNull(tool.LastParameters);
        Assert.Equal(
            "https://example.com/models",
            JsonDocument.Parse(tool.LastParameters!).RootElement.GetProperty("url").GetString()
        );
    }
}
