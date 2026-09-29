// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Aspire.TestUtilities;
using GenerateTemplateManifest;
using VerifyXunit;
using Xunit;

namespace Infrastructure.Tests.TemplateManifest;

public sealed class TemplateManifestCacheTests(ITestOutputHelper output)
{
    [Fact]
    [RequiresTools(["pwsh"])]
    public async Task RebuiltPackageRefreshesEveryGraphWithoutChangingItsVersion()
    {
        using var workspace = TemporaryWorkspace.Create(output);
        var source = workspace.CreateDirectory("source").FullName;
        var template = Directory.CreateDirectory(Path.Combine(source, "template")).FullName;
        var builtFeed = workspace.CreateDirectory("built feed").FullName;
        var externalFeed = workspace.CreateDirectory("external feed").FullName;
        var sharedCache = workspace.CreateDirectory("shared cache").FullName;
        var restoreDirectory = Path.Combine(workspace.Path, "restore");
        var manifestPath = Path.Combine(workspace.Path, "cgmanifest.json");
        var configPath = Path.Combine(workspace.Path, "nuget.config");

        Directory.CreateDirectory(Path.Combine(template, ".template.config"));
        File.WriteAllText(Path.Combine(template, ".template.config", "template.json"), """
            {
              "identity": "cache-fixture", "name": "Cache fixture", "shortName": "cache-fixture",
              "symbols": {
                "Framework": {
                  "type": "parameter", "datatype": "choice", "defaultValue": "net11.0",
                  "choices": [{"choice": "net11.0"}]
                }
              }
            }
            """);
        // Distinct projects share the same package, just like independently restored template
        // options. Check each assets file: unioning them can conceal a stale sibling graph.
        const int graphCount = 2;
        for (var i = 0; i < graphCount; i++)
        {
            File.WriteAllText(Path.Combine(template, $"Project{i}.csproj"), $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net11.0</TargetFramework>
                    <AssemblyName>Project{{i}}</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup><PackageReference Include="Local.CacheFixture" Version="1.0.0-dev" /></ItemGroup>
                </Project>
                """);
        }
        new XDocument(new XElement("configuration",
            new XElement("packageSources",
                new XElement("clear"),
                new XElement("add", new XAttribute("key", "external"), new XAttribute("value", externalFeed))),
            new XElement("packageSourceMapping",
                new XElement("clear"),
                new XElement("packageSource", new XAttribute("key", "external"),
                    new XElement("package", new XAttribute("pattern", "*")))),
            new XElement("fallbackPackageFolders", new XElement("clear")))).Save(configPath);

        // Target the installed repository SDK's framework so restore needs no downloaded
        // targeting packs. All feeds, HTTP caches and extracted packages are test-local.
        var dotnet = Path.Combine(RepoRoot.Path, ".dotnet", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
        Assert.True(File.Exists(dotnet), "The repository SDK must be initialized before running infrastructure tests.");
        var script = Path.Combine(workspace.Path, "generate.ps1");
        File.WriteAllText(script, """
            & $env:TEST_DOTNET $env:TEST_GENERATOR $env:TEST_SOURCE $env:TEST_SOURCE `
                $env:TEST_MANIFEST $env:TEST_CONFIG $env:TEST_FEED $env:TEST_RESTORE
            exit $LASTEXITCODE
            """);
        CreatePackage(externalFeed, "External.CacheFixture", "1.0.0", null);
        CreatePackage(externalFeed, "External.CacheFixture", "2.0.0", null);
        var staleSharedPackage = CreatePackage(sharedCache, "Local.CacheFixture", "1.0.0-dev", "1.0.0");
        var sharedPackageBytes = File.ReadAllBytes(staleSharedPackage);
        var configBefore = File.ReadAllText(configPath);

        using var command = new PowerShellCommand(script, output)
            .WithWorkingDirectory(workspace.Path)
            .WithTimeout(TimeSpan.FromMinutes(2))
            .WithEnvironmentVariable("TEST_DOTNET", dotnet)
            .WithEnvironmentVariable("PATH", Path.GetDirectoryName(dotnet) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"))
            .WithEnvironmentVariable("TEST_GENERATOR", typeof(TemplateRestorePlan).Assembly.Location)
            .WithEnvironmentVariable("TEST_SOURCE", source)
            .WithEnvironmentVariable("TEST_MANIFEST", manifestPath)
            .WithEnvironmentVariable("TEST_CONFIG", configPath)
            .WithEnvironmentVariable("TEST_FEED", builtFeed)
            .WithEnvironmentVariable("TEST_RESTORE", restoreDirectory)
            .WithEnvironmentVariable("NUGET_PACKAGES", sharedCache)
            .WithEnvironmentVariable("NUGET_HTTP_CACHE_PATH", Path.Combine(workspace.Path, "http-cache"))
            .WithEnvironmentVariable("DOTNET_CLI_HOME", workspace.Path)
            .WithEnvironmentVariable("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
            .WithEnvironmentVariable("DOTNET_GENERATE_ASPNET_CERTIFICATE", "false")
            .WithEnvironmentVariable("DOTNET_CLI_TELEMETRY_OPTOUT", "1")
            .WithEnvironmentVariable("DOTNET_NOLOGO", "1")
            // Restore one graph before checking its siblings for no-op eligibility, so
            // cache repopulation cannot depend on thread scheduling in this regression test.
            .WithEnvironmentVariable("RestoreDisableParallel", "true")
            .WithEnvironmentVariable("MSBUILDTERMINALLOGGER", "false");

        CreatePackage(builtFeed, "Local.CacheFixture", "1.0.0-dev", "1.0.0");
        (await command.ExecuteAsync()).EnsureSuccessful();
        AssertGraphs(restoreDirectory, graphCount, "1.0.0");
        var firstManifest = JsonNode.Parse(File.ReadAllText(manifestPath));

        // Keep the private cache and all assets files. Only replace the same-version nupkg.
        CreatePackage(builtFeed, "Local.CacheFixture", "1.0.0-dev", "2.0.0");
        (await command.ExecuteAsync()).EnsureSuccessful();
        AssertGraphs(restoreDirectory, graphCount, "2.0.0");
        var secondManifest = JsonNode.Parse(File.ReadAllText(manifestPath));

        var manifests = new JsonObject
        {
            ["First"] = firstManifest,
            ["Second"] = secondManifest
        };
        await Verifier.Verify(manifests.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), "json").UseDirectory("Snapshots");
        Assert.Equal(sharedPackageBytes, File.ReadAllBytes(staleSharedPackage));
        Assert.Equal(configBefore, File.ReadAllText(configPath));
        Assert.Equal([Path.GetFileName(staleSharedPackage)], Directory.GetFiles(sharedCache).Select(Path.GetFileName));
        Assert.Empty(Directory.GetDirectories(sharedCache));
    }

    private static void AssertGraphs(string restoreDirectory, int expectedCount, string dependencyVersion)
    {
        var solution = XDocument.Load(Path.Combine(restoreDirectory, "TemplateDependencies.slnx"));
        var projects = solution.Root!.Elements("Project").ToArray();
        Assert.Equal(expectedCount, projects.Length);
        foreach (var project in projects)
        {
            var path = project.Attribute("Path")!.Value.Replace('/', Path.DirectorySeparatorChar);
            var directory = Path.GetDirectoryName(Path.Combine(restoreDirectory, path))!;
            using var assets = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "obj", "project.assets.json")));
            Assert.Equal(
                [$"External.CacheFixture/{dependencyVersion}", "Local.CacheFixture/1.0.0-dev"],
                assets.RootElement.GetProperty("libraries").EnumerateObject().Select(p => p.Name).Order());
            var targets = assets.RootElement.GetProperty("targets").EnumerateObject().ToArray();
            var target = Assert.Single(targets);
            Assert.Equal($"[{dependencyVersion}]", target.Value.GetProperty("Local.CacheFixture/1.0.0-dev")
                .GetProperty("dependencies").GetProperty("External.CacheFixture").GetString());
        }
    }

    private static string CreatePackage(string feed, string name, string version, string? dependencyVersion)
    {
        var path = Path.Combine(feed, $"{name}.{version}.nupkg");
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        using var stream = archive.CreateEntry($"{name}.nuspec").Open();
        new XDocument(new XElement("package",
            new XElement("metadata",
                new XElement("id", name),
                new XElement("version", version),
                new XElement("authors", "Test"),
                new XElement("description", "Hermetic template manifest cache fixture"),
                dependencyVersion is null ? null :
                    new XElement("dependencies",
                        new XElement("dependency", new XAttribute("id", "External.CacheFixture"),
                            new XAttribute("version", $"[{dependencyVersion}]")))))).Save(stream);
        return path;
    }
}
