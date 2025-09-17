using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BastaAgent.Tests;

public class LLMClientRetryTimeoutTests
{
    private class TimeoutThenSuccessHandler : HttpMessageHandler
    {
        private int _count;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            _count++;
            if (_count == 1)
            {
                // 408 Request Timeout
                return Task.FromResult(
                    new HttpResponseMessage((HttpStatusCode)408)
                    {
                        Content = new StringContent("{\"error\":\"timeout\"}"),
                    }
                );
            }

            var body = new ChatResponse
            {
                Id = "id",
                Object = "chat.completion",
                Created = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                Model = "mock",
                Choices = new List<Choice>
                {
                    new Choice
                    {
                        Index = 0,
                        Message = new Message { Role = "assistant", Content = "ok-timeout-retry" },
                        FinishReason = "stop",
                    },
                },
                Usage = new Usage
                {
                    PromptTokens = 1,
                    CompletionTokens = 1,
                    TotalTokens = 2,
                },
            };
            var json = JsonSerializer.Serialize(body);
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json),
            };
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public async Task CompleteAsync_RetriesOn408_ThenSucceeds()
    {
        var http = new HttpClient(new TimeoutThenSuccessHandler())
        {
            BaseAddress = new Uri("http://localhost/"),
        };
        var cfg = Options.Create(
            new AgentConfiguration
            {
                API = new ApiConfiguration
                {
                    BaseUrl = "http://localhost/",
                    ApiKey = "none",
                    MaxRetries = 2,
                    RetryDelayMilliseconds = 1,
                },
            }
        );
        var client = new LLMClient(http, cfg, NullLogger<LLMClient>.Instance);

        var req = new ChatRequest
        {
            Model = "mock",
            Messages = new List<Message> { Message.User("hi") },
        };
        var resp = await client.CompleteAsync(req);

        Assert.Equal("ok-timeout-retry", resp.GetMessage()?.Content);
    }
}
