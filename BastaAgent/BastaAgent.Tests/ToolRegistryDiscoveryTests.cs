using System.Linq;
using BastaAgent.Tools;
using BastaAgent.Tools.BuiltIn;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BastaAgent.Tests;

public class ToolRegistryDiscoveryTests
{
    [Fact]
    public void DiscoverTools_GeneratesDefinitions_WithCorrectApprovalFlags()
    {
        var registry = new ToolRegistry(NullLogger<ToolRegistry>.Instance);

        // Discover built-in tools from the BastaAgent assembly containing the tools
        registry.DiscoverTools(typeof(FileSystemTool).Assembly);
        var defs = registry.GenerateToolDefinitions();

        Assert.NotEmpty(defs);

        // Build lookup of function name -> requiresConfirmation
        var map = defs.ToDictionary(d => d.Function!.Name, d => d.Function!.RequiresConfirmation);

        // Expected sanitized names
        Assert.True(map.ContainsKey("Web_Request"));
        Assert.True(map.ContainsKey("FileSystem_Read"));
        Assert.True(map.ContainsKey("FileSystem_Write"));
        Assert.True(map.ContainsKey("Directory_List"));
        Assert.True(map.ContainsKey("Web_Search"));

        // Approval flags from [Tool(RequiresApproval=...)]
        Assert.True(map["Web_Request"]); // RequiresApproval = true
        Assert.True(map["FileSystem_Read"]); // RequiresApproval = true
        Assert.True(map["FileSystem_Write"]); // RequiresApproval = true
        Assert.False(map["Directory_List"]); // RequiresApproval = false
        Assert.False(map["Web_Search"]); // RequiresApproval = false
    }
}
