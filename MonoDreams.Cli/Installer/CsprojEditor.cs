using System.Xml.Linq;
using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Installer;

internal static class CsprojEditor
{
    /// <summary>
    /// Injects a module's csproj properties and NuGet packages into <paramref name="csprojPath"/>,
    /// restricted to the packages that apply to <paramref name="targetPlatforms"/>. A package tagged
    /// for a single backend (e.g. <c>MonoGame.Framework.DesktopGL</c> on desktop,
    /// <c>nkast.Xna.Framework</c> on web) is added only when that platform is targeted; untagged
    /// (pure-.NET) packages are always added. For a multi-platform project (desktop + web) the editor
    /// wraps each backend-specific package in a <c>$(MonoDreamsPlatform)</c>-conditioned ItemGroup so
    /// the two backends never resolve into the same build.
    /// </summary>
    public static void ApplyModule(string csprojPath, ModuleManifest manifest, IReadOnlyList<Platform> targetPlatforms)
    {
        var doc = XDocument.Load(csprojPath);
        var project = doc.Root ?? throw new InvalidDataException($"'{csprojPath}' has no root element.");

        if (manifest.CsprojProperties.Count > 0)
        {
            var props = EnsureNamedPropertyGroup(project, "MonoDreams.Cli managed", condition: null);
            foreach (var (key, value) in manifest.CsprojProperties)
                SetProperty(props, key, value);
        }

        // Bucket each package by the set of target platforms it actually applies to, so a package can
        // land in an unconditioned group (applies to every target) or a per-backend conditioned group.
        var applicable = manifest.NugetDependencies
            .Select(n => (Dep: n, On: targetPlatforms.Where(n.AppliesTo).ToList()))
            .Where(x => x.On.Count > 0)
            .ToList();

        // Untagged packages (apply to all targeted platforms) go in one unconditioned group.
        var unconditioned = applicable.Where(x => x.On.Count == targetPlatforms.Count).Select(x => x.Dep).ToList();
        if (unconditioned.Count > 0)
        {
            var items = EnsureNamedItemGroup(project, "MonoDreams.Cli managed", condition: null);
            foreach (var nuget in unconditioned)
                AddOrReplacePackageReference(items, nuget);
        }

        // Backend-specific packages go in a $(MonoDreamsPlatform)-conditioned group per platform, so a
        // multi-platform Core resolves DesktopGL under a desktop head and nkast under a web head.
        foreach (var platform in targetPlatforms)
        {
            var perPlatform = applicable
                .Where(x => x.On.Count != targetPlatforms.Count && x.On.Contains(platform))
                .Select(x => x.Dep)
                .ToList();
            if (perPlatform.Count == 0) continue;

            var token = Platforms.ToToken(platform);
            var items = EnsureNamedItemGroup(project, $"MonoDreams.Cli managed ({token})", condition: $"'$(MonoDreamsPlatform)' == '{token}'");
            foreach (var nuget in perPlatform)
                AddOrReplacePackageReference(items, nuget);
        }

        doc.Save(csprojPath);
    }

    private static XElement EnsureNamedPropertyGroup(XElement project, string label, string? condition)
    {
        var group = project.Elements("PropertyGroup").FirstOrDefault(e => (string?)e.Attribute("Label") == label);
        if (group is null)
        {
            group = new XElement("PropertyGroup", new XAttribute("Label", label));
            if (condition is not null) group.SetAttributeValue("Condition", condition);
            project.Add(group);
        }
        return group;
    }

    private static XElement EnsureNamedItemGroup(XElement project, string label, string? condition)
    {
        var group = project.Elements("ItemGroup").FirstOrDefault(e => (string?)e.Attribute("Label") == label);
        if (group is null)
        {
            group = new XElement("ItemGroup", new XAttribute("Label", label));
            if (condition is not null) group.SetAttributeValue("Condition", condition);
            project.Add(group);
        }
        return group;
    }

    private static void SetProperty(XElement propertyGroup, string name, string value)
    {
        var existing = propertyGroup.Element(name);
        if (existing is null)
            propertyGroup.Add(new XElement(name, value));
        else
            existing.Value = value;
    }

    private static void AddOrReplacePackageReference(XElement itemGroup, NugetDep nuget)
    {
        var existing = itemGroup.Elements("PackageReference")
            .FirstOrDefault(e => (string?)e.Attribute("Include") == nuget.Id);
        if (existing is not null) existing.Remove();

        var element = new XElement("PackageReference",
            new XAttribute("Include", nuget.Id),
            new XAttribute("Version", nuget.Version));
        if (!string.IsNullOrEmpty(nuget.PrivateAssets))
            element.Add(new XAttribute("PrivateAssets", nuget.PrivateAssets));
        itemGroup.Add(element);
    }
}
