using HangfireDemo.Core.Jobs;
using HangfireDemo.Core.Jobs.Configuration;

namespace HangfireDemo.Tests.Configuration;

public sealed class JobTypeResolverTests
{
    [Theory]
    [InlineData("HangfireDemo.Jobs.ImportCommandeJob, HangfireDemo.Jobs")]
    [InlineData("HangfireDemo.Jobs.ImportCommandeJob, HangfireDemo.Jobs, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")]
    public void Resolve_LegacyJob_ReturnsMovedType(string typeName)
    {
        Assert.Equal(typeof(ImportCommandeJob), JobTypeResolver.Resolve(typeName));
    }

    [Fact]
    public void Resolve_CurrentJob_ReturnsCurrentType()
    {
        Assert.Equal(typeof(ImportCommandeJob),
            JobTypeResolver.Resolve(typeof(ImportCommandeJob).AssemblyQualifiedName!));
    }

    [Fact]
    public void Resolve_FrameworkType_UsesDefaultResolver()
    {
        Assert.Equal(typeof(string), JobTypeResolver.Resolve(typeof(string).AssemblyQualifiedName!));
    }
}
