using MonoDreams.Cli.Manifest;
using MonoDreams.Cli.Resolver;

namespace MonoDreams.Cli.Tests;

/// <summary>
/// Contract 1 of the audio-module plan: the 14th module <c>audio</c> is discovered by the CLI
/// registry with its dependencies resolved, supports both platforms, and injects its KNI audio
/// package on web only (desktop audio ships inside MonoGame.Framework.DesktopGL — no extra package).
/// </summary>
public class AudioModuleRegistryTests
{
    private static Registry LoadRegistry() => Registry.Load(CliTestSupport.FindRepoRoot());

    [Fact]
    public void Registry_DiscoversAudioModule_WithFoundationDependency()
    {
        var registry = LoadRegistry();

        Assert.Contains(registry.Index.Modules, m => m.Name == "audio");

        var audio = registry.GetModule("audio");
        Assert.Equal(new[] { "foundation" }, audio.Dependencies);
        Assert.True(audio.SupportsPlatform(Platform.Desktop));
        Assert.True(audio.SupportsPlatform(Platform.Web));
    }

    [Fact]
    public void Audio_InjectsKniAudioPackage_OnWebOnly()
    {
        var audio = LoadRegistry().GetModule("audio");

        var desktop = audio.NugetDependenciesFor(Platform.Desktop).Select(n => n.Id).ToList();
        var web = audio.NugetDependenciesFor(Platform.Web).Select(n => n.Id).ToList();

        Assert.Contains("nkast.Xna.Framework.Audio", web);
        Assert.DoesNotContain("nkast.Xna.Framework.Audio", desktop);
        Assert.Empty(desktop); // DesktopGL already ships Microsoft.Xna.Framework.Audio
    }

    // Platform is internal to the CLI assembly, so no [Theory] parameter — one Fact per backend.
    [Fact]
    public void Resolver_ResolvesAudioForDesktop_WithFoundationFirst() => AssertAudioResolves(Platform.Desktop);

    [Fact]
    public void Resolver_ResolvesAudioForWeb_WithFoundationFirst() => AssertAudioResolves(Platform.Web);

    private static void AssertAudioResolves(Platform platform)
    {
        var registry = LoadRegistry();

        var resolved = DependencyResolver.Resolve(registry, new[] { "audio" }, Array.Empty<string>(), platform);

        Assert.Contains("audio", resolved);
        Assert.Contains("foundation", resolved);
        Assert.True(resolved.IndexOf("foundation") < resolved.IndexOf("audio"),
            "foundation must be installed before the module that depends on it");
    }
}
