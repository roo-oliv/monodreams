# Changelog

MonoDreams is in **alpha**: breaking changes land as clean breaks, with no
compatibility shims. Each entry below names the old shape, the new shape, and the
one-line edit that migrates a call site. Modules are source you own (shadcn-style),
so migrating is editing your own copy.

## Unreleased

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
