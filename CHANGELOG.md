# Changelog

MonoDreams is in **alpha**: breaking changes land as clean breaks, with no
compatibility shims. Each entry below names the old shape, the new shape, and the
one-line edit that migrates a call site. Modules are source you own (shadcn-style),
so migrating is editing your own copy.

## Unreleased

### Added — scripted pointer replay in `debug` ([#90](https://github.com/roo-oliv/monodreams/issues/90))

`input_replay.json` speaks only named actions, so an entire genre — menus, business sims,
card games, editors — had no scripted-verification story. `PointerReplaySystem` adds the
pointer half: a `debug/pointer_replay.json` plan of `move` / `click` / `wheel` / `type` /
`waitUntil` / `label` commands in authoring-space coordinates, counted in frames, file-gated
and auto-exiting on drain exactly like the input replay. It **injects into the real
`CursorInputComponent`**, so a scripted click exercises the game's actual picking / focus / UI
path. Details in [`MonoDreams/debug/docs/overview.md`](MonoDreams/debug/docs/overview.md)
§ Pointer replay.

Nothing existing changes behaviour, but three source-owned modules gained surface:

- **`debug` now depends on `cursor`** (`module.json`), because the channel injects into
  `CursorInputComponent` rather than simulating one. `monodreams add debug` installs `cursor`
  too.
- **`Logger.LineSink`** (`foundation`) — a static, default-`null`, single-owner tap on every
  emitted message, invoked outside the writer lock. The socket lives in `foundation`, the plug
  in `debug` (the `waitUntil log` predicate), mirroring `GatedSystem.TimingSink`.
- **`TextInputSystem.KeyboardStateProvider`** (`ui`) — the repo's usual `Func<KeyboardState>`
  seam, defaulting to `Keyboard.GetState`, so a scripted `type` reaches a field through the
  system's own key diff.
- **`Cursor.ApplyPose`** (`cursor`) — the per-render-target cursor pose rule, extracted from
  `CursorPositionSystem` so the real mouse and an injected pointer place the cursor identically.
- **`ViewportManager.ScaleVirtualToScreenCoordinates`** (`rendering`) — the exact inverse of
  `MapMouse`, so an injection channel can fill
  `CursorInputComponent.ScreenPosition` in the backbuffer pixels that field means (chrome
  hit-tests read it raw, and it stays right at `DevicePixelRatio` 2).
- **`GameTestRunner.RunAsync(…, pointerPlan:)`** writes the plan into the run's debug dir.

### Breaking — presentation scaling is a declared policy ([#89](https://github.com/roo-oliv/monodreams/issues/89))

How the frame reaches a window that is not the render resolution is now declared once, on
`ViewportManager.Policy`, and resolved in one place: **overscan** to a clean scale →
**letter/pillarbox** at a clean scale → **stretch**, each step bounded by a gamedev-set
tolerance. The winner produces the single `DestinationRectangle` the compositor draws into
and `MapMouse` inverts. The engine default is `PresentationPolicy.Stretch` — the historical
aspect-fit present, so framing is unchanged until a game declares otherwise;
`PresentationPolicy.Default` is what a new game should declare. What DOES change by default
is filtering: layers now sample point at an integer scale and linear otherwise, so UI text
stops shimmering at a fractional present (and pixel art stops being bilinear-blurred at 2×).

- **`ViewportManager.ScalingMode`, `CurrentScalingMode`, `PixelPerfectDestinationRectangle`
  and `IntegerScale` are gone.** The three modes were the policy question asked in three
  incomplete ways, and the pixel-perfect rectangle was a second destination rectangle the
  mouse never inverted. `PresentationPolicy.PixelPerfect` takes over `ScalingMode.PixelPerfect`
  (whole steps, centered, with bars) — through the one `DestinationRectangle`, so picking now
  follows it. It matches the old mode exactly for any window at least as large as the render
  resolution in both axes; **below 1× it deliberately diverges**, because the old mode clamped
  its integer scale to a floor of 1 and cropped the frame off-screen (1920×1080 in a 1600×900
  window: 1920×1080 at (-160, -90)), while a no-overscan policy may not crop and keeps
  descending the ladder instead (960×540 centered, with bars). `Smooth` is subsumed by the
  per-layer sampler policy, and `KeepAspectRatio` is `PresentationPolicy.Stretch`.
  *Migration:* `viewport.CurrentScalingMode = ViewportManager.ScalingMode.PixelPerfect`
  becomes `viewport.Policy = PresentationPolicy.PixelPerfect`; read
  `viewport.DestinationRectangle` where you read `PixelPerfectDestinationRectangle`. If your
  game relied on the old crop, the crop is now an explicit, bounded decision: enable the
  overscan step (`PixelPerfect with { AllowOverscan = true, OverscanTolerance = 0.25f }` reaches
  1× from a fit down to 0.8) and set the tolerance to the frame edge you are willing to lose.

- **`RenderLayer.Sampler` is a `SamplerPolicy` (`Auto` / `Point` / `Linear`), not a
  `Func<ViewportManager, SamplerState>`**, and `RenderLayer.Overlay`'s optional
  `SamplerState?` parameter is a `SamplerPolicy` (default `Auto`). `FinalDrawSystem`
  resolves it per layer against that layer's own destination-over-target scale.
  *Migration:* `RenderLayer.Overlay(target, bounds, SamplerState.LinearClamp)` becomes
  `RenderLayer.Overlay(target, bounds, SamplerPolicy.Linear)`; a custom layer's
  `_ => SamplerState.PointClamp` becomes `SamplerPolicy.Point`.

- **`MonoDreams.Examples`' `GameSettings.ScalingMode` (string) is now
  `GameSettings.Presentation`**, taking `Default` (the shipped value) / `Crisp` /
  `PixelPerfect` / `Stretch`.
  *Migration:* rename the key in your `settings.json`; `"KeepAspectRatio"` becomes
  `"Stretch"` and `"Smooth"` is no longer a thing (the sampler policy covers it).

### Added — the presentation knobs ([#89](https://github.com/roo-oliv/monodreams/issues/89))

- `PresentationPolicy` (+ `PresentationMode`, `CleanScaleSteps`, `SamplerPolicy`) — the
  policy record, its four presets (`Stretch`, `Default`, `Crisp`, `PixelPerfect`), the
  clean-scale ladder (`CleanScaleAtOrBelow` / `CleanScaleAtOrAbove` / `IsClean`), and
  `Resolve(window, render)`, which is pure math and unit-testable on its own.
- `PresentationPolicy.ResolveRenderSize(designW, designH, windowW, windowH)` — the other end
  of the overscan dial: the render resolution at which the clean present fills the window
  with NO crop, so the tolerance buys extra world instead of lost frame edges. Call it at
  boot, before the screens allocate their render targets.
- `ViewportManager.Presentation` / `PresentScale` — which step won, and screen pixels per
  render pixel in the present pass (distinct from `RenderScale`, which is authoring →
  render). The manager also logs one line per presentation change.

### Breaking — authoring space and render space are distinct ([#88](https://github.com/roo-oliv/monodreams/issues/88))

`ViewportManager` now owns **two** resolutions: the RENDER (virtual) resolution — the
pixel size of the per-pass render targets and of the back buffer — and the AUTHORING
(layout) resolution every game number is written in (entity/UI coordinates, HUD and
overlay boxes, `Camera.Zoom`, culling extents, the mapped mouse point). `RenderScale`
is the single ratio between them and is applied in exactly one place: the per-pass
cameras `ViewportManager` hands out. The two spaces **default to being equal**
(`RenderScale == 1`), so the model is inert — no pixel, no coordinate and no test moves
until a game passes a layout size that differs from its virtual one. What does move is
naming: five public shapes now say which space they mean.

- **`ViewportManager.ScaleMouseToVirtualCoordinates(Vector2)` is now
  `ViewportManager.MapMouse(Vector2)`**, and its result is a point of AUTHORING space
  (`(0,0)` to `LayoutWidth`×`LayoutHeight`), not of virtual space — the same numbers in a
  single-space game. `null` still means "the pointer is outside the aspect-fit viewport".
  *Migration:* `viewport.ScaleMouseToVirtualCoordinates(mouse)` becomes
  `viewport.MapMouse(mouse)`; keep feeding the result to `Camera.VirtualScreenToWorld`,
  which now takes an authoring-space point.

- **`ViewportManager.SetVirtualResolution(int width, int height)` is now
  `SetResolution(int virtualWidth, int virtualHeight, int layoutWidth = 0, int
  layoutHeight = 0)`** — exactly the constructor's arguments, under the same convention:
  a layout dimension of `0` means "same as the render dimension". Both entry points now
  validate, where the old setter accepted anything: a non-positive render dimension or a
  negative layout dimension throws `ArgumentOutOfRangeException`, and a layout/render
  aspect-ratio mismatch throws `ArgumentException`.
  *Migration:* `viewport.SetVirtualResolution(1920, 1080)` becomes
  `viewport.SetResolution(1920, 1080)`; add the layout pair only to opt into two spaces.

- **`AutoLayoutBuilder.VirtualWidth` / `.VirtualHeight` are now `.LayoutWidth` /
  `.LayoutHeight`** — UI is laid out in authoring units, so `AutoLayoutSystem` sizes its
  screen root (and its anchor offsets) from `ViewportManager.LayoutWidth`/`LayoutHeight`.
  *Migration:* `builder.VirtualWidth` becomes `builder.LayoutWidth` (same value in a
  single-space game).

- **`CameraNav.FitZoom`, `CameraEntityGlyph.FrustumWorldCorners` (both `level-editor`)
  and the `DialogueSystem` constructor renamed their `virtualWidth`/`virtualHeight`
  parameters to `layoutWidth`/`layoutHeight`.** All three consume AUTHORING extents — a
  fit-zoom, a frustum outline and a balloon's box are authoring-space numbers; the render
  scale is the camera's business and never enters them. Positional calls are unaffected;
  named arguments break.
  *Migration:* `new DialogueSystem(…, virtualWidth: w, virtualHeight: h, …)` becomes
  `new DialogueSystem(…, layoutWidth: w, layoutHeight: h, …)`, and a call site feeding a
  camera passes `camera.LayoutWidth`/`camera.LayoutHeight` instead of
  `camera.VirtualWidth`/`camera.VirtualHeight`.

### Added — the two-space knobs ([#88](https://github.com/roo-oliv/monodreams/issues/88))

- `ViewportManager.LayoutWidth` / `LayoutHeight` / `RenderScale` — the authoring size and
  the render-pixels-per-authoring-unit ratio (`1` in a single-space game). Sizing a render
  target that must hold a layout-sized region is the one legitimate hand use of the ratio.
- `ViewportManager.CreateCamera()` / `LayoutCamera` / `CreateLayoutCamera(w, h)` — the one
  place the scale is applied. World passes take `CreateCamera()`; screen-space passes (UI,
  HUD, Scroll) take `LayoutCamera` **instead of a `null` camera**; a pass whose destination
  is a sub-target takes `CreateLayoutCamera(...)`. At `RenderScale` 1 `LayoutCamera`'s view
  matrix is exactly `Matrix.Identity`, so adopting it changes no pixel.
- `Camera(int virtualWidth, int virtualHeight, float renderScale = 1f)` plus
  `Camera.RenderScale` / `LayoutWidth` / `LayoutHeight` — the view size and view matrix now
  scale by `Zoom × RenderScale`. Build cameras through `ViewportManager` rather than
  `new Camera(...)` so the scale keeps living in one place.
- `GameSettings.LayoutWidth` / `LayoutHeight` (Examples) — `0` (the shipped default) means
  "same as the render resolution", i.e. the single-space game.
- `MONODREAMS_RENDER_SCALE` (Demos) — an opt-in render-resolution multiplier over the fixed
  1280×720 authoring canvas (`1.5` ⇒ a 1920×1080 render space, same demo coordinates).

### Added — `ui` exclusive panel groups ([#96](https://github.com/roo-oliv/monodreams/issues/96))

- `PanelGroupComponent` — a group of mutually exclusive panels (tab bodies, settings
  pages, wizard steps, an inventory/map switcher): the member root entities, the active
  index, and the park offset. `Active = PanelGroupComponent.None` (`-1`) means "no member
  active" — a closed menu, first class rather than a special case.
- `PanelGroupSystem` — parks every inactive member off-screen (alive, laid out, measured)
  and restores the active one to the exact position it left, gating the focusables under
  each panel: groups nest, so a control is navigable only when *every* panel above it is
  active. Register it after everything that writes a member's position (notably
  `AutoLayoutSystem`) and before `HierarchySystem`. Game code only ever writes `Active`.
  This is the sanctioned implementation of the module's **park, don't hide** premise —
  hiding a panel with `VisibleComponent` is a no-op on the UI/HUD/Scroll targets and
  un-preps it on Main. The ui demo's new **Panels** tab shows a tab bar and a paged
  settings menu built on the one component.

### Breaking — `level-loading` no longer depends on LDtk ([#54](https://github.com/roo-oliv/monodreams/issues/54))

`level-loading` is now format-agnostic: no LDtk type appears in its source, and the
dependency arrow points **level-ldtk → level-loading, never the reverse**. A game that
doesn't author levels in LDtk no longer compiles against `LDtkMonogame` or ships its
packages. Three public shapes changed.

- **`EntitySpawnRequest` lost its `LayerInstance Layer` member** (and the matching
  constructor parameter). Layer-derived data now rides `CustomFields` under the
  `ldtk:`-prefixed keys of `level-ldtk`'s `LDtkSpawnFields`.
  *Migration:* a `request.Layer._Opacity` read becomes
  `request.CustomFields[LDtkSpawnFields.LayerOpacity]` (float, default `1f`); a
  `request.Layer._GridSize` read becomes
  `request.CustomFields[LDtkSpawnFields.GridSize]` (int, default `16`). Read through
  `TryGetValue` + a type check + that default — a code-driven spawn (the lightweight
  `EntitySpawnRequest(identifier, position)` ctor, the `prefab:` channel) carries no
  `ldtk:` keys at all.

- **`LevelLoadRequestSystem` is native-only, unconditionally.** Its `ContentManager`
  parameter, the `enableLegacyLdtkFallback` flag, and the ~85-line LDtk
  `Content.Load<LDtkLevel>` fallback are gone; an unknown level id now fails loud
  (`Logger.Error`) with no silent fallback.
  *Migration:*
  `new LevelLoadRequestSystem(world, content, probe, enableLegacyLdtkFallback: false)`
  becomes `new LevelLoadRequestSystem(world, probe)`; the import op composes
  `level-ldtk`'s new `LDtkLevelLoadSystem(world, content)` **instead of** this system.

- **`CurrentLevelComponent` holds a string, not an `LDtkLevel`.** It stays the
  world-scoped marker for "the current level"; the LDtk payload moved into the module
  that reads it.
  *Migration:* `CurrentLevelComponent.LevelData` (`LDtkLevel`) becomes
  `CurrentLevelComponent.LevelIdentifier` (`string`); the LDtk payload lives on
  `level-ldtk`'s new `LDtkLevelDataComponent`, which is also what both LDtk parsers now
  subscribe to (previously `CurrentLevelComponent`).

### Added — `level-ldtk` owns the whole LDtk path ([#54](https://github.com/roo-oliv/monodreams/issues/54))

- `LDtkLevelDataComponent` — the module's own world singleton carrying the full
  `LDtkLevel`; the parsers' add-trigger.
- `LDtkLevelLoadSystem` — the import-path `LoadLevelRequest` handler: loads
  `World/<id>` and sets `LDtkLevelDataComponent` + `CurrentLevelComponent(id)` +
  `CurrentBackgroundColorComponent`.
- `LDtkSpawnFields` — the `ldtk:layerOpacity` / `ldtk:gridSize` `CustomFields` keys
  that replace `request.Layer` (LDtk field identifiers cannot contain `':'`, so they
  never collide with a designer's own fields).
