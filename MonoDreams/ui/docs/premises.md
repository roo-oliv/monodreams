# ui — premises

> Technical invariants the engine assumes about the UI module:
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
nested layout, mesh — without the UI module knowing what those
components look like. A future module can add a new measurable type by
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

## `ToggleSwitchComponent` drives a checkmark mesh's visibility from a bool

`ToggleSwitchComponent` pairs a bool state with an `Entity CheckmarkEntity` and the
checkmark's `MeshData CheckmarkMesh`. Each frame `ToggleSwitchSystem` fills the
checkmark entity's `DrawComponent` mesh from `CheckmarkMesh` when `On`, and empties it
(`Vertices = []`, `Indices = []`) when off — the checkbox box itself is a static sibling
mesh. Game code only flips the bool; the visual stays in sync without extra wiring. The
empty-mesh toggle (rather than `VisibleComponent`) is required because checkboxes live on
the UI / HUD target, which always renders regardless of `VisibleComponent` (see rendering
— "Three render targets, two behaviors"); the same reason the text-input caret hides by
emptying its mesh.

**Why:** a checkbox is a two-state toggle whose "on" visual is a generated checkmark, not
a sprite frame. Carrying the checkmark mesh on the component lets the system restore it
without re-deriving the box geometry, and emptying it is the only hide that works on a
screen-space target.
**Breaks:** mutating the checkmark entity's `DrawComponent` from elsewhere fights the
toggle system and races the value each frame. Removing `VisibleComponent` to hide the
checkmark instead leaves a stale mesh that still renders on UI/HUD (and drops the world
matrix `MeshPrepSystem` needs).
**Tests:** none yet (exercised by the camera demo's lerp checkbox and the physics demo's
gravity / floor-boost checkboxes).
**Depends on:** rendering — "Three render targets, two behaviors"; "`MeshPrepSystem` writes
the world matrix once per frame".

## The module owns the focus + visual MECHANISM and publishes `UIFocusActivated`; the ACTION stays game-side

The `ui` module now ships the interaction *mechanism* — pointer + keyboard
focus (`UIFocusSystem`), visual state (`ButtonVisualSystem` driving
`ButtonStateComponent`), and a message-based activation dispatch
(`UIFocusActivated(Entity Focused, string Id)`). `UIFocusSystem` detects
hover, moves focus, and on Enter / Space (or click) publishes
`UIFocusActivated` carrying the focused entity and its id. What an
activation *does* — load the next screen, fire a network call, mutate game
state — stays game-side: a game subscribes to `UIFocusActivated` and routes
by `Id`. This is the "configurable action-by-callback" the old Aspirational
direction named, realized as a message seam rather than an installed
callback. The split is deliberate: the framework owns "which control is
focused and that it was activated", the game owns "what activation means".
The games' own `DemoButtonInteractionSystem` (in `MonoDreams.Demos`) and
`MonoDreams.Examples/System/UI/ButtonInteractionSystem.cs` remain a
legacy / coexisting path — a game may still hand-roll hit-test + dispatch
instead of subscribing to `UIFocusActivated`, but new screens should prefer
the message seam.

**Why:** click dispatching needs to know what the click does, which is
necessarily game-specific — but *detecting* focus / hover / activation and
*reflecting* visual state are mechanical and reusable. Publishing
`UIFocusActivated` keyed by id lets the game keep its dispatch (route by id)
without re-implementing focus and hit-testing in every screen. Keeping the
action out of the module preserves the "buttons compose with my own
interaction logic" property; bundling a default action would either be
useless (no-op) or couple to a screen-transition message the framework
doesn't own.
**Breaks:** a game that neither subscribes to `UIFocusActivated` nor runs
its own dispatch system gets inert buttons (they focus and recolor but do
nothing). Two systems both acting on the same `UIFocusActivated` id
double-dispatch. Pulling a concrete action into the module would force every
game to accept or suppress it.
**Tests:** none yet.
**Depends on:** "`UIFocusSystem` is the single focus owner".

## Text-input focus is game-owned; key capture is the module's job

`TextInputComponent` carries the editable value, a `TextInputMask`, a max length,
a `Focused` flag, and a `CaretPosition` insertion index. `TextInputSystem` only
*reads* `Focused`: while it is true the system inserts characters typed this frame at
the caret (filtered by the mask, capped at the max length), edits at the caret on
Backspace / Delete, moves the caret on Left / Right / Home / End, mirrors the value
onto the linked text entity, and publishes `TextInputChanged`. Deciding *which* field
is focused — on click, on Tab, on click-away-to-blur — is game code's responsibility,
exactly like click dispatch (`ButtonInteractionSystem`) is. Placing the caret when a
field gains focus is part of that same focus policy: the system never repositions the
caret on focus, so a game that pre-fills `Text` should set `CaretPosition = Text.Length`
when it creates the field (otherwise editing starts at the front, index 0). The system
always clamps `CaretPosition` into `[0, Text.Length]`.

**Why:** focus policy is UX-specific (click-to-focus, tab order, single vs multiple
focus, blur on outside click, where the caret lands on focus). Baking one policy into
the module would force every game to accept or suppress it. Keeping the flag as the seam
lets a game drive focus from its own interaction system while the module owns the
mechanical key capture and caret editing.
The physics demo (`MonoDreams/physics/demo/PhysicsDemoScreen.cs`) is the reference
use site: it focuses a field on `DemoButtonClicked` and blurs the others, and
`DemoUI.CreateNumberInputRow` seeds `CaretPosition` to the initial value's length.
**Breaks:** a field that is never given focus is inert (no system sets the flag for
it); two fields left `Focused` at once both consume the same keystrokes. The
edge-triggered keyboard diff is shared per frame, so multi-focus is undefined by
design. A pre-filled field left at `CaretPosition = 0` inserts typed characters before
the existing value.
**Tests:** none yet (exercised by the physics demo: the `TextInputChanged` →
rebuild path is wired there).
**Depends on:** "`ButtonInteractionSystem` is deliberately NOT in this module".

## The text-input caret is a game-supplied mesh entity the system positions and toggles

When `TextInputComponent.CaretEntity` is set, `TextInputSystem` draws a vertical white
caret line at `CaretPosition` while the field is `Focused`. The caret entity is supplied
by game code — a `DrawComponent` of type Mesh plus a `TransformComponent`, parented under
the field's `TextEntity` and carrying `VisibleComponent` — exactly like `TextEntity`
itself is game-supplied (see the focus premise). Because the caret is parented under the
text, its local X is simply the rendered width of `Text[..CaretPosition]`
(`Font.MeasureString(...).Width * Scale`, read off the linked text's `DynamicTextComponent`)
and its line height is `Font.LineHeight * Scale`. The system writes that local X each
frame and builds the line mesh once per focus session; when the field is not focused (or
has no font) it empties the caret mesh so `MasterRenderSystem` skips it
(`HasValidMesh == false`). `CaretEntity` left at `default` opts out — the editing logic
still runs. The caret **blinks** (~0.5 s on / 0.5 s off, off `state.TotalTime`) — in the
off-half it empties the mesh, in the on-half it rebuilds it; it shows steadily for one
half-period right after any edit / caret move so typing is easy to follow. A **left click
inside a focused field's bounds** (read from a cursor `EntitySet` of `CursorInputComponent`,
hit-tested with `Rectangle(WorldPosition, FocusableComponent.Size)` vs the cursor's
world/virtual position by the field's `Target`) places the caret at the nearest character
boundary to the click X — computed from the value text's world start X (the field's
WorldPosition plus the value entity's local offset) by walking prefix widths
(`Font.MeasureString(text[..i]).Width * scale`). Click-to-place is still focus-policy-
agnostic: the system only repositions the caret when the field is already `Focused`.

**Why:** hiding on a screen-space target (UI / HUD) can't use `VisibleComponent` — only
the Main target consults it (see rendering — "Three render targets, two behaviors"), and
the caret needs `VisibleComponent` anyway so `MeshPrepSystem` refreshes its world matrix
each frame. Emptying the mesh is the toggle that works regardless of target. Parenting
under the text entity (rather than the box) means the caret offset is a pure text-width
measurement with no box-padding bookkeeping, and it tracks the text as layout moves the
row. Building the silhouette once (height is font-derived and stable) keeps the per-frame
work to a transform write.
**Breaks:** removing `VisibleComponent` to hide the caret leaves a stale world matrix and,
on HUD/UI, still renders. Baking the caret into the box outline (`ButtonMeshPrepSystem`)
would couple every button to text-input. Parenting the caret to the box instead of the
text reintroduces the box-padding offset the text already encodes.
**Tests:** none yet (exercised by the physics demo's number-input rows).
**Depends on:** rendering — "MeshPrepSystem writes the world matrix once per frame";
rendering-text — "`TextPrepSystem` writes the world-transformed position".

## `UIFocusSystem` is the single focus owner; pointer steals focus only on mouse move; nav is group-scoped

`UIFocusSystem` is the one place focus moves. At most one `FocusableComponent`
has `IsFocused == true` at a time across the controls it manages; the system
clears the previous owner when focus changes and publishes `FocusChanged`.
Keyboard navigation is *spatial* on WASD / arrows (nearest focusable in the
pressed direction) and *ordinal* on Tab (next by `TabIndex`). The pointer
steals focus only when the **mouse actually moves** (`cursor.Delta !=
Vector2.Zero`) and hovers a focusable — a still cursor never fights keyboard
navigation. All navigation is scoped to the **active group**: the system
reads `activeGroup()` (a `Func<int>?`, defaulting to group `0`) each frame
and only considers focusables whose `FocusableComponent.Group` matches, so a
dialog / dropdown traps focus by being the active group (see the overlay
premise). The system mirrors `IsFocused` onto a focusable's linked
`TextInputComponent.Focused` so text fields edit when focused, and sets
`ButtonStateComponent.IsPressed` while activating.

**Why:** a single owner is the only way to guarantee one focus highlight and
deterministic Tab order; multiple writers race the highlight. Group-scoping
is what makes modal trapping a data flag (raise the active group) rather than
a special focus mode. Stealing focus only on mouse *move* resolves the
classic "my keyboard selection jumps back under the resting cursor" bug.
**Breaks:** two systems writing `IsFocused` produce a flickering or doubled
highlight. If `activeGroup()` returns a group with no focusables, navigation
is inert (nothing is reachable) — the screen must keep the active group in
sync with which controls exist. A pointer that steals focus every frame
(ignoring `Delta`) makes keyboard nav unusable whenever the cursor rests over
a control.
**Depends on:** rendering — "Three render targets, two behaviors" (hit-test
target); "Text-input focus is game-owned; key capture is the module's job"
(the `TextInputComponent.Focused` mirror).
**Tests:** none yet.

## The gold focus ring follows a `:focus-visible` model — keyboard focus / active only, never pointer hover

`UIFocusSystem` writes two flags per focusable each frame: `IsFocused` (the single
focused entity in the active group) and `FocusVisible` (true only when that focus was
set via the **keyboard** pass — spatial/ordinal nav or keyboard activate — not a pointer
hover). `ButtonVisualSystem` reads both: it shows the bright **gold focus ring** when
`ButtonStateComponent.IsActive || (IsFocused && FocusVisible)`, and the **hover fill**
(the variant's Hover colors) whenever `IsFocused || IsActive` regardless of source. The
net effect mirrors CSS `:focus-visible`: a mouse hover changes only the background fill,
while keyboard focus (and a selected/active control like the current tab) gets the ring.
The pointer sets `FocusVisible = false` the instant it steals focus (`SetFocus(e,
fromKeyboard: false)`); keyboard nav / activate and a group-change-forced focus set it
true.

**Why:** hovering a control with the mouse should not make it look identical to the
keyboard-selected control — the gold ring is the keyboard/selection affordance, and a
pointer user already has the cursor as their position indicator. Splitting "is focused"
from "should the focus be visibly ringed" is the smallest data change that expresses the
distinction without a second focus owner.
**Breaks:** dropping `FocusVisible` (ringing on plain `IsFocused`) reintroduces the bug
where mouse-hovering any button/tab paints it with the same gold border as the selected
tab. Writing `FocusVisible` from anywhere but `UIFocusSystem` races the single-owner
guarantee the way a second `IsFocused` writer would.
**Depends on:** "`UIFocusSystem` is the single focus owner; pointer steals focus only on
mouse move; nav is group-scoped".
**Tests:** none yet.

## `SimpleButtonComponent.LayerDepth` configures the button mesh depth; 0 means the 0.95 default

`ButtonMeshPrepSystem` writes the button's outline + fill mesh at
`SimpleButtonComponent.LayerDepth`, treating `0` (an unset field) as the historical
default `0.95`. A screen sets it **lower** to push a button's fill / focus ring *behind*
sibling decorations that must stay visible over the fill — the canonical case is a
checkbox row's transparent hit-box (set to e.g. `0.40`) so its hover-highlight fill
renders behind the box (`0.95`) and checkmark (`0.96`), keeping the depth order strict
(row-fill < box < checkmark < label `0.97`). Higher `LayerDepth` draws on top in this
painter's-order pipeline. Normal buttons leave it at `0` and render at `0.95` as before.

**Why:** before this field, `ButtonMeshPrepSystem` hardcoded `0.95`, equal to the
checkbox box depth — a tie that `MasterRenderSystem`'s stable sort resolved by insertion
order, so hovering a checkbox row could draw the highlight fill *over* the box and
checkmark (they vanished). Making the depth a per-button datum lets the screen express
strict ordering instead of relying on a fragile insertion-order tiebreak.
**Breaks:** leaving two co-sibling meshes at the same depth reintroduces
insertion-order-dependent (i.e. non-deterministic) layering. Setting a checkbox hit-box
*above* its decorations hides them under the highlight fill again.
**Depends on:** rendering — "Layer-depth ownership pipeline"; "The mesh render path uses
premultiplied alpha — UI fills must be opaque".
**Tests:** none yet.

## A mesh cursor swaps silhouette on hover via `FocusableComponent.HoverCursor` + `CursorMeshLibraryComponent` + `CursorHoverSystem`

A focusable opts into a custom hover cursor purely as data:
`FocusableComponent.HoverCursor` (a `CursorType`, default `Default` = no override, e.g.
`Hand` for a link). A mesh cursor opts into swapping by carrying a
`CursorMeshLibraryComponent` (`Dictionary<CursorType, MeshData>`, with the `Default` entry
being the resting arrow). `CursorHoverSystem` (in `ui`, which already depends on `cursor`)
runs after `UIFocusSystem` and reads **the pointer pick** off the cursor entity
(`PointerPickComponent` — it runs no hit-test of its own, see "There is ONE pointer
pick"): it takes the picked entity's `HoverCursor` (`Default` when nothing is picked),
writes it onto `CursorControllerComponent.Type`, and — only when the type changes — swaps
the cursor entity's mesh `DrawComponent` to the matching library entry (falling back to
`Default`).

**Why:** "show a hand over links" is a reusable, mechanical UI behavior that should not
be hand-rolled per screen, and it generalizes to any focusable + any cursor type. Keeping
the request on the focusable (data) and the silhouettes in a library (data) lets the one
system own the swap, mirroring how `ButtonVisualSystem` owns the focus visual. A textured
cursor (no `CursorMeshLibraryComponent`) is left untouched, so the mechanism is additive.
Deriving the swap from the shared pick (rather than a private hit-test) is what keeps the
hand cursor and the click on the same entity: the system inherits the pick's group and
disabled filters, so a link trapped under an open dialog no longer paints a hand for a
click that can't reach it.
**Breaks:** registering the system but giving the mesh cursor no library records the
`CursorType` but never swaps the mesh (silent no-op). Registering it without the pick's
owner (`UIFocusSystem`) leaves the cursor on the resting arrow — the documented graceful
degradation, not a crash. A focusable whose `Size` is zero never hovers.
**Depends on:** "There is ONE pointer pick: `UIFocusSystem` publishes it, hover consumers
read it"; cursor — "A mesh cursor renders via `Cursor.CreateMesh` + `MeshPrepSystem`";
"`CursorMeshLibraryComponent` holds the per-`CursorType` silhouettes a mesh cursor swaps
between".
**Tests:** `MonoDreams.Tests/Ui/PointerPickTests.cs`
(`PickedFocusableRequestingAHand_SwapsTheCursorTypeAndMesh`,
`FocusableOutsideTheActiveGroup_DoesNotSwapTheCursor`,
`NoPickPublished_LeavesTheCursorUntouched`).

## `FocusableComponent.Disabled` (tab-gating) and `ButtonStateComponent.IsDisabled` (control-disabled) are separate, and both skip nav

`UIFocusSystem` skips a focusable from navigation when **either**
`FocusableComponent.Disabled` is true **or** the entity's
`ButtonStateComponent.IsDisabled` is true. The two mean different things and
are owned by different code:
`FocusableComponent.Disabled` is **tab-gating** — "this control is currently
out of the navigable set because the overlay it belongs to is closed",
flipped in bulk by `TabSystem` / `DialogSystem` / `DropdownSystem` as they
show/hide a group of controls.
`ButtonStateComponent.IsDisabled` is **control-disabled** — "this button is
greyed out and must never be activatable", owned by game logic.
They are kept distinct so that re-enabling a group (an overlay opening flips
`FocusableComponent.Disabled` back to false for all its controls) never
accidentally re-enables a control the game has deliberately disabled — the
`ButtonStateComponent.IsDisabled` flag survives the bulk toggle.

**Why:** if a single flag carried both meanings, the overlay-show code that
re-enables a tab group would clobber a game's "this option is unavailable"
state. Two orthogonal flags let the tab/overlay systems own visibility-gating
and the game own availability, each without stepping on the other.
**Breaks:** collapsing the two into one flag means opening a dialog
re-enables a button the game greyed out (now clickable when it shouldn't be),
or conversely a game disabling a control removes it from a tab group
permanently even after re-show. A nav check that tests only one of the two
flags lets a disabled control be reached by the other path.
**Depends on:** "`UIFocusSystem` is the single focus owner".
**Tests:** none yet.

## Tab / Dialog / Dropdown systems show/hide on the Main target via `VisibleComponent` and gate focus via `FocusableComponent.Disabled`

`TabSystem`, `DialogSystem`, and `DropdownSystem` share one overlay pattern:
to show a set of entities they add `VisibleComponent` (and clear
`FocusableComponent.Disabled` on the focusable ones); to hide the set they
remove `VisibleComponent` (and set `FocusableComponent.Disabled = true`).
`TabSystem` is the reference: it flips the visible/disabled pair for the
panel of the selected tab. `DialogComponent` / `DropdownComponent` carry the
content entity array and a `Group`; their systems mirror `VisibleComponent`
and `FocusableComponent.Disabled` to `IsOpen`. These systems do **not** own
the active-group value — they expose an `IsOpen` flag that the screen reads
to compute the topmost-open group and pass to `UIFocusSystem`'s
`activeGroup`. Group ids by convention: dialog = `100`, dropdown = `200`,
combobox-dropdown = `300` (base UI is group `0`).

**Why:** on the Main target `VisibleComponent` is the show/hide toggle (the
demo runs no `CullingSystem`, so the tag is the visibility switch), and
gating focus with `FocusableComponent.Disabled` keeps hidden controls out of
navigation without deleting them. Separating "am I open" (system-owned
`IsOpen`) from "which group is active" (screen-owned) is what lets the screen
stack overlays — the screen, not any one overlay system, decides which group
traps focus.
**Breaks:** hiding an overlay's entities without also setting
`FocusableComponent.Disabled` leaves invisible-but-focusable controls that
Tab still lands on. An overlay system that tried to set the active group
itself would fight other overlays when more than one is open — only the
screen sees the whole stack. Using these on a UI/HUD target (which ignores
`VisibleComponent`) would fail to hide — those targets need the
empty-the-mesh toggle instead (see the caret / toggle premises).
**Depends on:** rendering — "Three render targets, two behaviors";
"`UIFocusSystem` is the single focus owner"; "`FocusableComponent.Disabled`
(tab-gating) and `ButtonStateComponent.IsDisabled` (control-disabled) are
separate".
**Tests:** none yet.

## A combobox is a `TextInputComponent` driving a `DropdownComponent`'s filter

`ComboboxComponent` composes two existing widgets rather than introducing a
new editable-list control: a filter field (`ComboboxComponent.Input`, an
entity carrying `TextInputComponent`) and an attached dropdown
(`ComboboxComponent.DropdownEntity`, carrying `DropdownComponent`).
`ComboboxSystem` subscribes to `TextInputChanged` from the input and, on each
change, shows the dropdown option entities whose label
(`ComboboxComponent.ItemLabels`, index-aligned with
`DropdownComponent.Items`) contains the query case-insensitively and hides
the rest — opening the dropdown (`DropdownComponent.IsOpen = true`) so the
filtered list is visible. `DropdownSystem` still owns show/hide and
outside-click close; `ComboboxComponent` uses group `300` by convention.

**Why:** filtering is exactly "type to narrow a dropdown", so the combobox is
a text field plus a dropdown plus a filter rule — no new component duplicating
the text-edit or the popup-list contracts. Reusing `TextInputChanged` and
`DropdownComponent.IsOpen` keeps the combobox a thin coordinator over two
widgets that already work.
**Breaks:** filtering by mutating the dropdown's entity arrays directly
(instead of toggling each option's visibility/disabled) fights
`DropdownSystem`'s own show/hide. Forgetting to keep `ItemLabels` index-aligned
with `DropdownComponent.Items` filters the wrong rows.
**Depends on:** "Tab / Dialog / Dropdown systems show/hide on the Main
target …"; "Text-input focus is game-owned; key capture is the module's job".
**Tests:** none yet.

## There is ONE pointer pick: `UIFocusSystem` publishes it, hover consumers read it

The question "what is the pointer over?" is answered **once per frame, in one place**.
`UIFocusSystem`'s pointer pass already resolves the topmost `FocusableComponent` under the
cursor to drive hover-focus, press and activation; it now also **publishes** that answer on
the **cursor entity** as `PointerPickComponent` — the picked `Entity` (`default` for
"nothing") plus the `GameState.TotalTime` at which that entity *became* the picked one.
Every system that reacts to what the pointer is over — `TooltipSystem` and
`CursorHoverSystem` today — **reads that component and never hit-tests again**. Because the
pick is the same resolution focus and click act on, it carries the same filters for free
(in the active group, not tab-gated via `FocusableComponent.Disabled`, not control-disabled
via `ButtonStateComponent.IsDisabled`), and a hover affordance can never disagree with what
a click would do. The component is written only when the picked entity *changes*, so
`HoverStartTime` keeps running while the pointer rests and a consumer's dwell is one
subtraction (`state.TotalTime - pick.HoverStartTime`) with no private timer. A consumer must
still treat `Hovered` as untrusted — the pick is only refreshed while its owner runs, so it
re-checks `IsAlive` (a picked entity can be disposed the same frame).

**Why:** every "react to the hovered thing" feature (tooltip, hand cursor, hover sound,
highlight) is otherwise re-derived by its own `Rectangle(WorldPosition, Size)` sweep. Those
copies drift: they disagree on the topmost-wins rule, on which target's coordinates to
compare, and — the silent one — on the disabled / active-group filters, so a tooltip or a
hand cursor appears for a control a click cannot reach. One publisher and N readers makes
the agreement structural instead of a convention each new system has to remember. Putting
the pick on the *cursor* entity (rather than a world singleton) keeps it per-pointer, so it
survives a future second cursor.
**Breaks:** a consumer that runs its own hit-test reintroduces the drift the pick exists to
remove (that was `CursorHoverSystem` before this premise: group-blind, so it painted a hand
over a link trapped under an open dialog). A second writer of `PointerPickComponent` races
the dwell clock and makes every delay non-deterministic. Re-`Set`ting the component every
frame (instead of only on change) resets `HoverStartTime` continuously, so a dwell-gated
consumer never fires. Registering a consumer without `UIFocusSystem` yields no pick at all —
the consumer stands down (no tooltip, resting arrow), which is the intended degradation, not
a fallback hit-test.
**Depends on:** "`UIFocusSystem` is the single focus owner; pointer steals focus only on
mouse move; nav is group-scoped"; "`FocusableComponent.Disabled` (tab-gating) and
`ButtonStateComponent.IsDisabled` (control-disabled) are separate, and both skip nav";
cursor — "Cursor `TransformComponent.Position` depends on render target" (the pick compares
against `CursorInputComponent`'s virtual/world position by the focusable's `Target`, never
the cursor's own transform).
**Tests:** `MonoDreams.Tests/Ui/PointerPickTests.cs` (publication, topmost-wins, dwell clock,
the disabled / out-of-group filters, and `CursorHoverSystem` deriving its swap from the pick);
`MonoDreams.Tests/Ui/TooltipTests.cs` (`Tooltip_FollowsThePick_NotItsOwnHitTest`,
`PointerInsideADisabledControl_ShowsNoTooltip` — the two halves of "no second hit-test").

## The tooltip is a transient, system-owned, screen-space label that despawns with its pick

`TooltipComponent` is pure data on any **pickable** entity (anything carrying
`FocusableComponent`, on any render target): the label text plus an optional per-entity
`Delay` (`null` = use `TooltipStyle.Delay`, `0` = instant). `TooltipSystem` owns everything
else. It shows the picked entity's label once the dwell has elapsed, and the label entities
are **its own**: it creates them on show, repositions them every frame, rebuilds them when
the picked entity or its text changes, and disposes them on hover-out, on target death, and
on its own `Dispose`. They are never parented to the hovered entity (the system runs after
`HierarchySystem`, so a parent-relative label would render at a stale world position on the
frame it appears) and they carry no scene-serialization marker, so a transient tooltip never
lands in a saved scene. The label renders on a **screen-space** target (HUD by default; UI
allowed, world-space targets are refused in the constructor) at the style's `LayerDepth` —
below the cursor's `1.0` — and its top-left comes from the pure `TooltipPlacement.Place`,
which offsets from the pointer and **flips to the opposite side of the pointer** on
whichever axis would otherwise push it past the screen margin, clamping (never overflowing)
a label too big to fit either way.

**Why:** a tooltip is defined by *not* occupying layout space: it must float above
everything, follow the pointer, and vanish without the screen having to bookkeep it. That
only works if exactly one owner controls its lifetime — a game-created label would have to
be shown, hidden, re-measured, re-positioned and torn down by every screen that wants one,
which is the fiddly re-implementation this primitive exists to delete. The edge flip is the
non-obvious half: without it the tooltip of the right-most icon — precisely the one a user
hovers to discover — slides off screen. Keeping the placement pure (no world, no font, no
`GraphicsDevice`) is what makes that behavior testable at all.
**Breaks:** a game that creates or mutates the tooltip entities fights the system and races
them each frame. Placing the label on a world-space target puts the camera transform between
the pointer and the label, so the "edge" it flips at is not the screen's. Parenting it to the
hovered entity resurrects the stale-world-position frame and, worse, makes
`HierarchySystem.DisposeOrphans` cascade into it. Giving it a `LayerDepth` above the cursor
draws the label over the pointer.
**Depends on:** "There is ONE pointer pick: `UIFocusSystem` publishes it, hover consumers
read it"; rendering — "Three render targets, two behaviors"; "`MeshPrepSystem` writes the
world matrix once per frame" (the panel carries `VisibleComponent` so its world matrix is
written the frame it appears); rendering-text — "`TextPrepSystem` writes the
world-transformed position".
**Tests:** `MonoDreams.Tests/Ui/TooltipTests.cs` (dwell + per-entity override, hover-out and
target-death despawn, rebuild-on-retarget, dispose, the world-space-target refusal, and the
edge-flip / clamp matrix on `TooltipPlacement.Place`).

## Flexbox implements cross-axis Stretch and per-axis Fill with main-axis flex-grow distribution

The flexbox solver (`LayoutNodeComponent` + `AutoLayoutSystem`) now resolves
sizing beyond intrinsic measurement. Per node: `WidthFill` / `HeightFill`
mark an axis as "fill the container" (Figma semantics), and `FlexGrow`
weights how leftover *main-axis* space is shared among the fill children
(weight defaults to `1` when `FlexGrow <= 0`). On the **cross** axis a child
grows to the inner cross size when either the parent's `AlignItems ==
CrossAxisAlignment.Stretch` *or* the child's own cross-`Fill` flag is set;
stretched / cross-filled children pin to cross-position `0`. Resolution is
parent-driven and top-down: a node sizes its fill children only once its own
size is known, so the tree must be solved root-to-leaf.

**Why:** intrinsic-only sizing can't express "this row fills the panel width"
or "these two buttons split the remaining space" — the two most common layout
needs after centering. Folding Fill + flex-grow into the existing solver
(rather than a new layout pass) keeps one solver authoritative. Resolving in
the parent is required because only the parent knows the leftover space to
distribute.
**Breaks:** a fill child whose parent's size isn't yet resolved (solved
out of order, leaf-first) computes its fill against a zero or stale container
and collapses or overflows. Setting `FlexGrow` on a child that isn't a main-axis
fill child has no effect (grow only distributes among `MainFill` children) —
a silent no-op footgun.
**Depends on:** "`IntrinsicSizingSystem` runs before `AutoLayoutSystem`";
"`AutoLayoutBuilder` is the canonical entry point".
**Tests:** none yet.

## Open questions

- **Flexbox parity** — the solver supports flex-direction, justify,
  align, gap, padding, margin, cross-axis Stretch, per-axis Fill
  (`WidthFill` / `HeightFill`), and main-axis `FlexGrow` distribution,
  but not flex-shrink, flex-basis, wrap, or absolute positioning. Which
  of those become premises (must-have) vs aspirations (nice-to-have) is
  unsettled.
- **Tooltips on unpickable controls** — riding the pick means a disabled control
  (`ButtonStateComponent.IsDisabled`) and one gated out of the active group
  (`FocusableComponent.Disabled`) have no tooltip, because the pick skips them —
  yet "why is this greyed out?" is one of the tooltip's best uses. Whether the
  pick should carry a second, filter-free `HoveredAny` slot for exactly this (and
  what a hand cursor or a click-sound consumer should read) is unsettled; a
  second private hit-test in `TooltipSystem` is explicitly NOT the answer.
- **Re-measuring on content change** — `NeedsRemeasure` is set to true
  at construction and to false after measuring; nothing today flips it
  back to true when content changes. Dynamic text that grows
  mid-frame won't trigger a re-measure. Whether the framework should
  auto-detect changes (via `DefaultEcs.NotifyChanged` plumbing) is open.

## Aspirational direction

- Make `LayoutNodeComponent` itself the ECS component (drop the
  parallel pure-C# tree) once DefaultEcs supports the access pattern
  cheaply, collapsing the two-hierarchy split.
- ~~`ButtonInteractionSystem` as a *configurable* (action-by-callback)
  system that game code can install, recovering composability without
  game-specific coupling.~~ **Realized** as the `UIFocusSystem` +
  `ButtonVisualSystem` mechanism plus the `UIFocusActivated` message
  seam — see "The module owns the focus + visual MECHANISM and publishes
  `UIFocusActivated`".

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `IntrinsicSizingSystem` runs before `AutoLayoutSystem`
- Intrinsic sizing is via callback, not via reading the content entity
- `ButtonMeshPrepSystem` bakes world coords and must run AFTER `MeshPrepSystem`
- Image-backed buttons reuse `SimpleButtonComponent` with a transparent outline
- `ToggleSwitchComponent` drives a sprite's source rectangle from a bool
- `AutoLayoutBuilder` is the canonical entry point
- `LayoutNodeComponent` is a pure C# tree, not an ECS hierarchy
- The module owns the focus + visual MECHANISM and publishes `UIFocusActivated`; the ACTION stays game-side
- `UIFocusSystem` is the single focus owner; pointer steals focus only on mouse move; nav is group-scoped
- `FocusableComponent.Disabled` (tab-gating) and `ButtonStateComponent.IsDisabled` (control-disabled) are separate, and both skip nav
- Tab / Dialog / Dropdown systems show/hide on the Main target via `VisibleComponent` and gate focus via `FocusableComponent.Disabled`
- A combobox is a `TextInputComponent` driving a `DropdownComponent`'s filter
- Flexbox implements cross-axis Stretch and per-axis Fill with main-axis flex-grow distribution
- Text-input focus is game-owned; key capture is the module's job
- The text-input caret is a game-supplied mesh entity the system positions and toggles
