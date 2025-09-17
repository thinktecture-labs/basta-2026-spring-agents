using System.Text.Json;
using BastaAgent.Tools;
using BastaAgent.Tools.BuiltIn;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

/// <summary>
/// Comprehensive unit tests for built-in tools
/// Tests WebRequestTool, FileSystemTool, and DirectoryTool implementations
/// </summary>
public class BuiltInToolsTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _testFile;

    public BuiltInToolsTests()
    {
        // Create a temporary test directory for file system tests
        _testDirectory = Path.Combine(Path.GetTempPath(), $"BastaAgentTests_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testDirectory);
        _testFile = Path.Combine(_testDirectory, "test.txt");
    }

    public void Dispose()
    {
        // Clean up test directory
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, true);
        }
    }

    #region WebRequestTool Tests

    /// <summary>
    /// Test that WebRequestTool has correct metadata
    /// </summary>
    [Fact]
    public void WebRequestTool_ShouldHaveCorrectMetadata()
    {
        // Arrange
        var tool = new WebRequestTool(NullLogger<WebRequestTool>.Instance);

        // Assert
        Assert.Equal("Web.Request", tool.Name);
        Assert.Contains("HTTP request", tool.Description);

        // Verify schema is valid JSON
        var schema = JsonDocument.Parse(tool.ParametersSchema);
        Assert.NotNull(schema);

        // Check required properties exist
        var root = schema.RootElement;
        Assert.Equal("object", root.GetProperty("type").GetString());
        Assert.True(root.GetProperty("properties").TryGetProperty("url", out _));
        Assert.Contains(
            "url",
            root.GetProperty("required").EnumerateArray().Select(e => e.GetString())
        );
    }

    /// <summary>
    /// Test that WebRequestTool validates URL parameter
    /// </summary>
    [Fact]
    public async Task WebRequestTool_ShouldValidateUrlParameter()
    {
        // Arrange
        var tool = new WebRequestTool(NullLogger<WebRequestTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("URL is required", result.Content);
    }

    /// <summary>
    /// Test that WebRequestTool rejects invalid URLs
    /// </summary>
    [Fact]
    public async Task WebRequestTool_ShouldRejectInvalidUrls()
    {
        // Arrange
        var tool = new WebRequestTool(NullLogger<WebRequestTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { url = "not-a-valid-url" });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Invalid URL", result.Content);
    }

    /// <summary>
    /// Test that WebRequestTool only allows HTTP/HTTPS
    /// </summary>
    [Fact]
    public async Task WebRequestTool_ShouldOnlyAllowHttpHttps()
    {
        // Arrange
        var tool = new WebRequestTool(NullLogger<WebRequestTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { url = "ftp://example.com/file.txt" });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Only HTTP/HTTPS", result.Content);
    }

    #endregion

    #region FileSystemTool Tests

    /// <summary>
    /// Test that FileSystemTool can read files
    /// </summary>
    [Fact]
    public async Task FileSystemTool_ShouldReadFiles()
    {
        // Arrange
        var expectedContent = "Hello, World!";
        await File.WriteAllTextAsync(_testFile, expectedContent);

        var tool = new FileSystemTool(NullLogger<FileSystemTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { path = _testFile });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedContent, result.Content);
        Assert.NotNull(result.Metadata);
        Assert.Contains("path", result.Metadata.Keys);
        Assert.Contains("size", result.Metadata.Keys);
    }

    /// <summary>
    /// Test that FileSystemTool handles non-existent files
    /// </summary>
    [Fact]
    public async Task FileSystemTool_ShouldHandleNonExistentFiles()
    {
        // Arrange
        var tool = new FileSystemTool(NullLogger<FileSystemTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { path = "/non/existent/file.txt" });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("File not found", result.Content);
    }

    /// <summary>
    /// Test that FileWriteTool can write files
    /// </summary>
    [Fact]
    public async Task FileWriteTool_ShouldWriteFiles()
    {
        // Arrange
        var content = "Test content";
        var outputFile = Path.Combine(_testDirectory, "output.txt");

        var tool = new FileWriteTool(NullLogger<FileWriteTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { path = outputFile, content = content });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("Successfully wrote", result.Content);

        // Verify file was created
        Assert.True(File.Exists(outputFile));
        var actualContent = await File.ReadAllTextAsync(outputFile);
        Assert.Equal(content, actualContent);
    }

    /// <summary>
    /// Test that FileWriteTool can append to files
    /// </summary>
    [Fact]
    public async Task FileWriteTool_ShouldAppendToFiles()
    {
        // Arrange
        var initialContent = "Initial";
        var appendContent = " Appended";
        await File.WriteAllTextAsync(_testFile, initialContent);

        var tool = new FileWriteTool(NullLogger<FileWriteTool>.Instance);
        var parameters = JsonSerializer.Serialize(
            new
            {
                path = _testFile,
                content = appendContent,
                append = true,
            }
        );

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.True(result.Success);
        var actualContent = await File.ReadAllTextAsync(_testFile);
        Assert.Equal(initialContent + appendContent, actualContent);
    }

    /// <summary>
    /// Test that FileWriteTool creates directories if needed
    /// </summary>
    [Fact]
    public async Task FileWriteTool_ShouldCreateDirectories()
    {
        // Arrange
        var nestedPath = Path.Combine(_testDirectory, "sub", "dir", "file.txt");
        var tool = new FileWriteTool(NullLogger<FileWriteTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { path = nestedPath, content = "test" });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.True(result.Success);
        Assert.True(File.Exists(nestedPath));
    }

    #endregion

    #region DirectoryTool Tests

    /// <summary>
    /// Test that DirectoryTool lists directory contents
    /// </summary>
    [Fact]
    public async Task DirectoryTool_ShouldListDirectoryContents()
    {
        // Arrange
        // Create some test files
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "file1.txt"), "content1");
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "file2.txt"), "content2");
        Directory.CreateDirectory(Path.Combine(_testDirectory, "subdir"));

        var tool = new DirectoryTool(NullLogger<DirectoryTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { path = _testDirectory });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.True(result.Success);

        var resultJson = JsonDocument.Parse(result.Content);
        var root = resultJson.RootElement;

        Assert.Equal(2, root.GetProperty("files").GetInt32());
        Assert.Equal(1, root.GetProperty("directories").GetInt32());

        var items = root.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
    }

    /// <summary>
    /// Test that DirectoryTool supports pattern matching
    /// </summary>
    [Fact]
    public async Task DirectoryTool_ShouldSupportPatternMatching()
    {
        // Arrange
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "test.txt"), "content");
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "test.md"), "content");
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "other.json"), "content");

        var tool = new DirectoryTool(NullLogger<DirectoryTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { path = _testDirectory, pattern = "*.txt" });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.True(result.Success);

        var resultJson = JsonDocument.Parse(result.Content);
        Assert.Equal(1, resultJson.RootElement.GetProperty("files").GetInt32());
    }

    /// <summary>
    /// Test that DirectoryTool supports recursive listing
    /// </summary>
    [Fact]
    public async Task DirectoryTool_ShouldSupportRecursiveListing()
    {
        // Arrange
        var subdir = Path.Combine(_testDirectory, "subdir");
        Directory.CreateDirectory(subdir);
        await File.WriteAllTextAsync(Path.Combine(_testDirectory, "root.txt"), "content");
        await File.WriteAllTextAsync(Path.Combine(subdir, "nested.txt"), "content");

        var tool = new DirectoryTool(NullLogger<DirectoryTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { path = _testDirectory, recursive = true });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.True(result.Success);

        var resultJson = JsonDocument.Parse(result.Content);
        Assert.Equal(2, resultJson.RootElement.GetProperty("files").GetInt32());
        Assert.Equal(1, resultJson.RootElement.GetProperty("directories").GetInt32());
    }

    /// <summary>
    /// Test that DirectoryTool handles non-existent directories
    /// </summary>
    [Fact]
    public async Task DirectoryTool_ShouldHandleNonExistentDirectories()
    {
        // Arrange
        var tool = new DirectoryTool(NullLogger<DirectoryTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { path = "/non/existent/directory" });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Directory not found", result.Content);
    }

    #endregion

    #region WebSearchTool Tests

    /// <summary>
    /// Test that WebSearchTool returns mock results
    /// </summary>
    [Fact]
    public async Task WebSearchTool_ShouldReturnMockResults()
    {
        // Arrange
        var tool = new WebSearchTool(NullLogger<WebSearchTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { query = "test query" });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.True(result.Success);

        var resultJson = JsonDocument.Parse(result.Content);
        var results = resultJson.RootElement.EnumerateArray().ToList();

        Assert.NotEmpty(results);
        Assert.All(
            results,
            r =>
            {
                Assert.True(r.TryGetProperty("title", out _));
                Assert.True(r.TryGetProperty("url", out _));
                Assert.True(r.TryGetProperty("snippet", out _));
            }
        );
    }

    /// <summary>
    /// Test that WebSearchTool respects maxResults parameter
    /// </summary>
    [Fact]
    public async Task WebSearchTool_ShouldRespectMaxResults()
    {
        // Arrange
        var tool = new WebSearchTool(NullLogger<WebSearchTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { query = "test", maxResults = 2 });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.True(result.Success);

        var resultJson = JsonDocument.Parse(result.Content);
        var results = resultJson.RootElement.EnumerateArray().ToList();

        Assert.Equal(2, results.Count);
    }

    /// <summary>
    /// Test that WebSearchTool requires query parameter
    /// </summary>
    [Fact]
    public async Task WebSearchTool_ShouldRequireQuery()
    {
        // Arrange
        var tool = new WebSearchTool(NullLogger<WebSearchTool>.Instance);
        var parameters = JsonSerializer.Serialize(new { });

        // Act
        var result = await tool.ExecuteAsync(parameters);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Query is required", result.Content);
    }

    #endregion
}
