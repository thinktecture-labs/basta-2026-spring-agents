using System.Text;
using BastaAgent.Tools.BuiltIn;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

public class FileSystemToolPrettyPrintTests : IDisposable
{
    private readonly string _dir;

    public FileSystemToolPrettyPrintTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"FsPretty_{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public async Task FileWrite_Overwrite_PrettyPrintsJson()
    {
        var path = Path.Combine(_dir, "pretty.json");
        var tool = new FileWriteTool(NullLogger<FileWriteTool>.Instance);
        var compact = "{\"a\":1,\"b\":[2,3]}";
        var args = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                path,
                content = compact,
                append = false,
            }
        );

        var res = await tool.ExecuteAsync(args);
        Assert.True(res.Success);

        var text = await File.ReadAllTextAsync(path, Encoding.UTF8);
        // JSON should still parse equivalently
        var doc = System.Text.Json.JsonDocument.Parse(text);
        Assert.Equal(1, doc.RootElement.GetProperty("a").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("b")[0].GetInt32());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }
        catch { }
    }
}
