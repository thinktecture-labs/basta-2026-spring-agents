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

public class LLMClientStreamingTests
{
    private class SseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            // Minimal SSE stream with one JSON chunk and [DONE]
            var sse =
                ": OPENROUTER PROCESSING\n\n"
                + "data: {\"choices\":[{\"delta\":{\"content\":\"Hello\"}}]}\n\n"
                + "data: [DONE]\n\n";
            var bytes = Encoding.UTF8.GetBytes(sse);
            var stream = new MemoryStream(bytes);
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            var resp = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public async Task StreamAsync_ShouldYieldChunks_FromEventStream()
    {
        var http = new HttpClient(new SseHandler()) { BaseAddress = new Uri("http://localhost/") };
        var cfg = Options.Create(
            new AgentConfiguration
            {
                API = new ApiConfiguration { BaseUrl = "http://localhost/", ApiKey = "none" },
            }
        );
        var client = new LLMClient(http, cfg, NullLogger<LLMClient>.Instance);

        var req = new ChatRequest
        {
            Model = "mock",
            Messages = new List<Message> { Message.User("hi") },
        };

        int count = 0;
        await foreach (var chunk in client.StreamAsync(req))
        {
            count++;
        }

        Assert.True(count >= 1);
    }
}
