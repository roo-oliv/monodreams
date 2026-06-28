# Phase 0 — BlazorGL de-risking spike (THROWAWAY)

Status: **PROVEN on macOS (Apple Silicon).** A minimal BlazorGL head referencing a
trivial slice of MonoDreams (`foundation` + `rendering`, recompiled unchanged against
`nkast.Xna.Framework.*`) compiles, publishes to a WASM bundle, and renders one sprite
through the real MonoDreams render pipeline in Chrome 149 / WebGL.

Proof screenshot: `spike-proof.png` — the engine's default warm-beige
`FinalDrawSystem.ClearColor` (245,235,220) with a 256×256 OrangeRed/Gold checker sprite
centered, produced by `CullingSystem → SpritePrepSystem → MasterRenderSystem →
FinalDrawSystem` running on BlazorGL. The automated driver (`cdp/run.mjs`) asserts the
screenshot contains exactly the 3 expected colors.

This whole directory is throwaway. It validates the plan's core hypotheses before the
large refactor; the package versions + host files here become the templates for Phase 3
(Examples.Web head) and Phase 4 (CLI scaffolder).

## What the spike proves (the two contract items)

1. **MonoDreams source recompiles UNCHANGED against KNI.** `MonoDreams.Spike.csproj`
   `<Compile Include>`s the real `MonoDreams/foundation`, `MonoDreams/rendering`, and
   `MonoDreams/rendering-text` source (no copies, no edits) and builds with **0 errors**
   against `nkast.Xna.Framework.*` 4.2.9001 + `KNI.Extended` 6.0.0. KNI keeps the
   `Microsoft.Xna.Framework` namespace under a different assembly identity, so every
   `using Microsoft.Xna.Framework...` and the `MonoGame.Extended.BitmapFonts` types resolve.
2. **That recompiled pipeline renders in Chrome.** `MonoDreams.Spike.Web` (BlazorGL head)
   builds the standard renderable entity stack (`EntityInfoComponent + TransformComponent +
   SpriteInfoComponent + DrawComponent`, `VisibleComponent` added by `CullingSystem`) and
   runs the real render systems every frame on the KNI BlazorGL backend.

## Exact working versions (CAPTURE — use these as the Phase 3/4 template)

| Concern | Package | Version |
|---|---|---|
| dotnet SDK | — | 8.0.416 (net8.0) |
| dotnet workload (REQUIRED) | `wasm-tools` | manifest 8.0.28 (installed during spike) |
| KNI split framework (×10) | `nkast.Xna.Framework[.Content/.Graphics/.Audio/.Media/.Input/.Game/.Devices/.Storage/.XR]` | **4.2.9001** |
| KNI BlazorGL platform | `nkast.Kni.Platform.Blazor.GL` | **4.2.9001** (transitively pulls all 10 framework pkgs + 6 `nkast.Wasm.*`) |
| KNI port of MonoGame.Extended | `KNI.Extended` | **6.0.0** (depends on `nkast.Xna.Framework.* >= 4.0.9001`) |
| Blazor WASM hosting | `Microsoft.AspNetCore.Components.WebAssembly` (+ `.DevServer` PrivateAssets=all) | **8.0.11** |
| JS interop shims (transitive) | `nkast.Wasm.*` (Dom, Canvas, Audio, XHR, XR, **JSInterop**) | **8.0.11** |
| ECS (unchanged, pure .NET) | `DefaultEcs` | 0.18.0-beta01 |

Note: the `nkast.Xna.Framework.Blazor` **metapackage** caps at 3.14.9001 — do NOT use it
for the 4.x line. Use `nkast.Kni.Platform.Blazor.GL` (4.2.9001) like the canonical KNI
sample (`nkast/WebGLxnaProj`).

## Host wiring template (from `nkast/WebGLxnaProj`, adapted)

- **Head SDK**: `Microsoft.NET.Sdk.BlazorWebAssembly`, `net8.0`,
  `<KniPlatform>BlazorGL</KniPlatform>`, `<DefineConstants>$(DefineConstants);BLAZORGL`,
  `EnableDefaultCompileItems=false` (explicit `<Compile>`), `Nullable=disable`,
  `ImplicitUsings=disable`, `BlazorEnableTimeZoneSupport=false`.
- **Game loop is JS-driven**: `wwwroot/index.html` calls `initRenderJS(instance)` on first
  render, then a `requestAnimationFrame` loop invokes `[JSInvokable] TickDotNet()`; the page
  code-behind lazily constructs the `Game` and calls `_game.Run()` once, `_game.Tick()` each
  frame. The `Game` subclass uses `GraphicsDeviceManager` + standard MonoGame
  `Initialize`/`LoadContent`/`Update`/`Draw` — nothing BlazorGL-specific in the game code.
- **`index.html` MUST load the `nkast.Wasm.*` JS shims** at the exact restored versions
  (`_content/<PkgId>/js/<File>.<ver>.js`). The canvas is `<canvas id="theCanvas">` inside a
  full-viewport `<div id="canvasHolder">`.
- Files to reuse: `Program.cs`, `App.razor`, `MainLayout.razor`, `_Imports.razor`,
  `Pages/Index.razor` + `.razor.cs`, `wwwroot/index.html`, `wwwroot/css/app.css`.

## Findings that matter for later phases (do NOT re-discover these)

- **`_Imports.razor` `System` namespace collision (Phase 4 template gotcha).** A head whose
  root namespace begins with `MonoDreams` (e.g. `MonoDreams.Examples.Web`) makes Razor resolve
  `@using System.Net.Http` to `MonoDreams.System.*` (the engine defines `MonoDreams.System.Draw`).
  **Fix: prefix the System usings with `global::`** in `_Imports.razor`
  (`@using global::System.Net.Http`). Regular `.cs` files are unaffected — only Razor-generated
  code. The CLI scaffolder must emit `_Imports.razor` with the `global::` prefix (or give the
  web head a non-`MonoDreams`-prefixed root namespace).
- **`rendering` is NOT standalone-compilable; it hard-references `rendering-text`.**
  `DrawComponent` and `MasterRenderSystem` reference `DynamicTextComponent` (the Text draw
  path), which lives in the `rendering-text` module. Any per-platform `Core`/spike build that
  includes `rendering` must also include `rendering-text`. Relevant to Phase 2/3 content/module
  wiring and to the module manifest dependency graph (Phase 4): `rendering → rendering-text`.
- **`JSObject` moved package**: in `nkast.Wasm.*` 8.0.11, `JSObject.<ver>.js` ships in
  **`nkast.Wasm.JSInterop`**, not `nkast.Wasm.Dom` (the older 8.0.5 sample loaded it from Dom).
- **GraphicsProfile / HiDef question is still OPEN.** This sprite-only spike uses no shaders and
  leaves `GraphicsProfile` at the BlazorGL default; it ran on WebGL with `glError == 0`. The
  plan's HiDef-vs-Reach concern (`MultiTextureEffect`) is deferred to the Phase 2 shader port —
  the spike does NOT settle it.
- **No content pipeline used.** The sprite texture is generated procedurally
  (`Texture2D.SetData`), so the spike has zero `.mgcb`/`.xnb` dependency. The KNI web content
  build (`nkast.Xna.Framework.Content.Pipeline.Builder`, `KniFXC`) is Phase 3 work and was NOT
  exercised here — its macOS availability is still an open risk per the ledger directive.
- **In-page `canvas.readPixels` reads `0,0,0,0` — a measurement artifact, not a render
  failure.** WebGL `preserveDrawingBuffer` is false, so reading the default framebuffer outside
  the RAF returns cleared pixels. The **page screenshot** (which composites the live presented
  canvas) is the authoritative observation; `cdp/run.mjs` asserts on the screenshot.

## How to reproduce

```bash
# 0. one-time: the wasm SDK workload (installed cleanly on this macOS host during the spike)
dotnet workload install wasm-tools

# 1. recompile-hypothesis gate: real MonoDreams source against KNI
dotnet build scratchpad/blazor-spike/MonoDreams.Spike/MonoDreams.Spike.csproj -c Debug   # 0 errors

# 2. build + publish the BlazorGL head
dotnet build   scratchpad/blazor-spike/MonoDreams.Spike.Web/MonoDreams.Spike.Web.csproj -c Debug
dotnet publish scratchpad/blazor-spike/MonoDreams.Spike.Web/MonoDreams.Spike.Web.csproj -c Release -o publish

# 3. serve + render in Chrome
cd scratchpad/blazor-spike/MonoDreams.Spike.Web
dotnet run -c Debug --urls http://127.0.0.1:5280 &          # Blazor DevServer (correct .wasm MIME)
cd ../cdp && PUPPETEER_SKIP_DOWNLOAD=1 npm i puppeteer-core@23
node run.mjs                                                # drives system Chrome headless; prints RENDER PASS
```
