using System.Net;
using System.Net.Http.Headers;
using System.Text;
using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BastaAgent.Tests;

public class LLMClientStreamingRetryTests
{
    private class FlakySseHandler : HttpMessageHandler
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
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent("error"),
                    }
                );
            }

            var sse =
                "data: {\"choices\":[{\"delta\":{\"content\":\"hi\"}}]}\n\n" + "data: [DONE]\n\n";
            var bytes = Encoding.UTF8.GetBytes(sse);
            var stream = new MemoryStream(bytes);
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK) { Content = content }
            );
        }
    }

    [Fact]
    public async Task StreamAsync_RetriesOnFailure_ThenStreams()
    {
        var http = new HttpClient(new FlakySseHandler())
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
        int cnt = 0;
        await foreach (var _ in client.StreamAsync(req))
        {
            cnt++;
        }
        Assert.True(cnt >= 1);
    }
}
