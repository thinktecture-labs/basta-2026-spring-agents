using System.Text.Json;
using BastaAgent.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

public class ToolRegistryAdditionalTests
{
    private class DummyTool : ITool
    {
        public string Name => "My Tool!@#";
        public string Description => "Dummy tool for testing registration";
        public string ParametersSchema => JsonSerializer.Serialize(new { type = "object" });

        public Task<ToolResult> ExecuteAsync(
            string parameters,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(ToolResult.Ok("done"));
    }

    [Fact]
    public void RegisterTool_SanitizesApiName_AndGeneratesDefinition()
    {
        var reg = new ToolRegistry(NullLogger<ToolRegistry>.Instance);

        reg.RegisterTool(new DummyTool());
        var defs = reg.GenerateToolDefinitions();

        Assert.Single(defs);
        var def = defs[0];
        Assert.Equal("function", def.Type);
        Assert.NotNull(def.Function);
        Assert.Equal("My_Tool_", def.Function!.Name); // non-allowed chars replaced by '_' and collapsed
        Assert.Equal("Dummy tool for testing registration", def.Function.Description);
    }
}
