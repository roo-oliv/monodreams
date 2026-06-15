using System.Xml.Linq;
using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Installer;

internal static class CsprojEditor
{
    public static void ApplyModule(string csprojPath, ModuleManifest manifest)
    {
        var doc = XDocument.Load(csprojPath);
        var project = doc.Root ?? throw new InvalidDataException($"'{csprojPath}' has no root element.");

        if (manifest.CsprojProperties.Count > 0)
        {
            var props = EnsureNamedPropertyGroup(project, "MonoDreams.Cli managed");
            foreach (var (key, value) in manifest.CsprojProperties)
                SetProperty(props, key, value);
        }

        if (manifest.NugetDependencies.Count > 0)
        {
            var items = EnsureNamedItemGroup(project, "MonoDreams.Cli managed");
            foreach (var nuget in manifest.NugetDependencies)
                AddOrReplacePackageReference(items, nuget);
        }

        doc.Save(csprojPath);
    }

    private static XElement EnsureNamedPropertyGroup(XElement project, string label)
    {
        var group = project.Elements("PropertyGroup").FirstOrDefault(e => (string?)e.Attribute("Label") == label);
        if (group is null)
        {
            group = new XElement("PropertyGroup", new XAttribute("Label", label));
            project.Add(group);
        }
        return group;
    }

    private static XElement EnsureNamedItemGroup(XElement project, string label)
    {
        var group = project.Elements("ItemGroup").FirstOrDefault(e => (string?)e.Attribute("Label") == label);
        if (group is null)
        {
            group = new XElement("ItemGroup", new XAttribute("Label", label));
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
