using System.Text;
using BastaAgent.Utilities;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BastaAgent.Tests;

public class SimpleFileLoggerTests : IDisposable
{
    private readonly string _dir;
    private readonly string _file;

    public SimpleFileLoggerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"LogTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_dir);
        _file = Path.Combine(_dir, "log.txt");
    }

    [Fact]
    public void Logger_WritesMessage_AndException_AndScope()
    {
        var provider = new SimpleFileLoggerProvider(_file);
        // Set scope provider
        (provider as ISupportExternalScope)?.SetScopeProvider(new LoggerExternalScopeProvider());

        var logger = provider.CreateLogger("Test.Category");

        using (logger.BeginScope("scope-123"))
        {
            logger.LogInformation("Hello {Name}", "World");
            logger.LogError(new InvalidOperationException("boom"), "Failure {Code}", 42);
        }

        var text = File.ReadAllText(_file, Encoding.UTF8);
        Assert.Contains("Test.Category", text);
        Assert.Contains("Hello World", text);
        Assert.Contains("Failure 42", text);
        Assert.Contains("InvalidOperationException", text);
        Assert.Contains("=> Scope: scope-123", text);
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
