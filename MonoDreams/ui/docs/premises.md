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
- `AutoLayoutBuilder` is the canonical entry point
- `LayoutNodeComponent` is a pure C# tree, not an ECS hierarchy
- `ButtonInteractionSystem` is deliberately NOT in this block
