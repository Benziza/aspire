// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection;

[assembly: Aspire.Hosting.Tests.RunnerLossActivity]

namespace Aspire.Hosting.Tests;

[AttributeUsage(AttributeTargets.Assembly)]
internal sealed class RunnerLossActivityAttribute : BeforeAfterTestAttribute
{
    private static readonly object s_lock = new();

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        WriteActivity("START", test.TestCase.TestClassName, methodUnderTest.Name);
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        WriteActivity("END", test.TestCase.TestClassName, methodUnderTest.Name);
    }

    private static void WriteActivity(string phase, string testClassName, string methodName)
    {
        var activityFile = Environment.GetEnvironmentVariable("ASPIRE_TEST_ACTIVITY_FILE");
        if (string.IsNullOrWhiteSpace(activityFile))
        {
            return;
        }

        lock (s_lock)
        {
            File.AppendAllText(
                activityFile,
                $"{DateTime.UtcNow:O}\t{phase}\t{testClassName}.{methodName}{Environment.NewLine}");
        }
    }
}
