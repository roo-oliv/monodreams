# ui — overview

Flexbox-style layout with a fluent builder API (`AutoLayoutBuilder` → `ContainerBuilder` → `SlotBuilder`), an intrinsic-sizing pass driven by callbacks, and primitives for interactive buttons (`SimpleButtonComponent` + outline rendering via `rendering`). Install for game-UI screens — menus, level-select grids, HUD chrome.

## Purpose

This module is a flexbox-ish solver for UI. The flexbox solver positions children inside containers based on direction, justify, align, gap, padding, and margin; intrinsic sizes (the dimensions of text, sprites, buttons) come from per-slot callbacks so the solver doesn't need to introspect content types it doesn't know about. The module is deliberately scoped to *visuals and layout* — `ButtonInteractionSystem` (hover detection, click dispatch, screen transitions) lives in `MonoDreams.Examples/` because click dispatch is necessarily game-specific. Games copy that pattern; the framework provides the layout primitives.

## What ships

### Components

- `LayoutNodeComponent` — the flexbox solver's tree node (direction, justify, align, gap, padding, margin, computed bounds). A pure-C# tree maintained in parallel with `TransformComponent` hierarchy
- `LayoutSlotComponent` — per-slot data: `SizeMeasurer` callback, `IsRoot`, `NeedsRemeasure`, attached content entity
- `UIElementComponent` — marker for UI entities (used by game-side interaction systems for hit-testing)
- `SimpleButtonComponent` — button state (idle / hover / pressed) and visual style
- `TooltipComponent` — "hover me and read this" on any pickable entity (anything with a `FocusableComponent`): the label text plus an optional per-entity hover `Delay` (`null` = the system's default dwell, `0` = instant). Everything else — spawning, placement, edge-flip, teardown — belongs to `TooltipSystem`
- `PointerPickComponent` — THE pointer pick, published on the *cursor* entity by `UIFocusSystem`: which focusable the pointer is over and when that hover began. Read by every system that reacts to what the pointer is over (`TooltipSystem`, `CursorHoverSystem`) so none of them hit-tests again (see premises)
- `TextInputComponent` — a minimal editable single-line text field: current value, character mask (`TextInputMask.None` / `Numeric`), max length, a `Focused` flag, the linked text entity that displays the value, a `CaretPosition` insertion index, and an optional `CaretEntity` the system draws a white caret line into. Focus is game-owned (see premises); formatting / placeholder / error states are intentionally out of scope and can be layered on later

### Builders (fluent API)

- `AutoLayoutBuilder` — entry point: `new AutoLayoutBuilder(world, viewportManager).CreateRoot(anchor)...`
- `ContainerBuilder` — `.Direction(...)`, `.Gap(...)`, `.Padding(...)`, `.AddSlot(...)`, `.AddContainer(...)`
- `SlotBuilder` — `.Attach(entity).MeasureWith(measurer)`

### Systems

- `IntrinsicSizingSystem` — invokes each slot's `SizeMeasurer` callback, writes results into `LayoutNodeComponent.Width/Height`. Runs first
- `AutoLayoutSystem` — the flexbox solver: computes positions from the measured-size tree, writes to `TransformComponent`. Runs after `IntrinsicSizingSystem`
- `ButtonMeshPrepSystem` — paints button outlines via `rendering` based on `SimpleButtonComponent` state
- `TextInputSystem` — inserts masked characters at the caret (and handles Backspace / Delete / Left / Right / Home / End) into focused `TextInputComponent`s, mirrors the value onto the linked text entity, publishes `TextInputChanged`, and — when a `CaretEntity` is set — positions and shows a white vertical caret line at the insertion point. Reads the keyboard directly (edge-triggered); it only *consumes* the `Focused` flag — game code decides which field is focused
- `TooltipSystem` — floats a label next to the pointer for whatever the pointer is over. It reads the pick (`PointerPickComponent`) rather than hit-testing, waits out the entity's hover delay, rides the cursor, flips away from the screen edges, draws on a screen-space target above everything, and despawns on hover-out or target death. Look and feel come from a `TooltipStyle`; the placement math is the pure, unit-tested `TooltipPlacement.Place`
- `CursorHoverSystem` — swaps a mesh cursor's silhouette from the picked focusable's `FocusableComponent.HoverCursor` (e.g. a hand over a link), using the same pick
- `LayoutDebugSystem` — optional outline visualization (toggle `LayoutDebugSystem.Enabled`)

### Other

- `ButtonStyle` — visual configuration (colors, outline thickness, hover/pressed tints)
- `LayoutEnums` — `LayoutDirection`, `JustifyContent`, `AlignItems`, `ScreenAnchor`
- `TextInputChanged` — message published when a `TextInputComponent`'s value changes (carries the input entity and new text)

## Pipeline wiring

**Compose UI declaratively** with the builder chain:
```csharp
var layout = new AutoLayoutBuilder(world, viewportManager);
layout.CreateRoot(ScreenAnchor.Center)
    .Direction(LayoutDirection.Vertical)
    .Gap(40)
    .AddSlot(slot => slot.Attach(titleEntity).MeasureWith(MeasureText))
    .AddSlot(slot => slot.Attach(buttonEntity).MeasureWith(_ => buttonSize))
    .Build();
```

**Pipeline order** (within a screen's update pipeline):

1. **`IntrinsicSizingSystem`** — measure content via callbacks.
2. **`AutoLayoutSystem`** — compute and apply positions.
3. **Your own interaction systems** — hover detection, click dispatch (game-specific; see `MonoDreams.Examples/System/UI/ButtonInteractionSystem.cs`) — or `UIFocusSystem`, which also publishes the pointer pick.
4. **Pick consumers** — `CursorHoverSystem`, `TooltipSystem`. Both must run **after** `UIFocusSystem` (the pick's publisher); `TooltipSystem` additionally wants the cursor's fresh virtual position, so put it after `CursorPositionSystem` too.
5. **`ButtonMeshPrepSystem`** — paint button outlines via `rendering`.
6. **`LayoutDebugSystem`** (optional) — toggle on for layout debugging.

The module ships `SimpleButtonComponent` + the mesh-prep system but **deliberately doesn't ship a `ButtonInteractionSystem`** — click dispatch is necessarily game-specific (load a screen, fire a network call, mutate game state). Copy the pattern from `MonoDreams.Examples/System/UI/ButtonInteractionSystem.cs`.

## Cross-module dependencies

- `foundation` — slots are entities with `TransformComponent`; the builder wires `TransformComponent.Parent` for the rendered hierarchy.
- `rendering` — `ButtonMeshPrepSystem` and `LayoutDebugSystem` draw outlines via the `IMeshGenerator` primitives shipped by `rendering`; the tooltip panel is a rounded-rect mesh from the same primitives.
- `cursor` — the pointer half of the module: `UIFocusSystem` hit-tests against `CursorInputComponent` and publishes the pick **on the cursor entity**; `CursorHoverSystem` and `TooltipSystem` read it back from there.

## Extension points

- **New layout primitives.** Add to `LayoutEnums` for new direction/justify/align values, then teach `AutoLayoutSystem` to handle them. Flex-grow, flex-shrink, wrap, and absolute positioning are open questions (see premises).
- **Custom content types.** Anything measurable works — provide a `SizeMeasurer` callback (`Func<Entity, Vector2>`). The module never introspects the content entity's components, so you can attach text, sprites, meshes, or nested layouts.
- **Custom button styles.** Construct your own `ButtonStyle` instance or extend `SimpleButtonComponent` with extra fields.
- **Custom interaction system.** Read `UIElementComponent` + `LayoutNodeComponent.ComputedBounds` against `CursorInputComponent.WorldPosition` for hit-testing. See the Examples implementation for the canonical pattern.

## See also

- [Premises](premises.md) — load-bearing invariants (`IntrinsicSizingSystem` before `AutoLayoutSystem`, callback-based intrinsic sizing, `AutoLayoutBuilder` as canonical entry point, parallel `LayoutNodeComponent` + `TransformComponent` trees, `ButtonInteractionSystem` deliberately out of module)
- Related modules: `rendering` (button outlines and debug overlays draw via `IMeshGenerator` shapes from this module), `rendering-text` (text labels in UI slots), `cursor` (provides `CursorInputComponent.WorldPosition` for hit-testing in your game's interaction system), `dialogue` (does not use this module yet — uses hand-rolled offsets; aspirational to migrate)
