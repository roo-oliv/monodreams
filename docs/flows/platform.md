---
flow: platform
covers:
  - Directory.Build.props
  - MonoDreams.Examples.Web/**
  - MonoDreams.Demos.Web/**
  - MonoDreams.Examples.Desktop/**
  - MonoDreams.Web.Hosting/**
  - docs/web-targeting.md
sensitive: true
---

# Platform targeting (desktop ⇄ web)

The same MonoDreams source ships to MonoGame `DesktopGL` and to KNI/BlazorGL (WebAssembly),
and the choice of backend is made **entirely outside the engine modules** — by the consuming
head project, never by a `MonoDreams/` source file. The seam that makes this work: KNI keeps
the `Microsoft.Xna.Framework` *namespace* but ships under a different *assembly identity*
(`nkast.Xna.Framework.*`), so engine source recompiles unchanged against either backend; only
*precompiled* third-party deps and *content* need a backend-matched variant. The targeting
decision flows in one direction — a head sets the platform, the platform flows into the shared
`.Core` library and the engine, and that selection picks the runtime framework package, the OS
services (`IPlatformServices`), the content builder, and the output layout. The load-bearing
rule is the inversion: **no `#if MONODREAMS_WEB`, no framework-package reference, and no
`GraphicsProfile` literal lives in a `MonoDreams/` module** — every such choice is a head-level
or `Directory.Build.props`-level input. A change that puts a backend-specific decision inside an
engine module breaks one backend while the other keeps working, which is the failure shape this
flow exists to prevent: it survives the desktop build/test gate and only surfaces in the browser.

## Entities & lifecycle

The targeting path, head → output, in order:

1. **Head picks the platform.** A desktop head (`MonoDreams.Examples.Desktop`) builds with the
   default; a web head (`MonoDreams.Examples.Web`, `MonoDreams.Demos.Web`) is built with the
   **global** property `-p:MonoDreamsPlatform=web`. Global matters: `AdditionalProperties` on a
   `ProjectReference` does **not** propagate through `restore`, so only `-p:` reaches `.Core`'s
   restore and resolves the KNI packages transitively.
2. **Property flows into `.Core`, then the engine.** `Directory.Build.props` defines
   `$(MonoDreamsPlatform)` (default `desktop`, other value `web`) and, for `web`, the
   `MONODREAMS_WEB` compile symbol. `.Examples.Core` forwards it to `MonoDreams.csproj` via
   `AdditionalProperties="MonoDreamsPlatform=$(MonoDreamsPlatform)"` so the same engine source
   compiles once per backend with no assembly-identity collision. `MonoDreams.Web.Hosting`
   forwards it the same way.
3. **Backend NuGet selected.** `MonoDreams.csproj` and `.Examples.Core.csproj` gate framework
   packages on `$(MonoDreamsPlatform)`: desktop ⇒ `MonoGame.Framework.DesktopGL` + `MonoGame.Extended`;
   web ⇒ `nkast.Xna.Framework.*` + `KNI.Extended`. The vendored LDtk runtime (no NuGet KNI build)
   recompiles against whichever backend the flowed property selects.
4. **Head wiring + OS services chosen.** A desktop head runs the real `DesktopPlatformServices`
   (the default holder). A web head's one-line `Program.Main` calls `WebHost.RunAsync` (in the
   shared `MonoDreams.Web.Hosting` Razor Class Library), which installs `WebPlatformServices`
   into `PlatformServices.Current` as the first startup step and drives `Game.Tick()` from a
   `requestAnimationFrame` loop. `MONODREAMS_WEB` flips head-level conditionals only —
   `GraphicsProfile.Reach` instead of `HiDef`, dropping `Window.Position`/`ClientSizeChanged`.
5. **Content built per platform from the same `.mgcb`.** The identical `Content.mgcb` (in
   `.Examples.Core`) builds for either backend; what changes is the *builder* and the
   *pipeline-assembly references*: desktop uses `MonoGame.Content.Builder.Task`
   (`/platform:DesktopGL`); web uses KNI's builder (`/platform:BlazorGL`) with custom processors
   recompiled against the KNI pipeline assemblies and surfaced via `/reference:`. Off-Windows the
   KNI MGCB needs the native-lib shim (`BuildWebContentPipelineDlls` + `PrepareKniContentNativeShim`
   in `MonoDreams.Examples.Web.csproj`).
6. **Output relocated per platform.** Web builds of the *shared* libs go to `obj/web` + `bin/web`
   so they never clobber the desktop build at the default `bin/$(Config)/net8.0`. The `*.Web`
   heads and `MonoDreams.Web.Hosting` are **excluded** from that relocation (their Blazor
   boot/static-web-asset pipeline assumes the default layout). The web head is in the `.sln` but
   has **no `Build.0` entry**, so `dotnet build MonoDreams.sln` (the desktop regression) skips it.

## Invariants

Authoritative in [`MonoDreams/foundation/docs/premises.md`](../../MonoDreams/foundation/docs/premises.md)
("Engine source is backend/OS-agnostic — non-portable calls go through `IPlatformServices`";
"The platform … is selected by the head project, never by engine source") and
[`MonoDreams/level-loading/docs/premises.md`](../../MonoDreams/level-loading/docs/premises.md)
("Content is built per-platform from the same `.mgcb`"). The ones this flow's path leans on:

- No `MonoDreams/` module hard-codes a backend: no `#if MONODREAMS_WEB`, no framework-package
  reference, no `GraphicsProfile` literal. The backend is an external MSBuild input.
- Non-portable OS calls (`System.IO`/`Console`/`Environment`/`AppDomain`) in engine source go
  through `PlatformServices.Current`, never directly — read-only *game content* is the exception
  (it flows through `ContentManager`/`TitleContainer`, served over HTTP on web).
- The web property must be **global** (`-p:MonoDreamsPlatform=web`); `AdditionalProperties` alone
  doesn't survive `restore`, so a head-set `web` value must reach `.Core` via `-p:`.
- A content custom-processor must link the pipeline assemblies **matching the output backend** —
  a desktop processor cannot emit BlazorGL `.xnb` and vice versa.

## Load-bearing quantities

- `$(MonoDreamsPlatform)` — `desktop` (default) selects `MonoGame.Framework.DesktopGL` 3.8.4 +
  `MonoGame.Extended` + the MonoGame content pipeline; `web` selects `nkast.Xna.Framework.*`
  4.2.9001 + `KNI.Extended` 6.0.0 + the KNI/BlazorGL pipeline, defines `MONODREAMS_WEB`, and
  relocates shared-lib output to `obj/web` + `bin/web`.
- `MONODREAMS_WEB` — compile symbol; flips **head-level** code only (`GraphicsProfile.Reach` vs
  `HiDef`, dropping `Window.Position`/`ClientSizeChanged`). Engine modules never read it.
- Reach 16-bit-index budget — WebGL ES2/Reach rejects 32-bit indices that `SpriteBatch` switches
  to past **5461 sprites** per `Begin`/`End`. Fixed **unconditionally** (no `GraphicsProfile`
  branch) on both render paths: `MasterRenderSystem` flushes below `SpriteBatchFlush.MaxSpritesPerBatch`
  (`< 5461`), and meshes render through 16-bit indices via `DrawComponent.Get16BitIndices()`. See
  [`docs/web-targeting.md`](../web-targeting.md) ("The Reach 32-bit-index render limit").

## Failure modes

- **Backend-specific API in engine source** — a `MonoDreams/` module references a desktop-only
  API, a `GraphicsProfile` literal, a framework package, or a direct `File`/`Console`/`Environment`
  call. The desktop build and full test suite stay green; the web build either fails to compile or
  throws/no-ops only in the browser. Highest-severity and hardest to catch — the desktop gate
  doesn't exercise it. This is the core reason the flow is sensitive.
- **Content not built (or built for the wrong backend) for web** — a new content item, or a custom
  processor compiled against the desktop pipeline, makes the web `.mgcb` fail at content-build time
  (importer/processor-not-found, assembly-load) or emit DesktopGL-tagged `.xnb` a BlazorGL runtime
  can't load. Non-obvious: the csproj appears to reference the assembly correctly.
- **`.sln`-excludes-web-head gotcha** — assuming `dotnet build MonoDreams.sln` covers web. It does
  not (no `Build.0` entry); a web regression ships unbuilt unless someone runs the explicit
  `-p:MonoDreamsPlatform=web` build. Conversely, *adding* the web head to the default build makes
  the desktop `.sln` build restore `.Core` for desktop under it and fail on KNI type mismatches.
- **Property passed non-globally** — using `AdditionalProperties`/per-reference metadata instead of
  `-p:` for the web build; `restore` resolves desktop packages for `.Core` and the build fails on
  KNI type mismatches before any code runs.
- **Web output clobbers desktop (or vice versa)** — defeating the `obj/web`/`bin/web` relocation
  (e.g. relocating a `*.Web` head, which must stay at the default layout) leaves a web-backed dll
  on the shared path, and the next desktop compile picks it up → CS0012; the reverse 404s the
  Blazor boot pipeline.
