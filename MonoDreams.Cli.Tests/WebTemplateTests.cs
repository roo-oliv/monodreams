using System.Text.RegularExpressions;
using System.Xml.Linq;
using MonoDreams.Cli.Installer;
using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Tests;

/// <summary>
/// Scaffolds a web-head project ONCE for the whole class and exposes the emitted web-template files as
/// strings. Files that a template revision may not have emitted yet read back as <see cref="string.Empty"/>
/// (with the path kept for existence assertions) so a missing file fails the one test that pins it instead
/// of collapsing every test in the class with a fixture-constructor exception.
/// </summary>
public sealed class WebScaffoldFixture : IDisposable
{
    public string Root { get; }
    public string ProjectDir { get; }
    public string WebDir { get; }
    public string CoreDir { get; }

    public string IndexHtmlPath { get; }
    public string WebCsprojPath { get; }
    public string IndexRazorPath { get; }
    public string IndexRazorCsPath { get; }
    public string BootProgressPath { get; }
    public string WebPlatformServicesPath { get; }
    public string GameRootPath { get; }

    public string IndexHtml { get; }
    public string WebCsproj { get; }
    public string IndexRazor { get; }
    public string IndexRazorCs { get; }
    public string BootProgress { get; }
    public string WebPlatformServices { get; }
    public string GameRoot { get; }

    public WebScaffoldFixture()
    {
        Root = CliTestSupport.NewTempDir("webtemplate");
        ProjectDir = Path.Combine(Root, "Tmp");
        ProjectScaffolder.Scaffold(ProjectDir, "Tmp", new[] { Platform.Web });

        WebDir = Path.Combine(ProjectDir, "Tmp.Web");
        CoreDir = Path.Combine(ProjectDir, "Tmp.Core");

        IndexHtmlPath = Path.Combine(WebDir, "wwwroot", "index.html");
        WebCsprojPath = Path.Combine(WebDir, "Tmp.Web.csproj");
        IndexRazorPath = Path.Combine(WebDir, "Pages", "Index.razor");
        IndexRazorCsPath = Path.Combine(WebDir, "Pages", "Index.razor.cs");
        BootProgressPath = Path.Combine(WebDir, "BootProgress.cs");
        WebPlatformServicesPath = Path.Combine(WebDir, "WebPlatformServices.cs");
        GameRootPath = Path.Combine(CoreDir, "GameRoot.cs");

        IndexHtml = ReadOrEmpty(IndexHtmlPath);
        WebCsproj = ReadOrEmpty(WebCsprojPath);
        IndexRazor = ReadOrEmpty(IndexRazorPath);
        IndexRazorCs = ReadOrEmpty(IndexRazorCsPath);
        BootProgress = ReadOrEmpty(BootProgressPath);
        WebPlatformServices = ReadOrEmpty(WebPlatformServicesPath);
        GameRoot = ReadOrEmpty(GameRootPath);
    }

    private static string ReadOrEmpty(string path) => File.Exists(path) ? File.ReadAllText(path) : string.Empty;

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { /* best-effort temp cleanup */ }
    }
}

/// <summary>
/// Issue #55 — "correct-by-default index.html" contract protection for the CLI's WEB template
/// (<see cref="ProjectScaffolder"/>'s <c>Tmp.Web</c> head). Every assertion here pins something a scaffolded
/// game silently gets wrong otherwise and that NO build step can catch:
///
/// <list type="bullet">
///   <item>a relative <c>&lt;base&gt;</c> so the page boots from a sub-path (itch.io / GitHub Pages), not just
///         the site root;</item>
///   <item>nkast.Wasm.* shim script versions and nkast.* package versions stamped from single constants —
///         a drifting shim version is a runtime 404, invisible at build time;</item>
///   <item>a splash that lives OUTSIDE <c>#app</c> (Blazor wipes <c>#app</c>'s content on start, so a splash
///         inside it is destroyed before the game can paint), ticks on a real interval, and has a backstop;</item>
///   <item>a byte-weighted loading bar that is honest end-to-end: download bytes via
///         <c>loadBootResource</c>, then managed-side boot milestones via <c>BootProgress</c>;</item>
///   <item>a sharp canvas: one virtual resolution shared by the page JS and the game, nearest-neighbour
///         scaling, and the keyboard/scroll housekeeping the canvas needs to be playable;</item>
///   <item>a <c>WebPlatformServices</c> that implements the FULL live <c>IPlatformServices</c> surface — the
///         tripwire for the <c>ExportScene</c>-style breakage where an engine interface grows a member and
///         the template silently stops compiling for consumers.</item>
/// </list>
///
/// The scaffold is in-process (no <c>dotnet</c> invocation); the emitted-project build lives in
/// <see cref="ScaffolderBuildTests"/>.
/// </summary>
public class WebTemplateTests : IClassFixture<WebScaffoldFixture>
{
    private readonly WebScaffoldFixture _p;

    public WebTemplateTests(WebScaffoldFixture project) => _p = project;

    // ---- base href ----------------------------------------------------------------------------

    [Fact]
    public void RelativeBaseHref_BaseIsDotSlash_NotSiteRoot()
    {
        Assert.Contains("<base href=\"./\" />", _p.IndexHtml);
        Assert.DoesNotContain("<base href=\"/\" />", _p.IndexHtml);
    }

    // ---- version stamping ---------------------------------------------------------------------

    [Fact]
    public void ShimVersionsAreStamped_EveryWasmScriptCarriesTheConstant()
    {
        var srcs = WasmShimScriptSrcs(_p.IndexHtml);

        Assert.True(srcs.Count >= 10,
            $"Expected at least 10 _content/nkast.Wasm.* script tags in index.html, found {srcs.Count}.");

        var expected = ProjectScaffolder.WasmShimVersion;
        foreach (var src in srcs)
        {
            Assert.True(src.Contains("." + expected + ".js"),
                $"Shim script src '{src}' is not stamped with WasmShimVersion '{expected}'.");

            // Consistency check with no build-time equivalent: pull the version segment back out of the
            // src and demand it IS the constant — a stale/mixed shim version is a runtime 404, not a
            // compile error.
            var version = Regex.Match(src, @"\.(?<v>\d+(?:\.\d+)+)\.js$");
            Assert.True(version.Success, $"Shim script src '{src}' carries no <name>.<version>.js version segment.");
            Assert.Equal(expected, version.Groups["v"].Value);
        }
    }

    [Fact]
    public void KniPackageVersionsAreStamped_EveryNkastPackageCarriesTheConstant()
    {
        Assert.True(File.Exists(_p.WebCsprojPath), $"Missing web head csproj: {_p.WebCsprojPath}");

        var nkast = XDocument.Load(_p.WebCsprojPath)
            .Descendants("PackageReference")
            .Select(p => (Include: (string?)p.Attribute("Include") ?? "", Version: (string?)p.Attribute("Version") ?? ""))
            .Where(p => p.Include.StartsWith("nkast.", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(nkast);
        foreach (var pkg in nkast)
        {
            Assert.True(pkg.Version == ProjectScaffolder.KniPackageVersion,
                $"PackageReference '{pkg.Include}' is pinned to '{pkg.Version}' but must carry " +
                $"KniPackageVersion '{ProjectScaffolder.KniPackageVersion}'.");
        }
    }

    // ---- splash -------------------------------------------------------------------------------

    [Fact]
    public void SplashLivesOutsideApp_AppDivIsEmptyAndLoadingFollowsIt()
    {
        // Blazor replaces the contents of #app when it starts, so a splash nested inside it is torn down
        // before the game paints its first frame — it must be a sibling that renders after it.
        Assert.Contains("<div id=\"app\"></div>", _p.IndexHtml);

        var app = _p.IndexHtml.IndexOf("id=\"app\"", StringComparison.Ordinal);
        var loading = _p.IndexHtml.IndexOf("id=\"loading\"", StringComparison.Ordinal);
        Assert.True(app >= 0, "index.html has no id=\"app\" element.");
        Assert.True(loading >= 0, "index.html has no id=\"loading\" element.");
        Assert.True(loading > app,
            $"id=\"loading\" (index {loading}) must appear AFTER id=\"app\" (index {app}) — outside, not nested.");
    }

    [Fact]
    public void SplashTickerIsIntervalWithBackstop()
    {
        // A real timer, not a rAF chain the game loop starves, plus a hard dismissal so a stuck boot can
        // never leave the splash pinned over the canvas forever.
        Assert.Contains("setInterval(splashPaint", _p.IndexHtml);
        Assert.Contains("setTimeout(splashDismiss, 60000)", _p.IndexHtml);
    }

    // ---- honest loading bar -------------------------------------------------------------------

    [Fact]
    public void ByteWeightedLoader_StartsBlazorManuallyAndDrivesCssPercentage()
    {
        Assert.Contains("autostart=\"false\"", _p.IndexHtml);
        Assert.Contains("Blazor.start(", _p.IndexHtml);
        Assert.Contains("loadBootResource", _p.IndexHtml);
        Assert.Contains("--monodreams-load-percentage", _p.IndexHtml);
        Assert.Contains("var(--monodreams-load-percentage, var(--blazor-load-percentage, 0%))", _p.IndexHtml);

        // The dotnet.js runtime asset must be returned unmodified from loadBootResource, or the runtime
        // fails to boot.
        Assert.Contains("if (type === 'dotnetjs') return defaultUri;", _p.IndexHtml);
    }

    [Fact]
    public void BootContractDefined_WindowMonodreamsBootExposesSetPhaseAndSetProgress()
    {
        Assert.Contains("window.monodreamsBoot", _p.IndexHtml);
        Assert.Contains("setPhase", _p.IndexHtml);
        Assert.Contains("setProgress", _p.IndexHtml);
    }

    [Fact]
    public void HeadCallsBootMilestones_BootProgressIsWiredFromManagedCode()
    {
        // Managed side of the boot contract: bytes downloaded is only half the wait — the runtime start,
        // content load, and Game construction are the other half, and only C# can report them.
        Assert.Contains("BootProgress.Attach(JsRuntime)", _p.IndexRazorCs);
        Assert.Contains("BootProgress.SetPhase(\"runtime started\")", _p.IndexRazorCs);
        Assert.Contains("BootProgress.SetPhase(\"loading content\")", _p.IndexRazorCs);
        Assert.Contains("BootProgress.SetPhase(\"game constructed\")", _p.IndexRazorCs);

        Assert.True(File.Exists(_p.BootProgressPath), $"Missing web head file: {_p.BootProgressPath}");
        Assert.Contains("public static class BootProgress", _p.BootProgress);
        Assert.Contains("SetPhase(string", _p.BootProgress);
        Assert.Contains("SetProgress(int", _p.BootProgress);
        // Synchronous JS interop: the boot milestones must land before the frame they describe.
        Assert.Contains("IJSInProcessRuntime", _p.BootProgress);

        // EnableDefaultCompileItems is off in the web head, so a new file that is not listed simply is
        // not compiled — and the milestones silently vanish.
        var compiled = XDocument.Load(_p.WebCsprojPath)
            .Descendants("Compile")
            .Select(c => (string?)c.Attribute("Include"))
            .ToList();
        Assert.Contains("BootProgress.cs", compiled);
    }

    // ---- sharp canvas -------------------------------------------------------------------------

    [Fact]
    public void SharpCanvas_VirtualResolutionAndPixelatedScalingAreConsistent()
    {
        // One virtual resolution, stamped from the same constants into the page JS and the game.
        Assert.Contains($"const GAME_WIDTH = {ProjectScaffolder.DefaultVirtualWidth}", _p.IndexHtml);
        Assert.Contains($"GAME_HEIGHT = {ProjectScaffolder.DefaultVirtualHeight}", _p.IndexHtml);

        Assert.Contains("image-rendering: pixelated", _p.IndexRazor);

        Assert.Contains($"VirtualWidth = {ProjectScaffolder.DefaultVirtualWidth}", _p.GameRoot);
        Assert.Contains($"VirtualHeight = {ProjectScaffolder.DefaultVirtualHeight}", _p.GameRoot);

        // Keyboard/scroll housekeeping the canvas needs to stay playable (focusable canvas, no context
        // menu on right-click, no page scroll from arrows/space/wheel).
        Assert.Contains("tabindex", _p.IndexHtml);
        Assert.Contains("contextmenu", _p.IndexHtml);
        Assert.Contains("preventDefault", _p.IndexHtml);
        Assert.Contains("{ passive: false }", _p.IndexHtml);
    }

    // ---- IPlatformServices parity tripwire ------------------------------------------------------

    [Fact]
    public void WebPlatformServicesImplementsFullInterface()
    {
        var interfacePath = Path.Combine(
            CliTestSupport.FindRepoRoot(), "MonoDreams", "foundation", "Platform", "IPlatformServices.cs");
        Assert.True(File.Exists(interfacePath), $"Missing engine interface: {interfacePath}");

        var members = InterfaceMemberNames(File.ReadAllText(interfacePath));
        Assert.True(members.Count >= 5,
            $"Extracted only {members.Count} members from IPlatformServices — the extraction regex went stale.");

        foreach (var member in members)
        {
            Assert.True(_p.WebPlatformServices.Contains(member, StringComparison.Ordinal),
                $"IPlatformServices member '{member}' is not implemented in the scaffolded " +
                $"WebPlatformServices.cs — a web head generated by `monodreams init` will not compile. " +
                $"(Members found: {string.Join(", ", members)}.)");
        }

        // The member whose absence broke consumers once already — pinned by name so the tripwire is
        // legible even if the extraction above ever regresses.
        Assert.Contains("ExportScene", _p.WebPlatformServices);
    }

    // ---- helpers --------------------------------------------------------------------------------

    /// <summary>Every <c>_content/nkast.Wasm.*</c> script src in the document, in source order.</summary>
    private static List<string> WasmShimScriptSrcs(string html) =>
        Regex.Matches(html, "<script[^>]*\\ssrc=\"(?<src>_content/nkast\\.Wasm\\.[^\"]+)\"")
            .Select(m => m.Groups["src"].Value)
            .ToList();

    /// <summary>
    /// Member NAMES declared in the body of <c>IPlatformServices</c>: lines of the form
    /// <c>&lt;type&gt; Name(</c> (methods) and <c>&lt;type&gt; Name {</c> / <c>=&gt;</c> (properties),
    /// with comment lines skipped so prose parentheses do not register as members.
    /// </summary>
    private static List<string> InterfaceMemberNames(string source)
    {
        var declaration = source.IndexOf("interface IPlatformServices", StringComparison.Ordinal);
        Assert.True(declaration >= 0, "Could not locate the IPlatformServices declaration.");

        var names = new List<string>();
        var lines = source[declaration..].Split('\n').Skip(1);
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("//") || line.StartsWith("*") || line.StartsWith("/*"))
                continue;

            var match = Regex.Match(line, @"^(?:public\s+|internal\s+)?[\w\.<>\[\]\?]+\s+(?<name>\w+)\s*(\(|\{|=>)");
            if (match.Success && match.Groups["name"].Value != "IPlatformServices")
                names.Add(match.Groups["name"].Value);
        }
        return names.Distinct().ToList();
    }
}
