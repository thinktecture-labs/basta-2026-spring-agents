using System.Text;
using BastaAgent.Tools.BuiltIn;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

public class FileSystemToolTests : IDisposable
{
    private readonly string _dir;

    public FileSystemToolTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"FsToolTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public async Task FileWrite_OverwriteAndAppend_Works()
    {
        var path = Path.Combine(_dir, "test.txt");
        var write = new FileWriteTool(NullLogger<FileWriteTool>.Instance);

        // Overwrite
        var args1 = $"{{ \"path\": \"{path}\", \"content\": \"B\", \"append\": false }}";
        var r1 = await write.ExecuteAsync(args1);
        Assert.True(r1.Success);
        Assert.Equal("B", await File.ReadAllTextAsync(path));

        // Append
        var args2 = $"{{ \"path\": \"{path}\", \"content\": \"C\", \"append\": true }}";
        var r2 = await write.ExecuteAsync(args2);
        Assert.True(r2.Success);
        Assert.Equal("BC", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FileRead_ReturnsContent_AndMetadata()
    {
        var path = Path.Combine(_dir, "data.txt");
        await File.WriteAllTextAsync(path, "Hello", Encoding.UTF8);

        var read = new FileSystemTool(NullLogger<FileSystemTool>.Instance);
        var args = $"{{ \"path\": \"{path}\" }}";
        var res = await read.ExecuteAsync(args);

        Assert.True(res.Success);
        Assert.Equal("Hello", res.Content);
        Assert.NotNull(res.Metadata);
        Assert.True(res.Metadata!.ContainsKey("path"));
        Assert.True(res.Metadata!.ContainsKey("size"));
        Assert.Equal(
            "utf-8",
            (res.Metadata!["encoding"]?.ToString() ?? string.Empty).ToLowerInvariant()
        );
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
