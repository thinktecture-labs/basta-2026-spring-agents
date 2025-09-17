using System.Net;
using System.Net.Http.Headers;
using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BastaAgent.Tests;

public class LLMClientNegativeTests
{
    private class NonJsonHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new StringContent("not json");
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            return Task.FromResult(resp);
        }
    }

    [Fact]
    public async Task CompleteAsync_NonJsonContent_ThrowsInvalidOperation()
    {
        var http = new HttpClient(new NonJsonHandler())
        {
            BaseAddress = new Uri("http://localhost/"),
        };
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

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.CompleteAsync(req));
    }
}
