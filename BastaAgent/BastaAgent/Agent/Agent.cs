using System.Text.Json;
using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using BastaAgent.Memory;
using BastaAgent.Tools;
using BastaAgent.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BastaAgent.Agent;

/// <summary>
/// Main agent implementation that orchestrates LLM, tools, and memory.
///
/// <para><b>Conference Note - Core Concepts:</b></para>
/// <para>This is the heart of our AI agent system, demonstrating key patterns for BASTA 2025:</para>
/// <list type="bullet">
/// <item>Orchestration between LLM, tools, and memory management</item>
/// <item>State persistence with auto-save after every interaction</item>
/// <item>Multi-model support (reasoning vs execution models)</item>
/// <item>Tool calling with reflection-based discovery</item>
/// <item>Streaming responses with real-time reasoning display</item>
/// </list>
///
/// <para><b>The Agent Loop:</b></para>
/// <list type="number">
/// <item>User sends a message</item>
/// <item>Agent adds it to memory and saves state</item>
/// <item>Agent sends to LLM with full context</item>
/// <item>LLM responds with text or tool calls</item>
/// <item>Agent executes tools (with user approval)</item>
/// <item>Results go back to LLM for final response</item>
/// <item>State is saved after each step</item>
/// </list>
/// </summary>
public class Agent : IAgent
{
    private readonly ILogger<Agent> _logger;
    private readonly ILLMClient _llmClient;
    private readonly IStreamingHandler _streamingHandler;
    private readonly IToolRegistry _toolRegistry;
    private readonly IMemoryManager _memoryManager;
    private readonly IToolApprovalManager _approvalManager;
    private readonly InteractiveConsole _console;
    private readonly AgentConfiguration _configuration;
    private AgentState _state;
    private string _systemPrompt = "You are a helpful AI assistant.";

    /// <summary>
    /// Creates a new agent instance with dependency injection.
    ///
    /// <para><b>Conference Note - Dependency Injection:</b></para>
    /// <para>We use constructor injection (a DI pattern) to keep our components loosely coupled and testable.</para>
    /// <para>Each dependency serves a specific purpose:</para>
    /// <list type="bullet">
    /// <item><b>ILogger:</b> Structured logging throughout the agent</item>
    /// <item><b>ILLMClient:</b> Handles all communication with the LLM API</item>
    /// <item><b>IStreamingHandler:</b> Processes streaming responses and extracts reasoning</item>
    /// <item><b>IToolRegistry:</b> Discovers and manages available tools via reflection</item>
    /// <item><b>IMemoryManager:</b> Manages conversation history and compaction</item>
    /// <item><b>IToolApprovalManager:</b> Handles user approval for tool executions</item>
    /// <item><b>InteractiveConsole:</b> Manages the CLI user interface</item>
    /// <item><b>IOptions&lt;AgentConfiguration&gt;:</b> Configuration from appsettings.json</item>
    /// </list>
    /// </summary>
    public Agent(
        ILogger<Agent> logger,
        ILLMClient llmClient,
        IStreamingHandler streamingHandler,
        IToolRegistry toolRegistry,
        IMemoryManager memoryManager,
        IToolApprovalManager approvalManager,
        InteractiveConsole console,
        IOptions<AgentConfiguration> configuration
    )
    {
        _logger = logger;
        _llmClient = llmClient;
        _streamingHandler = streamingHandler;
        _toolRegistry = toolRegistry;
        _memoryManager = memoryManager;
        _approvalManager = approvalManager;
        _console = console;
        _configuration = configuration.Value;
        _state = new AgentState();

        // Load system prompt from Prompts/system.md if available
        try
        {
            var promptPath = Path.Combine("Prompts", "system.md");
            if (File.Exists(promptPath))
            {
                _systemPrompt = File.ReadAllText(promptPath);
                _logger.LogInformation("Loaded system prompt from {PromptPath}", promptPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to load system prompt from Prompts/system.md; using default."
            );
        }

        // Wire up streaming handler events for reasoning display
        _streamingHandler.ReasoningStepDetected += OnReasoningStepDetected;
        _streamingHandler.ContentReceived += OnContentReceived;
    }

    /// <summary>
    /// Process a user message and generate a response.
    ///
    /// <para><b>Conference Note - Main Entry Point:</b></para>
    /// <para>This is where all user interactions begin. The method demonstrates several key patterns:</para>
    /// <list type="bullet">
    /// <item><b>Auto-save:</b> State is persisted after EVERY interaction for crash recovery</item>
    /// <item><b>Memory management:</b> Messages are added to both memory and state</item>
    /// <item><b>Context building:</b> Recent conversation history is included up to token limits</item>
    /// <item><b>Tool integration:</b> Tools are passed to the LLM as available functions</item>
    /// <item><b>Cancellation support:</b> Users can press ESC to cancel operations</item>
    /// </list>
    /// </summary>
    /// <param name="userMessage">The message from the user</param>
    /// <param name="cancellationToken">Token to cancel the operation (triggered by ESC key)</param>
    /// <returns>The agent's response text</returns>
    public async Task<string> ProcessMessageAsync(
        string userMessage,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ProcessingStarted?.Invoke(this, new AgentEventArgs(userMessage));

            // Conference Note: We immediately persist every interaction for crash recovery.
            // This ensures we never lose conversation context, even if the app crashes.
            // The memory manager handles token limits and compaction automatically.

            // Add user message to memory
            _memoryManager.AddMemory(
                new MemoryEntry { Type = MemoryType.User, Content = userMessage }
            );

            // Update state with user message and auto-save
            _state.AddMessage(new Message { Role = "user", Content = userMessage });
            _state.MessageCount++;
            await AutoSaveStateAsync();

            // Build context from memory
            var context = _memoryManager.BuildContext(4000);

            // Prepare messages
            var messages = new List<Message>
            {
                new() { Role = "system", Content = BuildSystemPrompt() },
            };

            // Add context if available
            if (!string.IsNullOrEmpty(context))
            {
                messages.Add(
                    new Message
                    {
                        Role = "system",
                        Content = $"Previous conversation context:\n{context}",
                    }
                );
            }

            // Add current user message
            messages.Add(new Message { Role = "user", Content = userMessage });

            // Conference Note: Tools are discovered via reflection and passed to the LLM
            // in OpenAI function calling format. The LLM decides when to use them.

            // Get available tools in OpenAI format
            var tools = _toolRegistry.GenerateToolDefinitions();

            // Create request with tools
            var request = new ChatRequest
            {
                Model = _configuration.Models?.Reasoning ?? "anthropic/claude-opus-4.1",
                Messages = messages,
                Tools = tools.Count > 0 ? tools : null, // Only include tools if available
                // Do not set tool_choice explicitly; 'auto' is default and some providers reject string value
                Temperature = 0.7,
                MaxTokens = 4000,
            };

            // Process with reasoning model
            // Conference Note: Show progress indicator for long-running LLM operations
            var response = await ProcessWithReasoningAsync(request, cancellationToken);

            // Add response to memory
            _memoryManager.AddMemory(
                new MemoryEntry { Type = MemoryType.Assistant, Content = response }
            );

            // Update state with assistant response and auto-save
            _state.AddMessage(new Message { Role = "assistant", Content = response });
            await AutoSaveStateAsync();

            ProcessingCompleted?.Invoke(this, new AgentEventArgs(response));

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            ErrorOccurred?.Invoke(this, new AgentErrorEventArgs(userMessage, ex));
            throw;
        }
    }

    /// <summary>
    /// Process with reasoning model.
    ///
    /// <para><b>Conference Note - Streaming & Reasoning:</b></para>
    /// <para>We use streaming for better UX - users see progress in real-time.</para>
    /// <para>The StreamingHandler extracts reasoning blocks (the AI's thought process)</para>
    /// <para>and displays them separately from the final answer.</para>
    /// <para>This transparency helps users understand how the AI approaches problems.</para>
    /// </summary>
    private async Task<string> ProcessWithReasoningAsync(
        ChatRequest request,
        CancellationToken cancellationToken
    )
    {
        // Use streaming for better UX
        IAsyncEnumerable<StreamingChatResponse>? stream = null;
        try
        {
            stream = _llmClient.StreamAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Streaming setup failed; will try non-streaming fallback");
        }

        var processedStream = stream is not null
            ? _streamingHandler.ProcessStreamAsync(stream, cancellationToken)
            : GetEmptyStream();

        var fullResponse = new List<string>();
        var pendingToolCalls = new List<ToolCall>();

        try
        {
            await foreach (var chunk in processedStream)
            {
                switch (chunk.Type)
                {
                    case ChunkType.Content:
                        fullResponse.Add(chunk.Content);
                        // Display content as it streams in
                        _console.Write(chunk.Content, ConsoleMessageType.Agent);
                        break;

                    case ChunkType.ReasoningStart:
                        _logger.LogDebug("Reasoning started: {ReasoningType}", chunk.ReasoningType);
                        // Show reasoning indicator
                        _console.WriteLine();
                        _console.WriteLine("🤔 Reasoning...", ConsoleMessageType.Reasoning);
                        _console.WriteLine(
                            $"  Type: {chunk.ReasoningType}",
                            ConsoleMessageType.Reasoning
                        );
                        break;

                    case ChunkType.ReasoningStep:
                        _logger.LogDebug("Reasoning step: {Content}", chunk.Content);
                        // Display reasoning steps in real-time
                        _console.WriteLine($"  → {chunk.Content}", ConsoleMessageType.Reasoning);
                        break;

                    case ChunkType.ReasoningComplete:
                        _logger.LogDebug("Reasoning complete: {Content}", chunk.Content);
                        // Show reasoning summary
                        _console.WriteLine($"  ✓ {chunk.Content}", ConsoleMessageType.Reasoning);
                        _console.WriteLine();
                        break;

                    case ChunkType.ToolCall:
                        if (chunk.ToolCalls is not null)
                        {
                            // Collect tool calls silently; we'll summarize once after streaming
                            pendingToolCalls.AddRange(chunk.ToolCalls);
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Streaming failed mid-flight; falling back");
        }

        // Handle tool calls if any
        if (pendingToolCalls.Count != 0)
        {
            // Aggregate streamed tool_calls by id and concatenate argument deltas
            // Aggregate primarily by index, merging id when it arrives later
            var byIndex = new Dictionary<int, ToolCall>();
            var indexOrder = new List<int>();
            var idToIndex = new Dictionary<string, int>();

            foreach (var tc in pendingToolCalls)
            {
                if (tc.Function is null)
                    continue;

                int? idx = tc.Index;
                if (
                    !idx.HasValue
                    && !string.IsNullOrWhiteSpace(tc.Id)
                    && idToIndex.TryGetValue(tc.Id, out var mapped)
                )
                {
                    idx = mapped;
                }

                if (!idx.HasValue)
                {
                    // If no index is available yet, try to assign a temporary index based on order
                    idx = indexOrder.Count > 0 ? indexOrder.Max() + 1 : 0;
                }

                if (!byIndex.TryGetValue(idx.Value, out var agg))
                {
                    agg = new ToolCall
                    {
                        Id = tc.Id ?? string.Empty,
                        Index = idx,
                        Type = tc.Type,
                        Function = new FunctionCall
                        {
                            Name = string.Empty,
                            Arguments = string.Empty,
                        },
                    };
                    byIndex[idx.Value] = agg;
                    indexOrder.Add(idx.Value);
                }

                // Map id to index when available
                if (!string.IsNullOrWhiteSpace(tc.Id))
                {
                    agg.Id = tc.Id;
                    idToIndex[tc.Id] = idx.Value;
                }

                // Update name if provided
                if (!string.IsNullOrWhiteSpace(tc.Function.Name))
                {
                    agg.Function!.Name = tc.Function.Name;
                }

                // Append/assign arguments
                if (!string.IsNullOrEmpty(tc.Function.Arguments))
                {
                    agg.Function!.Arguments += tc.Function.Arguments;
                }
                else if (tc.Function.Input.HasValue)
                {
                    // Prefer structured input if provided
                    var json = JsonSerializer.Serialize(tc.Function.Input.Value);
                    agg.Function!.Arguments = json;
                }
            }

            var aggregatedToolCalls = indexOrder.Select(i => byIndex[i]).ToList();

            // Filter to complete tool calls (must have function name and id)
            var validToolCalls = aggregatedToolCalls
                .Where(tc =>
                    tc.Function is not null
                    && !string.IsNullOrWhiteSpace(tc.Function!.Name)
                    && !string.IsNullOrWhiteSpace(tc.Id)
                )
                .ToList();

            if (validToolCalls.Count == 0)
            {
                _logger.LogWarning(
                    "Tool calls detected but none were complete (missing id/name) – skipping execution"
                );
            }
            else
            {
                // Add assistant message with tool_calls per OpenAI-compatible format
                request.Messages.Add(
                    new Message { Role = "assistant", ToolCalls = validToolCalls }
                );

                _console.WriteLine(
                    $"\n📦 Executing {validToolCalls.Count} tool(s)...",
                    ConsoleMessageType.Info
                );
                var toolResults = await ExecuteToolsAsync(validToolCalls, cancellationToken);

                // Add tool results to messages, linked via tool_call_id
                foreach (var result in toolResults)
                {
                    if (!string.IsNullOrWhiteSpace(result.ToolCallId))
                    {
                        request.Messages.Add(Message.Tool(result.Content, result.ToolCallId));
                    }
                    else
                    {
                        // Fallback (should not happen if we filtered above)
                        request.Messages.Add(
                            new Message { Role = "tool", Content = result.Content }
                        );
                    }
                }

                // Continue conversation with execution model
                request.Model = _configuration.Models?.Execution ?? "anthropic/claude-sonnet-4";

                // Conference Note: Show progress for execution model processing
                _console.WriteLine("\n🔄 Processing tool results...", ConsoleMessageType.Info);

                var executionResponse = await _llmClient.CompleteAsync(request, cancellationToken);

                // Follow-up loop: handle further tool calls until final content or limit
                var maxFollowUps = Math.Max(0, _configuration.Conversation?.MaxFollowUps ?? 3);
                for (int i = 0; i < maxFollowUps; i++)
                {
                    var msg = executionResponse.GetMessage();
                    var followupCalls = msg?.ToolCalls ?? [];

                    if (followupCalls.Count == 0)
                    {
                        // No more tool calls
                        var text = msg?.Content;
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text!;
                        }

                        // Nudge the model to complete remaining steps if content is empty
                        var nudge =
                            _configuration.Conversation?.FollowUpNudgeSystemMessage
                            ?? "Continue and complete the remaining requested changes. If additional file updates are required (e.g., adjusting project assignments), issue tool calls now; otherwise provide the final summary.";
                        request.Messages.Add(new Message { Role = "system", Content = nudge });
                        executionResponse = await _llmClient.CompleteAsync(
                            request,
                            cancellationToken
                        );
                        continue;
                    }

                    // Add assistant message with additional tool_calls
                    request.Messages.Add(
                        new Message { Role = "assistant", ToolCalls = followupCalls }
                    );

                    // Execute and append results
                    var moreResults = await ExecuteToolsAsync(followupCalls, cancellationToken);
                    foreach (var result in moreResults)
                    {
                        if (!string.IsNullOrWhiteSpace(result.ToolCallId))
                            request.Messages.Add(Message.Tool(result.Content, result.ToolCallId));
                        else
                            request.Messages.Add(
                                new Message { Role = "tool", Content = result.Content }
                            );
                    }

                    // Ask model again
                    executionResponse = await _llmClient.CompleteAsync(request, cancellationToken);
                }

                // Safety net
                {
                    var msg = executionResponse.GetMessage();
                    var text = msg?.Content;
                    return string.IsNullOrWhiteSpace(text) ? "No response" : text!;
                }
            }
        }

        // If streaming yielded no visible content, fall back to non-streaming request
        var streamed = string.Join("", fullResponse);
        if (string.IsNullOrWhiteSpace(streamed))
        {
            _console.WriteLine();
            _console.WriteLine(
                "⚠️ No streamed content received, retrying without streaming...",
                ConsoleMessageType.Warning
            );

            request.Stream = false;
            var fallback = await _llmClient.CompleteAsync(request, cancellationToken);
            var text = fallback.Choices?.Count > 0 ? fallback.Choices[0].Message?.Content : null;
            return string.IsNullOrWhiteSpace(text) ? "No response" : text!;
        }

        return streamed;
    }

    private static async IAsyncEnumerable<ProcessedChunk> GetEmptyStream()
    {
        // Ensure this async iterator contains an await to avoid CS1998
        await Task.Yield();
        yield break;
    }

    /// <summary>
    /// Execute tool calls requested by the LLM.
    ///
    /// <para><b>Conference Note - Tool Execution:</b></para>
    /// <para>Tools extend the LLM's capabilities beyond text generation.</para>
    /// <para>Key safety features:</para>
    /// <list type="bullet">
    /// <item><b>User approval:</b> Each tool call requires explicit permission</item>
    /// <item><b>Timeout protection:</b> Tools can't run forever (default 30s)</item>
    /// <item><b>Error handling:</b> Failures are gracefully reported back to LLM</item>
    /// <item><b>State tracking:</b> All tool calls are recorded in memory</item>
    /// </list>
    /// <para>Tools are discovered via reflection from classes with [Tool] attribute.</para>
    /// </summary>
    private async Task<List<ToolExecutionResult>> ExecuteToolsAsync(
        List<ToolCall> toolCalls,
        CancellationToken cancellationToken
    )
    {
        var results = new List<ToolExecutionResult>();

        foreach (var toolCall in toolCalls)
        {
            if (toolCall.Function is null)
                continue;

            var toolName = toolCall.Function.Name;
            var toolCallId = toolCall.Id;
            var tool = _toolRegistry.GetTool(toolName);

            if (tool is null)
            {
                _logger.LogWarning("Tool not found: {ToolName}", toolName);
                results.Add(
                    new ToolExecutionResult
                    {
                        ToolName = toolName,
                        Success = false,
                        Content = $"Tool '{toolName}' not found",
                        ToolCallId = toolCallId,
                    }
                );
                continue;
            }

            // Normalize parameters for display/approval/execution
            string? normalizedParams = toolCall.Function.Arguments;
            if (string.IsNullOrWhiteSpace(normalizedParams) && toolCall.Function.Input.HasValue)
            {
                normalizedParams = JsonSerializer.Serialize(toolCall.Function.Input.Value);
            }
            if (string.IsNullOrWhiteSpace(normalizedParams))
            {
                normalizedParams = "{}";
            }

            // Show tool call details for the user (independent of logger)
            _console.WriteLine("\n🔧 Tool Call", ConsoleMessageType.Info);
            _console.WriteLine($"  Name: {toolName}", ConsoleMessageType.Info);
            _console.WriteLine("  Parameters:", ConsoleMessageType.Info);
            try
            {
                var jsonDoc = JsonDocument.Parse(normalizedParams);
                var pretty = JsonSerializer.Serialize(
                    jsonDoc,
                    new JsonSerializerOptions { WriteIndented = true }
                );
                foreach (var line in pretty.Split('\n'))
                {
                    _console.WriteLine($"    {line}");
                }
            }
            catch
            {
                foreach (var line in normalizedParams.Split('\n'))
                {
                    _console.WriteLine($"    {line}");
                }
            }

            // Always require first approval for any tool; session approvals auto-apply thereafter
            var metadata = _toolRegistry.GetToolMetadata(toolName);
            _logger.LogInformation("Requesting approval for tool: {ToolName}", toolName);
            var approvalResult = await _approvalManager.RequestApprovalAsync(
                toolName,
                tool.Description,
                normalizedParams,
                cancellationToken
            );

            if (!approvalResult.Approved)
            {
                _logger.LogInformation(
                    "Tool {ToolName} was denied: {Reason}",
                    toolName,
                    approvalResult.Reason
                );
                results.Add(
                    new ToolExecutionResult
                    {
                        ToolName = toolName,
                        Success = false,
                        Content = $"Tool execution denied: {approvalResult.Reason}",
                        ToolCallId = toolCallId,
                    }
                );
                continue;
            }

            try
            {
                _logger.LogInformation("Executing tool: {ToolName}", toolName);

                // Conference Note: Tools can be long-running (e.g., web requests),
                // so we enforce timeouts to prevent hanging. Each tool can specify
                // its own timeout via the [Tool] attribute's TimeoutSeconds property.

                // Create timeout for tool execution (default 30 seconds)
                var toolTimeout = metadata?.Timeout ?? TimeSpan.FromSeconds(30);
                using var toolCts = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken
                );
                toolCts.CancelAfter(toolTimeout);

                ToolResult? result = null;
                try
                {
                    result = await tool.ExecuteAsync(normalizedParams, toolCts.Token);

                    results.Add(
                        new ToolExecutionResult
                        {
                            ToolName = toolName,
                            Success = result.Success,
                            Content = result.Content,
                            ToolCallId = toolCallId,
                        }
                    );
                }
                catch (OperationCanceledException)
                    when (toolCts.IsCancellationRequested
                        && !cancellationToken.IsCancellationRequested
                    )
                {
                    // Tool timed out
                    _logger.LogWarning(
                        "Tool {ToolName} execution timed out after {Seconds} seconds",
                        toolName,
                        toolTimeout.TotalSeconds
                    );
                    results.Add(
                        new ToolExecutionResult
                        {
                            ToolName = toolName,
                            Success = false,
                            Content =
                                $"Tool execution timed out after {toolTimeout.TotalSeconds} seconds",
                            ToolCallId = toolCallId,
                        }
                    );
                    continue;
                }

                // Add to memory if execution was successful
                if (result is not null)
                {
                    _memoryManager.AddMemory(
                        new MemoryEntry
                        {
                            Type = MemoryType.Tool,
                            Content = $"Tool {toolName}: {result.Content}",
                            Metadata = result.Metadata,
                        }
                    );

                    // Update state with tool call
                    _state.ToolCallCount++;
                    await AutoSaveStateAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing tool {ToolName}", toolName);
                results.Add(
                    new ToolExecutionResult
                    {
                        ToolName = toolName,
                        Success = false,
                        Content = $"Error: {ex.Message}",
                        ToolCallId = toolCallId,
                    }
                );
            }
        }

        return results;
    }

    /// <summary>
    /// Build system prompt with tool descriptions.
    ///
    /// <para><b>Conference Note - System Prompts:</b></para>
    /// <para>The system prompt sets the AI's behavior and capabilities.</para>
    /// <para>We dynamically add tool descriptions so the LLM knows what's available.</para>
    /// <para>This prompt is cached for Claude models to reduce costs.</para>
    /// </summary>
    private string BuildSystemPrompt()
    {
        var prompt = _systemPrompt;

        var tools = _toolRegistry.GetAllTools();
        if (tools.Any())
        {
            prompt += "\n\nYou have access to the following tools:\n";
            foreach (var tool in tools)
            {
                prompt += $"- {tool.Name}: {tool.Description}\n";
            }
            prompt += "\nUse tools when appropriate to help answer questions.";
        }

        return prompt;
    }

    /// <summary>
    /// Run the agent's main loop
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Agent starting...");

        // Initialize tools
        _toolRegistry.DiscoverTools(typeof(Agent).Assembly);

        while (!cancellationToken.IsCancellationRequested)
        {
            // Main loop would be implemented by the console UI
            await Task.Delay(100, cancellationToken);
        }

        _logger.LogInformation("Agent stopped");
    }

    /// <summary>
    /// Auto-save agent state (called after every LLM interaction).
    ///
    /// <para><b>Conference Note - Auto-Save Pattern:</b></para>
    /// <para>Critical for production agents! We save after EVERY interaction to ensure</para>
    /// <para>we can resume from any point. The save is async and non-blocking,</para>
    /// <para>with error handling to prevent disrupting the main conversation flow.</para>
    /// <para>Uses atomic file operations (write to temp, then move) to prevent corruption.</para>
    /// </summary>
    private async Task AutoSaveStateAsync()
    {
        try
        {
            // Use a fixed filename for auto-save to avoid creating too many files
            var statePath = Path.Combine("state", "agent_autosave.json");

            // Ensure directory exists
            try
            {
                Directory.CreateDirectory("state");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create state directory for auto-save");
                return; // Don't fail the operation, just skip auto-save
            }

            // Include approval states in the auto-save
            var stateData = new
            {
                State = _state,
                ApprovedTools = _approvalManager.GetApprovedTools().ToList(),
                DeniedTools = _approvalManager.GetDeniedTools().ToList(),
                Timestamp = DateTime.UtcNow,
            };

            var json = JsonSerializer.Serialize(
                stateData,
                new JsonSerializerOptions { WriteIndented = true }
            );

            // Write to temp file first for atomic operation
            var tempPath = statePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, statePath, overwrite: true);

            // Save memory separately with auto-save naming
            var memoryPath = Path.Combine("state", "memory_autosave.json");
            await _memoryManager.SaveToFileAsync(memoryPath);

            _logger.LogDebug("Auto-saved agent state");
        }
        catch (Exception ex)
        {
            // Log error but don't fail the operation
            _logger.LogError(ex, "Failed to auto-save state");
        }
    }

    /// <summary>
    /// Save agent state (manual save with timestamp).
    ///
    /// <para><b>Conference Note - Manual Save:</b></para>
    /// <para>Unlike auto-save, manual saves create timestamped files.</para>
    /// <para>This allows users to create checkpoints they can return to later.</para>
    /// <para>Both agent state and memory are saved as separate JSON files.</para>
    /// </summary>
    public async Task SaveStateAsync()
    {
        var statePath = Path.Combine("state", $"agent_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

        try
        {
            Directory.CreateDirectory("state");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create state directory");
            throw;
        }

        var stateData = new
        {
            State = _state,
            ApprovedTools = _approvalManager.GetApprovedTools().ToList(),
            DeniedTools = _approvalManager.GetDeniedTools().ToList(),
            Timestamp = DateTime.UtcNow,
        };

        var json = JsonSerializer.Serialize(
            stateData,
            new JsonSerializerOptions { WriteIndented = true }
        );
        await File.WriteAllTextAsync(statePath, json);

        // Save memory separately
        var memoryPath = Path.Combine("state", $"memory_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        await _memoryManager.SaveToFileAsync(memoryPath);

        _logger.LogInformation("State saved to {StatePath}", statePath);
    }

    /// <summary>
    /// Load agent state from disk.
    ///
    /// <para><b>Conference Note - State Recovery:</b></para>
    /// <para>Loads the most recent state (auto-save or manual).</para>
    /// <para>This allows resuming conversations after crashes or restarts.</para>
    /// <para>Includes:</para>
    /// <list type="bullet">
    /// <item>Conversation history</item>
    /// <item>Tool approval states</item>
    /// <item>Memory manager contents</item>
    /// <item>Reasoning steps</item>
    /// </list>
    /// </summary>
    public async Task LoadStateAsync()
    {
        var stateDir = "state";
        if (!Directory.Exists(stateDir))
        {
            _logger.LogWarning("No state directory found");
            return;
        }

        // First try to load auto-save, then fall back to latest timestamped save
        var autoSavePath = Path.Combine(stateDir, "agent_autosave.json");
        string? stateFile = null;

        if (File.Exists(autoSavePath))
        {
            stateFile = autoSavePath;
        }
        else
        {
            stateFile = Directory
                .GetFiles(stateDir, "agent_*.json")
                .OrderByDescending(f => f)
                .FirstOrDefault();
        }

        if (stateFile is not null)
        {
            try
            {
                var json = await File.ReadAllTextAsync(stateFile);

                SavedStateData? stateData;
                try
                {
                    stateData = JsonSerializer.Deserialize<SavedStateData>(json);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex, "Failed to deserialize state from {StateFile}", stateFile);
                    // Continue without state rather than failing completely
                    return;
                }

                if (stateData is not null)
                {
                    _state = stateData.State ?? new AgentState();

                    // Restore approval states
                    foreach (var tool in stateData.ApprovedTools ?? [])
                    {
                        _approvalManager.ApproveForSession(tool);
                    }

                    foreach (var tool in stateData.DeniedTools ?? [])
                    {
                        _approvalManager.DenyForSession(tool, "Restored from saved state");
                    }

                    _logger.LogInformation("State loaded from {StateFile}", stateFile);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load state from {StateFile}", stateFile);
            }
        }

        // Load memory
        var autoSaveMemoryPath = Path.Combine(stateDir, "memory_autosave.json");
        string? memoryFile = null;

        if (File.Exists(autoSaveMemoryPath))
        {
            memoryFile = autoSaveMemoryPath;
        }
        else
        {
            memoryFile = Directory
                .GetFiles(stateDir, "memory_*.json")
                .OrderByDescending(f => f)
                .FirstOrDefault();
        }

        if (memoryFile is not null)
        {
            await _memoryManager.LoadFromFileAsync(memoryFile);
        }
    }

    /// <summary>
    /// Reset the agent
    /// </summary>
    public async Task ResetAsync()
    {
        _state = new AgentState();
        _approvalManager.ClearSessionApprovals();
        _memoryManager.Clear();
        _streamingHandler.Reset();
        _logger.LogInformation("Agent reset");
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handle reasoning step detection from streaming handler.
    ///
    /// <para><b>Conference Note - Reasoning Transparency:</b></para>
    /// <para>Modern LLMs can show their "thinking" process in reasoning blocks.</para>
    /// <para>We capture and display these to help users understand the AI's logic.</para>
    /// <para>Only the last 10 steps are kept to limit memory usage.</para>
    /// </summary>
    private void OnReasoningStepDetected(object? sender, LLM.ReasoningStepEventArgs e)
    {
        _logger.LogDebug("Reasoning step detected: {Type} - {Summary}", e.Type, e.Summary);

        // Store reasoning in state for potential analysis
        _state.LastReasoningSteps.Add(
            new ReasoningStep
            {
                Type = e.Type,
                Content = e.FullContent,
                Summary = e.Summary,
                Timestamp = e.Timestamp,
            }
        );

        // Keep only last 10 reasoning steps
        while (_state.LastReasoningSteps.Count > 10)
        {
            _state.LastReasoningSteps.RemoveAt(0);
        }
    }

    /// <summary>
    /// Handle content received from streaming handler
    /// </summary>
    private void OnContentReceived(object? sender, LLM.ContentEventArgs e)
    {
        _logger.LogDebug("Content received: {Characters} characters", e.Content.Length);
    }

    // Events
    public event EventHandler<AgentEventArgs>? ProcessingStarted;
    public event EventHandler<AgentEventArgs>? ProcessingCompleted;
    public event EventHandler<AgentErrorEventArgs>? ErrorOccurred;

    // Helper class for tool results
    private class ToolExecutionResult
    {
        public string ToolName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string Content { get; set; } = string.Empty;
        public string ToolCallId { get; set; } = string.Empty;
    }

    // Helper class for saved state deserialization
    private class SavedStateData
    {
        public AgentState? State { get; set; }
        public List<string>? ApprovedTools { get; set; }
        public List<string>? DeniedTools { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
