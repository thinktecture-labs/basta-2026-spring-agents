using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.Memory;
using BastaAgent.Tools;
using BastaAgent.UI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BastaAgent;

/// <summary>
/// Main entry point for the BastaAgent demo application
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🤖 BASTA! Spring 2026 - AI Agent Demo");
        Console.WriteLine("=====================================");
        Console.WriteLine();

        // Build the host
        var host = CreateHostBuilder(args).Build();

        // Get services
        var agent = host.Services.GetRequiredService<IAgent>();
        var console = host.Services.GetRequiredService<InteractiveConsole>();
        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        var toolRegistry = host.Services.GetRequiredService<IToolRegistry>();
        var configOptions = host.Services.GetRequiredService<IOptions<AgentConfiguration>>();
        var agentConfig = configOptions.Value;

        // Set up cancellation
        using var mainCts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            mainCts.Cancel();
        };

        try
        {
            // Discover built-in tools via reflection and report count
            toolRegistry.DiscoverTools(typeof(Program).Assembly);
            var loadedToolCount = toolRegistry.GenerateToolDefinitions().Count;

            // Load any saved state
            await agent.LoadStateAsync();

            console.WriteLine("✅ Agent initialized successfully!", ConsoleMessageType.Success);
            console.WriteLine($"🔧 Tools loaded: {loadedToolCount}", ConsoleMessageType.Info);
            console.WriteLine("📝 Type your message or use commands:", ConsoleMessageType.Info);
            console.WriteLine("   /help   - Show available commands");
            console.WriteLine("   /config - Show current configuration");
            console.WriteLine("   /tools  - List loaded tools");
            console.WriteLine("   /reset  - Reset conversation");
            console.WriteLine("   /save   - Save current state");
            console.WriteLine("   /exit   - Exit the application");
            console.WriteLine("   ESC     - Cancel current operation");
            console.WriteLine();

            // Start the interactive console
            console.Start();

            // Set up cancellation handling
            var processingCts = CancellationTokenSource.CreateLinkedTokenSource(mainCts.Token);
            console.CancellationRequested += (s, e) =>
            {
                logger.LogInformation("User requested cancellation");
                processingCts.Cancel();
                processingCts.Dispose();
                processingCts = CancellationTokenSource.CreateLinkedTokenSource(mainCts.Token);
            };

            // Main conversation loop
            while (!mainCts.Token.IsCancellationRequested)
            {
                // Wait for input
                var input = await console.WaitForInputAsync(mainCts.Token);
                if (string.IsNullOrWhiteSpace(input))
                    continue;

                // Handle commands
                if (input.StartsWith('/'))
                {
                    var handled = await HandleCommand(
                        input,
                        agent,
                        console,
                        logger,
                        agentConfig,
                        toolRegistry
                    );
                    if (!handled)
                        break; // Exit command
                    continue;
                }

                try
                {
                    // Mark as processing
                    console.SetProcessing(true);
                    console.ShowSimpleProgress("Thinking...");

                    // Process the user's message with cancellation support
                    var response = await agent.ProcessMessageAsync(input, processingCts.Token);

                    // Clear progress and show response
                    console.ClearProgress();
                    console.WriteLine($"Agent> {response}", ConsoleMessageType.Agent);
                    console.WriteLine();
                }
                catch (OperationCanceledException)
                {
                    console.WriteLine("❌ Operation cancelled by user", ConsoleMessageType.Warning);
                    logger.LogInformation("Operation cancelled by user");
                }
                catch (Exception ex)
                {
                    console.WriteLine($"❌ Error: {ex.Message}", ConsoleMessageType.Error);
                    logger.LogError(ex, "Error processing message");
                }
                finally
                {
                    console.SetProcessing(false);

                    // Reset cancellation token for next operation
                    if (processingCts.IsCancellationRequested)
                    {
                        processingCts.Dispose();
                        processingCts = CancellationTokenSource.CreateLinkedTokenSource(
                            mainCts.Token
                        );
                    }
                }
            }

            // Save state before exiting
            await agent.SaveStateAsync();
            console.WriteLine("👋 Goodbye! State saved.", ConsoleMessageType.Success);

            // Stop the console
            await console.StopAsync();
        }
        catch (Exception ex)
        {
            console.WriteLine($"❌ Fatal error: {ex.Message}", ConsoleMessageType.Error);
            logger.LogCritical(ex, "Fatal error in main loop");
        }
    }

    /// <summary>
    /// Handle special commands
    /// </summary>
    internal static async Task<bool> HandleCommand(
        string command,
        IAgent agent,
        InteractiveConsole console,
        ILogger logger,
        AgentConfiguration config,
        IToolRegistry toolRegistry
    )
    {
        switch (command.ToLower())
        {
            case "/help":
                ShowHelp(console);
                return true;

            case "/config":
                ShowConfig(console, config);
                return true;

            case "/tools":
                ShowTools(console, toolRegistry);
                return true;

            case "/reset":
                await agent.ResetAsync();
                console.WriteLine("✅ Conversation reset.", ConsoleMessageType.Success);
                return true;

            case "/save":
                await agent.SaveStateAsync();
                console.WriteLine("✅ State saved.", ConsoleMessageType.Success);
                return true;

            case "/exit":
            case "/quit":
                return false; // Signal to exit

            default:
                console.WriteLine($"❓ Unknown command: {command}", ConsoleMessageType.Warning);
                return true;
        }
    }

    /// <summary>
    /// Show help information
    /// </summary>
    static void ShowHelp(InteractiveConsole console)
    {
        console.WriteLine();
        console.WriteLine("📚 Available Commands:", ConsoleMessageType.Info);
        console.WriteLine("  /help   - Show this help message");
        console.WriteLine("  /config - Show current configuration");
        console.WriteLine("  /tools  - List loaded tools");
        console.WriteLine("  /reset  - Clear conversation history and start fresh");
        console.WriteLine("  /save   - Save the current conversation state");
        console.WriteLine("  /exit   - Exit the application");
        console.WriteLine();
        console.WriteLine("⌨️ Keyboard Shortcuts:", ConsoleMessageType.Info);
        console.WriteLine("  ESC     - Cancel the current operation");
        console.WriteLine("  Ctrl+C  - Exit the application");
        console.WriteLine();
        console.WriteLine("💡 Tips:", ConsoleMessageType.Info);
        console.WriteLine("  - You can type while the agent is thinking");
        console.WriteLine("  - The agent can use tools to read files, search the web, etc.");
        console.WriteLine("  - Reasoning blocks show the agent's thought process");
        console.WriteLine("  - State is automatically saved on exit");
        console.WriteLine();
    }

    /// <summary>
    /// Create and configure the host
    /// </summary>
    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration(
                (context, config) =>
                {
                    var env = context.HostingEnvironment;
                    config
                        .SetBasePath(Directory.GetCurrentDirectory())
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

                    // Load user-secrets in Development so sensitive values stay out of appsettings.json
                    if (env.IsDevelopment())
                    {
                        config.AddUserSecrets<Program>(optional: true);
                    }

                    // Env vars and command-line override everything
                    config.AddEnvironmentVariables();
                    config.AddCommandLine(args);
                }
            )
            .ConfigureServices(
                (context, services) =>
                {
                    // Bind configuration
                    services.Configure<AgentConfiguration>(
                        context.Configuration.GetSection("Agent")
                    );

                    // Register HTTP client
                    services.AddHttpClient<ILLMClient, LLMClient>();

                    // Register core services
                    services.AddSingleton<IStreamingHandler, StreamingHandler>();
                    services.AddSingleton<IToolRegistry, ToolRegistry>();
                    services.AddSingleton<IMemoryManager>(provider =>
                    {
                        var logger = provider.GetRequiredService<ILogger<MemoryManager>>();
                        var llmClient = provider.GetRequiredService<ILLMClient>();
                        return new MemoryManager(
                            logger,
                            llmClient,
                            maxTokens: 100000,
                            compactionThreshold: 80000
                        );
                    });

                    // Register the interactive console
                    services.AddSingleton<InteractiveConsole>();

                    // Register tool approval manager
                    services.AddSingleton<IToolApprovalManager, ToolApprovalManager>();

                    // Register the agent
                    services.AddSingleton<IAgent, Agent.Agent>();

                    // Configure logging: console + file
                    services.AddLogging(builder =>
                    {
                        builder.ClearProviders();
                        // Console: keep concise (Information+)
                        builder.AddConsole(options =>
                        {
                            options.FormatterName = "simple";
                        });
                        // File: write full logs to log.txt
                        builder.AddProvider(
                            new Utilities.SimpleFileLoggerProvider(
                                Path.Combine(Directory.GetCurrentDirectory(), "log.txt")
                            )
                        );
                        builder.SetMinimumLevel(LogLevel.Information);
                    });
                }
            )
            .UseConsoleLifetime();

    /// <summary>
    /// Show effective configuration without revealing secrets
    /// </summary>
    internal static void ShowConfig(InteractiveConsole console, AgentConfiguration config)
    {
        var baseUrl = config.API?.BaseUrl ?? string.Empty;
        var model = config.Models?.Reasoning ?? string.Empty;
        var apiKey = config.API?.ApiKey?.Trim() ?? string.Empty;
        var isPlaceholder = apiKey.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
        var hasKey =
            !string.IsNullOrEmpty(apiKey)
            && !string.Equals(apiKey, "none", StringComparison.OrdinalIgnoreCase)
            && !isPlaceholder;

        console.WriteLine();
        console.WriteLine("🔧 Configuration:", ConsoleMessageType.Info);
        console.WriteLine($"  BaseUrl: {baseUrl}");
        console.WriteLine($"  Reasoning Model: {model}");
        console.WriteLine(
            $"  ApiKey: {(hasKey ? "set (hidden)" : string.Equals(apiKey, "none", StringComparison.OrdinalIgnoreCase) ? "none" : "not set")}"
        );
        console.WriteLine(
            $"  Tools Require Approval: {config.Tools?.RequireApproval.ToString() ?? "false"}"
        );
        console.WriteLine();
    }

    /// <summary>
    /// List loaded tools with basic metadata
    /// </summary>
    internal static void ShowTools(InteractiveConsole console, IToolRegistry toolRegistry)
    {
        var tools = toolRegistry.GetAllTools().ToList();
        console.WriteLine();
        console.WriteLine($"🧰 Loaded Tools: {tools.Count}", ConsoleMessageType.Info);
        if (tools.Count == 0)
        {
            console.WriteLine("  (none)");
            console.WriteLine();
            return;
        }

        foreach (var tool in tools.OrderBy(t => t.Name))
        {
            var meta = toolRegistry.GetToolMetadata(tool.Name);
            var category = meta?.Category ?? "General";
            var approval = (meta?.RequiresApproval ?? false) ? "yes" : "no";
            console.WriteLine($"  - {tool.Name} [{category}] (approval: {approval})");
        }
        console.WriteLine();
    }
}
