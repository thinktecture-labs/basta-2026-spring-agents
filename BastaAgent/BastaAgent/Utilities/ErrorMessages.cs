namespace BastaAgent.Utilities;

/// <summary>
/// Centralized error messages for better troubleshooting and user experience.
///
/// <para><b>Conference Note - Error Handling Best Practices:</b></para>
/// <para>This class demonstrates proper error messaging patterns for production applications:</para>
/// <list type="bullet">
/// <item><b>User-Friendly Messages:</b> Clear explanations without technical jargon</item>
/// <item><b>Actionable Advice:</b> Suggest what the user can do to resolve the issue</item>
/// <item><b>Context Preservation:</b> Include relevant details for debugging</item>
/// <item><b>Consistent Formatting:</b> Standardized message structure across the application</item>
/// </list>
/// </summary>
public static class ErrorMessages
{
    /// <summary>
    /// LLM API related error messages
    /// </summary>
    public static class LLM
    {
        /// <summary>
        /// Format error message for API communication failure
        /// </summary>
        public static string CommunicationFailed(string baseUrl, string details) =>
            $"Unable to connect to LLM API at {baseUrl}.\n"
            + $"Details: {details}\n"
            + $"Troubleshooting steps:\n"
            + $"  1. Check your internet connection\n"
            + $"  2. Verify the API endpoint is correct in appsettings.json\n"
            + $"  3. Ensure your API key is valid\n"
            + $"  4. Check if the API service is currently available";

        /// <summary>
        /// Format error message for request timeout
        /// </summary>
        public static string RequestTimeout(int timeoutSeconds, string model) =>
            $"The request to {model} timed out after {timeoutSeconds} seconds.\n"
            + $"This could be due to:\n"
            + $"  • Heavy API load - try again in a few moments\n"
            + $"  • Large request size - consider reducing the conversation history\n"
            + $"  • Network issues - check your connection stability\n"
            + $"You can increase the timeout in appsettings.json if needed.";

        /// <summary>
        /// Format error message for invalid response format
        /// </summary>
        public static string InvalidResponse(string model) =>
            $"Received an invalid response from {model}.\n"
            + $"This might indicate:\n"
            + $"  • API format changes - ensure you're using a compatible model\n"
            + $"  • Corrupted response - try the request again\n"
            + $"  • Model configuration issues - check your model settings";

        /// <summary>
        /// Format error message for rate limiting
        /// </summary>
        public static string RateLimited(int retryAfterSeconds) =>
            $"API rate limit exceeded. Please wait {retryAfterSeconds} seconds.\n"
            + $"To avoid this:\n"
            + $"  • Reduce request frequency\n"
            + $"  • Consider using a different API key\n"
            + $"  • Upgrade your API plan for higher limits";

        /// <summary>
        /// Format error message for authentication failure
        /// </summary>
        public static string AuthenticationFailed() =>
            "Authentication failed with the LLM API.\n"
            + "Please check:\n"
            + "  • Your API key is correctly set in appsettings.json or .env\n"
            + "  • The API key has not expired\n"
            + "  • You have the necessary permissions for the requested model";
    }

    /// <summary>
    /// Tool execution related error messages
    /// </summary>
    public static class Tools
    {
        /// <summary>
        /// Format error message for tool not found
        /// </summary>
        public static string ToolNotFound(string toolName, List<string> availableTools) =>
            $"Tool '{toolName}' not found.\n"
            + $"Available tools: {string.Join(", ", availableTools)}\n"
            + $"Ensure the tool is properly registered and has the [Tool] attribute.";

        /// <summary>
        /// Format error message for tool execution failure
        /// </summary>
        public static string ExecutionFailed(string toolName, string error) =>
            $"Failed to execute tool '{toolName}'.\n"
            + $"Error: {error}\n"
            + $"This might be due to:\n"
            + $"  • Invalid parameters passed to the tool\n"
            + $"  • Tool internal error - check the tool implementation\n"
            + $"  • Resource unavailability (file, network, etc.)";

        /// <summary>
        /// Format error message for tool timeout
        /// </summary>
        public static string ToolTimeout(string toolName, int timeoutSeconds) =>
            $"Tool '{toolName}' execution timed out after {timeoutSeconds} seconds.\n"
            + $"The tool might be:\n"
            + $"  • Processing a large amount of data\n"
            + $"  • Waiting for an external resource\n"
            + $"  • Stuck in an infinite loop\n"
            + $"Consider increasing the timeout or checking the tool's implementation.";

        /// <summary>
        /// Format error message for invalid tool parameters
        /// </summary>
        public static string InvalidParameters(string toolName, string parameterDetails) =>
            $"Invalid parameters provided for tool '{toolName}'.\n"
            + $"Details: {parameterDetails}\n"
            + $"Please ensure all required parameters are provided with correct types.";
    }

    /// <summary>
    /// Memory and state related error messages
    /// </summary>
    public static class Memory
    {
        /// <summary>
        /// Format error message for state save failure
        /// </summary>
        public static string SaveStateFailed(string path, string error) =>
            $"Failed to save agent state to {path}.\n"
            + $"Error: {error}\n"
            + $"Possible causes:\n"
            + $"  • Insufficient disk space\n"
            + $"  • No write permissions for the directory\n"
            + $"  • Disk I/O error\n"
            + $"Your conversation is still in memory but may be lost if the application closes.";

        /// <summary>
        /// Format error message for state load failure
        /// </summary>
        public static string LoadStateFailed(string path, string error) =>
            $"Failed to load agent state from {path}.\n"
            + $"Error: {error}\n"
            + $"The state file might be:\n"
            + $"  • Corrupted - try deleting it to start fresh\n"
            + $"  • From an incompatible version\n"
            + $"  • Missing required permissions\n"
            + $"Starting with a new conversation.";

        /// <summary>
        /// Format error message for memory compaction failure
        /// </summary>
        public static string CompactionFailed(string error) =>
            $"Failed to compact conversation memory.\n"
            + $"Error: {error}\n"
            + $"This won't affect your current conversation, but you might:\n"
            + $"  • Experience slower responses due to large context\n"
            + $"  • Hit token limits sooner\n"
            + $"Try manually summarizing or restarting the conversation if issues persist.";
    }

    /// <summary>
    /// File system related error messages
    /// </summary>
    public static class FileSystem
    {
        /// <summary>
        /// Format error message for file read failure
        /// </summary>
        public static string ReadFileFailed(string path, string error) =>
            $"Failed to read file: {path}\n"
            + $"Error: {error}\n"
            + $"Check that:\n"
            + $"  • The file exists\n"
            + $"  • You have read permissions\n"
            + $"  • The file is not locked by another process";

        /// <summary>
        /// Format error message for file write failure
        /// </summary>
        public static string WriteFileFailed(string path, string error) =>
            $"Failed to write file: {path}\n"
            + $"Error: {error}\n"
            + $"Possible issues:\n"
            + $"  • Directory doesn't exist\n"
            + $"  • No write permissions\n"
            + $"  • Disk is full\n"
            + $"  • File is locked by another process";

        /// <summary>
        /// Format error message for directory access failure
        /// </summary>
        public static string DirectoryAccessFailed(string path, string error) =>
            $"Failed to access directory: {path}\n"
            + $"Error: {error}\n"
            + $"Ensure the directory exists and you have appropriate permissions.";
    }

    /// <summary>
    /// Network related error messages
    /// </summary>
    public static class Network
    {
        /// <summary>
        /// Format error message for web request failure
        /// </summary>
        public static string WebRequestFailed(string url, string error) =>
            $"Failed to fetch content from: {url}\n"
            + $"Error: {error}\n"
            + $"Possible causes:\n"
            + $"  • Website is down or unreachable\n"
            + $"  • Network connectivity issues\n"
            + $"  • URL is invalid or has moved\n"
            + $"  • Firewall or proxy blocking the request";

        /// <summary>
        /// Format error message for SSL/TLS failure
        /// </summary>
        public static string SslError(string url) =>
            $"SSL/TLS error when connecting to: {url}\n"
            + $"This might indicate:\n"
            + $"  • Invalid or expired certificate\n"
            + $"  • Certificate validation issues\n"
            + $"  • Proxy interference\n"
            + $"For local/development servers, you may need to trust the certificate.";
    }

    /// <summary>
    /// Configuration related error messages
    /// </summary>
    public static class Configuration
    {
        /// <summary>
        /// Format error message for missing configuration
        /// </summary>
        public static string MissingConfiguration(string settingName) =>
            $"Required configuration '{settingName}' is missing.\n"
            + $"Please add it to:\n"
            + $"  • appsettings.json, or\n"
            + $"  • Environment variables, or\n"
            + $"  • .env file (for local development)\n"
            + $"Refer to the documentation for the correct format.";

        /// <summary>
        /// Format error message for invalid configuration
        /// </summary>
        public static string InvalidConfiguration(string settingName, string expectedFormat) =>
            $"Configuration '{settingName}' has an invalid format.\n"
            + $"Expected format: {expectedFormat}\n"
            + $"Please check your configuration files and correct the value.";
    }
}
