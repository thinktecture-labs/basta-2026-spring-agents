using BastaAgent.Core;
using BastaAgent.LLM;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BastaAgent.Tests;

public class LLMClientModelSelectionTests
{
    private class DummyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        ) =>
            Task.FromResult(
                new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("{}"),
                }
            );
    }

    [Fact]
    public void GetModelForPurpose_ReturnsConfiguredModels()
    {
        var http = new HttpClient(new DummyHandler())
        {
            BaseAddress = new Uri("http://localhost/"),
        };
        var cfg = Options.Create(
            new AgentConfiguration
            {
                API = new ApiConfiguration { BaseUrl = "http://localhost/", ApiKey = "none" },
                Models = new ModelConfiguration
                {
                    Execution = "exec-model",
                    Reasoning = "reason-model",
                    Summarization = "sum-model",
                },
            }
        );

        var client = new LLMClient(http, cfg, NullLogger<LLMClient>.Instance);

        Assert.Equal("reason-model", client.GetModelForPurpose(ModelPurpose.Reasoning));
        Assert.Equal("exec-model", client.GetModelForPurpose(ModelPurpose.Execution));
        Assert.Equal("sum-model", client.GetModelForPurpose(ModelPurpose.Summarization));
    }
}
