using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using BastaAgent.LLM.Models;
using Microsoft.Extensions.Logging;

namespace BastaAgent.LLM;

/// <summary>
/// Handles streaming responses from the LLM with reasoning extraction
/// </summary>
public partial class StreamingHandler(ILogger<StreamingHandler> logger) : IStreamingHandler
{
    private readonly StringBuilder _fullContent = new();
    private readonly StringBuilder _buffer = new();
    private bool _inReasoningBlock;
    private string _currentReasoningType = string.Empty;

    // Regex patterns for extracting structured reasoning
    private static readonly Regex ReasoningStartPattern = ReasoningStartRegex();
    private static readonly Regex ReasoningEndPattern = ReasoningEndRegex();
    private static readonly Regex StepPattern = StepRegex();

    /// <summary>
    /// Event raised when a reasoning step is detected
    /// </summary>
    public event EventHandler<ReasoningStepEventArgs>? ReasoningStepDetected;

    /// <summary>
    /// Event raised when content is received (non-reasoning)
    /// </summary>
    public event EventHandler<ContentEventArgs>? ContentReceived;

    /// <summary>
    /// Initialize the streaming handler
    /// </summary>
    private readonly ILogger<StreamingHandler> _logger = logger;

    /// <summary>
    /// Process a stream of chat responses
    /// </summary>
    public async IAsyncEnumerable<ProcessedChunk> ProcessStreamAsync(
        IAsyncEnumerable<StreamingChatResponse> stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        await foreach (var chunk in stream.WithCancellation(cancellationToken))
        {
            if (chunk.Choices?.Count > 0)
            {
                var delta = chunk.Choices[0].Delta;
                if (delta?.Content is not null)
                {
                    // Add to buffer and process
                    _buffer.Append(delta.Content);
                    _fullContent.Append(delta.Content);

                    // Process the buffer
                    foreach (var processed in ProcessBuffer())
                    {
                        if (processed is not null)
                            yield return processed;
                    }
                }

                // Handle tool calls in streaming
                if (delta?.ToolCalls?.Count > 0)
                {
                    try
                    {
                        foreach (var t in delta.ToolCalls)
                        {
                            var name = t.Function?.Name ?? string.Empty;
                            var id = t.Id ?? string.Empty;
                            var idx = t.Index?.ToString() ?? "";
                            var args = t.Function?.Arguments ?? string.Empty;
                            _logger.LogDebug(
                                "Stream tool_call delta: name={Name} id={Id} index={Index} argsLen={Len}",
                                name,
                                id,
                                idx,
                                args.Length
                            );
                        }
                    }
                    catch { }
                    yield return new ProcessedChunk
                    {
                        Type = ChunkType.ToolCall,
                        Content = "Tool call detected",
                        ToolCalls = delta.ToolCalls,
                    };
                }
            }
        }

        // Flush any remaining content
        foreach (var processed in FlushBuffer())
        {
            if (processed is not null)
                yield return processed;
        }
    }

    /// <summary>
    /// Process the current buffer content
    /// </summary>
    private IEnumerable<ProcessedChunk> ProcessBuffer()
    {
        var bufferContent = _buffer.ToString();

        // Conference Note: This state machine tracks whether we're inside <reasoning> tags.
        // Some models (like o1) output their thinking process in special tags.
        // We extract and display this separately from the main response.
        if (!_inReasoningBlock)
        {
            // Conference Note: Regex patterns detect special reasoning tags in the stream.
            // Pattern: <reasoning type="planning"> or just <reasoning>
            // Check for reasoning start
            var startMatch = ReasoningStartPattern.Match(bufferContent);
            if (startMatch.Success)
            {
                // Conference Note: Content before the reasoning tag is regular response text.
                // We emit it immediately so users see output as it streams.
                // Emit content before reasoning if any
                if (startMatch.Index > 0)
                {
                    var beforeContent = bufferContent.Substring(0, startMatch.Index);
                    ContentReceived?.Invoke(this, new ContentEventArgs { Content = beforeContent });
                    yield return new ProcessedChunk
                    {
                        Type = ChunkType.Content,
                        Content = beforeContent,
                    };
                }

                _inReasoningBlock = true;
                _currentReasoningType = !string.IsNullOrEmpty(startMatch.Groups[1].Value)
                    ? startMatch.Groups[1].Value
                    : "general";

                yield return new ProcessedChunk
                {
                    Type = ChunkType.ReasoningStart,
                    Content = $"Starting {_currentReasoningType} reasoning...",
                    ReasoningType = _currentReasoningType,
                };

                // Conference Note: Buffer management - we only keep unparsed content.
                // This prevents re-processing the same text multiple times.
                // Keep only content after the start tag
                _buffer.Clear();
                _buffer.Append(bufferContent.Substring(startMatch.Index + startMatch.Length));
            }
        }
        else
        {
            // Conference Note: When inside reasoning tags, we look for the closing tag.
            // Everything between <reasoning> and </reasoning> is the model's thought process.
            // Check for reasoning end
            var endMatch = ReasoningEndPattern.Match(bufferContent);
            if (endMatch.Success)
            {
                // Extract reasoning content
                var reasoningContent = bufferContent.Substring(0, endMatch.Index);

                // Conference Note: Models can output structured steps like:
                // <step type="analysis">Analyzing the user's request...</step>
                // We extract and display these for transparency.
                // Check for steps and emit them
                var stepMatches = StepPattern.Matches(reasoningContent);
                foreach (Match stepMatch in stepMatches)
                {
                    var stepType = stepMatch.Groups[1].Value ?? "step";
                    var stepContent = stepMatch.Groups[2].Value.Trim();
                    var displayContent = TruncateContent(stepContent, 40);

                    yield return new ProcessedChunk
                    {
                        Type = ChunkType.ReasoningStep,
                        Content = $"[{stepType}] {displayContent}",
                        ReasoningType = _currentReasoningType,
                    };
                }

                // Generate summary
                var summary = ExtractReasoningSummary(reasoningContent);

                ReasoningStepDetected?.Invoke(
                    this,
                    new ReasoningStepEventArgs
                    {
                        Type = _currentReasoningType,
                        FullContent = reasoningContent,
                        Summary = summary,
                    }
                );

                yield return new ProcessedChunk
                {
                    Type = ChunkType.ReasoningComplete,
                    Content = summary,
                    ReasoningType = _currentReasoningType,
                };

                _inReasoningBlock = false;

                // Process content after reasoning block
                var afterContent = bufferContent.Substring(endMatch.Index + endMatch.Length);
                _buffer.Clear();
                if (!string.IsNullOrEmpty(afterContent))
                {
                    _buffer.Append(afterContent);
                    // Recursively process remaining content
                    foreach (var processed in ProcessBuffer())
                    {
                        yield return processed;
                    }
                }
            }
        }
    }

    /// <summary>
    /// Flush any remaining buffer content
    /// </summary>
    private IEnumerable<ProcessedChunk> FlushBuffer()
    {
        if (_buffer.Length > 0)
        {
            var content = _buffer.ToString();

            if (_inReasoningBlock)
            {
                // Incomplete reasoning block - just summarize what we have
                var summary = ExtractReasoningSummary(content);
                yield return new ProcessedChunk
                {
                    Type = ChunkType.ReasoningComplete,
                    Content = summary,
                    ReasoningType = _currentReasoningType,
                };
            }
            else
            {
                // Regular content
                ContentReceived?.Invoke(this, new ContentEventArgs { Content = content });
                yield return new ProcessedChunk { Type = ChunkType.Content, Content = content };
            }

            _buffer.Clear();
        }
    }

    /// <summary>
    /// Extract a summary from reasoning content
    /// </summary>
    private string ExtractReasoningSummary(string reasoningContent)
    {
        // Extract step summaries
        var steps = new List<string>();
        var stepMatches = StepPattern.Matches(reasoningContent);

        foreach (Match match in stepMatches)
        {
            var stepType = match.Groups[1].Value ?? "step";
            var stepContent = match.Groups[2].Value.Trim();

            // Summarize common reasoning patterns
            var summary = SummarizeStep(stepType, stepContent);
            if (!string.IsNullOrEmpty(summary))
            {
                steps.Add(summary);
            }
        }

        if (steps.Count > 0)
        {
            return string.Join(" → ", steps);
        }

        // Fallback: extract first meaningful sentence
        var firstSentence = ExtractFirstSentence(reasoningContent);
        return firstSentence ?? "Processing...";
    }

    /// <summary>
    /// Summarize a reasoning step based on type
    /// </summary>
    private string SummarizeStep(string stepType, string content)
    {
        return stepType.ToLowerInvariant() switch
        {
            "planning" => $"Planning: {TruncateContent(content, 50)}",
            "tool_selection" => $"Selecting tool: {ExtractToolName(content)}",
            "analysis" => $"Analyzing: {TruncateContent(content, 40)}",
            "decision" => $"Decision: {TruncateContent(content, 40)}",
            "execution" => $"Executing: {TruncateContent(content, 40)}",
            _ => TruncateContent(content, 60),
        };
    }

    /// <summary>
    /// Extract tool name from content
    /// </summary>
    private string ExtractToolName(string content)
    {
        // Look for common patterns like "using FileSystemTool" or "FileSystemTool.Read"
        var toolMatch = Regex.Match(content, @"\b(\w+Tool)\b");
        if (toolMatch.Success)
        {
            return toolMatch.Groups[1].Value;
        }

        return TruncateContent(content, 30);
    }

    /// <summary>
    /// Truncate content to specified length
    /// </summary>
    private static string TruncateContent(string content, int maxLength)
    {
        if (content.Length <= maxLength)
            return content;

        return content[..(maxLength - 3)] + "...";
    }

    /// <summary>
    /// Extract first sentence from content
    /// </summary>
    private static string? ExtractFirstSentence(string content)
    {
        var match = Regex.Match(content, @"^[^.!?]+[.!?]");
        return match.Success ? match.Value.Trim() : null;
    }

    /// <summary>
    /// Get the full accumulated content
    /// </summary>
    public string GetFullContent()
    {
        return _fullContent.ToString();
    }

    /// <summary>
    /// Reset the handler for a new stream
    /// </summary>
    public void Reset()
    {
        _fullContent.Clear();
        _buffer.Clear();
        _inReasoningBlock = false;
        _currentReasoningType = string.Empty;
    }

    [GeneratedRegex(@"<reasoning(?:\s+type=""([^""]+)"")?>", RegexOptions.Compiled)]
    private static partial Regex ReasoningStartRegex();

    [GeneratedRegex(@"</reasoning>", RegexOptions.Compiled)]
    private static partial Regex ReasoningEndRegex();

    [GeneratedRegex(@"<step(?:\s+type=""([^""]+)"")?>([^<]+)</step>", RegexOptions.Compiled)]
    private static partial Regex StepRegex();
}

/// <summary>
/// Interface for streaming handler
/// </summary>
public interface IStreamingHandler
{
    /// <summary>
    /// Process a stream of chat responses
    /// </summary>
    IAsyncEnumerable<ProcessedChunk> ProcessStreamAsync(
        IAsyncEnumerable<StreamingChatResponse> stream,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Get the full accumulated content
    /// </summary>
    string GetFullContent();

    /// <summary>
    /// Reset the handler
    /// </summary>
    void Reset();

    /// <summary>
    /// Event raised when a reasoning step is detected
    /// </summary>
    event EventHandler<ReasoningStepEventArgs>? ReasoningStepDetected;

    /// <summary>
    /// Event raised when content is received
    /// </summary>
    event EventHandler<ContentEventArgs>? ContentReceived;
}

/// <summary>
/// Processed chunk from the stream
/// </summary>
public class ProcessedChunk
{
    /// <summary>
    /// Type of chunk
    /// </summary>
    public ChunkType Type { get; set; }

    /// <summary>
    /// Content of the chunk
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// Reasoning type if applicable
    /// </summary>
    public string? ReasoningType { get; set; }

    /// <summary>
    /// Tool calls if applicable
    /// </summary>
    public List<ToolCall>? ToolCalls { get; set; }
}

/// <summary>
/// Type of processed chunk
/// </summary>
public enum ChunkType
{
    Content,
    ReasoningStart,
    ReasoningStep,
    ReasoningComplete,
    ToolCall,
}

/// <summary>
/// Event args for reasoning step events
/// </summary>
public class ReasoningStepEventArgs : EventArgs
{
    public string Type { get; set; } = string.Empty;
    public string FullContent { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}

/// <summary>
/// Event args for content events
/// </summary>
public class ContentEventArgs : EventArgs
{
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; } = DateTime.UtcNow;
}
