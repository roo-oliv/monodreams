using System.Xml.Linq;
using MonoDreams.Cli.Installer;
using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Tests;

/// <summary>
/// Phase 4b (CLI scaffolder + platform-aware editors + commands) contract protection. These tests
/// drive <see cref="ProjectScaffolder"/>, <see cref="CsprojEditor"/>, <see cref="MgcbEditor"/>, and
/// <see cref="StateFile"/> in-process (no `dotnet` invocation) and assert the emitted project tree,
/// the per-platform package routing, and the recorded target platform(s). The build-verification of
/// the emitted projects (contract item "produce buildable projects") lives in
/// <see cref="ScaffolderBuildTests"/>, which actually runs `dotnet build`.
/// </summary>
public class ScaffolderPlatformTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "md-scaffold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---- Scaffolder: Core + heads + .sln driven by --platform --------------------------------

    [Fact]
    public void Scaffold_Desktop_EmitsCoreAndDesktopHeadOnly()
    {
        var root = NewTempDir();
        try
        {
            ProjectScaffolder.Scaffold(Path.Combine(root, "Tmp"), "Tmp", new[] { Platform.Desktop });
            var proj = Path.Combine(root, "Tmp");

            Assert.True(File.Exists(Path.Combine(proj, "Tmp.Core", "Tmp.Core.csproj")));
            Assert.True(File.Exists(Path.Combine(proj, "Tmp.Desktop", "Tmp.Desktop.csproj")));
            Assert.False(Directory.Exists(Path.Combine(proj, "Tmp.Web")));
            Assert.True(File.Exists(Path.Combine(proj, "Tmp.sln")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Scaffold_Web_EmitsCoreAndWebHeadWithHostWiring()
    {
        var root = NewTempDir();
        try
        {
            ProjectScaffolder.Scaffold(Path.Combine(root, "Tmp"), "Tmp", new[] { Platform.Web });
            var web = Path.Combine(root, "Tmp", "Tmp.Web");

            Assert.False(Directory.Exists(Path.Combine(root, "Tmp", "Tmp.Desktop")));
            // Blazor WASM host files (the plan's required web-head pieces).
            Assert.True(File.Exists(Path.Combine(web, "Program.cs")));
            Assert.True(File.Exists(Path.Combine(web, "Pages", "Index.razor")));
            Assert.True(File.Exists(Path.Combine(web, "wwwroot", "index.html")));
            Assert.True(File.Exists(Path.Combine(web, "App.razor")));

            var csproj = File.ReadAllText(Path.Combine(web, "Tmp.Web.csproj"));
            Assert.Contains("Microsoft.NET.Sdk.BlazorWebAssembly", csproj);
            Assert.Contains("nkast.Kni.Platform.Blazor.GL", csproj);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Scaffold_Multi_EmitsBothHeads_WebExcludedFromDefaultSolutionBuild()
    {
        var root = NewTempDir();
        try
        {
            ProjectScaffolder.Scaffold(Path.Combine(root, "Tmp"), "Tmp", Platforms.All);
            var proj = Path.Combine(root, "Tmp");

            Assert.True(File.Exists(Path.Combine(proj, "Tmp.Desktop", "Tmp.Desktop.csproj")));
            Assert.True(File.Exists(Path.Combine(proj, "Tmp.Web", "Tmp.Web.csproj")));

            var sln = File.ReadAllText(Path.Combine(proj, "Tmp.sln"));
            Assert.Contains("Tmp.Core", sln);
            Assert.Contains("Tmp.Desktop", sln);
            Assert.Contains("Tmp.Web", sln);

            // The web head must NOT have a Build.0 line (excluded from the default solution build so a
            // plain `dotnet build` of the .sln does not build it without -p:MonoDreamsPlatform=web).
            var webGuid = ExtractProjectGuid(sln, "Tmp.Web");
            var desktopGuid = ExtractProjectGuid(sln, "Tmp.Desktop");
            Assert.DoesNotContain($"{{{webGuid}}}.Debug|Any CPU.Build.0", sln);
            Assert.Contains($"{{{desktopGuid}}}.Debug|Any CPU.Build.0", sln);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Scaffold_Core_CarriesBackendGateButNoPreDeclaredFrameworkPackages()
    {
        var root = NewTempDir();
        try
        {
            ProjectScaffolder.Scaffold(Path.Combine(root, "Tmp"), "Tmp", Platforms.All);
            var core = File.ReadAllText(Path.Combine(root, "Tmp", "Tmp.Core", "Tmp.Core.csproj"));

            // The backend gate is present; framework packages are NOT pre-declared (the module install
            // injects them platform-tagged via CsprojEditor — otherwise they would be duplicated).
            Assert.Contains("'$(MonoDreamsPlatform)' == 'web'", core);
            Assert.DoesNotContain("PackageReference", core);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ---- CsprojEditor: platform-aware package injection --------------------------------------

    [Fact]
    public void CsprojEditor_Desktop_InjectsOnlyDesktopVariant()
    {
        var (path, manifest) = WriteBareCsprojWithRenderingManifest();
        try
        {
            CsprojEditor.ApplyModule(path, manifest, new[] { Platform.Desktop });
            var doc = XDocument.Load(path);
            var pkgs = doc.Descendants("PackageReference").Select(p => (string?)p.Attribute("Include")).ToList();

            Assert.Contains("MonoGame.Extended", pkgs);
            Assert.DoesNotContain("KNI.Extended", pkgs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CsprojEditor_Web_InjectsOnlyWebVariant()
    {
        var (path, manifest) = WriteBareCsprojWithRenderingManifest();
        try
        {
            CsprojEditor.ApplyModule(path, manifest, new[] { Platform.Web });
            var doc = XDocument.Load(path);
            var pkgs = doc.Descendants("PackageReference").Select(p => (string?)p.Attribute("Include")).ToList();

            Assert.Contains("KNI.Extended", pkgs);
            Assert.DoesNotContain("MonoGame.Extended", pkgs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CsprojEditor_Multi_InjectsBothInBackendConditionedGroups()
    {
        var (path, manifest) = WriteBareCsprojWithRenderingManifest();
        try
        {
            CsprojEditor.ApplyModule(path, manifest, Platforms.All);
            var doc = XDocument.Load(path);

            var desktopGroup = doc.Descendants("ItemGroup")
                .First(g => (string?)g.Attribute("Condition") == "'$(MonoDreamsPlatform)' == 'desktop'");
            var webGroup = doc.Descendants("ItemGroup")
                .First(g => (string?)g.Attribute("Condition") == "'$(MonoDreamsPlatform)' == 'web'");

            Assert.Contains(desktopGroup.Elements("PackageReference"),
                p => (string?)p.Attribute("Include") == "MonoGame.Extended");
            Assert.Contains(webGroup.Elements("PackageReference"),
                p => (string?)p.Attribute("Include") == "KNI.Extended");
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void CsprojEditor_UntaggedPackage_IsUnconditioned()
    {
        var (path, manifest) = WriteBareCsprojWithFoundationManifest();
        try
        {
            CsprojEditor.ApplyModule(path, manifest, Platforms.All);
            var doc = XDocument.Load(path);

            // DefaultEcs is untagged (pure .NET) -> goes in the unconditioned managed group.
            var unconditioned = doc.Descendants("ItemGroup")
                .First(g => (string?)g.Attribute("Label") == "MonoDreams.Cli managed" && g.Attribute("Condition") is null);
            Assert.Contains(unconditioned.Elements("PackageReference"),
                p => (string?)p.Attribute("Include") == "DefaultEcs");
        }
        finally { File.Delete(path); }
    }

    // ---- MgcbEditor: platform-filtered content lines -----------------------------------------

    [Fact]
    public void MgcbEditor_AppendsOnlyEntriesForTargetPlatform()
    {
        var projDir = NewTempDir();
        try
        {
            var contentDir = Path.Combine(projDir, "Content");
            Directory.CreateDirectory(contentDir);
            File.WriteAllText(Path.Combine(contentDir, "Content.mgcb"), "/outputDir:bin\n");

            var manifest = new ModuleManifest
            {
                Name = "demo",
                MgcbEntries = new List<MgcbEntry>
                {
                    new() { Value = "/importer:Shared" },                            // both
                    new() { Value = "/reference:desktop.dll", PlatformsRaw = new() { "desktop" } },
                    new() { Value = "/reference:web.dll", PlatformsRaw = new() { "web" } },
                },
            };

            MgcbEditor.ApplyModule(projDir, manifest, new[] { Platform.Web });
            var content = File.ReadAllText(Path.Combine(contentDir, "Content.mgcb"));

            Assert.Contains("/importer:Shared", content);
            Assert.Contains("/reference:web.dll", content);
            Assert.DoesNotContain("/reference:desktop.dll", content);
        }
        finally { Directory.Delete(projDir, recursive: true); }
    }

    // ---- StateFile: records target platform(s) -----------------------------------------------

    [Fact]
    public void StateFile_RoundTripsPlatforms()
    {
        var dir = NewTempDir();
        try
        {
            var state = StateFile.LoadOrCreate(dir);
            state.Platforms = new List<string> { "desktop", "web" };
            state.Save(dir);

            var reloaded = StateFile.LoadOrCreate(dir);
            Assert.Equal(new[] { "desktop", "web" }, reloaded.Platforms);
            Assert.Equal(new[] { Platform.Desktop, Platform.Web }, reloaded.TargetPlatforms);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void StateFile_MissingPlatforms_DefaultsToDesktop()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, StateFile.FileName),
                """{ "version": 1, "modules": ["foundation"] }""");
            var state = StateFile.LoadOrCreate(dir);
            Assert.Equal(new[] { Platform.Desktop }, state.TargetPlatforms);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---- Runner.ParsePlatformOption ----------------------------------------------------------

    [Theory]
    [InlineData(null, "desktop")]
    [InlineData("desktop", "desktop")]
    [InlineData("web", "web")]
    [InlineData("multi", "desktop,web")]
    public void ParsePlatformOption_MapsTokensToPlatformSet(string? input, string expectedTokens)
    {
        var resolved = MonoDreams.Cli.Commands.Runner.ParsePlatformOption(input)
            .Select(Platforms.ToToken);
        Assert.Equal(expectedTokens, string.Join(",", resolved));
    }

    [Fact]
    public void ParsePlatformOption_RejectsUnknownToken()
    {
        Assert.Throws<InvalidDataException>(() => MonoDreams.Cli.Commands.Runner.ParsePlatformOption("xbox"));
    }

    // ---- helpers ------------------------------------------------------------------------------

    private static (string Path, ModuleManifest Manifest) WriteBareCsprojWithRenderingManifest()
    {
        var path = Path.Combine(Path.GetTempPath(), "md-csproj-" + Guid.NewGuid().ToString("N") + ".csproj");
        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup /></Project>");
        var manifest = new ModuleManifest
        {
            Name = "rendering",
            NugetDependencies = new List<NugetDep>
            {
                new() { Id = "MonoGame.Extended", Version = "4.1.0", PlatformsRaw = new() { "desktop" } },
                new() { Id = "KNI.Extended", Version = "6.0.0", PlatformsRaw = new() { "web" } },
            },
        };
        return (path, manifest);
    }

    private static (string Path, ModuleManifest Manifest) WriteBareCsprojWithFoundationManifest()
    {
        var path = Path.Combine(Path.GetTempPath(), "md-csproj-" + Guid.NewGuid().ToString("N") + ".csproj");
        File.WriteAllText(path, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup /></Project>");
        var manifest = new ModuleManifest
        {
            Name = "foundation",
            NugetDependencies = new List<NugetDep>
            {
                new() { Id = "MonoGame.Framework.DesktopGL", Version = "3.8.4", PlatformsRaw = new() { "desktop" } },
                new() { Id = "nkast.Xna.Framework", Version = "4.2.9001", PlatformsRaw = new() { "web" } },
                new() { Id = "DefaultEcs", Version = "0.18.0-beta01" }, // untagged -> all
            },
        };
        return (path, manifest);
    }

    private static string ExtractProjectGuid(string sln, string projectName)
    {
        // Project("{type}") = "<name>", "<path>", "{<guid>}"
        var line = sln.Split('\n').First(l => l.Contains($"= \"{projectName}\","));
        var lastBrace = line.LastIndexOf('{');
        return line.Substring(lastBrace + 1, 36);
    }
}
