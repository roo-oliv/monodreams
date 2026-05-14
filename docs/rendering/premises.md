# Rendering — premises

> Technical invariants the engine assumes about the rendering pipeline.
> Each entry: title, paragraph, **Why** / **Breaks** / **Tests** / **Depends on**.
> Aspirational items at the bottom (intended end-state, not yet enforced).

## `DrawComponent.Type` is mutually exclusive

A `DrawComponent`'s `Type` is one of `Sprite`, `Text`, `NinePatch`, or
`Mesh`. One render component per renderable entity; an entity that
needs two visual types needs two entities (or a new `DrawElementType`
that composes both).

**Why:** `MasterRenderSystem` switches batch context based on `Type`.
Allowing a single `DrawComponent` to span two types would force every
render path through a per-call branch.
**Breaks:** game code that sets a `DrawComponent` to one type and
then expects sprite + text behavior renders only one of them, with no
diagnostic.
**Tests:** none yet.
**Depends on:** —

## `DrawComponent` is the only render component

Do not create new draw or render components. Adding a new visual type
extends `DrawElementType` and `MasterRenderSystem`; it does not fork
the pipeline.

**Why:** a parallel render component would also need a parallel render
system, parallel culling treatment, and parallel layer-depth handling
— the framework's value comes from there being exactly one path
through the pipeline.
**Breaks:** the parallel system either skips entities the main path
expected (silent invisibility) or competes with it (z-fighting,
double-rendering).
**Tests:** none yet.
**Depends on:** —

## `MasterRenderSystem` is the sole renderer

All `SpriteBatch` calls happen inside `MasterRenderSystem`. Game code
must not call `SpriteBatch` directly, and no parallel render system
should exist.

**Why:** centralizing the draw call lets one place own the render-
target switches, the batch settings, the sort, and the layer-depth
contract. A second renderer fragments all four.
**Breaks:** parallel renderers fight over the active `SpriteBatch`,
producing flickering frames, dropped draws, or driver-level errors.
**Tests:** none yet.
**Depends on:** —

## Renderable entity stack on the Main target

An entity is rendered to the Main target only if it has
`EntityInfo + Transform + SpriteInfo + DrawComponent + Visible`.
Missing `Visible` means `MasterRenderSystem` skips silently.

**Why:** `Visible` is the culling output. `CullingSystem` adds it for
entities inside the camera frustum and removes it for entities
outside, letting `MasterRenderSystem` skip a frustum check per
entity. Splitting it from `DrawComponent` is a wart — see Aspirational.
**Breaks:** the canonical missed-tag bug — a dev creates an entity
with `SpriteInfo + DrawComponent` and stares at an invisible result
with no error. Forgetting to register `CullingSystem` in a new screen
makes everything Main-target invisible.
**Tests:** none yet. *This is the specific bug from the bootstrap
interview that motivated writing premises files in the first place.*
**Depends on:** "`Visible` is owned exclusively by `CullingSystem`".

## `Visible` is owned exclusively by `CullingSystem`

Game code must not add or remove the `Visible` component. It is
managed automatically by `CullingSystem` based on the camera's
`VirtualScreenBounds`.

**Why:** `Visible` is a derived state. Manual mutation creates ghost
entities (visible but outside the frustum, wasting draw calls) or
phantom-invisible entities (inside the frustum but skipped).
**Breaks:** entities flicker in and out as `CullingSystem` overwrites
game-set values frame to frame; or the cull check fights game code
forever.
**Tests:** none yet.
**Depends on:** —

## Three render targets, two behaviors

`RenderTargetID.Main` is camera-transformed and respects culling.
`RenderTargetID.UI` and `RenderTargetID.HUD` are screen-space and
always render. Only Main consults `Visible`.

**Why:** UI and HUD are always-on-screen by definition. Culling them
would mean checking against the *screen* frustum, which is a degenerate
case (everything is inside).
**Breaks:** putting a Main-target entity on UI/HUD by mistake skips
camera transforms — the entity renders at its world coordinates,
unscaled by zoom. The reverse (UI on Main) gets culled away when the
camera moves.
**Tests:** none yet.
**Depends on:** —

## Rendering systems run last in the pipeline

In any screen's pipeline assembly, the prep / cull / sort / render
block goes at the tail. Logic that mutates renderable state (positions,
sprite source rects, text contents, layer depths) must complete before
the prep block reads it.

**Why:** the prep block freezes the state of `DrawComponent`s into
the draw queue; mutations after the queue is built are silently lost
until next frame.
**Breaks:** a game system that updates text mid-render-block sees the
old text on screen. A movement system after `CullingSystem` updates
positions without re-culling — entities pop in or out by one frame.
**Tests:** none yet.
**Depends on:** —

## Layer-depth ownership pipeline

`SpritePrepSystem` initializes `LayerDepth` from the sprite's layer.
`YSortSystem` may override it for entities on Y-sorted layers.
`MasterRenderSystem` sorts on the final value. Game systems writing
`LayerDepth` after `YSortSystem` create undefined sort behavior.

**Why:** three writers in a defined order produce one final value.
A fourth writer in between (or after) breaks the contract.
**Breaks:** entities flicker, depth-fight, or render behind/in-front
of static elements unexpectedly.
**Tests:** none yet.
**Depends on:** —

## Y-sort tiebreaker is parent-child bias only

`YSortSystem` uses a minimal epsilon to bias children relative to
their parent's final depth, preserving group ordering. Same-layer
entities at the same world Y with no parent-child relationship have no
further tiebreaker — render order falls through to entity insertion
order.

**Why:** parent-child bias is the only ambiguity case the framework
treats explicitly; "true ties" are rare in practice (entities at the
exact same Y on the same layer).
**Breaks:** if true ties become common, flicker can appear when
insertion order changes (e.g., re-spawn). The fix is a tiebreaker —
possibly entity ID — but that's a framework change, not a workaround.
**Tests:** none yet.
**Depends on:** —

## `Camera.VirtualResolution` is immutable

`Camera.VirtualWidth` and `Camera.VirtualHeight` are readonly
properties set in the constructor. Only zoom, position, and rotation
are mutable on a live `Camera`.

**Why:** virtual resolution defines the world-units-per-pixel ratio.
Changing it mid-frame would require recomputing every entity's
on-screen size and re-running culling.
**Breaks:** a system that tries to change resolution at runtime either
silently fails (readonly) or, if it bypasses, produces a fractional
frame where culling, layout, and rendering disagree.
**Tests:** none yet.
**Depends on:** —

## `CameraFollowSystem` is optional, not required

Fixed-camera, local multiplayer (multiple cameras at once), and
CCTV-style setups are explicitly in scope. A screen owns whether and
how its camera updates; `CullingSystem` reads whatever the `Camera`
reports.

**Why:** the framework targets any 2D game, including ones with no
moving camera or multiple simultaneous cameras. Requiring
`CameraFollowSystem` would foreclose those.
**Breaks:** assuming `CameraFollowSystem` is always in the pipeline
leads code to read stale follow-target data when it isn't there.
**Tests:** none yet.
**Depends on:** —

## Open questions

- **Same-Y flicker in practice** — has this surfaced? If so, the
  tiebreaker premise above needs to harden.
- **Mixed render targets per entity** — is it ever valid for the same
  entity to render on Main *and* HUD (via two separate
  `DrawComponent`s on different entities)? Probably always two
  entities, but not yet a settled convention.
- **`DrawElement` cache invalidation** — what happens if a prep system
  mutates `SpriteInfo` after the draw queue was built?

## Aspirational direction

- `Visible` becomes a property of `DrawComponent` (removes the
  easy-to-miss tag).
- Render targets become more configurable — custom post-processing
  passes, shader effects, multiple Main targets for split-screen.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `DrawComponent.Type` is mutually exclusive
- `DrawComponent` is the only render component
- `MasterRenderSystem` is the sole renderer
- Renderable entity stack on the Main target
- `Visible` is owned exclusively by `CullingSystem`
- Three render targets, two behaviors
- Rendering systems run last in the pipeline
- Layer-depth ownership pipeline
- Y-sort tiebreaker is parent-child bias only
- `Camera.VirtualResolution` is immutable
- `CameraFollowSystem` is optional, not required

Architectural tests for ECS-purity premises (no parallel render
systems, no game `SpriteBatch` calls, `Visible` not added outside
`CullingSystem`) are the highest-leverage candidates here.
