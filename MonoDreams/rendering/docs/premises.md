# rendering — premises

> Technical invariants the engine assumes about the rendering pipeline:
> `DrawComponent`, `SpriteInfoComponent`, `VisibleComponent`, the
> `Camera` class, render targets, `CullingSystem`, `SpritePrepSystem`,
> `YSortSystem`, `MasterRenderSystem`, `FinalDrawSystem`, plus the mesh
> primitives (`IMeshGenerator`, `MeshData`, `MeshPrepSystem`). Read this
> before changing any of those pieces or any system that produces draw
> data downstream.

## `DrawComponent.Type` is mutually exclusive

A `DrawComponent`'s `Type` is one of `Sprite`, `Text`, `NinePatch`, or
`Mesh`. One render component per renderable entity; an entity that needs
two visual types needs two entities (or a new `DrawElementType` that
composes both).

**Why:** `MasterRenderSystem` switches batch context based on `Type`
(`Sprite`/`Text`/`NinePatch` go through `SpriteBatch`, `Mesh` goes
through `BasicEffect` triangles). Allowing a single `DrawComponent` to
span two types would force every render path through a per-call branch.
**Breaks:** game code that sets a `DrawComponent` to one type and then
expects sprite + text behavior renders only one of them, with no
diagnostic.
**Tests:** none yet.
**Depends on:** —

## `DrawComponent` is the only render component

Do not create new draw or render components. Adding a new visual type
extends `DrawElementType` and `MasterRenderSystem`; it does not fork the
pipeline.

**Why:** a parallel render component would also need a parallel render
system, parallel culling treatment, and parallel layer-depth handling —
the framework's value comes from there being exactly one path through
the pipeline.
**Breaks:** the parallel system either skips entities the main path
expected (silent invisibility) or competes with it (z-fighting,
double-rendering).
**Tests:** none yet.
**Depends on:** —

## `MasterRenderSystem` is the sole render *implementation*

All `SpriteBatch` / `BasicEffect` draw calls happen inside
`MasterRenderSystem` (back-buffer composition is `FinalDrawSystem`'s job).
Game code must not call `SpriteBatch` directly and must not write a second
render system. This is about a single *implementation*, not a single
*instance*: a screen registers as many `MasterRenderSystem` instances as it
has views (see "One `MasterRenderSystem` instance is one render pass").

**Why:** centralizing the draw call lets one well-understood class own the
render-target switch, the batch settings, the sort, and the layer-depth
contract. A bespoke second renderer fragments all four; N instances of the
*same* renderer do not.
**Breaks:** a parallel hand-rolled renderer fights over the active
`SpriteBatch`, producing flickering frames, dropped draws, or driver-level
errors, and bypasses the sort/layer-depth contract.
**Tests:** none yet.
**Depends on:** —

## One `MasterRenderSystem` instance is one render pass

A `MasterRenderSystem` instance renders exactly one pass: every entity
whose `DrawComponent.Target` equals its `source` id, through its optional
`camera` (null ⇒ screen-space, identity transform), into its `destination`
render target, which it clears to transparent first. The `source == Main`
pass additionally filters on `VisibleComponent` (culling); screen-space
passes always render. Multiple cameras, minimaps, splitscreen, CCTV, and
portal textures are all just additional instances — a minimap is a second
instance with `source = Main`, a second camera, and its own target.
`FinalDrawSystem` then composites the targets onto the screen.

**Why:** decoupling *what* to draw (`source`), *how* (`camera`), and
*where* (`destination`) makes a second view a second instance, not a new
code path — the framework's general-over-specialized rule. It also removes
the old "the camera only applies to the Main-keyed target" coupling.
**Breaks:** two instances writing the *same* destination clear each
other's work (each clears on entry) — give every pass its own target and
let `FinalDrawSystem` overlap them. Expecting a non-Main source to honor
culling: only `source == Main` consults `VisibleComponent`.
**Tests:** none yet (the multi-pass + minimap path is exercised by the
camera demo, `MonoDreams/camera/demo/CameraDemoScreen.cs`).
**Depends on:** "A render pass's camera virtual resolution matches its
destination".

## A render pass's camera virtual resolution matches its destination

A `MasterRenderSystem` pass derives its mesh `BasicEffect` projection from
the *destination* render target's pixel size
(`CreateOrthographicOffCenter(0, dest.Width, dest.Height, 0, …)`), and the
camera's view transform centers the view at the camera's virtual
resolution. For the two to agree, a pass's `camera.VirtualWidth/Height`
must equal its destination render target's size. The minimap obeys this by
giving its second camera the same virtual resolution as the main camera and
a full-virtual-resolution target, then letting `FinalDrawSystem` shrink
that target into the on-screen box.

**Why:** projection maps destination pixels → NDC; the camera centers world
content at `(VirtualWidth/2, VirtualHeight/2)`. A mismatch puts the centered
content somewhere other than the target's middle and scales meshes wrongly.
Deriving projection from the destination (not the camera) is what lets
screen-space passes carry no camera at all.
**Breaks:** a camera whose virtual resolution differs from its destination
renders off-center and mis-scaled in that pass.
**Tests:** none yet.
**Depends on:** "`Camera.VirtualResolution` is immutable".

## `FinalDrawSystem` composites an explicit, ordered layer list

`FinalDrawSystem` takes an ordered `RenderLayer` list and draws each
target onto the back buffer in order (later = on top). `RenderLayer.Main`
/ `UI` / `HUD` are factories for the standard full-frame layers — all three
draw to the aspect-fit `ViewportManager.DestinationRectangle` (with their
own samplers), so they share one letterboxed viewport and never stretch or
spill into the bars; `RenderLayer.Overlay` places a target in a
sub-rectangle given in HUD virtual coordinates, mapped into that same
`DestinationRectangle` — so an overlay aligns with HUD chrome drawn at
those coordinates; `RenderLayer.Native` composites a provider-resolved
target 1:1 over the whole window (native resolution, no aspect-fit — the
editor shell's chrome layer), skipping the layer when the provider returns
null (how chrome contributes nothing outside Edit). The screen owns the
list, so it decides which targets exist, their order, and where each lands.

**Why:** the compositor is the natural seam for screen layout — overlays
(minimap, CCTV), and eventually tiled splitscreen — without touching the
renderer. Driving it from an explicit list (rather than a hardcoded
Main→UI→HUD sequence) makes those layouts data, not code forks.
**Breaks:** omitting a layer silently drops that target from the screen
(its render pass still ran and cleared its target — wasted work, blank
result). An overlay rect given in screen pixels instead of HUD virtual
coords misaligns with HUD chrome under non-1:1 scaling.
**Tests:** none yet (exercised by every demo/example screen and the minimap
overlay in the camera demo).
**Depends on:** —

## The HUD layer is aspect-fit, not screen-stretched (cursor depends on it)

The HUD render layer is composited to the aspect-fit
`ViewportManager.DestinationRectangle`, exactly like Main and UI — never
stretched to the raw back-buffer rectangle `(0,0,ScreenWidth,ScreenHeight)`.
HUD-space content is authored in virtual coordinates, and the cursor (a
`RenderTargetID.HUD` entity) is positioned by `CursorPositionSystem` via
`ViewportManager.ScaleMouseToVirtualCoordinates`, which inverts the aspect-fit
transform. The layer must be drawn back through that *same* transform, or the
cursor's render and its position math disagree.

**Why:** when the screen aspect ratio differs from the virtual one (any
letter/pillarboxed window — common on web, where the back buffer is the whole
browser canvas), stretching HUD to the full back buffer scales it
non-uniformly (square keycaps render as rectangles) and, because the cursor's
*position* is still computed in the aspect-fit space, the rendered cursor
drifts from the system pointer — they coincide only at the screen centre and
separate toward the edges by the letterbox amount.
**Breaks:** giving the HUD layer (or `Overlay`'s `MapVirtualToScreen`) the
`(0,0,ScreenWidth,ScreenHeight)` destination instead of `DestinationRectangle`
reintroduces the stretch + cursor-drift. At a matching aspect ratio the two
rectangles are equal, so the bug is invisible at 16:9 and only appears once the
window aspect diverges — test/observe at a non-virtual aspect ratio.
**Tests:** none yet (desktop headless renders at the virtual 16:9 resolution,
where the rectangles coincide; observed on the web heads at arbitrary window
aspects).
**Depends on:** —

## Renderable entity stack on the Main target

An entity is rendered to the Main target only if it has
`TransformComponent + SpriteInfoComponent (or text/mesh equivalent) +
DrawComponent + VisibleComponent`. Missing `VisibleComponent` means
`MasterRenderSystem` skips silently (it includes `.With<VisibleComponent>()`
in the Main-target query).

**Why:** `VisibleComponent` is the culling output. `CullingSystem` adds
it for entities inside the camera frustum and removes it for entities
outside, letting `MasterRenderSystem` skip a frustum check per entity.
Splitting it from `DrawComponent` is a wart — see Aspirational.
**Breaks:** the canonical missed-tag bug — a dev creates an entity with
`SpriteInfoComponent + DrawComponent` and stares at an invisible result
with no error. Forgetting to register `CullingSystem` in a new screen
makes everything Main-target invisible.
**Tests:** none yet. *This is the specific bug from the bootstrap
interview that motivated writing premises files in the first place.*
**Depends on:** "`VisibleComponent` is owned exclusively by `CullingSystem`".

## `VisibleComponent` is owned exclusively by `CullingSystem`

Game code must not add or remove `VisibleComponent` on Main-target
entities. It is managed automatically by `CullingSystem` based on the
camera's `VirtualScreenBounds`. UI and HUD entities may set
`VisibleComponent` themselves as a one-shot tag, since those targets
ignore culling (see "Three render targets, two behaviors").

**Why:** `VisibleComponent` is a derived state on the Main target. Manual
mutation creates ghost entities (visible but outside the frustum, wasting
draw calls) or phantom-invisible entities (inside the frustum but
skipped). `CullingSystem` will overwrite the game-set value next frame
anyway.
**Breaks:** entities flicker in and out as `CullingSystem` overwrites
game-set values frame to frame; or the cull check fights game code
forever.
**Tests:** none yet.
**Depends on:** —

## Three render targets, two behaviors

`RenderTargetID.Main` is camera-transformed and respects culling.
`RenderTargetID.UI` and `RenderTargetID.HUD` are screen-space and always
render. Only Main consults `VisibleComponent`. The later additions follow
the screen-space behavior: `RenderTargetID.Scroll` (virtual-space overlay,
see its own premise) and `RenderTargetID.Editor` (the editor shell's
chrome at **native window resolution**, composited 1:1 by
`RenderLayer.Native` — entities on it lay out in physical screen pixels,
never virtual coordinates).

**Why:** UI and HUD are always-on-screen by definition. Culling them
would mean checking against the *screen* frustum, which is a degenerate
case (everything is inside). `MasterRenderSystem` enforces this by only
adding `.With<VisibleComponent>()` to the Main-target query.
**Breaks:** putting a Main-target entity on UI/HUD by mistake skips
camera transforms — the entity renders at its world coordinates,
unscaled by zoom. The reverse (UI on Main) gets culled away when the
camera moves. Authoring Editor-target content in virtual coordinates
renders it at the wrong scale/position (the Editor layer is never
aspect-fit).
**Tests:** none yet.
**Depends on:** —

## The viewport inset moves compositing and mouse mapping together

`ViewportManager.SetViewportInset(left, top, right, bottom)` reserves chrome
margins (the editor shell) around the game viewport: the aspect-fit
`DestinationRectangle` — and the pixel-perfect rectangle — are computed
inside the remaining centered sub-rectangle, and
`ScaleMouseToVirtualCoordinates` inverts that **same** rectangle. Because the
`ViewportManager` is the single source of truth, the final-draw compositing
and the cursor's virtual/world mapping can never disagree: a click inside the
inset viewport maps to the correct virtual point with no extra math, and a
click in the margins maps to `null` (`CursorPositionSystem` then flags
`CursorInputComponent.OutsideViewport`; chrome consumes the click in screen
space). An all-zero inset (the default, and `ClearViewportInset`) is
**byte-identical** to the historical full-window letterbox, so every screen
that never sets an inset is untouched. `IntegerScale` and
`PixelPerfectDestinationRectangle` recalculate lazily like
`DestinationRectangle` (a read after a resize/inset change is never stale) —
and `Recalculate` must assign their backing fields directly (reading the lazy
properties inside it recurses).

**Why:** the Wave-7 editor shell renders the game scaled-down in the center
with chrome around it; splitting the inset across two owners (compositor vs
mouse mapping) would desync every world pick by the margin offsets — the
exact class of bug the aspect-fit HUD premise exists for.
**Breaks:** an inset applied to `DestinationRectangle` but not to the mouse
inverse shifts all picking by (left, top); a non-restored inset (screen swap
without cleanup) letterboxes the next screen into a corner; a stale
`PixelPerfectDestinationRectangle` composites the Main layer one
resize/inset behind.
**Tests:** `MonoDreams.Tests/Rendering/ViewportInsetTests.cs` (zero-inset =
legacy rect + legacy mouse mapping; set+clear restores; inset rect centered
and aspect-correct in the available area; mouse maps inside / nulls in the
margins; resize recomputes; pixel-perfect uses the available area; negative
margins throw; oversized margins clamp).
**Depends on:** "The HUD layer is aspect-fit, not screen-stretched (cursor
depends on it)"; level-editor — "The editor shell insets the game viewport
and renders its chrome at native resolution".

## `MasterRenderSystem` samples per draw type: sprites/meshes PointClamp, text LinearClamp

`MasterRenderSystem` opens a separate `SpriteBatch` for sprites and for text so each gets
its own `SamplerState`. Sprites (and the mesh path) default to `PointClamp` — crisp
nearest-neighbour scaling for pixel art; text defaults to `LinearClamp` — smooth filtering
for bitmap fonts drawn at fractional scale. Both defaults are overridable via the
`spriteSampler` / `textSampler` constructor parameters. Because Sprite and Text are now
distinct batch types, the interleaved renderer flushes + reopens the batch when switching
between them; painter's order is preserved because the draw list is already depth-sorted.

**Why:** a single global `LinearClamp` blurred up-scaled pixel sprites (e.g. a 16×16
indicator drawn at 48×48); a single global `PointClamp` aliases down-scaled bitmap text.
Per-type samplers give each its correct filtering. The mesh path already forced `PointClamp`
(`ResetGraphicsStateForMeshRendering`), so sprites now match it.
**Breaks:** collapsing the Sprite/Text batch split (one sampler for both) forces a single
filter and reintroduces either blurry sprites or aliased text. Passing a non-clamp address
mode would wrap/tile sprites at their edges.
**Tests:** none yet.
**Depends on:** —

## Sprite runs flush below the Reach 16-bit-index budget

`MasterRenderSystem` caps the number of sprite quads submitted between a single
`SpriteBatch.Begin` and its matching `End`. When a contiguous sprite/text run
would push the running quad count past `SpriteBatchFlush.MaxSpritesPerBatch`
(a constant strictly below 5461), the renderer flushes (`End` + `Begin`, with
the same sampler) and resets the count before drawing the next element. Text
elements count one quad per glyph (plus one underline bar per line); a sprite or
pre-expanded nine-patch counts one. The cap is applied on **every** graphics
profile — the renderer contains no `GraphicsProfile` literal or `#if`.

**Why:** MonoGame's / KNI's `SpriteBatch` packs 4 vertices + 6 indices per
sprite; once one batch exceeds 5461 sprites it grows to 32-bit indices, which
the Reach profile (WebGL ES2 / BlazorGL) rejects with `Reach profile does not
support 32 bit indices`. A dense LDtk tile world exceeds 5461 on-screen sprites
even after culling, so without the split it paints on desktop (HiDef) but throws
on web. Splitting unconditionally keeps the engine source platform-agnostic
("the platform is selected by the head") — the head picks Reach vs HiDef; the
renderer never asks.
**Breaks:** removing the cap, raising it to/past 5461, or counting glyph-heavy
text as one quad lets a dense run cross into 32-bit indices and crash a web
build; capping too low pointlessly multiplies draw calls.
**Residual limitation (one draw call cannot be split):** the split happens
*between* draw elements, never *within* one. A single `DrawComponent` whose own
`EstimateSpriteQuads` already exceeds the 5461 hard limit — e.g. a >5461-glyph
text block on one line — is still submitted in one `SpriteBatch.Draw`/`DrawString`,
so it alone crosses into 32-bit indices and throws on Reach. The cap's headroom
(4096→5461) absorbs the conservative text over-estimate of a *normal* element,
not a pathologically huge one. Splitting one `DrawString` is the framework's job,
not the renderer's; if a game legitimately needs a single >5461-glyph run on web,
`TextPrepSystem` (rendering-text) must break it into multiple text entities first.
This residue is out of the splitter's reach by construction.
**Tests:** `MonoDreams.Tests/Rendering/SpriteBatchFlushTests.cs` — asserts the
cap stays below the 32-bit-index hard limit, that a 20000-sprite run splits into
segments each within the limit with no quads lost, and that text is counted per
glyph. The tests drive the **same** `SpriteBatchFlush.BatchRun` accumulator that
`MasterRenderSystem.RenderInterleaved` uses for its per-element flush decision, so
a regression inside the renderer's loop (dropping the per-`Begin` reset or skipping
the flush check) is reflected in the unit test, not only in an on-device web run.
The desktop demo headless tests
(`MonoDreams.Tests/IntegrationTests/HeadlessDemoTests.cs`) confirm the split is
visually transparent on HiDef.
**Depends on:** foundation — "The platform … is selected by the head project,
never by engine source".

## Mesh indices render through 16-bit indices (Reach-safe)

`MasterRenderSystem.DrawSingleMesh` draws a mesh through the **`short[]`** overload of
`GraphicsDevice.DrawUserIndexedPrimitives`, using the 16-bit index array
`DrawComponent.Get16BitIndices()` returns. Meshes are authored with `int[]` indices
(`MeshData.Indices`, every `IMeshGenerator`), so `Get16BitIndices()` converts once and caches
the result, rebuilding only when `Indices` is reassigned (reference identity changes). It
returns `null` only when the mesh has more vertices than a 16-bit index can address (more than
65536); the renderer then falls back to the 32-bit `int[]` overload, which is valid only on
HiDef. As with the sprite-run flush, the renderer contains no `GraphicsProfile` literal — the
16-bit path is taken on every profile.

**Why:** the overload's index-array type selects the GPU index width (`int[]` ⇒ 32-bit,
`short[]` ⇒ 16-bit), and the Reach profile (WebGL ES2 / BlazorGL) rejects 32-bit indices with
`Reach profile does not support 32 bit indices`. The player's orb is a `CircleMeshGenerator`
mesh; before this, its `int[]` indices took the 32-bit overload and threw on the first web
render tick (it painted on desktop, where HiDef accepts 32-bit). This is the mesh analog of the
sprite-run flush — both keep mesh/sprite submissions inside the 16-bit budget so the engine
source stays platform-agnostic.
**Breaks:** rendering meshes through the `int[]` overload again (e.g. passing `dc.Indices`
directly) reintroduces the Reach crash for every mesh-backed entity — orbs, the demo UI
buttons/checkboxes, physics-demo circles. Converting per frame instead of caching reintroduces
a per-frame allocation that fails the headless heap-flat assertion.
**Caching note:** the cache keys off the `Indices` array reference, not `SetMeshData` — factories
that set `DrawComponent.Indices` directly (e.g. `PlayerEntityFactory`) still get a correct,
rebuilt-on-change conversion.
**Tests:** `MonoDreams.Tests/Rendering/MeshIndexConversionTests.cs` — asserts the short values
match the int indices, that the conversion is cached across calls and rebuilt on reassignment,
that values above 32767 round-trip as unsigned 16-bit bit patterns, and that a mesh past the
16-bit vertex ceiling returns `null` (32-bit fallback). The physics/UI demo headless tests
exercise the mesh path on HiDef; the in-browser physics demo confirms it on Reach.
**Depends on:** foundation — "The platform … is selected by the head project,
never by engine source".

## The draw set is built once per instance, not per frame

A `MasterRenderSystem` instance builds its `EntitySet` (for its `source`)
lazily once and reuses it for the instance's lifetime; it is disposed in
`Dispose`. It must never call `world.GetEntities()...AsSet()` inside the
per-frame `Update`.

**Why:** DefaultEcs's `AsSet()` registers component/entity lifecycle
subscriptions on the `World`, and the World holds those subscription
delegates — which reference the `EntitySet` — alive forever. A set
created per frame is therefore never garbage-collected. An earlier version
built a fresh set every frame, leaking `EntitySet`s every frame, each
retaining its matched-entity array. This is the standing ECS-wide rule
(every other `AsSet()` in the engine is cached in a field) applied to the
renderer. Note that composing N render passes means N cached sets — fine,
because each is built once; two passes with the same `source` (e.g. the
main world pass and a minimap pass) hold two equivalent sets, a negligible
constant cost, not a per-frame leak.
**Breaks:** process memory climbs steadily even on a fully static scene
(no entities or assets added), as observed in the camera and physics
demos. Eventually OOM on a long-running session.
**Tests:** `MonoDreams.Tests/IntegrationTests/HeadlessDemoTests.cs`
asserts a flat live heap across 600 frames of the camera demo (now four
render passes).
**Depends on:** —

## Rendering systems run last in the pipeline

In any screen's pipeline assembly, the prep / cull / sort / render stage
goes at the tail. Logic that mutates renderable state (positions, sprite
source rects, text contents, layer depths) must complete before the prep
module reads it.

**Why:** the prep stage freezes the state of `DrawComponent`s into the
draw queue; mutations after the queue is built are silently lost until
next frame.
**Breaks:** a game system that updates text mid-render-module sees the
old text on screen. A movement system after `CullingSystem` updates
positions without re-culling — entities pop in or out by one frame.
**Tests:** none yet.
**Depends on:** —

## Layer-depth ownership pipeline

`SpritePrepSystem` initializes `DrawComponent.LayerDepth` from
`SpriteInfoComponent.LayerDepth`. `YSortSystem` may override it for
entities on Y-sorted layers. `MasterRenderSystem` sorts on the final
value. Game systems writing `LayerDepth` after `YSortSystem` create
undefined sort behavior.

**Why:** three writers in a defined order produce one final value. A
fourth writer in between (or after) breaks the contract.
**Breaks:** entities flicker, depth-fight, or render behind/in-front of
static elements unexpectedly.
**Tests:** none yet.
**Depends on:** —

## Y-sort tiebreaker is parent-child bias only

`YSortSystem` uses a minimal epsilon (`1e-6f` in
`MonoDreams/rendering/System/Draw/YSortSystem.cs`) to bias children
relative to their parent's final depth, preserving group ordering.
Same-layer entities at the same world Y with no parent-child relationship
have no further tiebreaker — render order falls through to entity
insertion order.

**Why:** parent-child bias is the only ambiguity case the framework
treats explicitly; "true ties" are rare in practice (entities at the
exact same Y on the same layer).
**Breaks:** if true ties become common, flicker can appear when
insertion order changes (e.g., re-spawn). The fix is a tiebreaker —
possibly entity ID — but that's a framework change, not a workaround.
**Tests:** none yet.
**Depends on:** —

## `Camera.VirtualResolution` is immutable

`Camera.VirtualWidth` and `Camera.VirtualHeight` are readonly properties
set in the constructor (the `Camera` class lives at
`MonoDreams/rendering/Camera.cs` because it is a hard dependency of the
draw stack — `MasterRenderSystem` reads its position, zoom, and view
matrix every frame). Only zoom, position, and rotation are mutable on a
live `Camera`.

**Why:** virtual resolution defines the world-units-per-pixel ratio.
Changing it mid-frame would require recomputing every entity's on-screen
size and re-running culling.
**Breaks:** a system that tries to change resolution at runtime either
silently fails (readonly) or, if it bypasses, produces a fractional
frame where culling, layout, and rendering disagree.
**Tests:** none yet.
**Depends on:** —

## `IMeshGenerator.Generate()` returns a triangle list

All canonical generators (`CircleMeshGenerator`, `CircleOutlineMeshGenerator`,
`LineMeshGenerator`, `RectangleOutlineMeshGenerator`,
`RoundedRectangleOutlineMeshGenerator`,
`DashedRectangleOutlineMeshGenerator`, `FilledRectangleMeshGenerator`,
`FilledRoundedRectangleMeshGenerator`, `FilledTriangleMeshGenerator`,
`FilledPolygonMeshGenerator`, `PolygonOutlineMeshGenerator`,
`PolylineMeshGenerator`, `GradientPathMeshGenerator`,
`CompositeMeshGenerator`) return `MeshData`
whose indices describe a triangle list — every triple of indices is one
triangle. `MasterRenderSystem` invokes `DrawUserIndexedPrimitives` with
`PrimitiveType.TriangleList`. The filled triangle/polygon generators rely on
the mesh path's `CullNone` rasterizer state, so their winding order is free.

**Why:** triangle lists are the lowest common denominator for the
`BasicEffect` path. Triangle strips or fans would require per-shape
metadata about which primitive type to use, and would multiply the
render path's branching.
**Breaks:** a custom generator that emits a triangle strip without
setting `DrawComponent.PrimitiveType` correctly produces garbled
geometry — the renderer interprets the indices as a list and draws
random triangles between them.
**Tests:** none yet.
**Depends on:** —

## `MeshPrepSystem` writes the world matrix once per frame

`MeshPrepSystem` reads `TransformComponent.WorldMatrix` and writes it
into `DrawComponent.WorldMatrix` for every entity with `Type = Mesh`.
This must happen after `HierarchySystem` propagates dirty flags and
before `MasterRenderSystem` reads the matrix to bind it to
`BasicEffect.World`.

**Why:** meshes render in world space — their vertex positions are
the geometry's local-space layout, and the world matrix transforms
them to where the entity is. Reading `TransformComponent.WorldMatrix`
in `MasterRenderSystem` directly would couple the renderer to the
transform; staging through `DrawComponent` keeps the renderer
agnostic.
**Breaks:** running `MeshPrepSystem` before `HierarchySystem` produces
meshes positioned by last frame's parent transform (one-frame lag on
parented meshes). Running it after `MasterRenderSystem` is too late —
the renderer uses the previous frame's world matrix.
**Tests:** none yet.
**Depends on:** foundation — "`HierarchySystem` must run ahead of any
system reading WorldPosition"; "Rendering systems run last in the
pipeline" (above).

## `CompositeMeshGenerator` rebases indices into the combined buffer

When `CompositeMeshGenerator.Generate()` walks its sub-generators, it
adds each sub-mesh's vertices to a combined vertex list and adds
`indexOffset` to each sub-mesh's indices before appending to the
combined index list. A sub-generator returning indices that already
reference the combined buffer's range would draw the wrong triangles.

**Why:** composability is the design goal — a button outline + label
backdrop + glow can be one mesh entity. The rebase invariant is what
makes "just add another generator" work without per-generator
coordination.
**Breaks:** a custom generator that returns indices unrelated to its
own vertex buffer (e.g. via a stale cached array) corrupts the
composite's geometry. The bug manifests as visible triangles between
unrelated parts of the composite.
**Tests:** none yet.
**Depends on:** —

## The mesh render path uses premultiplied alpha — UI fills must be opaque

The mesh path (`DrawComponent.Type = Mesh`, rendered through `BasicEffect`
triangles in `MasterRenderSystem`) draws into render targets that composite
with **premultiplied alpha**. The vertex colors in `MeshData` are *not*
premultiplied by the generators, so a fill color with partial alpha
(`A` in `1..254`) renders far brighter than a designer expects — the
straight-alpha RGB is composited as if it were already premultiplied, so
a 50%-alpha mid-grey reads as near-white. UI mesh fills must therefore be
**opaque** (`A == 255`). Fully transparent (`A == 0`, `Color.Transparent`)
is also safe because the prep/draw path skips a zero-alpha fill entirely
(`ButtonMeshPrepSystem` produces a degenerate mesh; `ToggleSwitchSystem`
empties the mesh) — that's how a "no fill" button or an "off" checkbox is
expressed. Encode translucency by choosing a darker opaque color, not by
lowering alpha.

**Why:** the targets blend premultiplied; matching that would mean
premultiplying every vertex color in the generators or the prep systems,
which nothing does today. Restricting UI fills to opaque (or fully
transparent) sidesteps the brightness blow-up without a pipeline-wide color
transform. `ButtonTheme` / `ButtonVariantColors` pick opaque fills for this
reason, and `ButtonVisualSystem` keys the "no fill" state off `A == 0`.
**Breaks:** a button or panel given a partial-alpha fill (e.g. a "subtle"
`Color.Black * 0.3f`) renders as a glaring near-white block instead of a
faint tint — the canonical "why is my overlay white?" bug. Lowering alpha
to dim a fill makes it brighter, not dimmer.
**Tests:** none yet (exercised by every demo with mesh-backed UI — the
`ui` demo's buttons / panels, the camera and physics demos' checkboxes).
**Depends on:** "`IMeshGenerator.Generate()` returns a triangle list";
ui — "`ToggleSwitchComponent` drives a checkmark mesh's visibility from a
bool".

## Scrollable content uses a dedicated `RenderTargetID.Scroll` target composited via `RenderLayer.Overlay`

Scrollable UI regions render into their own `RenderTargetID.Scroll` render
target, composited onto the screen by a `RenderLayer.Overlay` entry in
`FinalDrawSystem`'s layer list. The overlay's HUD-virtual sub-rectangle is
the scroll viewport, so content drawn past the target's edges is clipped by
the target bounds (the overlay maps the whole target into the viewport box —
content outside the box's mapped region does not appear). Like UI and HUD,
the `Scroll` target is screen-space and always renders regardless of
`VisibleComponent`. For now there is **one** `Scroll` region per screen: a
screen registers a single `source = Scroll` `MasterRenderSystem` pass and a
single overlay layer for it.

**Why:** clipping a sub-region of scrolling content is exactly what a
separate render target plus an overlay sub-rect gives for free — the target
is the clip rect, the overlay is the placement. Reusing the existing
multi-pass + `RenderLayer.Overlay` machinery (the same path the minimap
uses) keeps scrolling as data, not a new code path. Bounding it to one
region per screen avoids inventing per-region target allocation before a
second region is actually needed.
**Breaks:** drawing scrollable content directly on UI/HUD with no dedicated
target gives no clip rect — content spills past the intended viewport.
Registering two `Scroll` regions today collides on the single target
(each pass clears it on entry, per "One `MasterRenderSystem` instance is one
render pass"), so the second region erases the first.
**Tests:** none yet (exercised by the `ui` demo's scroll region).
**Depends on:** "One `MasterRenderSystem` instance is one render pass";
"`FinalDrawSystem` composites an explicit, ordered layer list"; "Three
render targets, two behaviors".

## `RoundedRectangleOutlineMeshGenerator` draws a stroked rounded-rectangle border

`RoundedRectangleOutlineMeshGenerator` emits a triangle-list `MeshData` for
the *border* of a rounded rectangle (straight edges + quarter-circle corner
arcs of a given corner radius, stroked at a given line thickness) — the
hollow counterpart to `FilledRoundedRectangleMeshGenerator`'s solid fill. A
bordered rounded panel is therefore two sibling mesh entities sharing a
`TransformComponent`: a `FilledRoundedRectangleMeshGenerator` fill behind a
`RoundedRectangleOutlineMeshGenerator` stroke, exactly as a plain panel pairs
`FilledRectangleMeshGenerator` with `RectangleOutlineMeshGenerator`. Both the
fill and the outline obey the opaque-fill rule for the mesh path.

**Why:** rounded panels and rounded buttons want a crisp border distinct
from the fill, and (per `DrawComponent.Type` being mutually exclusive) one
mesh entity can carry either the fill *or* the outline, not both — so the
border is its own generator on its own entity. Matching the fill generator's
corner-radius parameterization lets the two line up pixel-for-pixel.
**Breaks:** mismatched corner radii between the fill and the outline leave
the stroke floating off the fill's rounded corners. Putting a partial-alpha
color on the stroke triggers the premultiplied-alpha brightness bug above.
**Tests:** none yet (exercised by the `ui` demo's rounded panels/buttons).
**Depends on:** "`IMeshGenerator.Generate()` returns a triangle list";
"`DrawComponent.Type` is mutually exclusive"; "The mesh render path uses
premultiplied alpha — UI fills must be opaque".

## Open questions

- **Same-Y flicker in practice** — has this surfaced? If so, the
  tiebreaker premise above needs to harden.
- **Mixed render targets per entity** — is it ever valid for the same
  entity to render on Main *and* HUD (via two separate `DrawComponent`s
  on different entities)? Probably always two entities, but not yet a
  settled convention.
- **`DrawElement` cache invalidation** — what happens if a prep system
  mutates `SpriteInfoComponent` after the draw queue was built?
- **Texturing meshes** — `MeshData` carries `VertexPositionColor`
  vertices; there's no `VertexPositionTexture` path. Whether to add one
  (and how it interacts with the SpriteBatch-based sprite path) is open.
- **Mesh culling extents** — meshes get `VisibleComponent` from
  `CullingSystem` based on `TransformComponent.WorldPosition` against
  the camera bounds. A mesh's actual extents (computed from its
  vertices) might lie outside the position's frustum, causing visible
  pop-out at edges. No bounding-box-from-vertices logic exists today.

## Aspirational direction

- `VisibleComponent` becomes a property of `DrawComponent` (removes the
  easy-to-miss tag).
- Multi-view (minimap / splitscreen / CCTV / portals) is now possible by
  composing `MasterRenderSystem` instances + `RenderLayer`s (the camera
  demo ships a minimap). What's still missing is **per-view culling**: a
  `source = Main` pass inherits the main camera's `VisibleComponent`, so a
  second camera can't yet cull to *its own* frustum. A per-camera cull set
  (or a cull-independent draw set option) is the next step.
- Render targets become more configurable — custom post-processing
  passes, shader effects.
- A `MeshTransformBatcher` that combines static meshes sharing a layer
  depth into a single submission, cutting `BasicEffect` draw calls.
- Per-vertex texture-coordinate support so meshes can sample sprite
  atlases (would let the dialogue indicator or UI nine-patches be a
  mesh rather than a `SpriteBatch` ninepatch).

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `DrawComponent.Type` is mutually exclusive
- `DrawComponent` is the only render component
- `MasterRenderSystem` is the sole render *implementation*
- One `MasterRenderSystem` instance is one render pass
- A render pass's camera virtual resolution matches its destination
- `FinalDrawSystem` composites an explicit, ordered layer list
- Renderable entity stack on the Main target
- `VisibleComponent` is owned exclusively by `CullingSystem`
- Three render targets, two behaviors
- `MasterRenderSystem` samples per draw type: sprites/meshes PointClamp, text LinearClamp
- Rendering systems run last in the pipeline
- Layer-depth ownership pipeline
- Y-sort tiebreaker is parent-child bias only
- `Camera.VirtualResolution` is immutable
- `IMeshGenerator.Generate()` returns a triangle list
- `MeshPrepSystem` writes the world matrix once per frame
- `CompositeMeshGenerator` rebases indices into the combined buffer
- The mesh render path uses premultiplied alpha — UI fills must be opaque
- Scrollable content uses a dedicated `RenderTargetID.Scroll` target composited via `RenderLayer.Overlay`
- `RoundedRectangleOutlineMeshGenerator` draws a stroked rounded-rectangle border

Architectural tests for ECS-purity premises (no parallel render systems,
no game `SpriteBatch` calls, `VisibleComponent` not added outside
`CullingSystem` on Main-target entities) are the highest-leverage
candidates here.
