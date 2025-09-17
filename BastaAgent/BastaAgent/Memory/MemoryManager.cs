using System.Text.Json;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using Microsoft.Extensions.Logging;

namespace BastaAgent.Memory;

/// <summary>
/// Manages conversation memory with token counting and compaction
/// </summary>
public class MemoryManager(
    ILogger<MemoryManager> logger,
    ILLMClient? llmClient = null,
    int maxTokens = 100000,
    int compactionThreshold = 80000
) : IMemoryManager
{
    private readonly List<MemoryEntry> _memories = [];
    private bool _isCompacting = false;
    private readonly int _configuredMaxTokens = maxTokens;

    /// <summary>
    /// Add a new memory entry
    /// </summary>
    public void AddMemory(MemoryEntry entry)
    {
        lock (_memories)
        {
            _memories.Add(entry);
        }
        logger.LogDebug("Added memory entry: {Type} at {Timestamp}", entry.Type, entry.Timestamp);

        // Check if compaction is needed (avoid recursive compaction)
        if (!_isCompacting)
        {
            var totalTokens = EstimateTotalTokens();
            if (totalTokens > compactionThreshold)
            {
                logger.LogInformation(
                    "Memory compaction triggered. Current tokens: {TotalTokens}",
                    totalTokens
                );

                // Run compaction asynchronously to avoid blocking (explicitly discard the Task)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await CompactMemoryAsync();
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Background memory compaction failed");
                    }
                });
            }
        }
    }

    /// <summary>
    /// Get all memories within token limit
    /// </summary>
    public List<MemoryEntry> GetRecentMemories(int maxTokens)
    {
        MemoryEntry[] snapshot;
        lock (_memories)
        {
            snapshot = _memories.ToArray();
        }

        var result = new List<MemoryEntry>();
        var currentTokens = 0;

        for (int i = snapshot.Length - 1; i >= 0; i--)
        {
            var entry = snapshot[i];
            var entryTokens = EstimateTokens(entry);

            if (currentTokens + entryTokens > maxTokens)
                break;

            result.Insert(0, entry);
            currentTokens += entryTokens;
        }

        return result;
    }

    /// <summary>
    /// Get memories for building context
    /// </summary>
    public string BuildContext(int maxTokens)
    {
        var effectiveMax = Math.Min(maxTokens, _configuredMaxTokens);
        var memories = GetRecentMemories(effectiveMax);
        var context = new List<string>();

        // Add system memories first (highest priority)
        foreach (var memory in memories.Where(m => m.Type == MemoryType.System))
        {
            context.Add($"[System] {memory.Content}");
        }

        // Add summaries
        foreach (var memory in memories.Where(m => m.Type == MemoryType.Summary))
        {
            context.Add($"[Summary] {memory.Content}");
        }

        // Add recent user/assistant exchanges
        foreach (
            var memory in memories.Where(m =>
                m.Type == MemoryType.User || m.Type == MemoryType.Assistant
            )
        )
        {
            var role = memory.Type == MemoryType.User ? "User" : "Assistant";
            context.Add($"[{role}] {memory.Content}");
        }

        return string.Join("\n\n", context);
    }

    /// <summary>
    /// Compact memory by summarizing older entries
    /// </summary>
    private async Task CompactMemoryAsync()
    {
        // Conference Note: Prevent concurrent compaction - this flag ensures
        // only one compaction happens at a time, preventing race conditions.
        if (_isCompacting)
            return;

        try
        {
            _isCompacting = true;

            // Conference Note: We always keep at least the last 10 user/assistant messages intact.
            // These are most relevant for immediate context and shouldn't be summarized.
            // We compute a cutoff index that preserves >=10 user/assistant messages (may include tools between them).
            const int keepRecentUserAssistant = 10;

            MemoryEntry[] snapshot;
            lock (_memories)
            {
                snapshot = _memories.ToArray();
            }

            if (snapshot.Length <= keepRecentUserAssistant)
                return; // Not enough memories to compact

            // Conference Note: Split memories into two groups:
            // - Old memories: Will be summarized to save tokens
            // - Recent memories: Kept as-is for full context
            // Determine cutoff index based on ensuring at least 10 user/assistant entries preserved
            int preservedUserAssistant = 0;
            int index = snapshot.Length - 1;
            for (; index >= 0; index--)
            {
                var t = snapshot[index].Type;
                if (t == MemoryType.User || t == MemoryType.Assistant)
                {
                    preservedUserAssistant++;
                    if (preservedUserAssistant >= keepRecentUserAssistant)
                    {
                        break;
                    }
                }
            }
            int cutIndex = Math.Max(0, index); // inclusive index of the earliest preserved item
            var oldMemories = snapshot.Take(cutIndex).ToList();
            var recentMemories = snapshot.Skip(cutIndex).ToList();

            // Conference Note: This is where the magic happens - we use an LLM
            // to create intelligent summaries of old conversations, preserving
            // key information while dramatically reducing token count.
            // Group old memories by conversation chunks
            var summaries = await SummarizeOldMemoriesAsync(oldMemories);

            // Conference Note: Thread-safe memory replacement using lock.
            // We completely rebuild the memory list with summaries + recent messages.
            // The lock prevents other threads from accessing memories during update.
            // Rebuild memory list
            lock (_memories)
            {
                _memories.Clear();
                _memories.AddRange(summaries); // Add compressed summaries first
                _memories.AddRange(recentMemories); // Then add recent full messages
            }

            logger.LogInformation(
                "Memory compacted: {OldCount} entries -> {SummaryCount} summaries",
                oldMemories.Count,
                summaries.Count
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to compact memory");
        }
        finally
        {
            _isCompacting = false;
        }
    }

    /// <summary>
    /// Summarize old memories into compact form
    /// </summary>
    private async Task<List<MemoryEntry>> SummarizeOldMemoriesAsync(List<MemoryEntry> memories)
    {
        var summaries = new List<MemoryEntry>();
        var currentChunk = new List<MemoryEntry>();
        var chunkTokens = 0;
        const int chunkSize = 2000; // Tokens per chunk

        // Conference Note: We chunk memories into ~2000 token groups for summarization.
        // This size is optimal for LLMs - large enough for context, small enough to process.
        foreach (var memory in memories)
        {
            var tokens = EstimateTokens(memory);

            // Conference Note: When a chunk gets too large, summarize it and start a new chunk.
            // This prevents sending too much text to the LLM at once.
            if (chunkTokens + tokens > chunkSize && currentChunk.Count > 0)
            {
                // Create summary for current chunk
                var summary = await CreateChunkSummaryAsync(currentChunk);
                summaries.Add(summary);

                currentChunk.Clear(); // Start fresh chunk
                chunkTokens = 0;
            }

            currentChunk.Add(memory);
            chunkTokens += tokens;
        }

        // Handle remaining chunk
        if (currentChunk.Count > 0)
        {
            var summary = await CreateChunkSummaryAsync(currentChunk);
            summaries.Add(summary);
        }

        return summaries;
    }

    /// <summary>
    /// Create a summary entry for a chunk of memories
    /// </summary>
    private async Task<MemoryEntry> CreateChunkSummaryAsync(List<MemoryEntry> chunk)
    {
        string summary;

        // Use LLM for summarization if available
        if (llmClient is not null)
        {
            try
            {
                var conversationText = string.Join(
                    "\n",
                    chunk.Select(m => $"[{m.Type}] {m.Content}")
                );

                var request = new ChatRequest
                {
                    Model = llmClient.GetModelForPurpose(ModelPurpose.Summarization),
                    Messages =
                    [
                        Message.System(
                            "You are a conversation summarizer. Create a concise summary of the following conversation that preserves key information, decisions, and action items."
                        ),
                        Message.User($"Summarize this conversation:\n\n{conversationText}"),
                    ],
                    Temperature = 0.3,
                    MaxTokens = 500,
                };

                var response = await llmClient.CompleteAsync(request);
                summary = response.GetMessage()?.Content ?? CreateFallbackSummary(chunk);

                logger.LogDebug("Created LLM-based summary for memory chunk");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to use LLM for summarization, using fallback");
                summary = CreateFallbackSummary(chunk);
            }
        }
        else
        {
            summary = CreateFallbackSummary(chunk);
        }

        return new MemoryEntry
        {
            Type = MemoryType.Summary,
            Content = summary,
            Timestamp = chunk.First().Timestamp,
            Metadata = new Dictionary<string, object>
            {
                ["originalCount"] = chunk.Count,
                ["startTime"] = chunk.First().Timestamp,
                ["endTime"] = chunk.Last().Timestamp,
                ["compactionMethod"] = llmClient is not null ? "llm" : "fallback",
            },
        };
    }

    /// <summary>
    /// Create a fallback summary when LLM is not available
    /// </summary>
    private string CreateFallbackSummary(List<MemoryEntry> chunk)
    {
        var userMessages = chunk.Where(m => m.Type == MemoryType.User).Select(m => m.Content);
        var assistantMessages = chunk
            .Where(m => m.Type == MemoryType.Assistant)
            .Select(m => m.Content);

        var summary =
            $"Conversation from {chunk.First().Timestamp:yyyy-MM-dd HH:mm} to {chunk.Last().Timestamp:HH:mm}:\n";
        summary += $"- User discussed: {string.Join(", ", userMessages.Take(3))}\n";
        summary += $"- Assistant provided: {string.Join(", ", assistantMessages.Take(3))}";

        return summary;
    }

    /// <summary>
    /// Estimate token count for a memory entry
    /// </summary>
    private int EstimateTokens(MemoryEntry entry)
    {
        // Simple estimation: ~4 characters per token
        if (string.IsNullOrEmpty(entry.Content))
            return 10; // Just metadata
        return (entry.Content.Length / 4) + 10; // +10 for metadata
    }

    /// <summary>
    /// Estimate total tokens in memory
    /// </summary>
    public int EstimateTotalTokens()
    {
        MemoryEntry[] snapshot;
        lock (_memories)
        {
            snapshot = _memories.ToArray();
        }
        return snapshot.Sum(EstimateTokens);
    }

    /// <summary>
    /// Clear all memories
    /// </summary>
    public void Clear()
    {
        lock (_memories)
        {
            _memories.Clear();
        }
        logger.LogInformation("Memory cleared");
    }

    /// <summary>
    /// Save memories to file
    /// </summary>
    public async Task SaveToFileAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create directory: {Directory}", directory);
                    throw;
                }
            }

            // Serialize to JSON
            string json;
            try
            {
                json = JsonSerializer.Serialize(
                    _memories,
                    new JsonSerializerOptions { WriteIndented = true }
                );
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Failed to serialize memories to JSON");
                throw;
            }

            // Write to file atomically
            var tempPath = path + ".tmp";
            try
            {
                await File.WriteAllTextAsync(tempPath, json, cancellationToken);
                File.Move(tempPath, path, overwrite: true);
                int count;
                lock (_memories)
                {
                    count = _memories.Count;
                }
                logger.LogInformation("Saved {Count} memories to {Path}", count, path);
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Failed to write memories to file: {Path}", path);
                // Clean up temp file if it exists
                if (File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch { }
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save memories to {Path}", path);
            throw;
        }
    }

    /// <summary>
    /// Load memories from file
    /// </summary>
    public async Task LoadFromFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            logger.LogWarning("Memory file not found: {Path}", path);
            return;
        }

        try
        {
            // Read file
            string json;
            try
            {
                json = await File.ReadAllTextAsync(path, cancellationToken);
            }
            catch (IOException ex)
            {
                logger.LogError(ex, "Failed to read memory file: {Path}", path);
                throw;
            }

            // Deserialize JSON
            List<MemoryEntry>? memories;
            try
            {
                memories = JsonSerializer.Deserialize<List<MemoryEntry>>(json);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Failed to deserialize memory file: {Path}", path);
                throw;
            }

            if (memories is not null)
            {
                _memories.Clear();
                _memories.AddRange(memories);
                logger.LogInformation("Loaded {Count} memories from {Path}", memories.Count, path);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load memories from {Path}", path);
            throw;
        }
    }

    /// <summary>
    /// Manually trigger memory compaction
    /// </summary>
    public async Task CompactAsync(CancellationToken cancellationToken = default)
    {
        await CompactMemoryAsync();
    }
}

/// <summary>
/// Interface for memory management
/// </summary>
public interface IMemoryManager
{
    void AddMemory(MemoryEntry entry);
    List<MemoryEntry> GetRecentMemories(int maxTokens);
    string BuildContext(int maxTokens);
    void Clear();
    Task SaveToFileAsync(string path, CancellationToken cancellationToken = default);
    Task LoadFromFileAsync(string path, CancellationToken cancellationToken = default);
    Task CompactAsync(CancellationToken cancellationToken = default);
    int EstimateTotalTokens();
}

/// <summary>
/// Represents a single memory entry
/// </summary>
public class MemoryEntry
{
    public MemoryType Type { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public Dictionary<string, object>? Metadata { get; set; }
}

/// <summary>
/// Types of memory entries
/// </summary>
public enum MemoryType
{
    System, // System prompts and instructions
    User, // User messages
    Assistant, // Assistant responses
    Tool, // Tool calls and results
    Summary, // Summarized conversations
    Context, // External context added
}
