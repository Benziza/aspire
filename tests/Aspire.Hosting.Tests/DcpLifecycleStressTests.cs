// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.Utils;
using Microsoft.AspNetCore.InternalTesting;

namespace Aspire.Hosting.Tests;

[Trait("Partition", "5")]
public class DcpLifecycleStressTests(ITestOutputHelper testOutputHelper)
{
    public static TheoryData<int> Cycles =>
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9,
        10, 11, 12, 13, 14, 15, 16, 17, 18,
        19, 20, 21, 22, 23, 24, 25, 26, 27
    ];

    [Theory]
    [MemberData(nameof(Cycles))]
    public async Task DashboardOnlyLifecycleCycle(int cycle)
    {
        testOutputHelper.WriteLine($"Starting dashboard-only lifecycle cycle {cycle}.");

        using var builder = TestDistributedApplicationBuilder.Create(testOutputHelper);
        await using var app = await builder.BuildAsync();

        await app.StartAsync().DefaultTimeout(TestConstants.DefaultOrchestratorTestLongTimeout);
        await app.StopAsync().DefaultTimeout(TestConstants.LongTimeoutDuration);
    }

    [Theory]
    [MemberData(nameof(Cycles))]
    public async Task ProjectLifecycleCycleWithDashboard(int cycle)
    {
        testOutputHelper.WriteLine($"Starting project lifecycle cycle {cycle} with the dashboard enabled.");

        using var builder = TestDistributedApplicationBuilder.Create(testOutputHelper);
        builder.AddProject<Projects.ServiceA>($"servicea-{cycle}");
        await using var app = await builder.BuildAsync();

        await app.StartAsync().DefaultTimeout(TestConstants.DefaultOrchestratorTestLongTimeout);
        await app.StopAsync().DefaultTimeout(TestConstants.LongTimeoutDuration);
    }

    [Theory]
    [MemberData(nameof(Cycles))]
    public async Task ProjectLifecycleCycleWithoutDashboard(int cycle)
    {
        testOutputHelper.WriteLine($"Starting project lifecycle cycle {cycle} with the dashboard disabled.");

        using var builder = TestDistributedApplicationBuilder.Create(
            options => options.DisableDashboard = true,
            testOutputHelper);
        builder.AddProject<Projects.ServiceA>($"servicea-{cycle}");
        await using var app = await builder.BuildAsync();

        await app.StartAsync().DefaultTimeout(TestConstants.DefaultOrchestratorTestLongTimeout);
        await app.StopAsync().DefaultTimeout(TestConstants.LongTimeoutDuration);
    }
}
