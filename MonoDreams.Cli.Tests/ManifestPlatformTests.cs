using System.Text.Json;
using MonoDreams.Cli.Manifest;
using MonoDreams.Cli.Resolver;

namespace MonoDreams.Cli.Tests;

/// <summary>
/// Phase 4a (manifest platform-tagging) contract protection. The manifest schema gained a
/// <c>platforms</c> field and made <c>nugetDependencies</c> / <c>mgcbEntries</c> entries
/// platform-taggable (<c>desktop</c> = MonoGame.Framework.DesktopGL / MonoGame.Extended,
/// <c>web</c> = nkast.Xna.Framework / KNI.Extended; default = all platforms). These tests load the
/// real engine manifests through the CLI's <see cref="Registry"/> and assert the data model reads
/// the new fields and resolves platform-specific dependencies correctly.
/// </summary>
public class ManifestPlatformTests
{
    private static Registry LoadRegistry() => Registry.Load(FindRepoRoot());

    /// <summary>foundation tags its framework package per backend: DesktopGL on desktop, nkast on web.</summary>
    [Fact]
    public void Foundation_InjectsBackendSpecificFrameworkPerPlatform()
    {
        var foundation = LoadRegistry().GetModule("foundation");

        var desktop = foundation.NugetDependenciesFor(Platform.Desktop).Select(n => n.Id).ToList();
        var web = foundation.NugetDependenciesFor(Platform.Web).Select(n => n.Id).ToList();

        Assert.Contains("MonoGame.Framework.DesktopGL", desktop);
        Assert.DoesNotContain("MonoGame.Framework.DesktopGL", web);

        Assert.Contains("nkast.Xna.Framework", web);
        Assert.DoesNotContain("nkast.Xna.Framework", desktop);

        // Untagged pure-.NET dep applies to both platforms.
        Assert.Contains("DefaultEcs", desktop);
        Assert.Contains("DefaultEcs", web);
    }

    /// <summary>rendering swaps MonoGame.Extended (desktop) for KNI.Extended (web).</summary>
    [Fact]
    public void Rendering_SwapsExtendedRuntimePerPlatform()
    {
        var rendering = LoadRegistry().GetModule("rendering");

        var desktop = rendering.NugetDependenciesFor(Platform.Desktop).Select(n => n.Id).ToList();
        var web = rendering.NugetDependenciesFor(Platform.Web).Select(n => n.Id).ToList();

        Assert.Contains("MonoGame.Extended", desktop);
        Assert.DoesNotContain("KNI.Extended", desktop);

        Assert.Contains("KNI.Extended", web);
        Assert.DoesNotContain("MonoGame.Extended", web);
    }

    /// <summary>
    /// level-ldtk's LDtkMonogame NuGet packages are desktop-only (the engine vendors LDtk as source
    /// for the web backend), so the web variant injects none of them.
    /// </summary>
    [Fact]
    public void LevelLdtk_LdtkNugetIsDesktopOnly()
    {
        var ldtk = LoadRegistry().GetModule("level-ldtk");

        var web = ldtk.NugetDependenciesFor(Platform.Web).Select(n => n.Id).ToList();
        var desktop = ldtk.NugetDependenciesFor(Platform.Desktop).Select(n => n.Id).ToList();

        Assert.Contains("LDtkMonogame", desktop);
        Assert.Contains("LDtkMonogame.ContentPipeline", desktop);
        Assert.DoesNotContain("LDtkMonogame", web);
        Assert.DoesNotContain("LDtkMonogame.ContentPipeline", web);
    }

    /// <summary>Every engine module declares support for both platforms (none is backend-locked today).</summary>
    [Fact]
    public void AllModules_SupportBothPlatforms()
    {
        var registry = LoadRegistry();
        foreach (var entry in registry.Index.Modules)
        {
            var manifest = registry.GetModule(entry.Name);
            Assert.True(manifest.SupportsPlatform(Platform.Desktop), $"{entry.Name} should support desktop");
            Assert.True(manifest.SupportsPlatform(Platform.Web), $"{entry.Name} should support web");
        }
    }

    /// <summary>
    /// The platform-aware resolver walks the full ldtk-platformer preset for either backend without
    /// rejecting any module (all preset modules support both platforms).
    /// </summary>
    [Fact]
    public void Resolver_ResolvesLdtkPresetForDesktop() => AssertLdtkPresetResolves(Platform.Desktop);

    [Fact]
    public void Resolver_ResolvesLdtkPresetForWeb() => AssertLdtkPresetResolves(Platform.Web);

    private static void AssertLdtkPresetResolves(Platform platform)
    {
        var registry = LoadRegistry();
        var preset = registry.GetPreset("ldtk-platformer");
        Assert.NotNull(preset);

        var resolved = DependencyResolver.Resolve(registry, preset!.Modules, Array.Empty<string>(), platform);

        Assert.Contains("foundation", resolved);
        Assert.Contains("level-ldtk", resolved);
        // foundation has no deps, so it must precede modules that depend on it transitively.
        Assert.True(resolved.IndexOf("foundation") < resolved.IndexOf("rendering"));
    }

    /// <summary>
    /// The resolver rejects a module whose platforms tag excludes the target platform. Synthesizes a
    /// web-only manifest in a temp registry and asserts a desktop resolve throws.
    /// </summary>
    [Fact]
    public void Resolver_RejectsModuleNotSupportingTargetPlatform()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "md-manifest-test-" + Guid.NewGuid().ToString("N"));
        var modulesDir = Path.Combine(tempRoot, "MonoDreams", "weben");
        Directory.CreateDirectory(modulesDir);
        try
        {
            File.WriteAllText(Path.Combine(modulesDir, "module.json"),
                """{ "name": "weben", "description": "web only", "platforms": ["web"] }""");

            var registry = Registry.Load(tempRoot);

            // web resolve succeeds, desktop resolve throws.
            var web = DependencyResolver.Resolve(registry, new[] { "weben" }, Array.Empty<string>(), Platform.Web);
            Assert.Contains("weben", web);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                DependencyResolver.Resolve(registry, new[] { "weben" }, Array.Empty<string>(), Platform.Desktop));
            Assert.Contains("does not support platform 'desktop'", ex.Message);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    /// <summary>
    /// <see cref="MgcbEntry"/> parses both manifest forms: a bare string (all platforms) and a
    /// <c>{ value, platforms }</c> object (backend-specific content-pipeline line).
    /// </summary>
    [Fact]
    public void MgcbEntry_ParsesStringAndTaggedObjectForms()
    {
        var json = """
        [ "/importer:YarnSpinnerImporter", { "value": "/reference:web.dll", "platforms": ["web"] } ]
        """;
        var entries = JsonSerializer.Deserialize<List<MgcbEntry>>(json)!;

        Assert.Equal(2, entries.Count);

        // bare string -> applies to all platforms
        Assert.Equal("/importer:YarnSpinnerImporter", entries[0].Value);
        Assert.True(entries[0].AppliesTo(Platform.Desktop));
        Assert.True(entries[0].AppliesTo(Platform.Web));

        // tagged object -> web only
        Assert.Equal("/reference:web.dll", entries[1].Value);
        Assert.False(entries[1].AppliesTo(Platform.Desktop));
        Assert.True(entries[1].AppliesTo(Platform.Web));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "MonoDreams", "module.schema.json")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Could not find repo root (directory containing MonoDreams/module.schema.json).");
    }
}
