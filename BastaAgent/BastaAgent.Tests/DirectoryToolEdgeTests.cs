using BastaAgent.Tools.BuiltIn;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

public class DirectoryToolEdgeTests : IDisposable
{
    private readonly string _root;

    public DirectoryToolEdgeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"DirToolTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_root);
        // Create nested structure
        var sub = Path.Combine(_root, "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(sub, "data.json"), "{}");
        File.WriteAllText(Path.Combine(sub, "note.txt"), "x");
    }

    [Fact]
    public async Task Directory_List_Pattern_NoRecursive_ShouldFindNone()
    {
        var tool = new DirectoryTool(NullLogger<DirectoryTool>.Instance);
        var args = $"{{ \"path\": \"{_root}\", \"pattern\": \"*.json\", \"recursive\": false }}";
        var res = await tool.ExecuteAsync(args);
        Assert.True(res.Success);

        // items should be empty because json is only in subdir and recursive=false
        var json = System.Text.Json.JsonDocument.Parse(res.Content!);
        Assert.Equal(0, json.RootElement.GetProperty("files").GetInt32());
    }

    [Fact]
    public async Task Directory_List_Pattern_Recursive_ShouldFindJson()
    {
        var tool = new DirectoryTool(NullLogger<DirectoryTool>.Instance);
        var args = $"{{ \"path\": \"{_root}\", \"pattern\": \"*.json\", \"recursive\": true }}";
        var res = await tool.ExecuteAsync(args);
        Assert.True(res.Success);

        var json = System.Text.Json.JsonDocument.Parse(res.Content!);
        Assert.True(json.RootElement.GetProperty("files").GetInt32() >= 1);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch { }
    }
}
