using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MonoDreams.Cli.Installer;
using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Tests;

/// <summary>
/// Issue #87 — the scaffolded WEB head must ship the full BlazorGL content-build block, not a
/// "copy it from the engine repo yourself" placeholder. The block is ~80 lines of MSBuild that a game
/// needs verbatim (KNI's MGCB is Windows-only in three places), so the risk it carries is not "is it
/// correct today" — the emitted-project build proves that — but "does it stay correct while the engine's
/// own web head evolves". These tests pin it to its single source of truth,
/// <c>MonoDreams.Demos.Web.csproj</c>: that head is the minimal, prebuilt-importers-only web content
/// build, exactly the shape a fresh game needs.
///
/// <para>
/// The comparison is structural (comments stripped, whitespace collapsed) and the ONE legitimate
/// difference — the project-specific path to the shared <c>Content.mgcb</c> — is normalised away. Any
/// other divergence (a bumped package version, a new staged dll, a dropped ffmpeg copy) fails here,
/// which is the point: a fix applied to the engine's web head that never reaches the template leaves
/// every scaffolded game broken in a way no build catches until a user hits it.
/// </para>
/// </summary>
public class WebContentBuildTemplateTests : IClassFixture<WebScaffoldFixture>
{
    private readonly WebScaffoldFixture _p;

    public WebContentBuildTemplateTests(WebScaffoldFixture project) => _p = project;

    /// <summary>Path to the in-repo web head that is the template's source of truth.</summary>
    private static string DemosWebCsprojPath => Path.Combine(
        CliTestSupport.FindRepoRoot(), "MonoDreams.Demos.Web", "MonoDreams.Demos.Web.csproj");

    // ---- drift ----------------------------------------------------------------------------------

    [Fact]
    public void ContentBuildBlock_IsStructurallyIdenticalToTheDemosWebHead()
    {
        Assert.True(File.Exists(DemosWebCsprojPath), $"Missing source of truth: {DemosWebCsprojPath}");

        var reference = Normalize(ExtractContentBuildBlock(File.ReadAllText(DemosWebCsprojPath), "MonoDreams.Demos.Web.csproj"));
        var emitted = Normalize(ExtractContentBuildBlock(_p.WebCsproj, "the scaffolded web head"));

        Assert.True(reference == emitted,
            "The CLI web template's BlazorGL content-build block has drifted from MonoDreams.Demos.Web.csproj " +
            "(its single source of truth). Port the change into ProjectScaffolder.WriteWebCsproj — a fix that " +
            "lands only in the engine's own head leaves every `monodreams init --platform web` project broken.\n" +
            $"Demos.Web:\n{reference}\n\nTemplate:\n{emitted}");
    }

    // ---- the pieces the block cannot work without -----------------------------------------------

    [Fact]
    public void ContentBuild_RunsTheManagedMgcbOffWindows_AndImportsTheKniBuilderTargets()
    {
        // KNI's builder package ships a Windows MGCB.exe; off Windows the managed MGCB.dll must be run
        // through `dotnet` instead, and the .targets that own RunContentBuilder must be imported.
        Assert.Contains("<KniContentBuilderExe Condition=\"'$(OS)' != 'Windows_NT'\">dotnet</KniContentBuilderExe>", _p.WebCsproj);
        Assert.Contains("nkast.Xna.Framework.Content.Pipeline.Builder.targets", _p.WebCsproj);
        Assert.Contains("BeforeTargets=\"RunContentBuilder\"", _p.WebCsproj);
    }

    [Fact]
    public void ContentBuild_DownloadsTheMonoGameMgcbToolItBorrowsNativesFrom()
    {
        // Every native/ffmpeg borrow below is Exists()-guarded against $(NuGetPackageRoot)dotnet-mgcb/<v>/.
        // On a machine that never built desktop content that package is simply absent, and the guards turn
        // the whole shim into a silent no-op — so the download is what makes the block self-contained.
        var doc = XDocument.Parse(_p.WebCsproj);
        var download = doc.Descendants("PackageDownload")
            .FirstOrDefault(d => (string?)d.Attribute("Include") == "dotnet-mgcb");
        Assert.True(download is not null,
            "The web template borrows FreeImage/freetype/ffmpeg from the dotnet-mgcb package but never " +
            "downloads it; on a web-only machine the shim silently no-ops and the first texture fails.");

        // PackageDownload requires exact-version bracket notation, and that version must be the same one
        // every borrow path is built from.
        Assert.Equal($"[{ProjectScaffolder.MonoGameMgcbToolVersion}]", (string?)download!.Attribute("Version"));
        Assert.Contains($"$(NuGetPackageRoot)dotnet-mgcb/{ProjectScaffolder.MonoGameMgcbToolVersion}/", _p.WebCsproj);
    }

    [Fact]
    public void ContentBuild_StagesTheNativeLibsAndFfmpegKniShipsForNoPlatform()
    {
        // FreeImage/freetype for the TextureImporter (macOS + Linux), ffmpeg/ffprobe for WavImporter.
        Assert.Contains("FreeImage.dylib", _p.WebCsproj);
        Assert.Contains("freetype6.dylib", _p.WebCsproj);
        Assert.Contains("FreeImage.so", _p.WebCsproj);
        Assert.Contains("ffmpeg", _p.WebCsproj);
        Assert.Contains("ffprobe", _p.WebCsproj);

        // KNI.Extended runtime + Autofac staged next to MGCB so the BitmapFont importer's dependency
        // probe (which only looks in the builder's tools dir) resolves.
        Assert.Contains("KNI.Extended.dll", _p.WebCsproj);
        Assert.Contains("Autofac.dll", _p.WebCsproj);
    }

    // ---- the .mgcb the block points at ----------------------------------------------------------

    [Fact]
    public void SharedContentMgcb_IsScaffoldedInCore_AndIsWhatTheWebHeadBuilds()
    {
        // A KniContentReference to a file that does not exist makes MGCB fail on `/@:<missing>`, so the
        // template must ship the .mgcb it names — and it must live in Core, because that is the directory
        // `monodreams add` appends a module's mgcbEntries to (Installer -> MgcbEditor).
        var mgcb = Path.Combine(_p.CoreDir, "Content", "Content.mgcb");
        Assert.True(File.Exists(mgcb), $"init did not scaffold the shared content project at {mgcb}");

        var text = File.ReadAllText(mgcb);
        Assert.Contains("/outputDir:bin", text);
        Assert.Contains("/intermediateDir:obj", text);

        var doc = XDocument.Parse(_p.WebCsproj);
        var reference = doc.Descendants("KniContentReference").Single();
        Assert.Equal(@"..\Tmp.Core\Content\Content.mgcb", (string?)reference.Attribute("Include"));
        // The Link's directory name is what the KNI targets use as the output ContentFolder — with this
        // Link the BlazorGL output lands in the head's wwwroot/Content/, where Content.RootDirectory
        // ("Content", set by the scaffolded GameRoot) resolves it over HTTP.
        Assert.Equal(@"Content\Content.mgcb", (string?)reference.Element("Link"));
    }

    [Fact]
    public void SharedContentMgcb_IsAlsoBuiltByTheDesktopHead_SoOneSourceFeedsBothBackends()
    {
        // Same .mgcb, two backends (level-loading premise "Content is built per-platform from the same
        // .mgcb"). The desktop head is scaffolded only for desktop/multi, so scaffold one here.
        var root = CliTestSupport.NewTempDir("webtemplate-multi");
        try
        {
            var projectDir = Path.Combine(root, "Multi");
            ProjectScaffolder.Scaffold(projectDir, "Multi", new[] { Platform.Desktop, Platform.Web });

            Assert.True(File.Exists(Path.Combine(projectDir, "Multi.Core", "Content", "Content.mgcb")));

            var desktop = XDocument.Load(Path.Combine(projectDir, "Multi.Desktop", "Multi.Desktop.csproj"));
            var reference = desktop.Descendants("MonoGameContentReference").Single();
            Assert.Equal(@"..\Multi.Core\Content\Content.mgcb", (string?)reference.Attribute("Include"));
            Assert.Equal(@"Content\Content.mgcb", (string?)reference.Element("Link"));

            var web = XDocument.Load(Path.Combine(projectDir, "Multi.Web", "Multi.Web.csproj"));
            Assert.Equal(@"..\Multi.Core\Content\Content.mgcb",
                (string?)web.Descendants("KniContentReference").Single().Attribute("Include"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { /* best-effort */ }
        }
    }

    [Fact]
    public void GeneratedWebContentOutput_IsGitignored()
    {
        // The BlazorGL content output is written INTO the head's wwwroot (that is where the browser
        // fetches it from), i.e. outside the bin/ + obj/ the .gitignore already covers.
        var gitignore = File.ReadAllText(Path.Combine(_p.ProjectDir, ".gitignore"));
        Assert.Contains("*.Web/wwwroot/Content/", gitignore);
    }

    // ---- version coupling -----------------------------------------------------------------------

    [Fact]
    public void KniExtendedVersion_MatchesTheVersionTheRenderingModuleInstallsIntoCore()
    {
        // The head's KNI.Extended.Content.Pipeline WRITES the .xnb; the KNI.Extended the rendering module
        // puts in Core READS it. They are one package pair — a split version is a runtime content-load
        // failure with a perfectly green build.
        var manifest = Path.Combine(CliTestSupport.FindRepoRoot(), "MonoDreams", "rendering", "module.json");
        Assert.True(File.Exists(manifest), $"Missing manifest: {manifest}");

        using var json = JsonDocument.Parse(File.ReadAllText(manifest));
        var extended = json.RootElement.GetProperty("nugetDependencies")
            .EnumerateArray()
            .Single(e => e.GetProperty("id").GetString() == "KNI.Extended");

        Assert.Equal(ProjectScaffolder.KniExtendedVersion, extended.GetProperty("version").GetString());
        Assert.Contains($"kni.extended/{ProjectScaffolder.KniExtendedVersion}/", _p.WebCsproj);
    }

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>
    /// The content-build block of a web head: everything from the <c>&lt;PropertyGroup&gt;</c> that declares
    /// <c>KniBuilderPkg</c> down to the end of the shim <c>&lt;Target&gt;</c>. Anchoring on those two markers
    /// (rather than on a comment banner) keeps the extraction stable across re-worded comments.
    /// </summary>
    private static string ExtractContentBuildBlock(string csproj, string what)
    {
        var marker = csproj.IndexOf("<KniBuilderPkg>", StringComparison.Ordinal);
        Assert.True(marker >= 0, $"No <KniBuilderPkg> property found in {what} — the content-build block is missing.");

        var start = csproj.LastIndexOf("<PropertyGroup>", marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"No <PropertyGroup> enclosing <KniBuilderPkg> in {what}.");

        var end = csproj.LastIndexOf("</Target>", StringComparison.Ordinal);
        Assert.True(end > start, $"No shim </Target> after the content-build properties in {what}.");

        return csproj[start..(end + "</Target>".Length)];
    }

    /// <summary>
    /// Strips XML comments and collapses whitespace, then normalises the ONE thing that legitimately
    /// differs between the reference head and the template: the relative path to the project's own
    /// <c>Content.mgcb</c>.
    /// </summary>
    private static string Normalize(string block)
    {
        var text = Regex.Replace(block, "<!--.*?-->", " ", RegexOptions.Singleline);
        text = Regex.Replace(text, @"Include=""\.\.\\[^""]*\\Content\\Content\.mgcb""", @"Include=""<SHARED_MGCB>""");
        return Regex.Replace(text, @"\s+", " ").Trim();
    }
}
