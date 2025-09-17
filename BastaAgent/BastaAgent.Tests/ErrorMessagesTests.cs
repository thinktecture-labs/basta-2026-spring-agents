using BastaAgent.Utilities;
using Xunit;

namespace BastaAgent.Tests.Utilities;

/// <summary>
/// Tests for the ErrorMessages utility class.
/// Ensures error messages are properly formatted and contain expected information.
/// </summary>
public class ErrorMessagesTests
{
    /// <summary>
    /// Test LLM error messages
    /// </summary>
    public class LLMErrorTests
    {
        [Fact]
        public void CommunicationFailed_ContainsUrlAndDetails()
        {
            // Arrange
            var baseUrl = "https://api.example.com";
            var details = "Connection refused";

            // Act
            var result = ErrorMessages.LLM.CommunicationFailed(baseUrl, details);

            // Assert
            Assert.Contains(baseUrl, result);
            Assert.Contains(details, result);
            Assert.Contains("Troubleshooting steps", result);
            Assert.Contains("Check your internet connection", result);
        }

        [Fact]
        public void RequestTimeout_ContainsTimeoutAndModel()
        {
            // Arrange
            var timeoutSeconds = 30;
            var model = "gpt-4";

            // Act
            var result = ErrorMessages.LLM.RequestTimeout(timeoutSeconds, model);

            // Assert
            Assert.Contains($"{timeoutSeconds} seconds", result);
            Assert.Contains(model, result);
            Assert.Contains("Heavy API load", result);
            Assert.Contains("appsettings.json", result);
        }

        [Fact]
        public void InvalidResponse_ContainsModel()
        {
            // Arrange
            var model = "claude-3";

            // Act
            var result = ErrorMessages.LLM.InvalidResponse(model);

            // Assert
            Assert.Contains(model, result);
            Assert.Contains("invalid response", result);
            Assert.Contains("API format changes", result);
        }

        [Fact]
        public void RateLimited_ContainsRetryTime()
        {
            // Arrange
            var retryAfterSeconds = 60;

            // Act
            var result = ErrorMessages.LLM.RateLimited(retryAfterSeconds);

            // Assert
            Assert.Contains($"{retryAfterSeconds} seconds", result);
            Assert.Contains("rate limit exceeded", result, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Reduce request frequency", result);
        }

        [Fact]
        public void AuthenticationFailed_ContainsInstructions()
        {
            // Act
            var result = ErrorMessages.LLM.AuthenticationFailed();

            // Assert
            Assert.Contains("Authentication failed", result);
            Assert.Contains("API key", result);
            Assert.Contains("appsettings.json", result);
            Assert.Contains(".env", result);
        }
    }

    /// <summary>
    /// Test Tool error messages
    /// </summary>
    public class ToolErrorTests
    {
        [Fact]
        public void ToolNotFound_ContainsToolNameAndAvailableTools()
        {
            // Arrange
            var toolName = "NonExistentTool";
            var availableTools = new List<string> { "FileSystem", "WebRequest", "Calculator" };

            // Act
            var result = ErrorMessages.Tools.ToolNotFound(toolName, availableTools);

            // Assert
            Assert.Contains(toolName, result);
            Assert.Contains("FileSystem", result);
            Assert.Contains("WebRequest", result);
            Assert.Contains("Calculator", result);
            Assert.Contains("[Tool] attribute", result);
        }

        [Fact]
        public void ExecutionFailed_ContainsToolNameAndError()
        {
            // Arrange
            var toolName = "FileSystem";
            var error = "Access denied";

            // Act
            var result = ErrorMessages.Tools.ExecutionFailed(toolName, error);

            // Assert
            Assert.Contains(toolName, result);
            Assert.Contains(error, result);
            Assert.Contains("Invalid parameters", result);
        }

        [Fact]
        public void ToolTimeout_ContainsToolNameAndTimeout()
        {
            // Arrange
            var toolName = "WebRequest";
            var timeoutSeconds = 30;

            // Act
            var result = ErrorMessages.Tools.ToolTimeout(toolName, timeoutSeconds);

            // Assert
            Assert.Contains(toolName, result);
            Assert.Contains($"{timeoutSeconds} seconds", result);
            Assert.Contains("large amount of data", result);
        }

        [Fact]
        public void InvalidParameters_ContainsToolNameAndDetails()
        {
            // Arrange
            var toolName = "Calculator";
            var parameterDetails = "Expected 'number' but got 'string'";

            // Act
            var result = ErrorMessages.Tools.InvalidParameters(toolName, parameterDetails);

            // Assert
            Assert.Contains(toolName, result);
            Assert.Contains(parameterDetails, result);
            Assert.Contains("required parameters", result);
        }
    }

    /// <summary>
    /// Test Memory error messages
    /// </summary>
    public class MemoryErrorTests
    {
        [Fact]
        public void SaveStateFailed_ContainsPathAndError()
        {
            // Arrange
            var path = "/state/agent.json";
            var error = "Disk full";

            // Act
            var result = ErrorMessages.Memory.SaveStateFailed(path, error);

            // Assert
            Assert.Contains(path, result);
            Assert.Contains(error, result);
            Assert.Contains("Insufficient disk space", result);
            Assert.Contains("conversation is still in memory", result);
        }

        [Fact]
        public void LoadStateFailed_ContainsPathAndError()
        {
            // Arrange
            var path = "/state/agent.json";
            var error = "Invalid JSON";

            // Act
            var result = ErrorMessages.Memory.LoadStateFailed(path, error);

            // Assert
            Assert.Contains(path, result);
            Assert.Contains(error, result);
            Assert.Contains("Corrupted", result);
            Assert.Contains("Starting with a new conversation", result);
        }

        [Fact]
        public void CompactionFailed_ContainsError()
        {
            // Arrange
            var error = "LLM unavailable";

            // Act
            var result = ErrorMessages.Memory.CompactionFailed(error);

            // Assert
            Assert.Contains(error, result);
            Assert.Contains("won't affect your current conversation", result);
            Assert.Contains("token limits", result);
        }
    }

    /// <summary>
    /// Test FileSystem error messages
    /// </summary>
    public class FileSystemErrorTests
    {
        [Fact]
        public void ReadFileFailed_ContainsPathAndError()
        {
            // Arrange
            var path = "/data/file.txt";
            var error = "File not found";

            // Act
            var result = ErrorMessages.FileSystem.ReadFileFailed(path, error);

            // Assert
            Assert.Contains(path, result);
            Assert.Contains(error, result);
            Assert.Contains("file exists", result);
            Assert.Contains("read permissions", result);
        }

        [Fact]
        public void WriteFileFailed_ContainsPathAndError()
        {
            // Arrange
            var path = "/data/output.txt";
            var error = "Permission denied";

            // Act
            var result = ErrorMessages.FileSystem.WriteFileFailed(path, error);

            // Assert
            Assert.Contains(path, result);
            Assert.Contains(error, result);
            Assert.Contains("Directory doesn't exist", result);
            Assert.Contains("Disk is full", result);
        }

        [Fact]
        public void DirectoryAccessFailed_ContainsPathAndError()
        {
            // Arrange
            var path = "/protected/directory";
            var error = "Access denied";

            // Act
            var result = ErrorMessages.FileSystem.DirectoryAccessFailed(path, error);

            // Assert
            Assert.Contains(path, result);
            Assert.Contains(error, result);
            Assert.Contains("directory exists", result);
            Assert.Contains("appropriate permissions", result);
        }
    }

    /// <summary>
    /// Test Network error messages
    /// </summary>
    public class NetworkErrorTests
    {
        [Fact]
        public void WebRequestFailed_ContainsUrlAndError()
        {
            // Arrange
            var url = "https://example.com/api";
            var error = "Connection timeout";

            // Act
            var result = ErrorMessages.Network.WebRequestFailed(url, error);

            // Assert
            Assert.Contains(url, result);
            Assert.Contains(error, result);
            Assert.Contains("Website is down", result);
            Assert.Contains("Firewall or proxy", result);
        }

        [Fact]
        public void SslError_ContainsUrl()
        {
            // Arrange
            var url = "https://localhost:5001";

            // Act
            var result = ErrorMessages.Network.SslError(url);

            // Assert
            Assert.Contains(url, result);
            Assert.Contains("SSL/TLS error", result);
            Assert.Contains("certificate", result);
            Assert.Contains("trust the certificate", result);
        }
    }

    /// <summary>
    /// Test Configuration error messages
    /// </summary>
    public class ConfigurationErrorTests
    {
        [Fact]
        public void MissingConfiguration_ContainsSettingName()
        {
            // Arrange
            var settingName = "API:BaseUrl";

            // Act
            var result = ErrorMessages.Configuration.MissingConfiguration(settingName);

            // Assert
            Assert.Contains(settingName, result);
            Assert.Contains("appsettings.json", result);
            Assert.Contains("Environment variables", result);
            Assert.Contains(".env file", result);
        }

        [Fact]
        public void InvalidConfiguration_ContainsSettingNameAndFormat()
        {
            // Arrange
            var settingName = "API:Timeout";
            var expectedFormat = "Integer value in seconds (e.g., 30)";

            // Act
            var result = ErrorMessages.Configuration.InvalidConfiguration(
                settingName,
                expectedFormat
            );

            // Assert
            Assert.Contains(settingName, result);
            Assert.Contains(expectedFormat, result);
            Assert.Contains("invalid format", result);
        }
    }
}
