# Web targeting (KNI / BlazorGL)

MonoDreams games can ship to the **web browser** (Chrome latest, WebGL) in
addition to desktop. The web backend is [KNI](https://github.com/kniEngine/kni),
a MonoGame-compatible fork whose **BlazorGL** platform runs on WebAssembly.

The key fact that makes this cheap: KNI keeps the same
`Microsoft.Xna.Framework` *namespace* but ships under different *assembly
identities* (`nkast.Xna.Framework.*`). So **MonoDreams' own source recompiles
unchanged** against either backend — only *precompiled* third-party
dependencies need a KNI-built variant.

> **Read first** if you are changing engine source: `docs/CORE_TENETS.md`,
> and the premises that govern this seam —
> `MonoDreams/foundation/docs/premises.md` ("Engine source is
> backend/OS-agnostic", "The platform … is selected by the head project"),
> `MonoDreams/level-loading/docs/premises.md` ("Content is built per-platform
> … processors must match the backend's pipeline assemblies"), and
> `MonoDreams/level-ldtk/docs/premises.md` ("LDtkMonogame is vendored as
> source").

## The project model: shared `.Core` + per-platform heads

A multi-platform game is **one shared game library plus one head per
platform**, never a single project that hard-codes a backend:

```
MyGame.Core/      shared game code + MonoDreams modules (platform-agnostic).
                  Backend chosen by the $(MonoDreamsPlatform) MSBuild property.
MyGame.Desktop/   DesktopGL head: Program.cs -> new Game(); game.Run().
                  References MonoGame.Framework.DesktopGL.
MyGame.Web/       BlazorGL head: just WebGame.cs + a one-line Program.cs +
                  wwwroot/index.html. All other host wiring comes from the shared
                  MonoDreams.Web.Hosting library (below).
MyGame.sln        the desktop heads build by default; the web head is excluded
                  (see "Why the web head is excluded from the .sln build").
```

The reference app `MonoDreams.Examples` is laid out exactly this way —
`MonoDreams.Examples.Core` + `.Desktop` + `.Web` — and doubles as the template
the CLI generates.

### The shared web host layer (`MonoDreams.Web.Hosting`)

Every web head is a Blazor WASM host with the *same* boot wiring: install the
web `IPlatformServices`, build the `WebAssemblyHostBuilder`, mount a full-window
`<canvas>`, and drive `Game.Tick()` from a `requestAnimationFrame` loop. That
wiring lives **once** in `MonoDreams.Web.Hosting` (a Razor Class Library), so a
head carries only what is genuinely its own:

- `WebGame.cs` — the game's `Game` subclass (screens, virtual resolution, boot screen).
- `Program.cs` — one line: `WebHost.RunAsync(args, () => new WebGame())`.
- `wwwroot/index.html` — the host page (title + the nkast.Wasm.* `<script>` tags),
  which pulls the shared CSS + game loop from `_content/MonoDreams.Web.Hosting/`.
- its per-game content build (the `.mgcb` differs per game).

The library owns: `WebPlatformServices`, `WebHost.RunAsync` (the bootstrap),
`GameCanvas` (the root component: canvas markup + the tick loop, with the head's
`Game` injected as a `Func<Game>`), and the shared `wwwroot/js/host.js` +
`wwwroot/css/app.css` (served to heads at `_content/MonoDreams.Web.Hosting/`).
The KNI runtime stack (`nkast.Xna.Framework.*` + `nkast.Kni.Platform.Blazor.GL`
+ `KNI.Extended`) is referenced here too; heads inherit it transitively. The
library is web-only host infrastructure (a sibling of the heads, **not** an
engine module) — it is excluded from the default `.sln` build and from the
`bin/web` relocation, exactly like the `*.Web` heads.

> **CLI note:** `monodreams init --platform web|multi` still scaffolds a
> *self-contained* web head (its own copy of the host wiring), because a
> shadcn-style generated game can't reference this in-repo library. Folding the
> shared host layer into a scaffolded/installable form is a tracked follow-up;
> the in-repo Examples/Demos heads use the shared library today.

### How the backend is selected

`Directory.Build.props` defines `$(MonoDreamsPlatform)` (default `desktop`,
the other value is `web`). A head flows it into `.Core`:

- The `web` value is passed as a **global** property: `-p:MonoDreamsPlatform=web`.
- It also defines the `MONODREAMS_WEB` compile symbol, which the **heads** use
  to gate `GraphicsProfile.Reach` (instead of `HiDef`) and to drop
  `Window.Position` / `Window.ClientSizeChanged` (no OS window on web).

MonoDreams modules contain **no** `#if MONODREAMS_WEB`, no framework-package
reference, and no `GraphicsProfile` literal — every such choice lives in the
head or in `Directory.Build.props`. This is the
"platform is selected by the head" premise; respect it when adding engine
code (filesystem/console/env access goes through `IPlatformServices`, not
direct `System.IO` / `System.Console` / `System.Environment`).

## Generating a project with the CLI

```bash
dotnet run --project MonoDreams.Cli -- init MyGame --platform desktop|web|multi
```

- `--platform desktop` (default) → `.Core` + `.Desktop` head.
- `--platform web` → `.Core` + `.Web` head.
- `--platform multi` → `.Core` + both heads + a `.sln`.

The target platform(s) are recorded in `monodreams.json`. `monodreams add
<module>` then injects the correct per-platform package for each module
(e.g. `MonoGame.Extended` for desktop, `KNI.Extended` for web) and warns when
a module is unsupported on one of the project's platforms.

## Building & running the web head

```bash
# Build the WASM bundle (requires the wasm-tools dotnet workload installed).
# -p is GLOBAL so it flows through restore to .Core.
dotnet build MonoDreams.Examples.Web/MonoDreams.Examples.Web.csproj -p:MonoDreamsPlatform=web

# Serve & open in Chrome (the Blazor dev server, or any static host over the
# publish output). Diagnose via the browser devtools console — the engine
# Logger routes through WebPlatformServices to console.log on web.
dotnet run --project MonoDreams.Examples.Web/MonoDreams.Examples.Web.csproj -p:MonoDreamsPlatform=web

# The module demos have a web head too — same flags. It boots the demo launcher
# (camera / physics / dialogue / UI / audio), mirroring the desktop MonoDreams.Demos flow.
dotnet run --project MonoDreams.Demos.Web/MonoDreams.Demos.Web.csproj -p:MonoDreamsPlatform=web
```

> **Two web heads today:** `MonoDreams.Examples.Web` (the reference game) and
> `MonoDreams.Demos.Web` (the per-module demos). Both are thin Blazor hosts that
> share their boot wiring through `MonoDreams.Web.Hosting` (see "The shared web
> host layer" above), so each is just its `WebGame.cs` + a one-line `Program.cs`
> + `index.html`. They differ only in their game source and content build:
> Examples.Web references `MonoDreams.Examples.Core`; Demos.Web compile-includes
> `MonoDreams.Demos` `Screens/` + `UI/` and the module `demo/` sources directly
> (the same cross-compile pattern the desktop `MonoDreams.Demos` uses), so the
> desktop Demos project and its issue-#28 headless tests are untouched. Every
> `*.Web` head — and `MonoDreams.Web.Hosting` — stays at the default
> `bin/$(Config)/net8.0` output (see `Directory.Build.props`) — the Blazor boot
> pipeline assumes it; only the shared engine/game libs relocate to `bin/web`.

### Why the web head is excluded from the `.sln` build

MSBuild `AdditionalProperties` on a `ProjectReference` **does not propagate
through `restore`**. If the web head were in the default `.sln` build,
`dotnet build MonoDreams.sln` would restore `.Core` for the *desktop* backend
under the web head and then fail on KNI type mismatches. So the web head is
included in the solution but has **no `Build.0` entry** in the active build
configuration — `dotnet build MonoDreams.sln` (the desktop regression) skips
it, and you build the web head explicitly with `-p:MonoDreamsPlatform=web`.

## Dependency parity (what changes per backend)

| Concern | Desktop | Web (KNI/BlazorGL) |
|---|---|---|
| Runtime framework | `MonoGame.Framework.DesktopGL` 3.8.4 | `nkast.Xna.Framework.*` 4.2.9001 + `nkast.Kni.Platform.Blazor.GL` |
| Audio (`SoundEffect` — the `audio` module) | ships inside `MonoGame.Framework.DesktopGL` | `nkast.Xna.Framework.Audio` 4.2.9001 (split package; see "Audio" below) |
| MonoGame.Extended (BitmapFont) | `MonoGame.Extended` 4.1.0 | `KNI.Extended` 6.0.0 |
| Extended content pipeline | `MonoGame.Extended.Content.Pipeline` | `KNI.Extended.Content.Pipeline` 6.0.0 |
| Content builder / MGCB | `MonoGame.Content.Builder.Task` 3.8.4 | `nkast.Xna.Framework.Content.Pipeline.Builder` 4.2.9001 |
| LDtk | (vendored — see below) | (vendored — same source, web backend) |
| YarnSpinner / CsvHelper / DefaultEcs | pure .NET | unchanged (no MonoGame ref) |
| Web host | — | `Microsoft.AspNetCore.Components.WebAssembly` 8.0.11 + `nkast.Wasm.*` JS shims |

These are tagged per platform in each module's `module.json`
(`nugetDependencies[].platforms`) and conditioned by `$(MonoDreamsPlatform)`
in `MonoDreams.csproj`.

### LDtk is vendored as source

`LDtkMonogame` has no KNI build on NuGet, so its runtime + content pipeline
are **vendored as source** under
`MonoDreams/level-ldtk/vendor/LDtkMonogame/` (MIT, pinned to tag 1.8.0 /
commit `4a652fb`) and recompiled against whichever backend
`$(MonoDreamsPlatform)` selects. The engine `ProjectReference`s the vendored
*runtime*; the vendored *content pipeline* is built per-platform and surfaced
to MGCB via `/reference:`. Re-sync deliberately on upstream changes (bump the
pinned version note in `vendor/.../LDtk.csproj` + `module.json` + the
level-ldtk premise).

## Content build (the same `.mgcb`, two backends)

The content project (`Content.mgcb`) is **not** platform-specific — the same
`.mgcb` builds for either backend. What changes is the *builder* and the
*pipeline-assembly references*:

- **Desktop:** `MonoGame.Content.Builder.Task`, `/platform:DesktopGL`.
- **Web:** KNI's `nkast.Xna.Framework.Content.Pipeline.Builder`,
  `/platform:BlazorGL`, with custom processors (`KNI.Extended.Content.Pipeline`,
  the vendored LDtk content pipeline, the Yarn importer) **recompiled against
  the KNI pipeline assemblies** and surfaced via `/reference:` lines.

A custom processor must link the pipeline assemblies matching the *output*
backend — a desktop processor cannot emit BlazorGL `.xnb` and vice versa.
This is the "content built per-platform" premise (`level-loading`).

### macOS / Linux native-lib shim (required)

KNI's MGCB builder package ships **only Windows-native** `FreeImage` /
`freetype` libraries, so on macOS/Linux the `TextureImporter` throws
`DllNotFoundException`. The working cross-platform recipe (implemented in
`MonoDreams.Examples.Web.csproj`, targets `BuildWebContentPipelineDlls` +
`PrepareKniContentNativeShim`):

1. Run the **managed** `MGCB.dll` via `dotnet` instead of the bundled Windows
   `MGCB.exe` (`KniContentBuilderExe = dotnet` on non-Windows).
2. Copy the matching macOS/Linux native libs from the desktop MonoGame MGCB
   tool (`dotnet-mgcb` 3.8.4, `runtimes/osx|linux-x64/native/`) into the KNI
   builder's `tools/` dir, renamed to the names KNI's P/Invoke probes
   (`libfreeimage.dylib` → `FreeImage.dylib`, `libfreetype.dylib` →
   `freetype6.dylib`; `.so` on Linux).
3. Stage `KNI.Extended.dll` + `KNI.Extended.Content.Pipeline.dll` + `Autofac.dll`
   in the builder `tools/` dir so the BitmapFont importer's dependency probe
   resolves.
4. Build the engine + vendored LDtk content pipeline **for web** into an
   isolated output dir via a nested `dotnet build -p:MonoDreamsPlatform=web`,
   and pass those web-backed dlls to MGCB with `/reference:`. (A plain
   in-process MSBuild target reuses the desktop `project.assets.json` and
   silently emits a *desktop* dll, which the KNI MGCB then fails to bind.)
5. **If the `.mgcb` builds audio (`.wav`)**: copy `ffmpeg` + `ffprobe` from the
   desktop MGCB tool (`dotnet-mgcb` 3.8.4, `tools/net8.0/any/osx|linux-x64/`)
   into the KNI builder's `tools/osx/` / `tools/linux-x64/` subdir. KNI's
   `WavImporter`/`SoundEffectProcessor` shell out to them via `ExternalTool`
   (which probes that per-OS subdir, then `PATH`) and the KNI builder package
   ships **no** ffmpeg for any OS — without the copy the content build fails
   with `Failed to open file <name> … not DRM protected`. (Implemented in
   `MonoDreams.Demos.Web.csproj`, target `PrepareKniContentNativeShim`.)

On Windows the native-lib shim (steps 1–3) is not needed — the bundled
`MGCB.exe` + native DLLs work as-is — but the ffmpeg gap (step 5) exists there
too: KNI ships no `ffmpeg.exe`, so audio content on Windows needs the
`dotnet-mgcb` `windows-x64` binaries or ffmpeg/ffprobe on `PATH` (unverified —
no Windows host here).

### Shaders (`.fx`) — status

The example `.fx` shaders are **dead code today** (the renderer uses
`BasicEffect`; no screen does `Load<Effect>`, and no `.mgcb` builds them). They
have been ported to a Reach-legal SM3 path for the GL/`__KNIFX__` branch, but
empirical compilation needs **KniFXC**, which is a DirectX-dependent tool
(2MGFX renamed) that requires Wine/Windows and is **not** installable as a
NuGet/dotnet-tool on macOS/Linux. If you add a shader that a `.mgcb` actually
builds for web, compile it with KniFXC on a Windows/Wine host — do not
fabricate `.knifx`/`.xnb` output.

## The Reach 32-bit-index render limit (Risk #1 — fixed in the renderer)

WebGL ES2 / the Reach profile rejects the 32-bit indices that `SpriteBatch`
switches to once a single `Begin`/`End` submits more than 5461 sprites. A dense
LDtk tile world exceeds that even after culling, so an unsplit draw throws
`Reach profile does not support 32 bit indices` (desktop/HiDef accepts 32-bit
indices, which is why the same scene paints there). This was the plan's
**Risk #1**.

32-bit indices arise on **two** render paths, both fixed unconditionally (no
`GraphicsProfile` branch — the renderer stays platform-agnostic per "platform is
selected by the head"; on HiDef the work is harmless, on Reach it is the fix):

1. **Sprite/text runs** — `MasterRenderSystem` flushes (`End` + `Begin`) the
   `SpriteBatch` before a run crosses `SpriteBatchFlush.MaxSpritesPerBatch`
   (`< 5461`), so no batch needs 32-bit indices. Guarded by
   `MonoDreams.Tests/Rendering/SpriteBatchFlushTests.cs` + the premise "Sprite
   runs flush below the Reach 16-bit-index budget".
2. **Mesh draws** — `DrawUserIndexedPrimitives` picks the index width from its
   array type (`int[]` ⇒ 32-bit, `short[]` ⇒ 16-bit). Meshes are authored with
   `int[]`, so `DrawComponent.Get16BitIndices()` converts to a cached `short[]`
   and `DrawSingleMesh` renders through the 16-bit overload (falling back to
   32-bit only for a mesh past the 16-bit vertex ceiling, HiDef-only). Without
   this the player's orb mesh (a `CircleMeshGenerator`) threw on the first web
   frame. Guarded by `MonoDreams.Tests/Rendering/MeshIndexConversionTests.cs` +
   the premise "Mesh indices render through 16-bit indices (Reach-safe)".

The desktop demo headless tests confirm neither change alters what renders on
HiDef.

> **Confirmed in Chrome.** Both fixes are verified in-browser: the
> `MonoDreams.Examples.Web` LDtk platformer paints its dense tile world (sprite
> path) and the player orb (mesh path), and the `MonoDreams.Demos.Web` physics
> demo paints filled circles, ring outlines, the box/floor, and mesh checkboxes
> — all on the Reach profile via the Blazor dev server.

### Tooling-host gaps

- **wasm-tools workload** is required for any web build.
- The macOS/Linux **MGCB native-lib shim** above is required for content
  builds off Windows.
- **KniFXC** (shader compiler) is unavailable off Windows/Wine.

## Audio (WebAudio) & the browser autoplay policy

The `audio` module's `SoundEffect` path runs on web through KNI's WebAudio
backend. Two engine-side facts make it compile-and-play without `#if`:

- **Split package.** KNI ships XNA audio as its own assembly, so the engine's
  web ItemGroup references `nkast.Xna.Framework.Audio` 4.2.9001 (desktop needs
  nothing — audio ships inside `MonoGame.Framework.DesktopGL`). The WebAudio JS
  shim (`_content/nkast.Wasm.Audio/js/Audio.8.0.11.js`) is already in both web
  heads' `index.html`, pulled by `nkast.Kni.Platform.Blazor.GL` transitively
  through `MonoDreams.Web.Hosting`.
- **API parity, verified against the 4.2.9001 `net8.0` binaries** (reflection
  sweep, 2026-07-22): `SoundEffect.CreateInstance()`, `SoundEffectInstance`
  (`IsLooped`/`Volume`/`Pitch`/`Pan`/`State`, `Play`/`Stop`/`Pause`/`Resume`/
  `Dispose`), `SoundState`, and `NoAudioHardwareException` are all public under
  the same `Microsoft.Xna.Framework.Audio` namespace — `ContentAudioPlayer`
  (including its no-hardware degradation catch) recompiles unchanged.

### Autoplay: the host page owns the AudioContext unlock

Browsers create an `AudioContext` in the **`suspended`** state when no user
gesture has happened yet; a suspended context renders nothing — audio is
silently muted until something calls `resume()` *from a gesture handler*.
**KNI does not do this itself.** Code inspection of the 4.2.9001 binaries
(2026-07-22):

- `Microsoft.Xna.Platform.Audio.ConcreteAudioService` (in `Kni.Platform.dll`,
  BlazorGL) creates a plain `new AudioContext()`; its `Suspend()` and
  `Resume()` overrides are **empty method bodies**.
- Nothing in `Kni.Platform.dll` calls `AudioContext.ResumeAsync()` (the
  interop for JS `audioContext.resume()` exists in `nkast.Wasm.Audio` but has
  zero call sites in the platform).
- The `nkast.Wasm.Audio` JS shim is a bare 1:1 WebAudio interop — no
  `pointerdown`/`keydown`/`touch` listener anywhere.

So the shared host layer owns the unlock: `MonoDreams.Web.Hosting`'s
`wwwroot/js/host.js` wraps the shim's AudioContext factories
(`nkAudioContext.Create`/`Create1`) to track live contexts and resumes any
suspended one on the first `pointerdown`/`keydown` (capture phase, window
level). Engine and game code stay gesture-unaware; sources started while
suspended (e.g. an ambient loop on screen load) begin sounding at the first
interaction. In-browser behaviour ("first click unlocks audio") is verified
manually in Chrome — it is not headlessly testable.

> **Scaffolded web heads:** `monodreams init --platform web|multi` generates a
> *self-contained* head with its own copy of the host wiring (see the CLI note
> above) — that copy must replicate the resume hook, or web audio stays muted.

## See also

- [`docs/CORE_TENETS.md`](CORE_TENETS.md) — engine-wide invariants.
- [`MonoDreams/MODULES.md`](../MonoDreams/MODULES.md) — manifest `platforms`
  fields and per-entry platform tags.
- [`CONTRIBUTING.md`](../CONTRIBUTING.md) — build/test workflow and OS setup.
</content>
</invoke>
