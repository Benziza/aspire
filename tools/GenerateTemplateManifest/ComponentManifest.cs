// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NuGet.Versioning;

namespace GenerateTemplateManifest;

internal sealed class ComponentManifest(IReadOnlySet<string> builtPackages)
{
    private readonly SortedDictionary<string, JsonObject> _registrations = new(StringComparer.Ordinal);

    internal int Count => _registrations.Count;

    internal void Register(string type, string name, string version, string source)
    {
        if (string.IsNullOrWhiteSpace(name) || !NuGetVersion.TryParse(version, out var parsedVersion))
        {
            throw new InvalidDataException($"Invalid {type} package '{name}' version '{version}' in {source}");
        }
        if (type == "nuget")
        {
            name = name.ToLowerInvariant();
            version = parsedVersion.ToNormalizedString().ToLowerInvariant();
            if (builtPackages.Contains($"{name}/{version}"))
            {
                return;
            }
        }
        _registrations[$"{type}/{name}/{version}"] = new JsonObject
        {
            ["component"] = new JsonObject
            {
                ["type"] = type,
                [type] = new JsonObject { ["name"] = name, ["version"] = version }
            }
        };
    }

    internal string Serialize()
    {
        var entries = new JsonArray();
        foreach (var registration in _registrations.Values)
        {
            entries.Add(registration.DeepClone());
        }
        var manifest = new JsonObject
        {
            ["$schema"] = "https://json.schemastore.org/component-detection-manifest.json",
            ["version"] = 1,
            ["registrations"] = entries
        };
        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
    }

    internal static HashSet<string> ReadBuiltPackages(string directory)
    {
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException($"Shipping packages not found: {directory}. Build with -pack before generating the template manifest.");
        }
        var packages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in Directory.EnumerateFiles(directory, "*.nupkg"))
        {
            using var archive = ZipFile.OpenRead(path);
            var nuspec = archive.Entries.Single(entry => entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            using var stream = nuspec.Open();
            var document = XDocument.Load(stream);
            var ns = document.Root!.Name.Namespace;
            var metadata = document.Root.Element(ns + "metadata")!;
            var id = metadata.Element(ns + "id")!.Value;
            var version = NuGetVersion.Parse(metadata.Element(ns + "version")!.Value).ToNormalizedString();
            packages.Add($"{id}/{version}");
        }
        if (packages.Count == 0)
        {
            throw new InvalidDataException($"No shipping NuGet packages found in {directory}.");
        }
        return packages;
    }

    internal bool Verify(string path, string generatedPath)
    {
        var generated = Serialize();
        if (File.Exists(path) && File.ReadAllText(path).ReplaceLineEndings("\n") == generated)
        {
            return true;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(generatedPath)!);
        File.WriteAllText(generatedPath, generated);
        return false;
    }
}
