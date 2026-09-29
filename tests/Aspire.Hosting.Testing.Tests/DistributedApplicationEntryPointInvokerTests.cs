// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Xunit;

namespace Aspire.Hosting.Testing.Tests;

public class DistributedApplicationEntryPointInvokerTests
{
    [Fact]
    public void ResolveEntryPointThrowsForMicrosoftTestingPlatformApplication()
    {
        var assembly = typeof(DistributedApplicationEntryPointInvokerTests).Assembly;

        var exception = Assert.Throws<InvalidOperationException>(
            () => DistributedApplicationEntryPointInvoker.ResolveEntryPoint(assembly));

        Assert.Equal(
            $"The assembly '{assembly.GetName().Name}' is a Microsoft.Testing.Platform test application. " +
            $"Invoking its entry point from {nameof(DistributedApplicationFactory)} would recursively run the test application. " +
            "Ensure the entry point type belongs to the AppHost executable assembly, or use " +
            $"{nameof(DistributedApplicationTestingBuilder)}.{nameof(DistributedApplicationTestingBuilder.Create)} to construct the application without invoking an entry point.",
            exception.Message);
    }

    [Fact]
    public void ResolveEntryPointAcceptsAppHostApplication()
    {
        var entryPoint = DistributedApplicationEntryPointInvoker.ResolveEntryPoint(
            typeof(Projects.TestingAppHost1_AppHost).Assembly);

        Assert.NotNull(entryPoint);
    }
}
