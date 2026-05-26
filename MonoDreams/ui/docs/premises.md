# ui — premises

> Technical invariants the engine assumes about the UI block:
> `LayoutNodeComponent`, `LayoutSlotComponent`, `UIElementComponent`,
> `SimpleButtonComponent`, the `AutoLayoutBuilder` / `ContainerBuilder` /
> `SlotBuilder` builder chain, `IntrinsicSizingSystem`, `AutoLayoutSystem`,
> `ButtonMeshPrepSystem`, and `LayoutDebugSystem`. Read this before
> changing any of those pieces or adding new layout primitives.

## `IntrinsicSizingSystem` runs before `AutoLayoutSystem`

`IntrinsicSizingSystem` reads each `LayoutSlotComponent`'s
`SizeMeasurer` callback, invokes it on the content entity, and writes
the result into the slot's `LayoutNodeComponent.Width` / `Height`.
`AutoLayoutSystem` then runs the flexbox solver using those measured
sizes. Reversing the order means the layout solver sees zero-sized
content.

**Why:** the layout solver needs intrinsic sizes (text width, sprite
size, button bounds) before it can position children. The split into
two systems is the cleanest way to express the dependency: one measures,
the next positions.
**Breaks:** running `AutoLayoutSystem` first produces a layout where
every slot has zero width/height — children stack at the top-left and
nothing is centered. The dev sees broken UI with no error.
**Tests:** none yet.
**Depends on:** —

## Intrinsic sizing is via callback, not via reading the content entity

`LayoutSlotComponent.SizeMeasurer` is a `Func<Entity, Vector2>` that
the slot's owner provides at construction time. `IntrinsicSizingSystem`
invokes it once per frame (when `NeedsRemeasure` is true), passes the
content entity, and writes the returned size into the layout node.
The layout system does **not** introspect the content entity's
components (e.g. reading `DynamicTextComponent.Font.MeasureString`)
itself.

**Why:** decoupling lets a slot measure any content — text, sprite,
nested layout, mesh — without the UI block knowing what those
components look like. A future block can add a new measurable type by
providing its own callback; nothing in `ui` needs to change.
**Breaks:** a slot without a `SizeMeasurer` keeps its initial size
(zero unless explicitly set), so its content collapses. The footgun
is real — `IntrinsicSizingSystem` early-exits silently when the
measurer is null.
**Tests:** none yet.
**Depends on:** —

## `AutoLayoutBuilder` is the canonical entry point

UI hierarchies are built via the fluent
`AutoLayoutBuilder → ContainerBuilder → SlotBuilder` chain.
`builder.CreateRoot(anchor)` returns a `ContainerBuilder` that emits a
root `LayoutSlotComponent` with `IsRoot = true`; `.AddSlot(...)` and
`.AddContainer(...)` add children; `.Build()` finalizes the tree and
creates the entities. Game code should not hand-roll
`LayoutSlotComponent` entities by setting components directly — the
builder is the contract.

**Why:** the builder pattern owns several non-obvious invariants:
parent-child wiring on the underlying `LayoutNodeComponent`, root vs
non-root flag setting, `NeedsRemeasure = true` priming, and attaching
the `TransformComponent` to each slot so `AutoLayoutSystem` can write
to it. Hand-rolling skips one of those and the layout silently breaks.
**Breaks:** missing `IsRoot = true` on a root slot means
`AutoLayoutSystem` never picks it up. Missing the `LayoutNodeComponent`
parent link means children render at world origin.
**Tests:** none yet.
**Depends on:** —

## `LayoutNodeComponent` is a pure C# tree, not an ECS hierarchy

The flexbox solver works on the `LayoutNodeComponent` tree (held by
each `LayoutSlotComponent`), which mirrors the slot entity tree but is
maintained separately. Slot entities also have `TransformComponent`
parents wired by the builder so the rendered children move with their
parents in world space. The two hierarchies are not the same and must
stay in sync — that sync is the builder's job at construction time.

**Why:** the solver needs random access to children, parent pointers,
and computed-size cache slots that don't fit on a struct component
queried by entity set. A pure-C# tree gives the solver O(n) traversal
without ECS overhead, while the parallel `TransformComponent.Parent`
chain keeps rendering consistent.
**Breaks:** mutating `LayoutNodeComponent.Children` directly without
updating the corresponding entity's `TransformComponent.Parent` (or
vice versa) desyncs the two trees: layout positions land in the right
slot, but rendering uses the wrong parent transform.
**Tests:** none yet.
**Depends on:** foundation — "`ChildOfComponent` and
`TransformComponent.Parent` are two intentional links".

## `ButtonMeshPrepSystem` bakes world coords and must run AFTER `MeshPrepSystem` whenever both are in the pipeline

`ButtonMeshPrepSystem` writes the four sides of the outline rectangle with
`transform.WorldPosition` already baked into the vertex positions, then sets
`DrawComponent.WorldMatrix = Matrix.Identity` so `MasterRenderSystem` uses the
vertices as-is. Other meshes (procedural shapes via `IMeshGenerator`) take
the opposite contract: their vertices are in local space and `MeshPrepSystem`
writes `transform.WorldMatrix` for the renderer to apply. When a screen runs
both — e.g. a demo screen with mesh art *and* clickable buttons — order the
draw pipeline as `... MeshPrepSystem -> ButtonMeshPrepSystem -> MasterRenderSystem`
so the button system's identity matrix overrides MeshPrepSystem's earlier
write.

**Why:** button outlines compose with the layout solver via
`SimpleButtonComponent.Size`, so it's natural to bake the final on-screen
quad once in world space. Procedural shapes go the other way: they're authored
at the origin and reused at many transforms. Mixing the two contracts in one
screen is fine as long as the screen orders the systems correctly.
**Breaks:** if `MeshPrepSystem` runs *after* `ButtonMeshPrepSystem`, the
button outline gets `transform.WorldMatrix` applied on top of already-world
vertices, doubling its offset — the outline drifts away from its text label
by the layout-computed top-left.
**Tests:** none yet.
**Depends on:** rendering — "MeshPrepSystem writes the world matrix once per
frame".

## Image-backed buttons reuse `SimpleButtonComponent` with a transparent outline

For sprite-backed buttons (icon caps, image tiles), do *not* introduce a parallel
component. Use `SimpleButtonComponent` with `LineThickness = 0` and
`Color = Color.Transparent` so `ButtonMeshPrepSystem` produces a degenerate mesh
that doesn't draw, while the same hit-test/interaction path still works. The
sprite background is a sibling entity that shares the button's
`TransformComponent`, carrying its own `SpriteInfoComponent` + `DrawComponent`.
Hover and active visuals come from a game-side recolor system that drives the
sprite's source rect or tint.

**Why:** SimpleButton's data (Size, TextEntity, Target) is already the right
shape for any button regardless of visual style. A second component would
duplicate the hit-test contract and force every interaction system to handle
two cases. Reusing it keeps `ButtonInteractionSystem` (and demos' equivalents)
single-path.
**Breaks:** treating image buttons as a separate component duplicates the
button query in every screen and forces a fork in the interaction logic.
**Tests:** none yet.
**Depends on:** rendering — `DrawComponent` Sprite/Mesh subtypes are
mutually exclusive on a single entity, which is why the sprite background
lives on a sibling.

## `ToggleSwitchComponent` drives a sprite's source rectangle from a bool

`ToggleSwitchComponent` pairs a bool state with two source rectangles
(`OffSource`, `OnSource`) and an `Entity SpriteEntity`. Each frame
`ToggleSwitchSystem` writes the matching rectangle onto the sprite's
`SpriteInfoComponent.Source`. Game code only flips the bool; the visual stays
in sync without extra wiring.

**Why:** two-state toggle artwork (off / on) ships as adjacent cells in a
sheet. Carrying both rectangles on the component lets the game store the bool
where it makes sense (often alongside its own state) without scattering
texture coordinates across the codebase.
**Breaks:** mutating `SpriteInfoComponent.Source` from elsewhere fights the
toggle system and races the value each frame. If a button is BOTH a toggle
and has hover-driven source swaps, gate them with a single system.
**Tests:** none yet.
**Depends on:** rendering — `SpritePrepSystem` reads `SpriteInfoComponent.Source`
to populate the draw call.

## `ButtonInteractionSystem` is deliberately NOT in this block

The interactive behavior of a button — hover detection, click
dispatching, screen transitions — is game-specific and lives in
`MonoDreams.Examples/System/UI/ButtonInteractionSystem.cs`. The `ui`
block ships only the *visuals* and *layout*
(`SimpleButtonComponent` + `ButtonMeshPrepSystem`) plus the hooks
(`UIElementComponent`, the builder). Each game writes its own
interaction system that consumes those.

**Why:** click dispatching needs to know what the click does — load
the next screen, fire a network call, mutate game state. That's
necessarily game-specific. Forcing a game-agnostic
`ButtonInteractionSystem` into the block would either be useless (no
default action) or coupling (assume some screen-transition message
the framework doesn't own).
**Breaks:** if a future refactor pulls `ButtonInteractionSystem` into
the block, every game has to either accept the bundled dispatch or
suppress it — the framework loses the "buttons compose with my own
interaction system" property.
**Tests:** none yet.
**Depends on:** —

## Open questions

- **Flexbox parity** — the solver supports flex-direction, justify,
  align, gap, padding, and margin, but not flex-grow, flex-shrink,
  flex-basis, wrap, or absolute positioning. Which of those become
  premises (must-have) vs aspirations (nice-to-have) is unsettled.
- **Re-measuring on content change** — `NeedsRemeasure` is set to true
  at construction and to false after measuring; nothing today flips it
  back to true when content changes. Dynamic text that grows
  mid-frame won't trigger a re-measure. Whether the framework should
  auto-detect changes (via `DefaultEcs.NotifyChanged` plumbing) is open.

## Aspirational direction

- Make `LayoutNodeComponent` itself the ECS component (drop the
  parallel pure-C# tree) once DefaultEcs supports the access pattern
  cheaply, collapsing the two-hierarchy split.
- `ButtonInteractionSystem` as a *configurable* (action-by-callback)
  system that game code can install, recovering composability without
  game-specific coupling.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `IntrinsicSizingSystem` runs before `AutoLayoutSystem`
- Intrinsic sizing is via callback, not via reading the content entity
- `ButtonMeshPrepSystem` bakes world coords and must run AFTER `MeshPrepSystem`
- Image-backed buttons reuse `SimpleButtonComponent` with a transparent outline
- `ToggleSwitchComponent` drives a sprite's source rectangle from a bool
- `AutoLayoutBuilder` is the canonical entry point
- `LayoutNodeComponent` is a pure C# tree, not an ECS hierarchy
- `ButtonInteractionSystem` is deliberately NOT in this block
