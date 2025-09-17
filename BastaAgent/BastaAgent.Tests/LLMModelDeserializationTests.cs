using System.Text.Json;
using BastaAgent.LLM.Models;
using Xunit;

namespace BastaAgent.Tests;

public class LLMModelDeserializationTests
{
    [Fact]
    public void ErrorResponse_DeserializesCorrectly()
    {
        var json =
            "{"
            + "\"error\": {"
            + "  \"message\": \"Invalid\","
            + "  \"type\": \"invalid_request_error\","
            + "  \"code\": \"400\","
            + "  \"param\": \"model\""
            + " }"
            + "}";
        var er = JsonSerializer.Deserialize<ErrorResponse>(json);
        Assert.NotNull(er);
        Assert.NotNull(er!.Error);
        Assert.Equal("Invalid", er.Error!.Message);
        Assert.Equal("invalid_request_error", er.Error.Type);
        Assert.Equal("400", er.Error.Code);
        Assert.Equal("model", er.Error.Param);
    }
}
