using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BastaAgent.Core;
using BastaAgent.LLM.Models;
using BastaAgent.Utilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BastaAgent.LLM;

/// <summary>
/// Client for interacting with OpenAI-compatible LLM APIs.
///
/// <para><b>Conference Note - Universal LLM Client:</b></para>
/// <para>This client works with any OpenAI-compatible API, demonstrating key patterns:</para>
/// <list type="bullet">
/// <item><b>Retry Logic:</b> Exponential backoff for handling API rate limits</item>
/// <item><b>Streaming:</b> Server-Sent Events (SSE) for real-time responses</item>
/// <item><b>Prompt Caching:</b> Anthropic's caching for faster responses</item>
/// <item><b>Multi-Model:</b> Different models for reasoning vs execution</item>
/// </list>
///
/// <para><b>Supported Providers:</b></para>
/// <list type="bullet">
/// <item>OpenAI (GPT-3.5, GPT-4)</item>
/// <item>Anthropic (Claude via OpenRouter)</item>
/// <item>Ollama (local models)</item>
/// <item>Any OpenAI-compatible endpoint</item>
/// </list>
/// </summary>
public class LLMClient : ILLMClient
{
    private readonly HttpClient _httpClient;
    private readonly AgentConfiguration _config;
    private readonly ILogger<LLMClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initialize a new LLM client
    /// </summary>
    public LLMClient(
        HttpClient httpClient,
        IOptions<AgentConfiguration> options,
        ILogger<LLMClient> logger
    )
    {
        _httpClient = httpClient;
        _config = options.Value;
        _logger = logger;

        // Configure JSON serialization options
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System
                .Text
                .Json
                .Serialization
                .JsonIgnoreCondition
                .WhenWritingNull,
            WriteIndented = false,
        };

        // Configure HTTP client
        ConfigureHttpClient();
    }

    /// <summary>
    /// Configure the HTTP client with base URL and headers
    /// </summary>
    private void ConfigureHttpClient()
    {
        if (!string.IsNullOrEmpty(_config.API.BaseUrl))
        {
            var baseUrl = _config.API.BaseUrl.Trim();
            if (!baseUrl.EndsWith('/'))
            {
                baseUrl += "/"; // Ensure trailing slash so relative paths append correctly
            }
            _httpClient.BaseAddress = new Uri(baseUrl);
        }

        _httpClient.DefaultRequestHeaders.Clear();

        // Determine if provider requires authentication
        var host = _httpClient.BaseAddress?.Host?.ToLowerInvariant() ?? string.Empty;
        var requiresAuth = host.Contains("openrouter.ai") || host.Contains("api.openai.com");

        // Normalize API key and detect placeholders
        var apiKey = _config.API.ApiKey?.Trim() ?? string.Empty;
        var isPlaceholder = apiKey.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
        var hasKey =
            !string.IsNullOrEmpty(apiKey)
            && !string.Equals(apiKey, "none", StringComparison.OrdinalIgnoreCase)
            && !isPlaceholder;

        if (requiresAuth && !hasKey)
        {
            // Fail fast with a clear message for providers that require a key
            throw new InvalidOperationException(ErrorMessages.LLM.AuthenticationFailed());
        }

        if (hasKey)
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                apiKey
            );
        }

        // Add common headers
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "BastaAgent/1.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json")
        );

        // OpenRouter requires identifying headers (either Referer or X-Title)
        if (host.Contains("openrouter.ai"))
        {
            if (!_httpClient.DefaultRequestHeaders.Contains("Referer"))
            {
                _httpClient.DefaultRequestHeaders.Add(
                    "Referer",
                    "https://github.com/gingter/basta-2025-fall"
                );
            }
            if (!_httpClient.DefaultRequestHeaders.Contains("X-Title"))
            {
                _httpClient.DefaultRequestHeaders.Add("X-Title", "BastaAgent Demo");
            }
        }

        // Set timeout
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.API.Timeout);
    }

    /// <summary>
    /// Send a chat completion request
    /// </summary>
    public async Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation("Sending chat completion request to model: {Model}", request.Model);

        // Ensure stream is false for non-streaming requests
        request.Stream = false;

        // Add prompt caching if enabled
        AddPromptCachingHeaders(request);

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            var response = await SendWithRetryAsync(
                async () =>
                    await _httpClient.PostAsync("chat/completions", content, cancellationToken),
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "HTTP {Status} {Reason}. Body (first 500 chars): {Snippet}",
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    body.Length > 500 ? body[..500] : body
                );
                response.EnsureSuccessStatusCode();
            }

            // Validate content type is JSON; if not, include body snippet in logs
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Unexpected content type: {MediaType}. Body (first 200 chars): {Snippet}",
                    mediaType,
                    responseJson.Length > 200 ? responseJson[..200] : responseJson
                );
            }
            var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseJson, _jsonOptions);

            if (chatResponse is null)
            {
                throw new InvalidOperationException("Received null response from API");
            }

            // Log token usage
            if (chatResponse.Usage is not null)
            {
                _logger.LogInformation(
                    "Token usage - Prompt: {PromptTokens}, Completion: {CompletionTokens}, Total: {TotalTokens}",
                    chatResponse.Usage.PromptTokens,
                    chatResponse.Usage.CompletionTokens,
                    chatResponse.Usage.TotalTokens
                );

                // Log cache statistics if available
                if (chatResponse.Usage.CacheReadInputTokens > 0)
                {
                    _logger.LogInformation(
                        "Cache hit! Read {CacheTokens} tokens from cache (saved {Percentage:P0} of prompt tokens)",
                        chatResponse.Usage.CacheReadInputTokens,
                        (double)chatResponse.Usage.CacheReadInputTokens
                            / chatResponse.Usage.PromptTokens
                    );
                }

                if (chatResponse.Usage.CacheCreationInputTokens > 0)
                {
                    _logger.LogInformation(
                        "Created cache entry with {CacheTokens} tokens",
                        chatResponse.Usage.CacheCreationInputTokens
                    );
                }
            }

            return chatResponse;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed");
            var errorMessage = ErrorMessages.LLM.CommunicationFailed(
                _httpClient.BaseAddress?.ToString() ?? "unknown",
                ex.Message
            );
            throw new InvalidOperationException(errorMessage, ex);
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Request timed out");
            var errorMessage = ErrorMessages.LLM.RequestTimeout(
                _config.API.Timeout,
                request.Model ?? "unknown"
            );
            throw new InvalidOperationException(errorMessage, ex);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse response");
            var errorMessage = ErrorMessages.LLM.InvalidResponse(request.Model ?? "unknown");
            throw new InvalidOperationException(errorMessage, ex);
        }
    }

    /// <summary>
    /// Stream a chat completion response
    /// </summary>
    public async IAsyncEnumerable<StreamingChatResponse> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation("Starting streaming request to model: {Model}", request.Model);

        // Enable streaming
        request.Stream = true;

        // Add prompt caching if enabled
        AddPromptCachingHeaders(request);

        var json = JsonSerializer.Serialize(request, _jsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        try
        {
            response = await SendWithRetryAsync(
                async () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
                    {
                        Content = content,
                    };
                    // For streaming, request event-stream explicitly
                    request.Headers.Accept.Clear();
                    request.Headers.Accept.Add(
                        new MediaTypeWithQualityHeaderValue("text/event-stream")
                    );
                    return await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead,
                        cancellationToken
                    );
                },
                cancellationToken
            );

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "HTTP {Status} {Reason} on stream. Body (first 500 chars): {Snippet}",
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    errorBody.Length > 500 ? errorBody[..500] : errorBody
                );
                response.EnsureSuccessStatusCode();
            }

            // Ensure we actually got an SSE stream
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!mediaType.Contains("event-stream", StringComparison.OrdinalIgnoreCase))
            {
                // Read body for diagnostics
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError(
                    "Expected event-stream, got {MediaType}. Body (first 200 chars): {Snippet}",
                    mediaType,
                    body.Length > 200 ? body[..200] : body
                );
                throw new InvalidOperationException(
                    ErrorMessages.LLM.InvalidResponse(request.Model ?? "unknown")
                );
            }

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream);

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) is not null)
            {
                // Skip empty lines
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                // Parse SSE format
                if (line.StartsWith(":"))
                {
                    // SSE comment (e.g., ": OPENROUTER PROCESSING") – ignore
                    continue;
                }
                if (line.StartsWith("data: "))
                {
                    var data = line.Substring(6); // Remove "data: " prefix

                    // Check for end of stream
                    if (data == "[DONE]")
                    {
                        _logger.LogDebug("Stream completed");
                        yield break;
                    }

                    // Parse the JSON chunk
                    StreamingChatResponse? chunk = null;
                    try
                    {
                        chunk = JsonSerializer.Deserialize<StreamingChatResponse>(
                            data,
                            _jsonOptions
                        );
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "Failed to parse streaming chunk: {Data}", data);
                        continue;
                    }

                    if (chunk is not null)
                    {
                        yield return chunk;
                    }
                }
            }
        }
        finally
        {
            response?.Dispose();
        }
    }

    /// <summary>
    /// Send HTTP request with retry logic.
    ///
    /// <para><b>Conference Note - Production-Grade Retry Logic:</b></para>
    /// <para>This demonstrates essential patterns for reliable API interactions:</para>
    /// <list type="bullet">
    /// <item><b>Exponential Backoff:</b> Delay doubles after each retry (1s, 2s, 4s, 8s...)</item>
    /// <item><b>Transient Fault Handling:</b> Retries on 429 (rate limit), 500+ (server errors)</item>
    /// <item><b>Timeout Recovery:</b> Retries on timeout unless explicitly cancelled</item>
    /// <item><b>Maximum Attempts:</b> Configurable retry limit to prevent infinite loops</item>
    /// </list>
    ///
    /// <para>Common scenarios handled:</para>
    /// <list type="bullet">
    /// <item>429 Too Many Requests - API rate limiting</item>
    /// <item>503 Service Unavailable - Temporary overload</item>
    /// <item>Network timeouts - Connection issues</item>
    /// </list>
    /// </summary>
    private async Task<HttpResponseMessage> SendWithRetryAsync(
        Func<Task<HttpResponseMessage>> sendFunc,
        CancellationToken cancellationToken
    )
    {
        var maxRetries = _config.API.MaxRetries;
        var delay = _config.API.RetryDelayMilliseconds;

        // Conference Note: This retry loop handles the most common LLM API failures.
        // We retry up to maxRetries times (default 5) with exponential backoff.
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                // Attempt to send the HTTP request
                var response = await sendFunc();

                // Conference Note: ShouldRetry() checks for retryable status codes:
                // - 429 (Too Many Requests) - Rate limiting
                // - 503 (Service Unavailable) - Temporary overload
                // - 500+ (Server Errors) - Transient server issues
                // Check if we should retry based on status code
                if (attempt < maxRetries && ShouldRetry(response))
                {
                    // Check for specific error codes to provide better messages
                    if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        // Try to get retry-after header
                        var retryAfter =
                            response.Headers.RetryAfter?.Delta?.TotalSeconds ?? delay / 1000;
                        _logger.LogWarning(
                            "Rate limited. Retrying in {Delay}ms (attempt {Attempt}/{MaxRetries})",
                            delay,
                            attempt + 1,
                            maxRetries
                        );
                    }
                    else if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                    {
                        _logger.LogError("Authentication failed - check API key");
                        throw new InvalidOperationException(
                            ErrorMessages.LLM.AuthenticationFailed()
                        );
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Request failed with status {StatusCode}. Retrying in {Delay}ms (attempt {Attempt}/{MaxRetries})",
                            response.StatusCode,
                            delay,
                            attempt + 1,
                            maxRetries
                        );
                    }

                    await Task.Delay(delay, cancellationToken);

                    // Conference Note: Exponential backoff prevents overwhelming the API.
                    // Delays double each time: 1s -> 2s -> 4s -> 8s -> 16s
                    // This gives the API time to recover from overload.
                    // Exponential backoff
                    delay *= 2;
                    continue;
                }

                return response;
            }
            // Conference Note: HttpRequestException indicates network-level failures.
            // Common causes: DNS resolution failed, connection refused, network unreachable.
            // The 'when' clause ensures we only catch if we have retries left.
            catch (HttpRequestException) when (attempt < maxRetries)
            {
                _logger.LogWarning(
                    "Request failed. Retrying in {Delay}ms (attempt {Attempt}/{MaxRetries})",
                    delay,
                    attempt + 1,
                    maxRetries
                );

                await Task.Delay(delay, cancellationToken);
                delay *= 2; // Double the delay for exponential backoff
            }
            // Conference Note: TaskCanceledException can mean two things:
            // 1. User pressed ESC (cancellationToken.IsCancellationRequested = true) - don't retry
            // 2. HTTP timeout (cancellationToken.IsCancellationRequested = false) - do retry
            // This distinction is crucial for proper timeout handling!
            catch (TaskCanceledException)
                when (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
            {
                // Timeout - retry if not explicitly cancelled
                _logger.LogWarning(
                    "Request timed out. Retrying in {Delay}ms (attempt {Attempt}/{MaxRetries})",
                    delay,
                    attempt + 1,
                    maxRetries
                );

                await Task.Delay(delay, cancellationToken);
                delay *= 2; // Continue exponential backoff
            }
        }

        // Final attempt
        return await sendFunc();
    }

    /// <summary>
    /// Determine if a response should trigger a retry
    /// </summary>
    private bool ShouldRetry(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;

        // Retry on server errors (5xx) and specific client errors
        return statusCode >= 500
            || statusCode == 429
            || // Too Many Requests
            statusCode == 408; // Request Timeout
    }

    /// <summary>
    /// Add prompt caching headers to the request for Anthropic Claude models
    /// This helps reduce latency and costs by caching system prompts
    /// </summary>
    private void AddPromptCachingHeaders(ChatRequest request)
    {
        // Only apply caching for Claude models
        if (!request.Model?.Contains("claude", StringComparison.OrdinalIgnoreCase) ?? true)
        {
            return;
        }

        // Add caching to system messages (they rarely change)
        if (request.Messages is not null)
        {
            foreach (var message in request.Messages)
            {
                if (message.Role == "system")
                {
                    // Add cache control for system messages
                    // This is specific to Anthropic's API
                    message.CacheControl = new CacheControl { Type = "ephemeral" };

                    _logger.LogDebug("Added prompt caching to system message");
                }
            }
        }

        // Add cache control for tool definitions if present
        // Tools definitions are also good candidates for caching
        if (request.Tools?.Count > 0)
        {
            // Note: Tool caching would require API support
            // This is a placeholder for when the API supports it
            _logger.LogDebug("Tool definitions present - would cache if API supported");
        }
    }

    /// <summary>
    /// Get model for specific purpose
    /// </summary>
    public string GetModelForPurpose(ModelPurpose purpose)
    {
        return purpose switch
        {
            ModelPurpose.Reasoning => _config.Models.Reasoning,
            ModelPurpose.Execution => _config.Models.Execution,
            ModelPurpose.Summarization => _config.Models.Summarization,
            _ => _config.Models.Execution,
        };
    }
}

/// <summary>
/// Interface for LLM client
/// </summary>
public interface ILLMClient
{
    /// <summary>
    /// Send a chat completion request
    /// </summary>
    Task<ChatResponse> CompleteAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Stream a chat completion response
    /// </summary>
    IAsyncEnumerable<StreamingChatResponse> StreamAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get the appropriate model for a specific purpose
    /// </summary>
    string GetModelForPurpose(ModelPurpose purpose);
}

/// <summary>
/// Model purpose enumeration
/// </summary>
public enum ModelPurpose
{
    /// <summary>
    /// For planning and reasoning about tasks
    /// </summary>
    Reasoning,

    /// <summary>
    /// For executing tasks
    /// </summary>
    Execution,

    /// <summary>
    /// For summarizing conversations
    /// </summary>
    Summarization,
}
