# ui — overview

Flexbox-style layout with a fluent builder API (`AutoLayoutBuilder` → `ContainerBuilder` → `SlotBuilder`), an intrinsic-sizing pass driven by callbacks, and primitives for interactive buttons (`SimpleButtonComponent` + outline rendering via `rendering`). Install for game-UI screens — menus, level-select grids, HUD chrome.

## Purpose

This module is a flexbox-ish solver for UI. The flexbox solver positions children inside containers based on direction, justify, align, gap, padding, and margin; intrinsic sizes (the dimensions of text, sprites, buttons) come from per-slot callbacks so the solver doesn't need to introspect content types it doesn't know about. The module is deliberately scoped to *visuals and layout* — `ButtonInteractionSystem` (hover detection, click dispatch, screen transitions) lives in `MonoDreams.Examples/` because click dispatch is necessarily game-specific. Games copy that pattern; the framework provides the layout primitives.

## What ships

### Components

- `LayoutNodeComponent` — the flexbox solver's tree node (direction, justify, align, gap, padding, margin, computed bounds). A pure-C# tree maintained in parallel with `TransformComponent` hierarchy
- `LayoutSlotComponent` — per-slot data: `SizeMeasurer` callback, `IsRoot`, `NeedsRemeasure`, attached content entity
- `PinnedLayoutRootComponent` — pins a ROOT slot at an arbitrary screen position (`Anchor` + `Offset`) instead of stacking it in the implicit solver container. Several independent panels — a HUD widget, a toolbar, a sticky note — each get their own root
- `UIElementComponent` — marker for UI entities (used by game-side interaction systems for hit-testing)
- `SimpleButtonComponent` — button state (idle / hover / pressed) and visual style
- `TooltipComponent` — "hover me and read this" on any pickable entity (anything with a `FocusableComponent`): the label text plus an optional per-entity hover `Delay` (`null` = the system's default dwell, `0` = instant). Everything else — spawning, placement, edge-flip, teardown — belongs to `TooltipSystem`
- `PointerPickComponent` — THE pointer pick, published on the *cursor* entity by `UIFocusSystem`: which focusable the pointer is over and when that hover began. Read by every system that reacts to what the pointer is over (`TooltipSystem`, `CursorHoverSystem`) so none of them hit-tests again (see premises)
- `HighlightComponent` — "draw attention to this entity": pulse speed, colour, thickness, padding, depth offset. Add it to ANY entity (sprite, text label, button, icon, or a bare hotspot with an explicit `Size`) and `HighlightSystem` keeps a pulsing outline on it; remove it and the outline goes away
- `PanelGroupComponent` — a group of mutually exclusive panels (tab bodies, settings pages, wizard steps): the member root entities, the active index (or `PanelGroupComponent.None` — "no member active", e.g. a closed menu), and the park offset. Pure data; game code only ever writes `Active`. `PanelParkedComponent` is the system's own bookkeeping on a parked member — never authored
- `TextInputComponent` — a minimal editable single-line text field: current value, character mask (`TextInputMask.None` / `Numeric`), max length, a `Focused` flag, the linked text entity that displays the value, a `CaretPosition` insertion index, and an optional `CaretEntity` the system draws a white caret line into. Focus is game-owned (see premises); formatting / placeholder / error states are intentionally out of scope and can be layered on later

### Builders (fluent API)

- `AutoLayoutBuilder` — entry point: `new AutoLayoutBuilder(world, viewportManager).CreateRoot(anchor)...`, or `.CreatePinnedRoot(position, anchor)...` for a root placed at a position of its own
- `ContainerBuilder` — `.Direction(...)`, `.Gap(...)`, `.Padding(...)`, `.AddSlot(...)`, `.AddContainer(...)`
- `SlotBuilder` — `.Attach(entity).MeasureWith(measurer)`

### Systems

- `IntrinsicSizingSystem` — invokes each slot's `SizeMeasurer` callback, writes results into `LayoutNodeComponent.Width/Height`. Runs first
- `AutoLayoutSystem` — the flexbox solver: computes positions from the measured-size tree, writes to `TransformComponent`. Runs after `IntrinsicSizingSystem`
- `PinnedLayoutRootSystem` — places every `PinnedLayoutRootComponent` root at `anchor + offset`, resolved against the root's solved size. Runs **after `AutoLayoutSystem` and before `HierarchySystem`** — that ordering is load-bearing (see premises)
- `ButtonMeshPrepSystem` — paints button outlines via `rendering` based on `SimpleButtonComponent` state
- `HighlightSystem` — owns one overlay entity per `HighlightComponent`: it rebuilds a pulsing outline from the target's *drawn* bounds, re-derives the overlay's layer depth from the target's every frame (so a z restack never buries it), inherits its render target + visibility, and disposes it with the target. Runs **last in the draw-prep stage**, after every prep system and before `MasterRenderSystem`
- `TextInputSystem` — inserts masked characters at the caret (and handles Backspace / Delete / Left / Right / Home / End) into focused `TextInputComponent`s, mirrors the value onto the linked text entity, publishes `TextInputChanged`, and — when a `CaretEntity` is set — positions and shows a white vertical caret line at the insertion point. Reads the keyboard directly (edge-triggered); it only *consumes* the `Focused` flag — game code decides which field is focused
- `TooltipSystem` — floats a label next to the pointer for whatever the pointer is over. It reads the pick (`PointerPickComponent`) rather than hit-testing, waits out the entity's hover delay, rides the cursor, flips away from the screen edges, draws on a screen-space target above everything, and despawns on hover-out or target death. Look and feel come from a `TooltipStyle`; the placement math is the pure, unit-tested `TooltipPlacement.Place`. Because it owns entities it also implements `ISuspendableSystem`: a screen may register it `EditTimeBehavior.Freeze` (it is a play-only pointer cosmetic) and the gate despawns the live label when the editor pauses, instead of stranding it on the HUD
- `CursorHoverSystem` — swaps a mesh cursor's silhouette from the picked focusable's `FocusableComponent.HoverCursor` (e.g. a hand over a link), using the same pick
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

**Several independent panels**, each at its own spot — anchored roots share one implicit solver container and therefore stack, so pin them instead:
```csharp
layout.CreatePinnedRoot(new Vector2(32, 32))                      // 32 px in from the top-left
    .Direction(LayoutDirection.Vertical)
    .AddSlot(...)
    .Build();

layout.CreatePinnedRoot(Vector2.Zero, ScreenAnchor.BottomCenter)  // a taskbar on the bottom edge
    .Direction(LayoutDirection.Horizontal)
    .AddSlot(...)
    .Build();
```

**Pipeline order** (within a screen's update pipeline):

1. **`IntrinsicSizingSystem`** — measure content via callbacks.
2. **`AutoLayoutSystem`** — compute and apply positions.
3. **`PinnedLayoutRootSystem`** — place the pinned roots. Must sit here: after the solver, before `HierarchySystem` and any world-position consumer.
4. **`UIFocusSystem`** — the one pointer pick, focus, press and the `UIFocusActivated` edge — plus **your own action system** subscribing to it (game-specific; see `MonoDreams.Examples/System/UI/ButtonInteractionSystem.cs`, which reads `FocusableComponent.IsFocused` for the hover colour and routes the activation to a screen transition).
5. **Pick consumers** — `CursorHoverSystem`, `TooltipSystem`. Both must run **after** `UIFocusSystem` (the pick's publisher); `TooltipSystem` additionally wants the cursor's fresh virtual position, so put it after `CursorPositionSystem` too.
6. **`ButtonMeshPrepSystem`** — paint button outlines via `rendering`.
7. **`HighlightSystem`** (optional) — pulsing outlines; register it at the END of the draw-prep stage (after `SpritePrepSystem` / `YSortSystem` / `TextPrepSystem` / `MeshPrepSystem` / `ButtonMeshPrepSystem`, before `MasterRenderSystem`) so it reads the bounds and depths those systems just wrote.
8. **`LayoutDebugSystem`** (optional) — toggle on for layout debugging.

**Highlight anything** — one line, no per-target asset, no new render path:
```csharp
// A tutorial's "click THIS": the outline rides the button, stays in front of it after a
// z restack, and disappears with the button (or when you remove the component).
buttonEntity.Set(new HighlightComponent());
labelEntity.Set(new HighlightComponent { Color = Color.Cyan, PulseSpeed = 1.4f, Padding = 4f });
hotspot.Set(new HighlightComponent { Size = new Vector2(64, 64) }); // an entity that draws nothing
```

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
- `rendering` — `ButtonMeshPrepSystem`, `HighlightSystem` and `LayoutDebugSystem` draw outlines via the `IMeshGenerator` primitives shipped by `rendering`; `HighlightSystem` also measures its target from the prepared `DrawComponent` (the same data `MasterRenderSystem` submits), which is what lets one derivation cover sprites, text, nine-patches and meshes.
- `rendering-text` — `ButtonVisualSystem` and `TextInputSystem` read and write the linked label's `DynamicTextComponent` (colour, `TextContent`, measurement), so the module does not compile without it.
- `cursor` — the pointer half of the module: `UIFocusSystem` hit-tests against `CursorInputComponent` and publishes the pick **on the cursor entity**; `CursorHoverSystem` and `TooltipSystem` read it back from there.

## Extension points

- **New layout primitives.** Add to `LayoutEnums` for new direction/justify/align values, then teach `AutoLayoutSystem` to handle them. Flex-grow, flex-shrink, wrap, and absolute positioning are open questions (see premises).
- **Custom content types.** Anything measurable works — provide a `SizeMeasurer` callback (`Func<Entity, Vector2>`). The module never introspects the content entity's components, so you can attach text, sprites, meshes, or nested layouts.
- **Custom button styles.** Construct your own `ButtonStyle` instance or extend `SimpleButtonComponent` with extra fields.
- **Custom interaction system.** Read `UIElementComponent` + `LayoutNodeComponent.ComputedBounds` against `CursorInputComponent.WorldPosition` for hit-testing. See the Examples implementation for the canonical pattern.
- **Attention / hint overlays.** `HighlightComponent` is the generic "look here" primitive — a tutorial, an onboarding step, a quest hint or a debug session all express themselves by adding it to an existing entity, never by authoring a glowing art variant per target. New draw types get measured for free (`HighlightSystem.DrawnQuad` switches on `DrawComponent.Type`).

## See also

- [Premises](premises.md) — load-bearing invariants (`IntrinsicSizingSystem` before `AutoLayoutSystem`, callback-based intrinsic sizing, `AutoLayoutBuilder` as canonical entry point, pinned roots out of flow with the pin pass between `AutoLayoutSystem` and `HierarchySystem`, parallel `LayoutNodeComponent` + `TransformComponent` trees, exclusive panel groups park rather than hide, `ButtonInteractionSystem` deliberately out of module)
- Related modules: `rendering` (button outlines and debug overlays draw via `IMeshGenerator` shapes from this module), `rendering-text` (text labels in UI slots), `cursor` (provides `CursorInputComponent.WorldPosition` for hit-testing in your game's interaction system), `dialogue` (does not use this module yet — uses hand-rolled offsets; aspirational to migrate)
