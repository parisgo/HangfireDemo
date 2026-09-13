using HangfireDemo.Core.Plugins;

namespace HangfireDemo.Tests.Plugins;

public sealed class CoreBoundaryTests
{
    [Fact]
    public void Core_ProvidesTheContractButNoBusinessPluginImplementations()
    {
        Assert.DoesNotContain(typeof(IPluginJob).Assembly.GetTypes(),
            type => type.IsClass && !type.IsAbstract && typeof(IPluginJob).IsAssignableFrom(type));
        Assert.DoesNotContain(typeof(IPluginJob).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name!.EndsWith(".Plugin", StringComparison.Ordinal));
    }
}
