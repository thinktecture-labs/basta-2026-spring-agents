using BastaAgent.LLM.Models;
using Xunit;

namespace BastaAgent.Tests;

public class ChatResponseUtilityTests
{
    [Fact]
    public void GetMessage_And_HasToolCalls_Work()
    {
        var resp = new ChatResponse
        {
            Choices = new List<Choice>
            {
                new Choice
                {
                    Index = 0,
                    Message = new Message
                    {
                        Role = "assistant",
                        Content = "hi",
                        ToolCalls = new List<ToolCall>
                        {
                            new ToolCall
                            {
                                Id = "1",
                                Type = "function",
                                Function = new FunctionCall { Name = "demo", Arguments = "{}" },
                            },
                        },
                    },
                },
            },
        };

        Assert.Equal("hi", resp.GetMessage()!.Content);
        Assert.True(resp.HasToolCalls());
    }
}
