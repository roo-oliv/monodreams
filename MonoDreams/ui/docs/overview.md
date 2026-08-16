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
- `PanelGroupComponent` — a group of mutually exclusive panels (tab bodies, settings pages, wizard steps): the member root entities, the active index (or `PanelGroupComponent.None` — "no member active", e.g. a closed menu), and the park offset. Pure data; game code only ever writes `Active`. `PanelParkedComponent` is the system's own bookkeeping on a parked member — never authored
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
- `PanelGroupSystem` — parks every inactive member of a `PanelGroupComponent` at the park offset (alive, laid out, off-screen) and restores the active one to the exact position it left, gating the focusables under each panel accordingly (groups nest: a focusable is navigable only when *every* panel above it is active). Run it after everything that writes a member's position (notably `AutoLayoutSystem`) and before `HierarchySystem` — and after `TabSystem` when both are present, since the panel gate is the finer one
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
3. **Your own interaction systems** — hover detection, click dispatch (game-specific; see `MonoDreams.Examples/System/UI/ButtonInteractionSystem.cs`).
4. **`ButtonMeshPrepSystem`** — paint button outlines via `rendering`.
5. **`LayoutDebugSystem`** (optional) — toggle on for layout debugging.

**Switch panels by parking, never by hiding.** Tabs, settings pages, and wizard steps are all one primitive: build each panel as a root entity with its content parented under it, list the roots in a `PanelGroupComponent`, and register `PanelGroupSystem` after the layout pass and before `HierarchySystem`:

```csharp
var tabs = world.CreateEntity();
tabs.Set(new PanelGroupComponent { Members = [overview, details, notes], Active = 0 });
// …later, from a UIFocusActivated handler — the ONLY thing game code writes:
tabs.Get<PanelGroupComponent>().Active = 2;                        // show "notes"
tabs.Get<PanelGroupComponent>().Active = PanelGroupComponent.None; // close: park them all
```

The inactive panels are translated off-screen, not hidden: they keep every component, stay laid out and measured, and come back at exactly the position they left. See the ui demo's **Panels** tab (`MonoDreams/ui/demo/UiDemoScreen.cs`) for a tab bar and a paged settings menu built on the same component, and the premises for why hiding is the wrong reflex.

The module ships `SimpleButtonComponent` + the mesh-prep system but **deliberately doesn't ship a `ButtonInteractionSystem`** — click dispatch is necessarily game-specific (load a screen, fire a network call, mutate game state). Copy the pattern from `MonoDreams.Examples/System/UI/ButtonInteractionSystem.cs`.

## Cross-module dependencies

- `foundation` — slots are entities with `TransformComponent`; the builder wires `TransformComponent.Parent` for the rendered hierarchy.
- `rendering` — `ButtonMeshPrepSystem` and `LayoutDebugSystem` draw outlines via the `IMeshGenerator` primitives shipped by `rendering`.

## Extension points

- **New layout primitives.** Add to `LayoutEnums` for new direction/justify/align values, then teach `AutoLayoutSystem` to handle them. Flex-grow, flex-shrink, wrap, and absolute positioning are open questions (see premises).
- **Custom content types.** Anything measurable works — provide a `SizeMeasurer` callback (`Func<Entity, Vector2>`). The module never introspects the content entity's components, so you can attach text, sprites, meshes, or nested layouts.
- **Custom button styles.** Construct your own `ButtonStyle` instance or extend `SimpleButtonComponent` with extra fields.
- **Custom interaction system.** Read `UIElementComponent` + `LayoutNodeComponent.ComputedBounds` against `CursorInputComponent.WorldPosition` for hit-testing. See the Examples implementation for the canonical pattern.

## See also

- [Premises](premises.md) — load-bearing invariants (`IntrinsicSizingSystem` before `AutoLayoutSystem`, callback-based intrinsic sizing, `AutoLayoutBuilder` as canonical entry point, parallel `LayoutNodeComponent` + `TransformComponent` trees, exclusive panel groups park rather than hide, `ButtonInteractionSystem` deliberately out of module)
- Related modules: `rendering` (button outlines and debug overlays draw via `IMeshGenerator` shapes from this module), `rendering-text` (text labels in UI slots), `cursor` (provides `CursorInputComponent.WorldPosition` for hit-testing in your game's interaction system), `dialogue` (does not use this module yet — uses hand-rolled offsets; aspirational to migrate)
