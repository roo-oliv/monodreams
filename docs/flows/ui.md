---
flow: ui
covers:
  - MonoDreams/ui/**
sensitive: false
---

# UI layout

A UI screen builds a layout tree, sizes it bottom-up, places it top-down, and the
positioned slot entities then emit `DrawComponent`s through the rendering module — all
in that order, every frame. `AutoLayoutBuilder → ContainerBuilder → SlotBuilder` is the
construction phase: it creates one entity per container/slot, each carrying a
`TransformComponent` and a `LayoutSlotComponent` whose `Node` (a pure-C#
`LayoutNodeComponent`) holds the flexbox config. The builder wires *two* parallel trees —
the `LayoutNodeComponent.Children`/`Parent` tree the solver walks, and the
`TransformComponent.Parent` chain rendering walks — and keeps them in sync at construction
time. Per frame, `IntrinsicSizingSystem` runs first: for each slot whose `NeedsRemeasure`
is set, it invokes the slot's `SizeMeasurer(Content)` callback and writes the result into
`Node.Width/Height` (clearing `WidthAuto/HeightAuto`). Then `AutoLayoutSystem` runs the
solver: it re-parents every `IsRoot` slot's node under a screen-sized `_screenRoot`, calls
`CalculateLayout`, and writes each node's computed `LayoutX/LayoutY` back onto the owning
entity's `TransformComponent.Position` (roots also get the `ScreenAnchor` → screen-offset
applied). Downstream, `ButtonMeshPrepSystem` reads each button's `transform.WorldPosition`
and `SimpleButtonComponent.Size` to bake the outline/fill mesh, and text/sprite content
entities (parented under their slot) render at the slot's resolved position. Layout writes
positions; rendering reads them — the seam is `TransformComponent`.

## Entities & lifecycle

- **Container / slot entities** — created once by the builder (`ContainerBuilder.Build`,
  `SlotBuilder.Build`). Each gets `TransformComponent` + `LayoutSlotComponent`; roots get
  `IsRoot = true` and a `ScreenAnchor`. The builder calls `SetParent` so the
  `TransformComponent` hierarchy mirrors the `Node` hierarchy. There is no second creator —
  hand-rolling these components bypasses the wiring (see invariants).
- **`LayoutNodeComponent`** — a plain C# object held by the slot, *not* an ECS component.
  Per frame its computed fields (`LayoutX/Y/Width/Height`) are recomputed from scratch:
  `AutoLayoutSystem` rebuilds `_screenRoot`'s child list each frame, so any change to the
  slot set is picked up on the next pass.
- **Per-frame transitions:** `NeedsRemeasure` true → `IntrinsicSizingSystem` measures →
  false. Then `CalculateLayout` does its own two sub-passes internally: `MeasureSize`
  (bottom-up, hug-contents base sizes) then `PositionChildren` (top-down, resolves
  flex-grow on the main axis and Stretch/cross-fill on the cross axis). Finally
  `ApplyLayout` writes positions onto transforms.
- **Content entities** (text, sprite, button) — game-created, attached via
  `SlotBuilder.Attach`, parented under the slot. They carry their own `DrawComponent`;
  layout never touches their draw data, only the slot transform they ride on.

## Invariants

Authoritative list in [`MonoDreams/ui/docs/premises.md`](../../MonoDreams/ui/docs/premises.md);
the ones this flow's ordering leans on:

- `IntrinsicSizingSystem` runs **before** `AutoLayoutSystem`. Reversed, the solver sees
  zero-sized content and everything stacks at the top-left.
- A slot with no `SizeMeasurer` (or with `NeedsRemeasure` false and never-measured) keeps
  its initial size — measurement is opt-in and the system early-exits silently.
- The `LayoutNodeComponent` tree and the `TransformComponent.Parent` chain are two
  hierarchies that must stay in sync; the builder owns that sync (ties into foundation's
  two-link hierarchy). Mutating one without the other desyncs position from render parent.
- Flex-grow and cross-fill resolve **top-down** in the parent — a node distributes leftover
  space only after its own size is final, so the tree must solve root-to-leaf.
- `AutoLayoutBuilder` is the canonical entry point; root slots need `IsRoot = true` or
  `AutoLayoutSystem` never picks them up.
- `ButtonMeshPrepSystem` bakes world coords and must run *after* `MeshPrepSystem` when both
  are present (it sets `WorldMatrix = Identity`); button geometry reads the post-layout
  `WorldPosition`, so it must run after `AutoLayoutSystem` too.

## Load-bearing quantities

- **Intrinsic size** — `Node.Width/Height` written by the `SizeMeasurer` callback, pixels.
  This is the leaf input; everything else derives from it.
- **Computed size/position** — `LayoutWidth/Height`, `LayoutX/Y`, pixels, top-left origin,
  Y-down. `AutoLayoutSystem` converts the root's to MonoDreams' center origin via the
  `ScreenAnchor` offset (and adds a half-screen shift for `HUD` targets, which render
  without the camera transform).
- **`FlexGrow`** — dimensionless weight; only distributes leftover *main-axis* space among
  children whose main-axis fill flag is set; defaults to `1` when `<= 0`. No effect on a
  non-fill child (silent no-op).
- **`Gap` / padding / margin** — pixels. Gap sits *between* children (×`count-1`); padding
  shrinks the inner box (`innerWidth = LayoutWidth - PaddingLeft - PaddingRight`); margin is
  added by the parent around each child.
- **Main vs cross axis** — `FlexDirection` picks which of width/height is "main"; justify
  spreads leftover main-axis space, align positions on the cross axis. Confusing the two is
  the classic flexbox bug here.

## Failure modes

- **Layout solved before sizing** — `AutoLayoutSystem` ordered before
  `IntrinsicSizingSystem` (or a slot never remeasured): every node measures zero, children
  collapse to the top-left, nothing centers. No error, just broken UI. Highest-frequency
  real bug.
- **Stale tree on mutation** — adding/removing slots, or resizing content, without setting
  `NeedsRemeasure` back to true: the solver re-runs but reuses last frame's measured size,
  so a grown label clips. (`NeedsRemeasure` is never re-armed automatically today.)
- **Two hierarchies desynced** — mutating `Node.Children` without the matching `SetParent`
  (or vice versa): layout lands a child in the right slot, but rendering uses the wrong
  parent transform, so the visual drifts from the layout box.
- **Button mesh double-offset** — `ButtonMeshPrepSystem` running before `AutoLayoutSystem`
  (reads a stale `WorldPosition`) or before `MeshPrepSystem` overwrites its identity matrix:
  the outline drifts from its label by the layout-computed top-left.
- **Flex-grow no-op** — setting `FlexGrow` on a child that isn't a main-axis fill child:
  silently does nothing; the row doesn't expand as intended.
