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
    private readonly GraphicsDeviceManager _graphics;

    public GameRoot()
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
#if MONODREAMS_WEB
        // BlazorGL / WebGL is Reach-profile only.
        _graphics.GraphicsProfile = GraphicsProfile.Reach;
#else
        _graphics.GraphicsProfile = GraphicsProfile.HiDef;
#endif
    }

    protected override void Initialize()
    {
        var debugDir = PlatformServices.Current.CombinePath(PlatformServices.Current.BaseDirectory, "debug");
        Logger.Initialize(debugDir);
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
    <Compile Include="Program.cs" />
    <Compile Include="WebGame.cs" />
    <Compile Include="WebPlatformServices.cs" />
    <Compile Include="Pages\Index.razor.cs" />
  </ItemGroup>

  <ItemGroup>
    <!-- Shared game library, built for the WEB backend (KNI). MonoDreamsPlatform=web must be a
         global property (-p:) for the restore to resolve the KNI packages transitively. -->
    <ProjectReference Include="..\{{projectName}}.Core\{{projectName}}.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- KNI split framework packages (transitively included by Kni.Platform.Blazor.GL, pinned here). -->
    <PackageReference Include="nkast.Xna.Framework" Version="4.2.9001" />
    <PackageReference Include="nkast.Xna.Framework.Content" Version="4.2.9001" />
    <PackageReference Include="nkast.Xna.Framework.Graphics" Version="4.2.9001" />
    <PackageReference Include="nkast.Xna.Framework.Audio" Version="4.2.9001" />
    <PackageReference Include="nkast.Xna.Framework.Media" Version="4.2.9001" />
    <PackageReference Include="nkast.Xna.Framework.Input" Version="4.2.9001" />
    <PackageReference Include="nkast.Xna.Framework.Game" Version="4.2.9001" />
    <PackageReference Include="nkast.Xna.Framework.Devices" Version="4.2.9001" />
    <PackageReference Include="nkast.Xna.Framework.Storage" Version="4.2.9001" />
    <PackageReference Include="nkast.Xna.Framework.XR" Version="4.2.9001" />
    <!-- BlazorGL platform (GraphicsDevice + window/canvas backend for WebGL). -->
    <PackageReference Include="nkast.Kni.Platform.Blazor.GL" Version="4.2.9001" />
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
">
    <canvas id="theCanvas" style="touch-action:none;"></canvas>
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
    public partial class Index
    {
        private Game _game;

        protected override void OnAfterRender(bool firstRender)
        {
            base.OnAfterRender(firstRender);
            if (firstRender)
                JsRuntime.InvokeAsync<object>("initRenderJS", DotNetObjectReference.Create(this));
        }

        [JSInvokable]
        public void TickDotNet()
        {
            if (_game == null)
            {
                _game = new WebGame();
                _game.Run();
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
    <base href="/" />
    <link href="css/app.css" rel="stylesheet" />
</head>

<body>

    <div id="app">
        <div id="loading" style="display: table-cell; margin: auto; width:100vw; height:100vh; vertical-align: middle; background: #1d2330;">
            <div style="display: block; margin: auto; width: 14em; color: white; font-family: 'Segoe UI', sans-serif;">
                <div style="text-align: center; font-size: 1.0em;">{{projectName}} (BlazorGL)</div>
                <div style="text-align: center; font-size: 1.6em;">loading&nbsp;.&nbsp;.&nbsp;.</div>
            </div>
        </div>
    </div>

    <div id="blazor-error-ui">
        An unhandled error has occurred.
        <a href="" class="reload">Reload</a>
        <a class="dismiss">x</a>
    </div>

    <script src="_framework/blazor.webassembly.js"></script>

    <!--
        nkast.Wasm.* JS interop shims. The "8.0.11" in each src below is the nkast.Wasm.* JS asset
        version, NOT a framework version, and it is HARD-COUPLED to the package nkast.Kni.Platform.Blazor.GL
        (4.2.9001 in {{projectName}}.Web.csproj) transitively pulls. If you bump nkast.Kni.Platform.Blazor.GL
        (or pin nkast.Wasm.* explicitly), update EVERY "8.0.11" in these script srcs to match the restored
        nkast.Wasm.* version, or the canvas/GL/JSObject interop fails to load at runtime with a 404 on these
        _content/ paths. There is no build-time check that these line up — see docs/web-targeting.md
        ("Web host" row of the dependency-parity table). JSObject ships in nkast.Wasm.JSInterop (it moved
        there from nkast.Wasm.Dom in 8.0.x).
    -->
    <script src="_content/nkast.Wasm.JSInterop/js/JSObject.8.0.11.js"></script>
    <script src="_content/nkast.Wasm.Dom/js/Window.8.0.11.js"></script>
    <script src="_content/nkast.Wasm.Dom/js/Document.8.0.11.js"></script>
    <script src="_content/nkast.Wasm.Dom/js/Navigator.8.0.11.js"></script>
    <script src="_content/nkast.Wasm.Dom/js/Gamepad.8.0.11.js"></script>
    <script src="_content/nkast.Wasm.Dom/js/Media.8.0.11.js"></script>
    <script src="_content/nkast.Wasm.XHR/js/XHR.8.0.11.js"></script>
    <script src="_content/nkast.Wasm.Canvas/js/Canvas.8.0.11.js"></script>
    <script src="_content/nkast.Wasm.Canvas/js/Canvas2dContext.8.0.11.js"></script>
    <script src="_content/nkast.Wasm.Canvas/js/CanvasGLContext.8.0.11.js"></script>
    <script src="_content/nkast.Wasm.Audio/js/Audio.8.0.11.js"></script>
    <script src="_content/nkast.Wasm.XR/js/XR.8.0.11.js"></script>

    <script>
        function tickJS() {
            window.theInstance.invokeMethod('TickDotNet');
            window.requestAnimationFrame(tickJS);
        }

        window.initRenderJS = (instance) => {
            window.theInstance = instance;

            var canvas = document.getElementById('theCanvas');
            var holder = document.getElementById('canvasHolder');
            canvas.width = holder.clientWidth;
            canvas.height = holder.clientHeight;

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
