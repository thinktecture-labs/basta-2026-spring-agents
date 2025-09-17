using Xunit;

namespace BastaAgent.Tests;

/// <summary>
/// Test collection definitions for controlling test execution order.
///
/// <para><b>Conference Note - Test Isolation:</b></para>
/// <para>Tests that manipulate shared resources like Console.Out need to run sequentially
/// to avoid race conditions. xUnit's test collections provide this control.</para>
/// </summary>
/// <summary>
/// Collection for tests that use Console output.
/// These tests will run sequentially to avoid Console.Out conflicts.
/// </summary>
[CollectionDefinition("Console Tests", DisableParallelization = true)]
public class ConsoleTestCollection : ICollectionFixture<ConsoleTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and all the
    // ICollectionFixture<> interfaces.
}

/// <summary>
/// Fixture that ensures Console.Out is properly restored after each test.
/// </summary>
public class ConsoleTestFixture : IDisposable
{
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;

    public ConsoleTestFixture()
    {
        // Save original console outputs
        _originalOut = Console.Out;
        _originalError = Console.Error;
    }

    public void Dispose()
    {
        // Restore original console outputs
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
    }

    /// <summary>
    /// Reset console to original state (can be called between tests)
    /// </summary>
    public void Reset()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
    }
}
