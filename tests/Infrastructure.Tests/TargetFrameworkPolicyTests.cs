// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Aspire.TestUtilities;
using Xunit;

namespace Infrastructure.Tests;

public sealed class TargetFrameworkPolicyTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("src/Aspire.Hosting/Aspire.Hosting.csproj", "net10.0")]
    [InlineData("src/Aspire.Hosting.AppHost/Aspire.Hosting.AppHost.csproj", "net10.0;net11.0")]
    [InlineData("src/Aspire.Cli/Aspire.Cli.csproj", "net11.0")]
    [InlineData("src/Aspire.Hosting.Tasks/Aspire.Hosting.Tasks.csproj", "net10.0;net472")]
    [InlineData("src/Aspire.Hosting.Analyzers/Aspire.Hosting.Analyzers.csproj", "netstandard2.0")]
    [InlineData("src/Components/Aspire.MongoDB.EntityFrameworkCore/Aspire.MongoDB.EntityFrameworkCore.csproj", "net10.0")]
    [InlineData("src/Components/Aspire.Oracle.EntityFrameworkCore/Aspire.Oracle.EntityFrameworkCore.csproj", "net10.0")]
    [InlineData("src/Components/Aspire.Pomelo.EntityFrameworkCore.MySql/Aspire.Pomelo.EntityFrameworkCore.MySql.csproj", "net10.0")]
    [InlineData("src/Components/Aspire.Npgsql.EntityFrameworkCore.PostgreSQL/Aspire.Npgsql.EntityFrameworkCore.PostgreSQL.csproj", "net10.0;net11.0")]
    [RequiresTools(["pwsh"])]
    public async Task ProjectsAdvertiseSupportedFrameworks(string project, string expectedFrameworks)
    {
        using var evaluation = await EvaluateAsync(project, targetFramework: null);
        var properties = evaluation.RootElement.GetProperty("Properties");
        var targetFrameworks = properties.GetProperty("TargetFrameworks").GetString();
        Assert.Equal(expectedFrameworks, string.IsNullOrEmpty(targetFrameworks)
            ? properties.GetProperty("TargetFramework").GetString()
            : targetFrameworks);
    }

    [Theory]
    [InlineData("net10.0", "10.")]
    [InlineData("net11.0", "11.")]
    [RequiresTools(["pwsh"])]
    public async Task FrameworkDependenciesMatchTarget(string targetFramework, string versionPrefix)
    {
        using var evaluation = await EvaluateAsync("src/Aspire.Hosting.AppHost/Aspire.Hosting.AppHost.csproj", targetFramework);
        var packages = evaluation.RootElement.GetProperty("Items").GetProperty("PackageVersion").EnumerateArray()
            .ToDictionary(package => package.GetProperty("Identity").GetString()!, package => package.GetProperty("Version").GetString()!);

        foreach (var package in new[]
        {
            "Microsoft.Extensions.Hosting",
            "Microsoft.Extensions.Configuration.Binder",
            "Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore",
            "Microsoft.AspNetCore.OpenApi",
            "Microsoft.AspNetCore.TestHost",
            "Microsoft.EntityFrameworkCore.SqlServer",
            "Npgsql.EntityFrameworkCore.PostgreSQL",
            "System.Text.Json",
            "System.IO.Hashing",
        })
        {
            Assert.StartsWith(versionPrefix, packages[package], StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("Aspire.Pomelo.EntityFrameworkCore.MySql", "Pomelo.EntityFrameworkCore.MySql", "9.")]
    [InlineData("Aspire.Pomelo.EntityFrameworkCore.MySql", "Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore", "9.")]
    [InlineData("Aspire.MongoDB.EntityFrameworkCore", "MongoDB.EntityFrameworkCore", "10.")]
    [InlineData("Aspire.Oracle.EntityFrameworkCore", "Oracle.EntityFrameworkCore", "10.")]
    [RequiresTools(["pwsh"])]
    public async Task ProviderDependenciesFollowAvailableMajor(string project, string package, string versionPrefix)
    {
        using var evaluation = await EvaluateAsync($"src/Components/{project}/{project}.csproj", "net10.0");
        var version = Assert.Single(evaluation.RootElement.GetProperty("Items").GetProperty("PackageVersion").EnumerateArray(),
            item => item.GetProperty("Identity").GetString() == package).GetProperty("Version").GetString();
        Assert.StartsWith(versionPrefix, version, StringComparison.Ordinal);
    }

    [Fact]
    public void CSharpTemplateFrameworkChoicesMatchSupportedTargets()
    {
        var templates = Directory.EnumerateFiles(Path.Combine(RepoRoot.Path, "src", "Aspire.ProjectTemplates", "templates"),
            "template.json", SearchOption.AllDirectories);
        var checkedTemplates = 0;
        foreach (var template in templates)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(template));
            if (!document.RootElement.GetProperty("symbols").TryGetProperty("Framework", out var framework))
            {
                continue;
            }

            Assert.Equal(["net10.0", "net11.0"],
                framework.GetProperty("choices").EnumerateArray().Select(choice => choice.GetProperty("choice").GetString()));
            Assert.Equal("net10.0", framework.GetProperty("defaultValue").GetString());
            foreach (var tag in document.RootElement.GetProperty("tags").EnumerateObject().Where(tag => tag.Name.EndsWith("-tfms", StringComparison.Ordinal)))
            {
                Assert.Equal("net10.0;net11.0", tag.Value.GetString());
            }
            checkedTemplates++;
        }

        Assert.True(checkedTemplates > 0);
    }

    private async Task<JsonDocument> EvaluateAsync(string project, string? targetFramework)
    {
        using var workspace = TemporaryWorkspace.Create(output);
        var script = Path.Combine(workspace.Path, "evaluate.ps1");
        await File.WriteAllTextAsync(script, """
            $arguments = @('msbuild', (Join-Path $env:TEST_REPO $env:TEST_PROJECT), '-nologo',
                '-getProperty:TargetFramework,TargetFrameworks', '-getItem:PackageVersion')
            if ($env:TEST_TARGET_FRAMEWORK) {
                $arguments += "-p:TargetFramework=$env:TEST_TARGET_FRAMEWORK"
            }
            & $env:TEST_DOTNET @arguments
            exit $LASTEXITCODE
            """);
        using var command = new PowerShellCommand(script, output)
            .WithWorkingDirectory(RepoRoot.Path)
            .WithEnvironmentVariable("TEST_DOTNET", Path.Combine(RepoRoot.Path, ".dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))
            .WithEnvironmentVariable("TEST_REPO", RepoRoot.Path)
            .WithEnvironmentVariable("TEST_PROJECT", project)
            .WithEnvironmentVariable("TEST_TARGET_FRAMEWORK", targetFramework ?? string.Empty);
        var result = await command.ExecuteAsync();
        result.EnsureSuccessful();

        return JsonDocument.Parse(result.Output);
    }
}
