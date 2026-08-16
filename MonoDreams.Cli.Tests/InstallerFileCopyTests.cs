using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Tests;

/// <summary>
/// What <c>monodreams add</c> copies out of a module directory. The file list is implicit — every file
/// inside the module ships (see MODULES.md) — so the exclusions are the contract, and each one exists
/// because copying that file broke a user's build.
/// </summary>
public class InstallerFileCopyTests
{
    /// <summary>
    /// A registry is normally a source checkout, and a module may hold a buildable project of its own
    /// (<c>level-ldtk/vendor/LDtkMonogame</c> ships the vendored LDtk sources with their <c>.csproj</c>), so
    /// a contributor's local build leaves <c>bin/</c> + <c>obj/</c> inside the module directory. Copied into
    /// a user's project those generated <c>AssemblyInfo.cs</c> files land in the SDK's default compile glob
    /// and the first <c>dotnet build</c> fails with CS0579 (duplicate assembly attributes) — a failure with
    /// no relation to anything the user did. Build outputs are never module source.
    /// </summary>
    [Fact]
    public void Apply_CopiesModuleSource_ButNeverBuildOutputs()
    {
        var engineRoot = CliTestSupport.NewTempDir("installer-copy");
        var moduleDir = Path.Combine(engineRoot, "MonoDreams", "widget");
        Directory.CreateDirectory(Path.Combine(moduleDir, "System"));
        Directory.CreateDirectory(Path.Combine(moduleDir, "vendor", "Third", "obj", "Debug"));
        Directory.CreateDirectory(Path.Combine(moduleDir, "vendor", "Third", "bin", "Debug"));
        File.WriteAllText(Path.Combine(engineRoot, "MonoDreams", "module.schema.json"), "{}");
        File.WriteAllText(Path.Combine(moduleDir, "module.json"),
            """{ "name": "widget", "description": "test module" }""");
        File.WriteAllText(Path.Combine(moduleDir, "System", "WidgetSystem.cs"), "// source");
        File.WriteAllText(Path.Combine(moduleDir, "vendor", "Third", "Third.cs"), "// vendored source");
        File.WriteAllText(Path.Combine(moduleDir, "vendor", "Third", "obj", "Debug", "Third.AssemblyInfo.cs"), "// generated");
        File.WriteAllText(Path.Combine(moduleDir, "vendor", "Third", "bin", "Debug", "Third.dll"), "binary");

        var projectDir = CliTestSupport.NewTempDir("installer-copy-project");
        var registry = Registry.Load(engineRoot);
        try
        {
            new MonoDreams.Cli.Installer.Installer(registry, projectDir, dryRun: false, new[] { Platform.Desktop })
                .Apply(registry.GetModule("widget"));

            var installed = Path.Combine(projectDir, "MonoDreams", "widget");
            Assert.True(File.Exists(Path.Combine(installed, "System", "WidgetSystem.cs")));
            Assert.True(File.Exists(Path.Combine(installed, "vendor", "Third", "Third.cs")));
            Assert.False(Directory.Exists(Path.Combine(installed, "vendor", "Third", "obj")),
                "`add` copied a build output directory (obj/) into the user's project");
            Assert.False(Directory.Exists(Path.Combine(installed, "vendor", "Third", "bin")),
                "`add` copied a build output directory (bin/) into the user's project");
        }
        finally
        {
            try { Directory.Delete(engineRoot, recursive: true); } catch { /* best effort */ }
            try { Directory.Delete(projectDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
