using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace BastaAgent.Tools.BuiltIn;

/// <summary>
/// Tool for making web requests
/// </summary>
[Tool(Category = "Web", RequiresApproval = true)]
public class WebRequestTool(ILogger<WebRequestTool>? logger = null) : BaseTool(logger)
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    static WebRequestTool()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "BastaAgent/1.0");
    }

    public override string Name => "Web.Request";

    public override string Description =>
        "Make an HTTP request to a specified URL and retrieve the response";

    public override string ParametersSchema =>
        JsonSerializer.Serialize(
            new
            {
                type = "object",
                properties = new
                {
                    url = new { type = "string", description = "The URL to request" },
                    method = new
                    {
                        type = "string",
                        description = "HTTP method (GET, POST, PUT, DELETE)",
                        @default = "GET",
                        @enum = new[] { "GET", "POST", "PUT", "DELETE", "HEAD", "OPTIONS" },
                    },
                    headers = new
                    {
                        type = "object",
                        description = "Optional HTTP headers",
                        additionalProperties = new { type = "string" },
                    },
                    body = new
                    {
                        type = "string",
                        description = "Request body (for POST/PUT requests)",
                    },
                    timeout = new
                    {
                        type = "integer",
                        description = "Request timeout in seconds",
                        @default = 30,
                        minimum = 1,
                        maximum = 300,
                    },
                },
                required = new[] { "url" },
            }
        );

    protected override async Task<ToolResult> ExecuteInternalAsync(
        string parameters,
        CancellationToken cancellationToken
    )
    {
        var args = ParseParameters<WebRequestParameters>(parameters);
        if (args is null)
        {
            return ToolResult.Error("Invalid parameters");
        }

        if (string.IsNullOrEmpty(args.Url))
        {
            return ToolResult.Error("URL is required");
        }

        if (!Uri.TryCreate(args.Url, UriKind.Absolute, out var uri))
        {
            return ToolResult.Error($"Invalid URL: {args.Url}");
        }

        // Security check - only allow HTTP/HTTPS
        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            return ToolResult.Error($"Only HTTP/HTTPS URLs are allowed");
        }

        try
        {
            using var request = new HttpRequestMessage
            {
                Method = GetHttpMethod(args.Method ?? "GET"),
                RequestUri = uri,
            };

            // Add custom headers
            if (args.Headers is not null)
            {
                foreach (var header in args.Headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            // Add body for POST/PUT
            if (
                !string.IsNullOrEmpty(args.Body)
                && (request.Method == HttpMethod.Post || request.Method == HttpMethod.Put)
            )
            {
                request.Content = new StringContent(
                    args.Body,
                    System.Text.Encoding.UTF8,
                    "application/json"
                );
            }

            // Use timeout if specified
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (args.Timeout > 0)
            {
                cts.CancelAfter(TimeSpan.FromSeconds(args.Timeout));
            }

            var response = await _httpClient.SendAsync(request, cts.Token);
            var responseContent = await response.Content.ReadAsStringAsync(cts.Token);

            var metadata = new Dictionary<string, object>
            {
                ["statusCode"] = (int)response.StatusCode,
                ["statusText"] = response.ReasonPhrase ?? string.Empty,
                ["contentType"] = response.Content.Headers.ContentType?.ToString() ?? "unknown",
                ["contentLength"] = response.Content.Headers.ContentLength ?? -1,
            };

            if (response.IsSuccessStatusCode)
            {
                return ToolResult.Ok(responseContent, metadata);
            }
            else
            {
                return ToolResult.Error($"HTTP {(int)response.StatusCode}: {responseContent}");
            }
        }
        catch (TaskCanceledException)
        {
            return ToolResult.Error($"Request to {args.Url} timed out");
        }
        catch (HttpRequestException ex)
        {
            return ToolResult.Error($"HTTP request failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Unexpected error: {ex.Message}");
        }
    }

    private HttpMethod GetHttpMethod(string method)
    {
        return method.ToUpperInvariant() switch
        {
            "GET" => HttpMethod.Get,
            "POST" => HttpMethod.Post,
            "PUT" => HttpMethod.Put,
            "DELETE" => HttpMethod.Delete,
            "HEAD" => HttpMethod.Head,
            "OPTIONS" => HttpMethod.Options,
            _ => HttpMethod.Get,
        };
    }

    private class WebRequestParameters
    {
        public string Url { get; set; } = string.Empty;
        public string? Method { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public string? Body { get; set; }
        public int Timeout { get; set; }
    }
}

/// <summary>
/// Tool for searching the web
/// </summary>
[Tool(Category = "Web", RequiresApproval = false)]
public class WebSearchTool(ILogger<WebSearchTool>? logger = null) : BaseTool(logger)
{
    public override string Name => "Web.Search";

    public override string Description => "Search the web using DuckDuckGo (no API key required)";

    public override string ParametersSchema =>
        JsonSerializer.Serialize(
            new
            {
                type = "object",
                properties = new
                {
                    query = new { type = "string", description = "Search query" },
                    maxResults = new
                    {
                        type = "integer",
                        description = "Maximum number of results to return",
                        @default = 5,
                        minimum = 1,
                        maximum = 20,
                    },
                },
                required = new[] { "query" },
            }
        );

    protected override async Task<ToolResult> ExecuteInternalAsync(
        string parameters,
        CancellationToken cancellationToken
    )
    {
        var args = ParseParameters<WebSearchParameters>(parameters);
        if (args is null)
        {
            return ToolResult.Error("Invalid parameters");
        }

        if (string.IsNullOrEmpty(args.Query))
        {
            return ToolResult.Error("Query is required");
        }

        try
        {
            // Note: This is a simplified implementation
            // In production, you'd want to use a proper search API
            // For demo purposes, we'll return mock results

            _logger?.LogInformation("Searching for: {Query}", args.Query);

            // Simulate search delay
            await Task.Delay(500, cancellationToken);

            var results = new[]
            {
                new
                {
                    title = $"Result 1 for '{args.Query}'",
                    url = "https://example.com/1",
                    snippet = $"This is a sample result for the query '{args.Query}'. It contains relevant information.",
                },
                new
                {
                    title = $"Result 2 for '{args.Query}'",
                    url = "https://example.com/2",
                    snippet = $"Another result matching '{args.Query}' with different content.",
                },
                new
                {
                    title = $"Result 3 for '{args.Query}'",
                    url = "https://example.com/3",
                    snippet = $"A third result for '{args.Query}' demonstrating search capabilities.",
                },
            };

            var maxResults = args.MaxResults > 0 ? Math.Min(args.MaxResults, results.Length) : 5;
            var limitedResults = results.Take(maxResults);

            return ToolResult.Ok(
                JsonSerializer.Serialize(
                    limitedResults,
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
        }
        catch (Exception ex)
        {
            return ToolResult.Error($"Search failed: {ex.Message}");
        }
    }

    private class WebSearchParameters
    {
        public string Query { get; set; } = string.Empty;
        public int MaxResults { get; set; } = 5;
    }
}
