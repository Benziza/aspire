// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.TestUtilities;
using Xunit;

namespace Infrastructure.Tests.TemplateManifest;

public sealed class TemplateManifestChangeTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("README.md", false)]
    [InlineData("src/Aspire.Hosting/Resource.cs", false)]
    [InlineData("src/Aspire.ProjectTemplates/templates/new-template/New.csproj", true)]
    [InlineData("src/Aspire.Hosting/Aspire.Hosting.csproj", true)]
    [InlineData("eng/Versions.props", true)]
    [InlineData("NuGet.config", true)]
    [InlineData("global.json", true)]
    [InlineData("cgmanifest.json", true)]
    [InlineData("tools/GenerateTemplateManifest/Program.cs", true)]
    [InlineData("eng/template-cg-inputs.txt", true)]
    [RequiresTools(["pwsh", "git"])]
    public async Task SkipsOnlyWhenTheComparedCommitsHaveNoManifestInputs(string changedPath, bool shouldVerify)
    {
        using var workspace = TemporaryWorkspace.Create(output);
        var root = workspace.CreateDirectory("repo").FullName;
        Directory.CreateDirectory(Path.Combine(root, "eng"));
        File.Copy(Path.Combine(RepoRoot.Path, "eng", "template-cg-inputs.txt"), Path.Combine(root, "eng", "template-cg-inputs.txt"));
        File.WriteAllText(Path.Combine(root, "cgmanifest.json"), "{}");
        var setup = Path.Combine(workspace.Path, "setup.ps1");
        File.WriteAllText(setup, """
            $ErrorActionPreference = 'Stop'
            git init --quiet
            git add .
            git -c user.name=Test -c user.email=test@example.invalid commit --quiet -m baseline
            if ($LASTEXITCODE -ne 0) { throw 'Fixture commit failed' }
            $file = Join-Path (Get-Location) $env:CHANGED_PATH
            New-Item -ItemType Directory -Path (Split-Path $file) -Force | Out-Null
            Add-Content -LiteralPath $file -Value "`n# changed"
            git add .
            git -c user.name=Test -c user.email=test@example.invalid commit --quiet -m change
            if ($LASTEXITCODE -ne 0) { throw 'Fixture commit failed' }
            """);
        using (var command = new PowerShellCommand(setup, output).WithWorkingDirectory(root).WithEnvironmentVariable("CHANGED_PATH", changedPath))
        {
            (await command.ExecuteAsync()).EnsureSuccessful();
        }

        using var check = new PowerShellCommand(Path.Combine(RepoRoot.Path, "eng", "scripts", "update-template-cgmanifest.ps1"), output);
        var result = await check.ExecuteAsync("-ChangesOnly", "-RepositoryRoot", $"\"{root}\"", "-BaseRef", "HEAD^");
        // The fixture deliberately has no package outputs. Reaching that prerequisite proves the
        // gate chose verification, without building or restoring packages in these tests.
        if (shouldVerify)
        {
            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("Shipping packages not found", result.Output);
        }
        else
        {
            result.EnsureSuccessful();
            Assert.Contains("inputs unchanged; skipping", result.Output);
        }
    }

    [Fact]
    [RequiresTools(["pwsh", "git"])]
    public async Task UnknownHistoryDoesNotSilentlySkip()
    {
        using var workspace = TemporaryWorkspace.Create(output);
        File.WriteAllText(Path.Combine(workspace.Path, "cgmanifest.json"), "{}");
        using var command = new PowerShellCommand(Path.Combine(RepoRoot.Path, "eng", "scripts", "update-template-cgmanifest.ps1"), output);
        var result = await command.ExecuteAsync("-ChangesOnly", "-RepositoryRoot", $"\"{workspace.Path}\"", "-BaseRef", "missing");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("verifying the template manifest rather than skipping", result.Output);
        Assert.Contains("Shipping packages not found", result.Output);
    }

    [Theory]
    [InlineData("rename", "HEAD^")]
    [InlineData("push-range", "HEAD~2")]
    [InlineData("pr-merge", "")]
    [RequiresTools(["pwsh", "git"])]
    public async Task CoversDeletedRenameSourcesAndEntirePushOrPullRequest(string scenario, string baseRef)
    {
        using var workspace = TemporaryWorkspace.Create(output);
        var root = workspace.CreateDirectory("repo").FullName;
        Directory.CreateDirectory(Path.Combine(root, "eng"));
        File.Copy(Path.Combine(RepoRoot.Path, "eng", "template-cg-inputs.txt"), Path.Combine(root, "eng", "template-cg-inputs.txt"));
        File.WriteAllText(Path.Combine(root, "cgmanifest.json"), "{}");
        var setup = Path.Combine(workspace.Path, "setup.ps1");
        File.WriteAllText(setup, """
            $ErrorActionPreference = 'Stop'
            function Commit([string]$message) {
              git add .
              git -c user.name=Test -c user.email=test@example.invalid commit --quiet -m $message
              if ($LASTEXITCODE -ne 0) { throw 'Fixture commit failed' }
            }
            git init --quiet
            New-Item -ItemType Directory -Path src/Aspire.ProjectTemplates -Force | Out-Null
            Set-Content src/Aspire.ProjectTemplates/input.txt original
            Commit baseline
            if ($env:SCENARIO -eq 'pr-merge') {
              $target = git branch --show-current
              git checkout -qb pull-request
            }
            if ($env:SCENARIO -eq 'rename') {
              git mv src/Aspire.ProjectTemplates/input.txt moved.txt
            } else {
              Add-Content src/Aspire.ProjectTemplates/input.txt changed
            }
            Commit input-change
            if ($env:SCENARIO -eq 'push-range') {
              Set-Content README.md unrelated
              Commit unrelated-tip
            }
            if ($env:SCENARIO -eq 'pr-merge') {
              git checkout -q $target
              Set-Content README.md target-change
              Commit target-change
              git -c user.name=Test -c user.email=test@example.invalid merge --no-ff --quiet pull-request -m merged
              if ($LASTEXITCODE -ne 0) { throw 'Fixture merge failed' }
            }
            """);
        using (var command = new PowerShellCommand(setup, output).WithWorkingDirectory(root).WithEnvironmentVariable("SCENARIO", scenario))
        {
            (await command.ExecuteAsync()).EnsureSuccessful();
        }
        using var check = new PowerShellCommand(Path.Combine(RepoRoot.Path, "eng", "scripts", "update-template-cgmanifest.ps1"), output)
            .WithEnvironmentVariable("TEMPLATE_CG_BASE_SHA", "");
        var arguments = new List<string> { "-ChangesOnly", "-RepositoryRoot", $"\"{root}\"" };
        if (baseRef.Length > 0)
        {
            arguments.AddRange(["-BaseRef", baseRef]);
        }
        var result = await check.ExecuteAsync([.. arguments]);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Shipping packages not found", result.Output);
    }
}
