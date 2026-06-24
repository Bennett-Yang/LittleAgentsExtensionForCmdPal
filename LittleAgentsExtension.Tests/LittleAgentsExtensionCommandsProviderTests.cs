using Xunit;

namespace LittleAgentsExtension.Tests;

public sealed class LittleAgentsExtensionCommandsProviderTests
{
    [Fact]
    public void LegacyPageTypeIsRemovedFromAssembly()
    {
        Assert.Null(typeof(LittleAgentsExtensionCommandsProvider).Assembly.GetType("LittleAgentsExtension.LittleAgentsExtensionPage"));
    }
}
