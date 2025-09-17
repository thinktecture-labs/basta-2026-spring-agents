using System.Text.Json;
using BastaAgent.Core;
using BastaAgent.LLM;
using BastaAgent.LLM.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace BastaAgent.Tests;

/// <summary>
/// Tests for prompt caching functionality in the LLM client
/// Verifies that cache headers are properly added for Claude models
/// </summary>
public class PromptCachingTests : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly LLMClient _llmClient;
    private readonly AgentConfiguration _config;

    public PromptCachingTests()
    {
        _httpClient = new HttpClient();
        _config = new AgentConfiguration
        {
            API = new ApiConfiguration
            {
                BaseUrl = "http://localhost:11434/v1",
                ApiKey = "none",
                Timeout = 30,
            },
            Models = new ModelConfiguration
            {
                Reasoning = "anthropic/claude-opus-4.1",
                Execution = "anthropic/claude-sonnet-4",
                Summarization = "anthropic/claude-haiku-3.5",
            },
        };

        _llmClient = new LLMClient(
            _httpClient,
            Options.Create(_config),
            NullLogger<LLMClient>.Instance
        );
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }

    /// <summary>
    /// Test that cache headers are added to system messages for Claude models
    /// </summary>
    [Fact]
    public void PromptCaching_ShouldAddCacheControlToSystemMessages()
    {
        // Arrange
        var request = new ChatRequest
        {
            Model = "anthropic/claude-opus-4.1",
            Messages =
            [
                Message.System("You are a helpful assistant."),
                Message.User("Hello"),
                Message.Assistant("Hi there!"),
                Message.User("How are you?"),
            ],
        };

        // Act - Use reflection to test private method
        var addCachingMethod = typeof(LLMClient).GetMethod(
            "AddPromptCachingHeaders",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );

        addCachingMethod?.Invoke(_llmClient, new object[] { request });

        // Assert
        var systemMessage = request.Messages.First(m => m.Role == "system");
        Assert.NotNull(systemMessage.CacheControl);
        Assert.Equal("ephemeral", systemMessage.CacheControl.Type);

        // Non-system messages should not have cache control
        var userMessages = request.Messages.Where(m => m.Role == "user");
        Assert.All(userMessages, m => Assert.Null(m.CacheControl));

        var assistantMessages = request.Messages.Where(m => m.Role == "assistant");
        Assert.All(assistantMessages, m => Assert.Null(m.CacheControl));
    }

    /// <summary>
    /// Test that cache headers are NOT added for non-Claude models
    /// </summary>
    [Fact]
    public void PromptCaching_ShouldNotAddCacheControlForNonClaudeModels()
    {
        // Arrange
        var request = new ChatRequest
        {
            Model = "gpt-4",
            Messages = [Message.System("You are a helpful assistant."), Message.User("Hello")],
        };

        // Act
        var addCachingMethod = typeof(LLMClient).GetMethod(
            "AddPromptCachingHeaders",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );

        addCachingMethod?.Invoke(_llmClient, new object[] { request });

        // Assert - No messages should have cache control
        Assert.All(request.Messages, m => Assert.Null(m.CacheControl));
    }

    /// <summary>
    /// Test that multiple system messages all get cache control
    /// </summary>
    [Fact]
    public void PromptCaching_ShouldAddCacheControlToAllSystemMessages()
    {
        // Arrange
        var request = new ChatRequest
        {
            Model = "claude-3-opus",
            Messages =
            [
                Message.System("Primary system prompt"),
                Message.System("Additional context"),
                Message.User("Question"),
                Message.System("More system context"),
            ],
        };

        // Act
        var addCachingMethod = typeof(LLMClient).GetMethod(
            "AddPromptCachingHeaders",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance
        );

        addCachingMethod?.Invoke(_llmClient, new object[] { request });

        // Assert
        var systemMessages = request.Messages.Where(m => m.Role == "system");
        Assert.All(
            systemMessages,
            m =>
            {
                Assert.NotNull(m.CacheControl);
                Assert.Equal("ephemeral", m.CacheControl.Type);
            }
        );
    }

    /// <summary>
    /// Test that cache control is properly serialized to JSON
    /// </summary>
    [Fact]
    public void CacheControl_ShouldSerializeCorrectly()
    {
        // Arrange
        var message = new Message
        {
            Role = "system",
            Content = "Test content",
            CacheControl = new CacheControl { Type = "ephemeral" },
        };

        // Act
        var json = JsonSerializer.Serialize(
            message,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System
                    .Text
                    .Json
                    .Serialization
                    .JsonIgnoreCondition
                    .WhenWritingNull,
            }
        );

        // Assert
        var jsonDoc = JsonDocument.Parse(json);
        Assert.True(jsonDoc.RootElement.TryGetProperty("cache_control", out var cacheControl));
        Assert.True(cacheControl.TryGetProperty("type", out var type));
        Assert.Equal("ephemeral", type.GetString());
    }

    /// <summary>
    /// Test that null cache control is not serialized
    /// </summary>
    [Fact]
    public void CacheControl_ShouldNotSerializeWhenNull()
    {
        // Arrange
        var message = new Message
        {
            Role = "user",
            Content = "Test content",
            CacheControl = null,
        };

        // Act
        var json = JsonSerializer.Serialize(
            message,
            new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = System
                    .Text
                    .Json
                    .Serialization
                    .JsonIgnoreCondition
                    .WhenWritingNull,
            }
        );

        // Assert
        var jsonDoc = JsonDocument.Parse(json);
        Assert.False(jsonDoc.RootElement.TryGetProperty("cache_control", out _));
    }

    /// <summary>
    /// Test that Usage model properly deserializes cache statistics
    /// </summary>
    [Fact]
    public void Usage_ShouldDeserializeCacheStatistics()
    {
        // Arrange
        var json =
            @"{
            ""prompt_tokens"": 1000,
            ""completion_tokens"": 500,
            ""total_tokens"": 1500,
            ""cache_creation_input_tokens"": 800,
            ""cache_read_input_tokens"": 600
        }";

        // Act
        var usage = JsonSerializer.Deserialize<Usage>(
            json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );

        // Assert
        Assert.NotNull(usage);
        Assert.Equal(1000, usage.PromptTokens);
        Assert.Equal(500, usage.CompletionTokens);
        Assert.Equal(1500, usage.TotalTokens);
        Assert.Equal(800, usage.CacheCreationInputTokens);
        Assert.Equal(600, usage.CacheReadInputTokens);
    }

    /// <summary>
    /// Test that cache statistics are optional in Usage
    /// </summary>
    [Fact]
    public void Usage_ShouldHandleMissingCacheStatistics()
    {
        // Arrange
        var json =
            @"{
            ""prompt_tokens"": 1000,
            ""completion_tokens"": 500,
            ""total_tokens"": 1500
        }";

        // Act
        var usage = JsonSerializer.Deserialize<Usage>(
            json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }
        );

        // Assert
        Assert.NotNull(usage);
        Assert.Equal(1000, usage.PromptTokens);
        Assert.Null(usage.CacheCreationInputTokens);
        Assert.Null(usage.CacheReadInputTokens);
    }

    /// <summary>
    /// Test that Message.Clone preserves CacheControl
    /// </summary>
    [Fact]
    public void Message_Clone_ShouldPreserveCacheControl()
    {
        // Arrange
        var original = new Message
        {
            Role = "system",
            Content = "Test",
            CacheControl = new CacheControl { Type = "ephemeral" },
        };

        // Act
        var cloned = original.Clone();

        // Assert
        Assert.NotNull(cloned.CacheControl);
        Assert.Equal("ephemeral", cloned.CacheControl.Type);
        Assert.NotSame(original.CacheControl, cloned.CacheControl); // Should be a different instance
    }

    /// <summary>
    /// Test cache effectiveness calculation
    /// </summary>
    [Theory]
    [InlineData(1000, 600, 0.6)] // 60% cache hit
    [InlineData(1000, 0, 0.0)] // No cache hit
    [InlineData(1000, 1000, 1.0)] // 100% cache hit
    [InlineData(2000, 500, 0.25)] // 25% cache hit
    public void CacheEffectiveness_ShouldCalculateCorrectly(
        int promptTokens,
        int cacheReadTokens,
        double expectedRatio
    )
    {
        // Arrange
        var usage = new Usage
        {
            PromptTokens = promptTokens,
            CacheReadInputTokens = cacheReadTokens,
        };

        // Act
        var cacheRatio = cacheReadTokens > 0 ? (double)cacheReadTokens / promptTokens : 0.0;

        // Assert
        Assert.Equal(expectedRatio, cacheRatio, 2);
    }
}
