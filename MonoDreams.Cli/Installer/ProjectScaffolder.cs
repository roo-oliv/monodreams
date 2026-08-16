using MonoDreams.Cli.Manifest;

namespace MonoDreams.Cli.Installer;

/// <summary>
/// Emits a fresh MonoDreams project as a shared <c>&lt;Name&gt;.Core</c> game library plus a per-platform
/// head per requested platform, wired into a <c>&lt;Name&gt;.sln</c>. The layout mirrors the reference app
/// (Examples.Core + Examples.Desktop + Examples.Web): MonoDreams source and game code live in Core
/// (backend-agnostic, the backend chosen by the <c>$(MonoDreamsPlatform)</c> MSBuild property the head
/// forwards), each head owns only its entry point and platform host wiring.
///
/// <para>
/// The platform is selected by the head, never baked into Core (see plan "platform is selected by the
/// head"). A desktop head sets the Directory.Build.props default (<c>desktop</c>); a web head builds with
/// <c>-p:MonoDreamsPlatform=web</c> (a GLOBAL property so the KNI packages resolve at restore time —
/// AdditionalProperties on a ProjectReference does not flow to restore). For a multi-platform project the
/// web head is intentionally excluded from the solution's default build configuration so a plain
/// <c>dotnet build</c> of the .sln (the desktop regression) does not try to build it without that property.
/// </para>
/// </summary>
internal static class ProjectScaffolder
{
    // ---- Stamped template constants ------------------------------------------------------------
    // Values the templates below interpolate instead of hard-coding at several sites. Each one exists
    // because the same number has to appear in two or more emitted files, and nothing in the build
    // checks that they agree — the failure is always a green build and a broken page.

    /// <summary>
    /// The KNI package version every <c>nkast.*</c> <c>PackageReference</c> in the web head pins.
    /// HARD-COUPLED to <see cref="WasmShimVersion"/>: the JS interop assets the head's
    /// <c>wwwroot/index.html</c> <c>&lt;script src&gt;</c>s come from the <c>nkast.Wasm.*</c> packages
    /// that <c>nkast.Kni.Platform.Blazor.GL</c> pulls transitively at THIS version. Bump them together.
    /// </summary>
    internal const string KniPackageVersion = "4.2.9001";

    /// <summary>
    /// The <c>nkast.Wasm.*</c> JS asset version stamped into every shim <c>&lt;script src&gt;</c> of the
    /// scaffolded <c>wwwroot/index.html</c> (e.g. <c>Canvas.8.0.11.js</c>). This is a JS asset version,
    /// NOT a framework version, and it is whatever <see cref="KniPackageVersion"/> transitively restores.
    /// Get it wrong and the shims 404 at runtime with zero build warning — see docs/web-targeting.md
    /// ("Web host" row of the dependency-parity table).
    /// </summary>
    internal const string WasmShimVersion = "8.0.11";

    /// <summary>
    /// The virtual resolution a scaffolded game is authored against. Stamped into BOTH
    /// <c>GameRoot.VirtualWidth</c>/<c>VirtualHeight</c> (Core) and <c>GAME_WIDTH</c>/<c>GAME_HEIGHT</c>
    /// in the web head's <c>index.html</c>, so the canvas backbuffer starts exactly 1:1 with the game's
    /// render targets (no resample, no devicePixelRatio blur). They must stay equal if either is changed.
    /// </summary>
    internal const int DefaultVirtualWidth = 1280;

    /// <inheritdoc cref="DefaultVirtualWidth"/>
    internal const int DefaultVirtualHeight = 720;

    /// <summary>
    /// Scaffolds the project tree. Returns the absolute path to the Core project directory — the shared
    /// library where module source and platform-agnostic NuGet packages are installed.
    /// </summary>
    public static string Scaffold(string projectDir, string projectName, IReadOnlyList<Platform> platforms)
    {
        Directory.CreateDirectory(projectDir);

        var coreName = $"{projectName}.Core";
        var coreDir = Path.Combine(projectDir, coreName);
        Directory.CreateDirectory(coreDir);

        var includeDesktop = platforms.Contains(Platform.Desktop);
        var includeWeb = platforms.Contains(Platform.Web);

        WriteCoreCsproj(coreDir, coreName, projectName);
        WriteCoreGameRoot(coreDir, projectName);

        if (includeDesktop) WriteDesktopHead(projectDir, projectName);
        if (includeWeb) WriteWebHead(projectDir, projectName);

        WriteSolution(projectDir, projectName, includeDesktop, includeWeb);
        WriteGitignore(projectDir);
        return coreDir;
    }

    // ---- Core shared library -------------------------------------------------------------------

    private static void WriteCoreCsproj(string coreDir, string coreName, string projectName)
    {
        // The Core csproj carries only project-level properties + the $(MonoDreamsPlatform) backend
        // gate. Framework + extension packages are NOT pre-declared here: they are injected by
        // `monodreams add` / the foundation install through CsprojEditor, which platform-tags them
        // (DesktopGL/MonoGame.Extended under a desktop head, nkast/KNI.Extended under a web head) into
        // $(MonoDreamsPlatform)-conditioned groups for a multi-platform project. Pre-declaring them
        // here would double the references the module install adds.
        var path = Path.Combine(coreDir, $"{coreName}.csproj");
        File.WriteAllText(path, $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <!--
    {{coreName}} — the shared game library. Holds MonoDreams engine source (added by
    `monodreams add <module>`) and your game code. Backend-agnostic: the platform/backend is
    selected by the head that references this project (AdditionalProperties="MonoDreamsPlatform=…"),
    never baked here. $(MonoDreamsPlatform) (set in Directory.Build.props or by the head) gates the
    backend packages that `monodreams add` injects.
  -->
  <PropertyGroup>
    <OutputType>Library</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RollForward>Major</RollForward>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>{{projectName}}</RootNamespace>
    <AssemblyName>{{coreName}}</AssemblyName>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <!-- Default the backend when built standalone (e.g. `dotnet build` of this project alone). -->
    <MonoDreamsPlatform Condition="'$(MonoDreamsPlatform)' == ''">desktop</MonoDreamsPlatform>
  </PropertyGroup>

  <PropertyGroup Condition="'$(MonoDreamsPlatform)' == 'web'">
    <DefineConstants>$(DefineConstants);MONODREAMS_WEB</DefineConstants>
  </PropertyGroup>
</Project>

""");
    }

    private static void WriteCoreGameRoot(string coreDir, string projectName)
    {
        // A minimal MonoGame Game that boots the MonoDreams ECS world. It clears to a color and runs a
        // DefaultEcs world each frame — the user grows it by adding screens/systems from the modules they
        // install. GraphicsProfile and window setup are gated for the web backend (MONODREAMS_WEB) so the
        // same source compiles to Reach/WebGL under a web head (foundation portability premise).
        // The desktop branch adopts foundation's WindowFit by default (issue #86), so a scaffolded game
        // is immune from day one to the "window bigger than the display renders offscreen" break; the
        // web branch is untouched (JS owns the canvas size there).
        var path = Path.Combine(coreDir, "GameRoot.cs");
        File.WriteAllText(path, $$"""
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoDreams.Platform;
using MonoDreams.State;

namespace {{projectName}};

/// <summary>
/// The MonoGame <see cref="Game"/> for {{projectName}}. The per-platform heads
/// ({{projectName}}.Desktop / {{projectName}}.Web) construct and run this; all shared game logic
/// lives here in the Core library so both backends share one code path.
/// </summary>
public class GameRoot : Game
{
    /// <summary>
    /// The virtual resolution this game is authored against — the size of the image the game renders,
    /// before any window/canvas scaling.
    ///
    /// <para><b>The web head's <c>wwwroot/index.html</c> stamps its <c>GAME_WIDTH</c>/<c>GAME_HEIGHT</c>
    /// from the SAME CLI constants as these two (<c>ProjectScaffolder.DefaultVirtualWidth</c>/
    /// <c>DefaultVirtualHeight</c>), so they start equal — and they MUST stay equal.</b> index.html fixes
    /// the canvas backbuffer at those numbers and lets CSS scale it up with
    /// <c>image-rendering: pixelated</c>, which is what keeps the picture 1:1 and sharp. If the two drift
    /// apart, every web frame is resampled at a fractional ratio and the game is blurry with no error
    /// anywhere. On web JS owns the canvas size; on desktop the backbuffer is set from them below.</para>
    /// </summary>
    public const int VirtualWidth = {{DefaultVirtualWidth}};

    /// <inheritdoc cref="VirtualWidth"/>
    public const int VirtualHeight = {{DefaultVirtualHeight}};

    private readonly GraphicsDeviceManager _graphics;

    public GameRoot()
    {
        // The logger comes up FIRST so the window-fit boot line below is not dropped — Logger writes
        // are silent no-ops until Initialize has run (foundation premise "Logger requires Initialize").
        var debugDir = PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        Logger.Initialize(debugDir);

        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
#if MONODREAMS_WEB
        // BlazorGL / WebGL is Reach-profile only. The backbuffer is NOT set here on web: the host page
        // (wwwroot/index.html) sizes the canvas to VirtualWidth x VirtualHeight and CSS-scales it, so JS
        // owns the canvas size. Setting it here too would fight the page for it.
        _graphics.GraphicsProfile = GraphicsProfile.Reach;
#else
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
        // Desktop: open the LARGEST aspect-correct window that actually fits the player's display,
        // capped at the virtual resolution (1:1 is the sharpest this game can present). Opening at the
        // virtual resolution unconditionally is the classic silent break: macOS does not clamp a FIXED
        // window, so on any laptop smaller than VirtualWidth x VirtualHeight the bottom of the game —
        // menus, Start buttons, HUD — renders below the physical screen with no crash and no warning.
        // WindowFit reads the display's usable area (menu bar / dock / taskbar excluded), snaps to a
        // multiple of 16, applies the backbuffer, and logs one line of display/usable/window/mode.
        // Set MONODREAMS_WINDOW=WxH to force an exact size (scripted runs, screenshots).
        WindowFit.Apply(_graphics, VirtualWidth, VirtualHeight, Window);
#endif
    }

    protected override void Initialize()
    {
        Logger.Info("{{projectName}} starting.");
        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(Color.CornflowerBlue);
        base.Draw(gameTime);
    }
}

""");
    }

    // ---- Desktop head --------------------------------------------------------------------------

    private static void WriteDesktopHead(string projectDir, string projectName)
    {
        var headName = $"{projectName}.Desktop";
        var headDir = Path.Combine(projectDir, headName);
        Directory.CreateDirectory(headDir);

        File.WriteAllText(Path.Combine(headDir, $"{headName}.csproj"), $$"""
<Project Sdk="Microsoft.NET.Sdk">
  <!--
    {{headName}} — the DesktopGL head. Thin: owns the entry point only; all game logic lives in
    {{projectName}}.Core (referenced as a desktop backend build, the Directory.Build.props default).
  -->
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <RollForward>Major</RollForward>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>{{projectName}}</RootNamespace>
    <AssemblyName>{{headName}}</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <!-- Shared game library, built for the desktop backend (default). -->
    <ProjectReference Include="..\{{projectName}}.Core\{{projectName}}.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="MonoGame.Framework.DesktopGL" Version="3.8.4" />
    <PackageReference Include="MonoGame.Content.Builder.Task" Version="3.8.4" />
  </ItemGroup>
</Project>

""");

        File.WriteAllText(Path.Combine(headDir, "Program.cs"), $$"""
using {{projectName}};

using var game = new GameRoot();
game.Run();

""");
    }

    // ---- Web head (Blazor WebAssembly / KNI BlazorGL) ------------------------------------------

    private static void WriteWebHead(string projectDir, string projectName)
    {
        var headName = $"{projectName}.Web";
        var headDir = Path.Combine(projectDir, headName);
        Directory.CreateDirectory(headDir);
        Directory.CreateDirectory(Path.Combine(headDir, "Pages"));
        Directory.CreateDirectory(Path.Combine(headDir, "wwwroot", "css"));

        WriteWebCsproj(headDir, headName, projectName);
        WriteWebProgram(headDir, projectName);
        WriteWebGame(headDir, projectName);
        WriteWebPlatformServices(headDir, projectName);
        WriteWebBootProgress(headDir, projectName);
        WriteWebRazor(headDir, projectName);
        WriteWebWwwroot(headDir, projectName);
    }

    private static void WriteWebCsproj(string headDir, string headName, string projectName)
    {
        File.WriteAllText(Path.Combine(headDir, $"{headName}.csproj"), $$"""
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">
  <!--
    {{headName}} — the BlazorGL (KNI) head: a Blazor WebAssembly host that boots WebGame
    ({{projectName}}.Core, built for the web backend) in the browser.

    BUILD IT WITH:  dotnet build {{headName}} -p:MonoDreamsPlatform=web
    The property must be GLOBAL (passed via -p:) so it propagates through the reference graph at
    RESTORE time (AdditionalProperties on a ProjectReference does not flow to restore). This head is
    intentionally excluded from the solution's default build so a plain `dotnet build` of the .sln
    does not try to build it without the property. Requires the `wasm-tools` dotnet workload
    (`dotnet workload install wasm-tools`).

    BEFORE YOUR FIRST UPLOAD (itch.io, GitHub Pages, any static host), read MonoDreams'
    docs/recipes/shipping.md — the shipping failures a green build cannot catch.
  -->
  <PropertyGroup>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <RootNamespace>{{projectName}}.Web</RootNamespace>
    <AssemblyName>{{headName}}</AssemblyName>
    <DefineConstants>$(DefineConstants);BLAZORGL;MONODREAMS_WEB</DefineConstants>
    <KniPlatform>BlazorGL</KniPlatform>
    <BlazorEnableTimeZoneSupport>false</BlazorEnableTimeZoneSupport>
  </PropertyGroup>

  <ItemGroup>
    <!-- EnableDefaultCompileItems is false above, so every head source file must be listed here. -->
    <Compile Include="Program.cs" />
    <Compile Include="WebGame.cs" />
    <Compile Include="WebPlatformServices.cs" />
    <Compile Include="BootProgress.cs" />
    <Compile Include="Pages\Index.razor.cs" />
  </ItemGroup>

  <ItemGroup>
    <!-- Shared game library, built for the WEB backend (KNI). MonoDreamsPlatform=web must be a
         global property (-p:) for the restore to resolve the KNI packages transitively. -->
    <ProjectReference Include="..\{{projectName}}.Core\{{projectName}}.Core.csproj" />
  </ItemGroup>

  <!--
    KNI backend, ONE version for every nkast.* package below.

    STAMPED BY THE CLI. The "{{KniPackageVersion}}" you see here was written at scaffold time from
    ProjectScaffolder.KniPackageVersion, and the "{{WasmShimVersion}}" in wwwroot/index.html's shim
    <script src> lines was written from ProjectScaffolder.WasmShimVersion. Those two are
    HARD-COUPLED: the JS interop assets index.html loads
    (_content/nkast.Wasm.*/js/*.{{WasmShimVersion}}.js) ship inside the nkast.Wasm.* packages that
    nkast.Kni.Platform.Blazor.GL {{KniPackageVersion}} pulls transitively. Bump one and you MUST bump
    the other, at both sites, in the same change — nothing in the build checks that they agree, so the
    symptom of getting it wrong is a perfectly green build and a runtime 404 on every _content/ shim
    (black canvas, no game). See docs/web-targeting.md ("Web host" row of the dependency-parity table).
  -->
  <ItemGroup>
    <!-- KNI split framework packages (transitively included by Kni.Platform.Blazor.GL, pinned here). -->
    <PackageReference Include="nkast.Xna.Framework" Version="{{KniPackageVersion}}" />
    <PackageReference Include="nkast.Xna.Framework.Content" Version="{{KniPackageVersion}}" />
    <PackageReference Include="nkast.Xna.Framework.Graphics" Version="{{KniPackageVersion}}" />
    <PackageReference Include="nkast.Xna.Framework.Audio" Version="{{KniPackageVersion}}" />
    <PackageReference Include="nkast.Xna.Framework.Media" Version="{{KniPackageVersion}}" />
    <PackageReference Include="nkast.Xna.Framework.Input" Version="{{KniPackageVersion}}" />
    <PackageReference Include="nkast.Xna.Framework.Game" Version="{{KniPackageVersion}}" />
    <PackageReference Include="nkast.Xna.Framework.Devices" Version="{{KniPackageVersion}}" />
    <PackageReference Include="nkast.Xna.Framework.Storage" Version="{{KniPackageVersion}}" />
    <PackageReference Include="nkast.Xna.Framework.XR" Version="{{KniPackageVersion}}" />
    <!-- BlazorGL platform (GraphicsDevice + window/canvas backend for WebGL). Source of the
         nkast.Wasm.* JS interop packages whose _content/ scripts index.html references. -->
    <PackageReference Include="nkast.Kni.Platform.Blazor.GL" Version="{{KniPackageVersion}}" />
  </ItemGroup>

  <ItemGroup Condition=" '$(TargetFramework)' == 'net8.0' ">
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="8.0.11" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="8.0.11" PrivateAssets="all" />
  </ItemGroup>

  <!--
    CONTENT BUILD (not wired here on purpose): this starter head boots the empty GameRoot, which loads
    NO content, so it runs with no content pipeline. The MOMENT you `monodreams add` a module that builds
    content for web (fonts via rendering-text, level-ldtk, dialogue/Yarn), this head needs KNI's web
    content-build wiring — the desktop head's MonoGame.Content.Builder.Task has no web equivalent here.
    The web recipe is non-trivial and OS-dependent (KNI's MGCB builder ships Windows-only native libs, so
    macOS/Linux needs a FreeImage/freetype shim, plus per-backend /reference: dlls). Copy the working set
    of targets from MonoDreams.Examples.Web.csproj (BuildWebContentPipelineDlls + PrepareKniContentNativeShim,
    nkast.Xna.Framework.Content.Pipeline.Builder, KniContentReference) and read docs/web-targeting.md
    ("Content build (the same .mgcb, two backends)") before adding a content-using module on web.
  -->
</Project>

""");
    }

    private static void WriteWebProgram(string headDir, string projectName)
    {
        File.WriteAllText(Path.Combine(headDir, "Program.cs"), $$"""
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MonoDreams.Platform;

namespace {{projectName}}.Web
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            // Install the web platform services BEFORE any engine type is constructed (Logger, systems).
            // PlatformServices.Current is a static holder defaulting to desktop; the web head must
            // override it first (foundation portability premise).
            PlatformServices.Current = new WebPlatformServices();

            var builder = WebAssemblyHostBuilder.CreateDefault(args);
            builder.RootComponents.Add<App>("#app");
            builder.RootComponents.Add<HeadOutlet>("head::after");
            builder.Services.AddScoped(sp => new HttpClient
            {
                BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
            });
            await builder.Build().RunAsync();
        }
    }
}

""");
    }

    private static void WriteWebGame(string headDir, string projectName)
    {
        File.WriteAllText(Path.Combine(headDir, "WebGame.cs"), $$"""
using Microsoft.Xna.Framework;

namespace {{projectName}}.Web
{
    /// <summary>
    /// The BlazorGL host's Game. {{projectName}}.Core's <see cref="{{projectName}}.GameRoot"/> already
    /// gates GraphicsProfile/window setup for the web backend (MONODREAMS_WEB); this subclass exists
    /// only so the Blazor page has a web-named type to construct. Grow {{projectName}}.GameRoot, not this.
    /// </summary>
    public class WebGame : {{projectName}}.GameRoot
    {
    }
}

""");
    }

    private static void WriteWebPlatformServices(string headDir, string projectName)
    {
        File.WriteAllText(Path.Combine(headDir, "WebPlatformServices.cs"), $$"""
using System;
using System.IO;
using MonoDreams.Platform;

namespace {{projectName}}.Web
{
    /// <summary>
    /// Web (Blazor/WASM) implementation of <see cref="IPlatformServices"/>. The browser sandbox has no
    /// writable host filesystem and no process environment, so reads of game content go through MonoGame's
    /// ContentManager (XNB over HTTP) rather than these methods, the log sink is the browser console,
    /// background work runs inline (WASM is single-threaded), and file writes are no-ops. The head installs
    /// this via PlatformServices.Current before any engine construction (foundation portability premise).
    /// </summary>
    public sealed class WebPlatformServices : IPlatformServices
    {
        public string BaseDirectory => "/";
        public string GetEnvironmentVariable(string name) => null;
        public string CombinePath(params string[] paths) => string.Join("/", paths);
        public bool FileExists(string path) => false;
        public string ReadAllText(string path) => string.Empty;
        public void WriteAllText(string path, string contents) { }
        public void WriteAllBytes(string path, byte[] bytes) { }

        public string ExportScene(string suggestedFileName, string contents)
        {
            // The out-of-band "hand this text to the user" seam. There is no writable host filesystem in
            // the browser sandbox, so a desktop-style File.Write is impossible. Until a JS-interop blob
            // download / clipboard copy is wired through the page's IJSRuntime, echo the payload to the dev
            // console so it is never silently lost, and warn loudly that nothing was downloaded.
            Console.WriteLine(
                $"[{{projectName}}.Web] WebPlatformServices.ExportScene: browser download is NOT wired; " +
                $"echoing '{suggestedFileName}' to the console. Copy it from here to save it.\n{contents}");
            // null => delivered out-of-band (here: only to the console, pending a real download).
            return null;
        }

        public void CreateDirectory(string path) { }
        public TextWriter OpenLogWriter(string directory, string fileName) => new ConsoleLogWriter();
        public void WriteLineToConsole(string line) => Console.WriteLine(line);
        public void RunBackground(Action work) => work?.Invoke();

        private sealed class ConsoleLogWriter : TextWriter
        {
            private readonly global::System.Text.StringBuilder _buffer = new();
            public override global::System.Text.Encoding Encoding => global::System.Text.Encoding.UTF8;

            public override void Write(char value)
            {
                if (value == '\n') { Console.WriteLine(_buffer.ToString()); _buffer.Clear(); }
                else if (value != '\r') { _buffer.Append(value); }
            }

            public override void Write(string value)
            {
                if (string.IsNullOrEmpty(value)) return;
                foreach (var c in value) Write(c);
            }
        }
    }
}

""");
    }

    private static void WriteWebBootProgress(string headDir, string projectName)
    {
        File.WriteAllText(Path.Combine(headDir, "BootProgress.cs"), $$"""
using System;
using Microsoft.JSInterop;

namespace {{projectName}}.Web
{
    /// <summary>
    /// The C# side of the OPTIONAL <c>window.monodreamsBoot</c> splash contract defined in
    /// <c>wwwroot/index.html</c>. The splash covers the page from the first byte of the download until
    /// the first rendered frame; the download half reports itself (byte-weighted, in JS), but everything
    /// after it — runtime start, content load, game construction — is silent unless the head says
    /// something. These calls are that "something": milestone labels, and optional counts for a stage
    /// that can actually be counted.
    ///
    /// <para><b>Every call is fire-and-forget and safe when the splash is gone or the contract object is
    /// absent.</b> The whole point of the splash is to reassure a player during boot; it must never be
    /// able to break the boot it is reporting on. So: no exceptions escape, no awaiting, and a missing
    /// <c>monodreamsBoot</c> (a hand-edited index.html, a different host page) is simply a no-op.</para>
    /// </summary>
    public static class BootProgress
    {
        // The in-process JS runtime. Blazor WASM's IJSRuntime implements IJSInProcessRuntime, which lets
        // these calls be synchronous void invocations — no Task to drop on the floor, no ordering risk
        // against the frame that dismisses the splash. If a host ever hands us a non-in-process runtime
        // the cast yields null and every method below no-ops.
        private static IJSInProcessRuntime _js;

        /// <summary>Binds the page's JS runtime. Call once, from the first render.</summary>
        public static void Attach(IJSRuntime js) => _js = js as IJSInProcessRuntime;

        /// <summary>
        /// Names the boot phase the splash shows, e.g. <c>"loading content"</c>. Clears any counts set by
        /// <see cref="SetProgress"/> for the previous phase.
        /// </summary>
        public static void SetPhase(string name)
        {
            if (_js == null) return;
            try { _js.InvokeVoid("monodreamsBoot.setPhase", name); }
            catch { /* the splash must never be able to break the game */ }
        }

        /// <summary>
        /// Reports countable progress WITHIN the current phase — the splash renders it next to the phase
        /// label ("loading content 12/47"). Call it from a load loop that knows its total; where a stage
        /// cannot be counted, just <see cref="SetPhase"/> it and let the elapsed-seconds ticker carry it.
        /// </summary>
        public static void SetProgress(int loaded, int total)
        {
            if (_js == null) return;
            try { _js.InvokeVoid("monodreamsBoot.setProgress", loaded, total); }
            catch { /* the splash must never be able to break the game */ }
        }
    }
}

""");
    }

    private static void WriteWebRazor(string headDir, string projectName)
    {
        File.WriteAllText(Path.Combine(headDir, "App.razor"), """
<Router AppAssembly="@typeof(App).Assembly">
    <Found Context="routeData">
        <RouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)" />
        <FocusOnNavigate RouteData="@routeData" Selector="h1" />
    </Found>
    <NotFound>
        <PageTitle>Not found</PageTitle>
        <LayoutView Layout="@typeof(MainLayout)">
            <p role="alert">Sorry, there's nothing at this address.</p>
        </LayoutView>
    </NotFound>
</Router>

""");

        File.WriteAllText(Path.Combine(headDir, "MainLayout.razor"), """
@inherits LayoutComponentBase

<div class="page">
    <main>
        @Body
    </main>
</div>

""");

        // A RootNamespace beginning with "MonoDreams" would make Razor resolve `@using System.*` to
        // `MonoDreams.System.*`; the consumer's project name is arbitrary, but `global::System.*` is
        // harmless and keeps the template robust to any name (spike gotcha).
        File.WriteAllText(Path.Combine(headDir, "_Imports.razor"), $$"""
@using global::System.Net.Http
@using global::System.Net.Http.Json
@using Microsoft.AspNetCore.Components.Forms
@using Microsoft.AspNetCore.Components.Routing
@using Microsoft.AspNetCore.Components.Web
@using Microsoft.AspNetCore.Components.Web.Virtualization
@using Microsoft.AspNetCore.Components.WebAssembly.Http
@using Microsoft.JSInterop
@using nkast.Wasm.Canvas
@using {{projectName}}.Web
@using {{projectName}}.Web.Pages

""");

        File.WriteAllText(Path.Combine(headDir, "Pages", "Index.razor"), """
@page "/"
@page "/index.html"
@inject IJSRuntime JsRuntime
@using nkast.Wasm.Canvas

<PageTitle>MonoDreams (BlazorGL)</PageTitle>

@*
    The canvas RENDERS at the game's virtual resolution (initRenderJS in wwwroot/index.html sets
    width/height to GAME_WIDTH/GAME_HEIGHT, stamped from GameRoot.VirtualWidth/VirtualHeight) and is
    SCALED to the window by CSS with image-rendering:pixelated — so the framebuffer is 1:1 with the
    game's pixels (no resample, no fractional scaling artefacts) and the browser's upscale is
    nearest-neighbour, which is what keeps pixel art sharp. Sizing the backbuffer to the window
    instead — the stock BlazorGL template's default — costs a fractional resample AND, on a 2x
    display, hands the browser a half-resolution image to smooth-upscale: blurry and slower at once.

    The flex centering below is what letterboxes the fixed-size canvas inside the full-window holder.
*@
<div id="canvasHolder" style="
    background: #000;
    margin: 0%;
    position: fixed;
    top: 0px;
    right: 0px;
    bottom: 0px;
    left: 0px;
    width: 100vw;
    height: 100vh;
    display: flex;
    align-items: center;
    justify-content: center;
">
    <canvas id="theCanvas" style="touch-action:none; image-rendering: pixelated; display: block;"></canvas>
</div>

""");

        File.WriteAllText(Path.Combine(headDir, "Pages", "Index.razor.cs"), $$"""
using System;
using Microsoft.JSInterop;
using Microsoft.Xna.Framework;

namespace {{projectName}}.Web.Pages
{
    // Code-behind for the canvas page. The JS in index.html calls initRenderJS once the canvas exists,
    // then drives a requestAnimationFrame loop that invokes TickDotNet every frame. The Game is built
    // lazily on the first tick (the GL context is ready by then) and Tick()ed thereafter.
    //
    // The BootProgress calls below are the head's half of the OPTIONAL window.monodreamsBoot splash
    // contract (defined in wwwroot/index.html): the splash covers the page until the first real frame,
    // and everything after the download is invisible to JS, so the milestones this file already passes
    // through are the only honest signal there is. A game whose content load is COUNTABLE should also
    // call BootProgress.SetProgress(loaded, total) from its own load loop — the splash then renders
    // "loading content 12/47" instead of a phase label with nothing behind it. Dismissal is NOT done
    // here: index.html's tickJS dismisses on the first tick, because a frame existing is the only
    // proof the game is actually up.
    public partial class Index
    {
        private Game _game;

        protected override void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);
            if (firstRender)
            {
                BootProgress.Attach(JsRuntime);
                BootProgress.SetPhase("runtime started");
                JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
            }
        }

        [JSInvokable]
        public void TickDotNet()
        {
            if (_game == null)
            {
                // Construction loads content and builds the world — the longest silent stretch of the
                // boot, and the one a static splash makes look hung.
                BootProgress.SetPhase("loading content");
                _game = new WebGame();
                _game.Run();
                BootProgress.SetPhase("game constructed");
            }
            _game.Tick();
        }
    }
}

""");
    }

    private static void WriteWebWwwroot(string headDir, string projectName)
    {
        File.WriteAllText(Path.Combine(headDir, "wwwroot", "index.html"), $$"""
<!DOCTYPE html>
<html>

<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no" />
    <title>{{projectName}} (BlazorGL)</title>
    <!--
        RELATIVE base — "./", not "/". DO NOT "clean this up" back to an absolute slash.

        Every asset Blazor fetches (_framework/blazor.boot.json, the Webcil assemblies, the _content/
        interop shims) is resolved against this. itch.io serves an HTML5 upload from a per-build
        SUBPATH (html-classic.itch.zone/html/<build-id>/), so an absolute base makes the loader ask the
        CDN ROOT for /_framework/... and the game dies on 404s before it draws a single frame. Plenty
        of other hosts do the same thing (GitHub Pages project sites, any /games/<name>/ folder). A
        relative base is correct at the root too — the Blazor dev server included — so there is ONE
        base href for every host and no publish-time rewrite step to forget.

        This is safe because the page is a single canvas with no client-side routing. If you ever add
        real routes, revisit it deliberately rather than by reflex.

        The base href is only the first upload trap — test the build AT a subpath, and prune Blazor's
        .br/.gz twins for hosts that don't negotiate. See MonoDreams' docs/recipes/shipping.md.
    -->
    <base href="./" />
    <link href="css/app.css" rel="stylesheet" />
    <style>
        /*
            The loading screen. It exists because a WASM game is tens of megabytes of runtime plus
            content, that takes real seconds to arrive, and the stock template's static
            "loading . . ." reads as a HUNG TAB rather than as work in progress. Three independent
            signals fix that, and none of them may depend on the others existing:

              1. A real progress FILL. --monodreams-load-percentage is BYTE-weighted (computed by the
                 loadBootResource hook further down); --blazor-load-percentage is Blazor's own, which
                 counts RESOURCES not bytes and so rockets to ~100% while the one enormous runtime
                 .wasm has barely started. Ours first, Blazor's as fallback, 0% if neither exists.
              2. A phase line with ELAPSED SECONDS, painted by a setInterval ticker. Elapsed time is
                 the only signal that cannot stall, which makes it the one that actually answers
                 "has this frozen?".
              3. A sweep that animates UNCONDITIONALLY, over whatever the fill is doing. Percentage
                 only ever covers fetching; the runtime start and the game's own construction report
                 nothing unless the head opts into the monodreamsBoot contract. Those silent phases
                 are exactly when a still screen looks broken, so something must keep moving that
                 depends on no progress data whatsoever.
        */
        #loading {
            position: fixed; inset: 0; display: flex; align-items: center; justify-content: center;
            background: #1d2330; z-index: 10;
        }
        .splash { width: min(22em, 70vw); font-family: 'Segoe UI', system-ui, sans-serif; }
        .splash-title {
            text-align: center; color: #eeeef2; font-size: 1.15em; letter-spacing: 0.14em;
            margin-bottom: 1.1em;
        }
        .splash-bar {
            position: relative; height: 10px; background: #12161f; overflow: hidden;
            border: 1px solid #3a4456;
        }
        /* Our byte-weighted variable first, Blazor's resource-count variable as the fallback, and 0%
           as the default so a missing variable can never widen the fill. */
        .splash-fill {
            width: var(--monodreams-load-percentage, var(--blazor-load-percentage, 0%));
            height: 100%;
            background: #60b0e8;
            transition: width 0.2s linear;
        }
        /* The unconditional sweep, over the top of whatever the fill is doing. */
        .splash-bar::after {
            content: ''; position: absolute; inset: 0; width: 35%;
            background: linear-gradient(90deg, transparent, rgba(255,255,255,0.22), transparent);
            animation: splash-sweep 1.15s ease-in-out infinite;
        }
        @keyframes splash-sweep {
            from { transform: translateX(-100%); }
            to   { transform: translateX(340%); }
        }
        .splash-pct {
            margin-top: 0.75em; text-align: center; color: #98a0b4; font-size: 0.85em;
            font-variant-numeric: tabular-nums;
        }
        .splash-phase {
            margin-top: 0.35em; text-align: center; color: #667089; font-size: 0.75em;
            letter-spacing: 0.06em; min-height: 1.2em;
        }
    </style>
</head>

<body>

    <!--
        #app stays EMPTY and the splash is its SIBLING, deliberately. Blazor replaces #app's contents
        with the game's canvas the moment the root component renders — which happens BEFORE the game
        has loaded content or drawn anything, so a splash nested INSIDE #app vanishes into a black
        canvas for the longest phase of the whole boot. Sitting outside, it survives until a real
        frame exists (tickJS dismisses it).
    -->
    <div id="app"></div>

    <div id="loading">
        <div class="splash">
            <div class="splash-title">{{projectName}}</div>
            <div class="splash-bar"><div class="splash-fill"></div></div>
            <div class="splash-pct"></div>
            <div class="splash-phase">downloading</div>
        </div>
    </div>

    <div id="blazor-error-ui">
        An unhandled error has occurred.
        <a href="" class="reload">Reload</a>
        <a class="dismiss">x</a>
    </div>

    <script>
        /*
            THE SPLASH — and why it is built exactly this way.

            Observed in production on an ~19MB build: a bar driven by Blazor's own
            --blazor-load-percentage hits ~100% almost immediately and then PARKS there, because that
            variable counts RESOURCES, not BYTES — dozens of small assemblies and one enormous runtime
            .wasm each count as one resource. The bar is honest about resource count and a lie about
            progress; and everything AFTER the download (runtime start, content load, game
            construction) reports nothing at all. Both halves are fixed here:

              (a) DOWNLOAD — byte-weighted. The Blazor.start({ loadBootResource }) hook below takes
                  over resource fetching and counts real bytes off the wire into
                  --monodreams-load-percentage, so the bar crawls honestly through the big .wasm.
                  The numerator is exact; the denominator has to be estimated — see splashEstTotal
                  for why, and for what stops the estimate from parking the bar at the clamp.
              (b) AFTER THE DOWNLOAD — the OPTIONAL boot-progress contract defined at the bottom of
                  this script: window.monodreamsBoot.setPhase(name) / setProgress(loaded, total). The
                  head calls it at milestones it already passes (runtime start, content load, game
                  constructed — see Pages/Index.razor.cs), and a game whose load is countable calls
                  setProgress so the splash shows "loading content 12/47" instead of a parked 100%.
                  Where a stage cannot be counted, phase label + elapsed seconds is the fallback.
        */
        const splashStart = performance.now();
        let splashPhase = 'downloading';
        let splashCount = null;                        // {loaded,total} from the optional setProgress
        let splashDone = false;
        let splashDeferred = false;                    // a dismissal is parked until the tab is visible

        // Written by the loadBootResource hook below. Only bootBytesLoaded is a fact; everything else
        // exists to ESTIMATE a denominator, because no honest one is available — see splashEstTotal.
        let bootBytesLoaded = 0;    // exact bytes received so far, summed over every boot resource
        let bootKnownTotal = 0;     // summed Content-Length of the resources whose headers have arrived
        let bootResRequested = 0;   // boot resources the runtime has asked for
        let bootResKnown = 0;       // ...of which this many reported a Content-Length
        let splashShownPct = 0;     // last percentage PAINTED — the fill never moves backwards

        function splashDismiss() {
            if (splashDone) return;

            // NEVER strip the splash off a canvas the user cannot see. The 60s backstop exists so a
            // missed hook can't cover a playable game forever — but in a HIDDEN tab there is no frame
            // yet BY DESIGN: the game is driven by requestAnimationFrame and Chrome suspends rAF while
            // a tab is backgrounded, so dismissing here uncovers a black canvas the user then finds on
            // their return. Defer instead, and re-arm a short backstop the moment the tab becomes
            // visible — the guarantee survives, now denominated in VISIBLE time (a missed hook cannot
            // cover a playable game for more than ~15s of it). The first-frame dismissal path is
            // untouched: a tick only happens in a visible tab, where document.hidden is false.
            if (document.hidden) {
                if (!splashDeferred) {
                    splashDeferred = true;
                    document.addEventListener('visibilitychange', function onVisible() {
                        if (document.hidden) return;   // act only on the transition TO visible
                        document.removeEventListener('visibilitychange', onVisible);
                        splashDeferred = false;        // a later backstop may need to defer again
                        setTimeout(splashDismiss, 15000);
                    });
                }
                return;
            }

            splashDone = true;
            clearInterval(splashTimer);
            const el = document.getElementById('loading');
            if (el) el.remove();
        }

        function splashMb(bytes) {
            return (bytes / 1048576).toFixed(1);
        }

        // THE DENOMINATOR IS AN ESTIMATE, AND IT HAS TO BE. Two facts force it:
        //   * net8's blazor.boot.json carries INTEGRITY HASHES, not sizes. There is no manifest total
        //     to read, and producing one would need build machinery this template does not have.
        //   * Content-Length arrives only with a response's own HEADERS, and Chrome caps HTTP/1.1 at 6
        //     connections per host — so at any instant the only known lengths belong to the ~6
        //     resources currently in flight while dozens sit queued. Summing just those makes the
        //     denominator TRAIL the numerator, max(total, loaded) collapses to loaded, and the bar pins
        //     itself to the clamp on its very first paint: the exact parked bar this splash exists to
        //     kill (measured: "3.9 / 3.9 MB" at 99% for the whole download).
        // What IS known early is the resource COUNT — dotnet.js enumerates and requests every boot
        // resource almost immediately, so bootResRequested reaches the full universe long before the
        // lengths do. So price each not-yet-known resource at the running average of the known ones,
        // and let the estimate refine itself as real lengths land.
        function splashEstTotal() {
            if (bootResKnown <= 0) return 0;
            const avg = bootKnownTotal / bootResKnown;
            const unknown = Math.max(0, bootResRequested - bootResKnown);
            return bootKnownTotal + unknown * avg;
        }

        // setInterval, NOT requestAnimationFrame. RAF is throttled to a standstill in a background
        // tab, so an RAF-driven label freezes at "0s" on exactly the load a player leaves in another
        // tab and comes back to — the worst possible moment to look broken. setInterval still fires
        // when backgrounded (clamped to about 1s), which a seconds counter does not care about.
        function splashPaint() {
            if (splashDone) return;

            // The byte-weighted fill: an EXACT numerator over an ESTIMATED denominator (splashEstTotal).
            // Painted MONOTONICALLY — the estimate jumps whenever a big resource's real length lands,
            // and a bar that moves backwards reads as broken — and clamped at 99 until dismissal, since
            // an estimate can always be beaten by the real thing. Left untouched entirely while nothing
            // has reported a length, so the CSS fallback to Blazor's own variable keeps driving the bar.
            const estTotal = splashEstTotal();
            if (estTotal > 0) {
                const pct = Math.min(99, 100 * bootBytesLoaded / Math.max(estTotal, bootBytesLoaded));
                if (pct > splashShownPct) splashShownPct = pct;
                document.documentElement.style.setProperty('--monodreams-load-percentage', splashShownPct.toFixed(1) + '%');
            }

            const pctEl = document.querySelector('.splash-pct');
            if (pctEl) {
                if (bootBytesLoaded > 0) {
                    // The tilde is honesty: while any requested resource has not reported its length the
                    // denominator is an estimate and must not read as a fact. It drops once they all have.
                    const denom = Math.max(estTotal, bootBytesLoaded);
                    const approx = bootResKnown < bootResRequested ? '~' : '';
                    pctEl.textContent = splashMb(bootBytesLoaded) + ' / ' + approx + splashMb(denom) + ' MB';
                } else {
                    // No byte counts (the hook bailed, or a fully cached start): fall back to whatever
                    // Blazor publishes, and to a word when even that is absent.
                    const t = getComputedStyle(document.documentElement)
                        .getPropertyValue('--blazor-load-percentage-text').trim().replace(/"/g, '');
                    pctEl.textContent = t || 'starting';
                }
            }

            const phaseEl = document.querySelector('.splash-phase');
            if (phaseEl) {
                const secs = Math.floor((performance.now() - splashStart) / 1000);
                let label = splashPhase;
                if (splashCount && splashCount.total > 0) {
                    label = label + ' ' + splashCount.loaded + '/' + splashCount.total;
                }
                // Past the download the game is driven by requestAnimationFrame, and Chrome suspends
                // rAF in a hidden tab — so the first frame CANNOT arrive until this tab is
                // foregrounded (measured: construction started ~194s after the runtime was up, the
                // instant visibility flipped). Without this hint the splash reads as frozen, which is
                // precisely the lie it exists to kill. Deliberately NOT shown during 'downloading':
                // downloads do progress in a background tab, so there the parenthetical would itself
                // be the lie.
                if (document.hidden && splashPhase !== 'downloading') {
                    label = label + ' (waiting for this tab to become visible)';
                }
                phaseEl.textContent = label + '  ·  ' + secs + 's';
            }
        }

        const splashTimer = setInterval(splashPaint, 250);
        splashPaint();

        // A hook that never fires must never leave the splash covering a playable game.
        setTimeout(splashDismiss, 60000);

        /*
            THE OPTIONAL BOOT-PROGRESS CONTRACT.
            Defined BEFORE the Blazor script tag so it exists no matter how early anything calls it.
            The C# side is BootProgress.cs; every entry point here is a no-op once the splash has been
            dismissed or its elements are gone, because boot reporting must never be able to break the
            boot it reports on.
        */
        // The boot milestones IN THE ORDER THEY ACTUALLY HAPPEN — which is NOT the order they are
        // reported. Blazor.start()'s promise resolves only after the root component has rendered and
        // initRenderJS has run, so its 'starting runtime' lands LAST and used to stomp the more
        // advanced 'building the game' (measured: all three arrived inside the same 0.1s, in exactly
        // the wrong order, leaving the splash showing a phase the boot had long passed). The ladder
        // gives the phase line the same monotonicity the bar already has.
        const splashMilestones = [
            'downloading', 'starting runtime', 'runtime started',
            'building the game', 'loading content', 'game constructed'
        ];

        window.monodreamsBoot = {
            setPhase: function (name) {
                if (splashDone) return;
                const next = String(name == null ? '' : name);
                const nextRank = splashMilestones.indexOf(next);
                const currentRank = splashMilestones.indexOf(splashPhase);
                // Drop a KNOWN milestone that ranks below the KNOWN one already showing — it is a late
                // report of a stage the boot has passed. A game's own custom phase names are unknown to
                // the ladder (rank -1) and are always accepted, as is any move out of an unknown phase;
                // the ladder only ever suppresses a known-vs-known regression.
                if (nextRank >= 0 && currentRank >= 0 && nextRank < currentRank) return;
                splashPhase = next;
                splashCount = null;    // a new phase invalidates the previous phase's counts
                splashPaint();
            },
            setProgress: function (loaded, total) {
                if (splashDone) return;
                splashCount = { loaded: loaded, total: total };
                splashPaint();
            }
        };
    </script>

    <!--
        nkast.Wasm.* JS interop shims. The "{{WasmShimVersion}}" in each src below is the nkast.Wasm.*
        JS asset version, NOT a framework version, and it is HARD-COUPLED to what the package
        nkast.Kni.Platform.Blazor.GL ({{KniPackageVersion}} in {{projectName}}.Web.csproj) pulls
        transitively.

        BOTH numbers were STAMPED BY THE MonoDreams CLI at scaffold time, from one constant pair
        (ProjectScaffolder.WasmShimVersion / ProjectScaffolder.KniPackageVersion) — that is the only
        reason they agree right now. If you bump nkast.Kni.Platform.Blazor.GL (or pin nkast.Wasm.*
        explicitly), update EVERY "{{WasmShimVersion}}" in these script srcs to match the restored
        nkast.Wasm.* version, or the canvas/GL/JSObject interop 404s at runtime on these _content/
        paths and you get a black page out of a perfectly green build. Nothing checks this at build
        time — see docs/web-targeting.md ("Web host" row of the dependency-parity table). JSObject
        ships in nkast.Wasm.JSInterop (it moved there from nkast.Wasm.Dom in 8.0.x).
    -->
    <script src="_content/nkast.Wasm.JSInterop/js/JSObject.{{WasmShimVersion}}.js"></script>
    <script src="_content/nkast.Wasm.Dom/js/Window.{{WasmShimVersion}}.js"></script>
    <script src="_content/nkast.Wasm.Dom/js/Document.{{WasmShimVersion}}.js"></script>
    <script src="_content/nkast.Wasm.Dom/js/Navigator.{{WasmShimVersion}}.js"></script>
    <script src="_content/nkast.Wasm.Dom/js/Gamepad.{{WasmShimVersion}}.js"></script>
    <script src="_content/nkast.Wasm.Dom/js/Media.{{WasmShimVersion}}.js"></script>
    <script src="_content/nkast.Wasm.XHR/js/XHR.{{WasmShimVersion}}.js"></script>
    <script src="_content/nkast.Wasm.Canvas/js/Canvas.{{WasmShimVersion}}.js"></script>
    <script src="_content/nkast.Wasm.Canvas/js/Canvas2dContext.{{WasmShimVersion}}.js"></script>
    <script src="_content/nkast.Wasm.Canvas/js/CanvasGLContext.{{WasmShimVersion}}.js"></script>
    <script src="_content/nkast.Wasm.Audio/js/Audio.{{WasmShimVersion}}.js"></script>
    <script src="_content/nkast.Wasm.XR/js/XR.{{WasmShimVersion}}.js"></script>

    <!--
        autostart="false": the boot is kicked off by hand below, so the loadBootResource hook can be
        installed first. Without it Blazor starts the moment this script parses and the hook is never
        seen — the bar then falls back to Blazor's resource-count variable.
    -->
    <script src="_framework/blazor.webassembly.js" autostart="false"></script>

    <script>
        /*
            BYTE-WEIGHTED DOWNLOAD PROGRESS.
            loadBootResource lets us fetch each boot resource ourselves, so real bytes off the wire can
            be counted instead of resources-completed. That is the difference between a bar that parks
            at 100% for twenty seconds and a bar that crawls honestly through the runtime .wasm.
        */
        Blazor.start({
            loadBootResource: function (type, name, defaultUri, integrity) {
                try {
                    // The runtime IMPORTs this one as a module, so it MUST stay a URI — handing back a
                    // Response (or a promise of one) for 'dotnetjs' breaks the boot outright.
                    if (type === 'dotnetjs') return defaultUri;

                    // Every other boot resource passes through here, and dotnet.js asks for them all
                    // almost at once — so this count reaches the full universe of files long before
                    // their sizes do. It is the free stand-in for the per-file sizes net8's
                    // blazor.boot.json does NOT carry (see splashEstTotal).
                    bootResRequested++;

                    return fetch(defaultUri, {
                        cache: 'no-cache',
                        integrity: integrity,
                        credentials: 'same-origin'
                    }).then(function (resp) {
                        if (!resp.ok || !resp.body) return resp;

                        // CONTENT-LENGTH CAVEAT. This header arrives only with THIS response's headers,
                        // so at any moment only the handful of in-flight resources have contributed one
                        // (Chrome: 6 connections per host on HTTP/1.1). And with Content-Encoding
                        // (gzip/br) it is the COMPRESSED size while the stream below yields DECOMPRESSED
                        // bytes, while plenty of static hosts omit it entirely. So this sum is never the
                        // total — it is the sample splashEstTotal prices the unseen resources from.
                        const len = resp.headers.get('content-length');
                        if (len) {
                            bootKnownTotal += (parseInt(len, 10) || 0);
                            bootResKnown++;
                        }

                        const reader = resp.body.getReader();
                        const countingStream = new ReadableStream({
                            start: function (controller) {
                                function pump() {
                                    return reader.read().then(function (r) {
                                        if (r.done) { controller.close(); return; }
                                        bootBytesLoaded += r.value.byteLength;
                                        controller.enqueue(r.value);
                                        return pump();
                                    });
                                }
                                return pump();
                            }
                        });

                        return new Response(countingStream, {
                            status: resp.status,
                            statusText: resp.statusText,
                            headers: resp.headers
                        });
                    });
                } catch (e) {
                    // THE SPLASH MUST NEVER BE ABLE TO BREAK THE BOOT. Any throw here (a browser with
                    // no ReadableStream, a CSP quirk, anything at all) returns undefined, which tells
                    // Blazor to load the resource its own default way. The bar then rides the CSS
                    // fallback to --blazor-load-percentage and nothing else about the page changes.
                    return undefined;
                }
            }
        }).then(function () {
            // The download is done and the runtime is coming up. From here the head reports through
            // window.monodreamsBoot (see Pages/Index.razor.cs).
            window.monodreamsBoot.setPhase('starting runtime');
        });
    </script>

    <script>
        // The game's virtual resolution — MUST match {{projectName}}.GameRoot.VirtualWidth /
        // VirtualHeight. Both sites are stamped by the MonoDreams CLI from ONE constant pair
        // (ProjectScaffolder.DefaultVirtualWidth / DefaultVirtualHeight), so they start equal; change
        // one and you must change the other. The canvas backbuffer is fixed at this size (1:1 with the
        // game's own render targets, so the final composite never resamples) and CSS scales it to the
        // window — see Pages/Index.razor.
        const GAME_WIDTH = {{DefaultVirtualWidth}}, GAME_HEIGHT = {{DefaultVirtualHeight}};

        // Fit the canvas into the window preserving aspect. Whole-number scales are preferred when the
        // window can hold one (every game pixel becomes an exact NxN block); otherwise the fractional
        // fit still renders sharp because image-rendering is pixelated.
        function fitCanvas() {
            const canvas = document.getElementById('theCanvas');
            if (!canvas) return;
            const scale = Math.min(window.innerWidth / GAME_WIDTH, window.innerHeight / GAME_HEIGHT);
            const snapped = scale >= 1 ? Math.floor(scale) : scale;
            canvas.style.width = (GAME_WIDTH * snapped) + 'px';
            canvas.style.height = (GAME_HEIGHT * snapped) + 'px';
        }

        let firstTick = true;
        function tickJS() {
            window.theInstance.invokeMethod('TickDotNet');
            if (firstTick) { firstTick = false; splashDismiss(); }   // a frame exists: the game is up
            window.requestAnimationFrame(tickJS);
        }

        window.initRenderJS = (instance) => {
            window.theInstance = instance;
            // The canvas exists, so the runtime is up and the game is about to construct itself
            // (content load, world build). Still no frame — say so rather than showing nothing.
            window.monodreamsBoot.setPhase('building the game');

            var canvas = document.getElementById('theCanvas');
            // The BACKBUFFER is the virtual resolution, not the window. Sizing it to the window — the
            // stock template's default — costs a fractional resample every frame AND ignores
            // devicePixelRatio, so on any 2x display the browser smooth-upscales a half-resolution
            // image and the game is smeared from the first frame. Fixed here + CSS-scaled = sharp.
            canvas.width = GAME_WIDTH;
            canvas.height = GAME_HEIGHT;
            fitCanvas();
            window.addEventListener('resize', fitCanvas);

            // keep canvas focusable so it receives keyboard input
            canvas.setAttribute('tabindex', '0');
            canvas.focus();
            canvas.addEventListener('pointerdown', () => canvas.focus());
            canvas.addEventListener("contextmenu", e => e.preventDefault());

            window.requestAnimationFrame(tickJS);
        };

        // Prevent Arrow keys / Spacebar from scrolling the outer page.
        window.addEventListener("keydown", function (event) {
            if ([32, 37, 38, 39, 40].indexOf(event.keyCode) > -1)
                event.preventDefault();
        });
        window.addEventListener("wheel", function (event) {
            event.preventDefault();
        }, { passive: false });
    </script>
</body>

</html>

""");

        File.WriteAllText(Path.Combine(headDir, "wwwroot", "css", "app.css"), """
html, body {
    margin: 0;
    padding: 0;
    width: 100%;
    height: 100%;
    overflow: hidden;
    background: #000;
}

#blazor-error-ui {
    background: lightyellow;
    bottom: 0;
    box-shadow: 0 -1px 2px rgba(0, 0, 0, 0.2);
    display: none;
    left: 0;
    padding: 0.6rem 1.25rem 0.7rem 1.25rem;
    position: fixed;
    width: 100%;
    z-index: 1000;
}

#blazor-error-ui .dismiss {
    cursor: pointer;
    position: absolute;
    right: 0.75rem;
    top: 0.5rem;
}

""");
    }

    // ---- Solution + .gitignore -----------------------------------------------------------------

    private static void WriteSolution(string projectDir, string projectName, bool includeDesktop, bool includeWeb)
    {
        // SDK-style csproj project-type GUID (used for every project, including the Blazor WASM head).
        const string csprojTypeGuid = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";

        var projects = new List<(string Name, string RelPath, string Guid, bool BuildInDefault)>();
        var coreName = $"{projectName}.Core";
        projects.Add((coreName, $"{coreName}\\{coreName}.csproj", NewGuid(), true));
        if (includeDesktop)
        {
            var n = $"{projectName}.Desktop";
            projects.Add((n, $"{n}\\{n}.csproj", NewGuid(), true));
        }
        if (includeWeb)
        {
            var n = $"{projectName}.Web";
            // Excluded from the default build: a plain `dotnet build` of the .sln (desktop regression)
            // must not build the web head without the global -p:MonoDreamsPlatform=web property.
            projects.Add((n, $"{n}\\{n}.csproj", NewGuid(), false));
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("Microsoft Visual Studio Solution File, Format Version 12.00");
        sb.AppendLine("# Generated by MonoDreams CLI");
        foreach (var p in projects)
        {
            sb.AppendLine($"Project(\"{{{csprojTypeGuid}}}\") = \"{p.Name}\", \"{p.RelPath}\", \"{{{p.Guid}}}\"");
            sb.AppendLine("EndProject");
        }
        sb.AppendLine("Global");
        sb.AppendLine("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");
        sb.AppendLine("\t\tDebug|Any CPU = Debug|Any CPU");
        sb.AppendLine("\t\tRelease|Any CPU = Release|Any CPU");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");
        foreach (var p in projects)
        {
            sb.AppendLine($"\t\t{{{p.Guid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU");
            if (p.BuildInDefault) sb.AppendLine($"\t\t{{{p.Guid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU");
            sb.AppendLine($"\t\t{{{p.Guid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU");
            if (p.BuildInDefault) sb.AppendLine($"\t\t{{{p.Guid}}}.Release|Any CPU.Build.0 = Release|Any CPU");
        }
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("\tGlobalSection(SolutionProperties) = preSolution");
        sb.AppendLine("\t\tHideSolutionNode = FALSE");
        sb.AppendLine("\tEndGlobalSection");
        sb.AppendLine("EndGlobal");

        File.WriteAllText(Path.Combine(projectDir, $"{projectName}.sln"), sb.ToString());
    }

    private static string NewGuid() => Guid.NewGuid().ToString().ToUpperInvariant();

    private static void WriteGitignore(string projectDir)
    {
        var path = Path.Combine(projectDir, ".gitignore");
        if (File.Exists(path)) return;
        File.WriteAllText(path, """
bin/
obj/
debug/
*.user

""");
    }
}
