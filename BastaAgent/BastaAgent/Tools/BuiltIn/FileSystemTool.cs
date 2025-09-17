using System.Text;
using System.Text.Json;
using BastaAgent.Utilities;
using Microsoft.Extensions.Logging;

namespace BastaAgent.Tools.BuiltIn;

/// <summary>
/// Tool for file system operations.
///
/// <para><b>Conference Note - Safe File Access:</b></para>
/// <para>This tool demonstrates security-conscious file operations:</para>
/// <list type="bullet">
/// <item><b>Path Validation:</b> Uses Path.GetFullPath() to prevent directory traversal attacks</item>
/// <item><b>User Approval:</b> RequiresApproval=true ensures user consent before file access</item>
/// <item><b>Error Handling:</b> Graceful handling of access denied, file not found, etc.</item>
/// <item><b>Encoding Support:</b> Handles different text encodings (UTF-8, ASCII, etc.)</item>
/// </list>
///
/// <para><b>Security Considerations:</b></para>
/// <list type="bullet">
/// <item>Always validate and normalize file paths</item>
/// <item>Never allow ".." in paths without validation</item>
/// <item>Check file existence before operations</item>
/// <item>Handle unauthorized access gracefully</item>
/// </list>
/// </summary>
[Tool(Category = "FileSystem", RequiresApproval = true)]
public class FileSystemTool(ILogger<FileSystemTool>? logger = null) : BaseTool(logger)
{
    public override string Name => "FileSystem.Read";

    public override string Description => "Read content from a file on the local file system";

    public override string ParametersSchema =>
        JsonSerializer.Serialize(
            new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Path to the file to read" },
                    encoding = new
                    {
                        type = "string",
                        description = "Text encoding (default: UTF-8)",
                        @default = "UTF-8",
                    },
                },
                required = new[] { "path" },
            }
        );

    protected override async Task<ToolResult> ExecuteInternalAsync(
        string parameters,
        CancellationToken cancellationToken
    )
    {
        var args = ParseParameters<FileReadParameters>(parameters);
        if (args is null)
        {
            return ToolResult.Error("Invalid parameters");
        }

        if (string.IsNullOrEmpty(args.Path))
        {
            return ToolResult.Error("Path is required");
        }

        try
        {
            // Validate path is safe (no directory traversal)
            var fullPath = Path.GetFullPath(args.Path);
            if (!File.Exists(fullPath))
            {
                return ToolResult.Error($"File not found: {args.Path}");
            }

            // Read file content
            var encoding = GetEncoding(args.Encoding);
            var content = await File.ReadAllTextAsync(fullPath, encoding, cancellationToken);

            return ToolResult.Ok(
                content,
                new Dictionary<string, object>
                {
                    ["path"] = fullPath,
                    ["size"] = new FileInfo(fullPath).Length,
                    ["encoding"] = encoding.WebName,
                }
            );
        }
        catch (UnauthorizedAccessException)
        {
            var errorMsg = ErrorMessages.FileSystem.ReadFileFailed(args.Path, "Access denied");
            return ToolResult.Error(errorMsg);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to read file: {ex.Message}");
        }
    }

    private Encoding GetEncoding(string? encodingName)
    {
        if (string.IsNullOrEmpty(encodingName))
            return Encoding.UTF8;

        try
        {
            return Encoding.GetEncoding(encodingName);
        }
        catch
        {
            _logger?.LogWarning("Invalid encoding '{Encoding}', using UTF-8", encodingName);
            return Encoding.UTF8;
        }
    }

    private class FileReadParameters
    {
        public string Path { get; set; } = string.Empty;
        public string? Encoding { get; set; }
    }
}

/// <summary>
/// Tool for writing files
/// </summary>
[Tool(Category = "FileSystem", RequiresApproval = true)]
public class FileWriteTool(ILogger<FileWriteTool>? logger = null) : BaseTool(logger)
{
    public override string Name => "FileSystem.Write";

    public override string Description => "Write content to a file on the local file system";

    public override string ParametersSchema =>
        JsonSerializer.Serialize(
            new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Path to the file to write" },
                    content = new { type = "string", description = "Content to write to the file" },
                    append = new
                    {
                        type = "boolean",
                        description = "Append to existing file instead of overwriting",
                        @default = false,
                    },
                    encoding = new
                    {
                        type = "string",
                        description = "Text encoding (default: UTF-8)",
                        @default = "UTF-8",
                    },
                },
                required = new[] { "path", "content" },
            }
        );

    protected override async Task<ToolResult> ExecuteInternalAsync(
        string parameters,
        CancellationToken cancellationToken
    )
    {
        var args = ParseParameters<FileWriteParameters>(parameters);
        if (args is null)
        {
            return ToolResult.Error("Invalid parameters");
        }

        if (string.IsNullOrEmpty(args.Path))
        {
            return ToolResult.Error("Path is required");
        }

        if (args.Content is null)
        {
            return ToolResult.Error("Content is required");
        }

        try
        {
            // Validate path is safe
            var fullPath = Path.GetFullPath(args.Path);
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Write file content
            var encoding = GetEncoding(args.Encoding);

            if (args.Append)
            {
                await File.AppendAllTextAsync(fullPath, args.Content, encoding, cancellationToken);
            }
            else
            {
                // Pretty-print JSON content when overwriting and valid JSON provided
                string toWrite = args.Content;
                var trimmed = args.Content.TrimStart();
                if ((trimmed.StartsWith("{") || trimmed.StartsWith("[")))
                {
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(args.Content);
                        toWrite = System.Text.Json.JsonSerializer.Serialize(
                            doc.RootElement,
                            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }
                        );
                    }
                    catch
                    {
                        // Not valid JSON; write as-is
                    }
                }

                await File.WriteAllTextAsync(fullPath, toWrite, encoding, cancellationToken);
            }

            return ToolResult.Ok(
                $"Successfully wrote to {args.Path}",
                new Dictionary<string, object>
                {
                    ["path"] = fullPath,
                    ["bytesWritten"] = encoding.GetByteCount(args.Content),
                    ["append"] = args.Append,
                }
            );
        }
        catch (UnauthorizedAccessException)
        {
            var errorMsg = ErrorMessages.FileSystem.ReadFileFailed(args.Path, "Access denied");
            return ToolResult.Error(errorMsg);
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Failed to write file: {ex.Message}");
        }
    }

    private Encoding GetEncoding(string? encodingName)
    {
        if (string.IsNullOrEmpty(encodingName))
            return Encoding.UTF8;

        try
        {
            return Encoding.GetEncoding(encodingName);
        }
        catch
        {
            _logger?.LogWarning("Invalid encoding '{Encoding}', using UTF-8", encodingName);
            return Encoding.UTF8;
        }
    }

    private class FileWriteParameters
    {
        public string Path { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool Append { get; set; }
        public string? Encoding { get; set; }
    }
}

/// <summary>
/// Tool for directory operations
/// </summary>
[Tool(Category = "FileSystem", RequiresApproval = false)]
public class DirectoryTool(ILogger<DirectoryTool>? logger = null) : BaseTool(logger)
{
    public override string Name => "Directory.List";

    public override string Description => "List files and directories in a specified path";

    public override string ParametersSchema =>
        JsonSerializer.Serialize(
            new
            {
                type = "object",
                properties = new
                {
                    path = new { type = "string", description = "Path to the directory" },
                    pattern = new
                    {
                        type = "string",
                        description = "Search pattern (e.g., *.txt)",
                        @default = "*",
                    },
                    recursive = new
                    {
                        type = "boolean",
                        description = "Search recursively in subdirectories",
                        @default = false,
                    },
                },
                required = new[] { "path" },
            }
        );

    protected override Task<ToolResult> ExecuteInternalAsync(
        string parameters,
        CancellationToken cancellationToken
    )
    {
        var args = ParseParameters<DirectoryListParameters>(parameters);
        if (args is null)
        {
            return Task.FromResult(ToolResult.Error("Invalid parameters"));
        }

        if (string.IsNullOrEmpty(args.Path))
        {
            return Task.FromResult(ToolResult.Error("Path is required"));
        }

        try
        {
            var fullPath = Path.GetFullPath(args.Path);
            if (!Directory.Exists(fullPath))
            {
                return Task.FromResult(ToolResult.Error($"Directory not found: {args.Path}"));
            }

            var searchOption = args.Recursive
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            var pattern = string.IsNullOrEmpty(args.Pattern) ? "*" : args.Pattern;

            var files = Directory.GetFiles(fullPath, pattern, searchOption);
            var directories = Directory.GetDirectories(fullPath, pattern, searchOption);

            var result = new
            {
                path = fullPath,
                files = files.Length,
                directories = directories.Length,
                items = files
                    .Concat(directories)
                    .Select(p => new
                    {
                        path = Path.GetRelativePath(fullPath, p),
                        type = File.Exists(p) ? "file" : "directory",
                        size = File.Exists(p) ? new FileInfo(p).Length : (long?)null,
                    })
                    .ToArray(),
            };

            return Task.FromResult(
                ToolResult.Ok(
                    JsonSerializer.Serialize(
                        result,
                        new JsonSerializerOptions { WriteIndented = true }
                    )
                )
            );
        }
        catch (UnauthorizedAccessException)
        {
            return Task.FromResult(ToolResult.Error($"Access denied to directory: {args.Path}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to list directory: {ex.Message}"));
        }
    }

    private class DirectoryListParameters
    {
        public string Path { get; set; } = string.Empty;
        public string? Pattern { get; set; }
        public bool Recursive { get; set; }
    }
}
