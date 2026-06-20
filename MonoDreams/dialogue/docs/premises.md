# dialogue — premises

> Technical invariants the engine assumes about the YarnSpinner dialogue
> module: `DialogueSystem`, `DialogueStateComponent`,
> `DialogueRunner`, the Yarn content pipeline
> (`YarnSpinnerImporter`/`Processor`/`YarnProgram`), and the
> `DialogueStartMessage` / `DialogueActiveMessage` contracts. Read this
> before changing any of those pieces or wiring a dialogue trigger.

## Dialogue is started by `DialogueStartMessage`, not by the module itself

The dialogue module does not own the *trigger* for a conversation.
`DialogueSystem` subscribes to `DialogueStartMessage` (carrying a
target node name) and reacts; it does not inspect collision zones, NPC
proximity, or scripted triggers. Game code is responsible for
publishing the message in response to whatever its triggering condition
is (collision zone, NPC interaction, scripted event).

**Why:** triggers are game-specific — a platformer fires dialogues on
proximity, a visual novel fires them on scene load, a roguelike fires
them on random encounter. Embedding any of those in the module would
foreclose the others. The message-passing seam is the engine's
extension point.
**Breaks:** installing this module without writing a trigger system
results in a dialogue runner that never starts. The dev sees
no error — just a silent runtime where their `.yarn` files are loaded
but never advance. The canonical pattern is
`MonoDreams.Examples/System/Dialogue/ZoneDialogueTriggerSystem.cs`.
**Tests:** none yet (the integration test
`MonoDreams.Tests/IntegrationTests/BlenderLevelTests.cs::PlayerReachesDialogueZone`
exercises one trigger pattern but doesn't pin this as a contract).
**Depends on:** —

## Yarn content pipeline needs `CopyLocalLockFileAssemblies` + `EnableDynamicLoading`

The `YarnSpinnerImporter` runs inside MonoGame's content pipeline
(MGCB) at build time. MGCB loads importer/processor assemblies
dynamically — without `CopyLocalLockFileAssemblies=true` and
`EnableDynamicLoading=true` set on the consuming project, the
YarnSpinner transitive dependencies aren't copied next to the importer
DLL and MGCB fails with `Could not load file or assembly` at build
time. The module's manifest adds both properties automatically; a
hand-edited csproj that loses them breaks the content build.

**Why:** YarnSpinner has its own transitive dependency chain
(Yarn.Compiler, protobuf, etc.). The two MSBuild properties together
make MGCB-as-host see them; without them, dynamic loading inside MGCB
can't resolve the chain.
**Breaks:** content build fails with a runtime assembly-load error
at MGCB time, not at runtime. The fix is non-obvious (the YarnSpinner
plugin DLL itself is present, just not its dependencies).
**Tests:** none yet.
**Depends on:** —

## `DialogueSystem` constructs its own UI entity hierarchy

`DialogueSystem`'s constructor creates a root entity plus three child
entities (box, text, indicator) — and, in balloon mode, a fourth (the
inner talk balloon) — wired via `SetParent`. Game code does not — and
should not — create these entities; the constructor returns a system
that already owns its UI. Toggling the dialogue on/off swaps the
children's `VisibleComponent` and clears their textures, but the
entities persist for the system's lifetime.

**Why:** the dialogue UI has cross-cutting state (the indicator's
position is anchored to the box's bottom-right, options stack
relative to the root, text reveals at a per-frame rate). Owning the
hierarchy means the system can guarantee its own invariants without
the game having to assemble the right entity shape.
**Breaks:** if a game tries to set `VisibleComponent` on
`_dialogueState.BoxEntity` from outside, `DialogueSystem`'s next state
transition will overwrite it. The dialogue UI is opaque to game code
once installed.

**Mesh chrome mode.** Passing `chromeFill` (with optional `chromeOutline` /
`chromeThickness` / `indicatorColor`) switches the box, balloon, and indicator from
sprite nine-patches to generated meshes (filled rect + outline for the box, an outline
frame for the balloon, a small filled caret for the indicator), so a screen can dress
dialogue without any sprite assets. The textured path is unchanged and is what
`MonoDreams.Examples` uses; the two are mutually exclusive (`chromeFill.HasValue` selects
mesh mode). In mesh mode `dialogBoxTexture` / `indicatorTexture` may be null, the panels
keep `VisibleComponent` permanently (so `MeshPrepSystem` refreshes their world matrices)
and are shown/hidden by filling vs. emptying their mesh — the same empty-to-hide rule the
indicator and options already rely on, because the dialogue lives on the always-rendering
UI target. Balloon mode in mesh mode is driven by `portraitGutter > 0` rather than a
balloon texture.
**Tests:** none yet.
**Depends on:** rendering-text — "Use `DynamicTextComponent` for any
text"; ui — "`LayoutNodeComponent` is a pure C# tree, not an ECS
hierarchy" (dialogue does NOT use ui's layout; it positions children
by hand-rolled offsets).

## Dialogue renders on a configurable target — default UI, Main when anchored

All of the dialogue entities (root, box, text, indicator, and the
optional inner balloon) target a single `renderTarget` passed to the
constructor, **defaulting to `RenderTargetID.UI`**. In the default
(UI) case they live in screen-space at fixed coordinates relative to
the virtual resolution, are not subject to culling, and sit between
world (Main) and cursor (HUD) in z-order. Passing
`renderTarget: RenderTargetID.Main` together with an `anchorEntity`
switches to *anchored* mode (see the next premise), where the whole
hierarchy is world-space and floats above a character.

**Why:** default dialogue UI is HUD-like in behavior (always visible
when active, fixed-position) but must sit *below* the cursor so the
user can click through the dialogue chrome without occlusion — UI
target is the right slot. A game that instead wants over-the-head
speech balloons needs the same chrome on Main so it tracks the camera.
**Breaks:** putting the *default* bottom panel on Main subjects it to
camera transforms — the box would scroll off-screen when the camera
moves. Putting it on HUD would put it above the cursor. Conversely, an
anchored balloon left on UI would not track its character.
**Tests:** none yet.
**Depends on:** rendering — "Three render targets, two behaviors".

## Anchored dialogue floats above a world entity on the Main target

Passing an `anchorEntity` (with `renderTarget: RenderTargetID.Main` and
mesh chrome) puts `DialogueSystem` in *anchored* mode: the dialogue is
drawn as a compact, tailed speech balloon whose root transform is
repositioned **every frame** to the anchor's `WorldPosition + anchorOffset`,
centred over and lifted above the anchor so the tail points at its head.
Anchored mode is **mesh-chrome only** (the constructor throws if
`anchorEntity` is passed without `chromeFill`), forces the legacy
text-on-box layout (no portrait gutter / inner balloon), and uses
`boxWidthOverride` for a compact bubble width. Because the dialogue now
lives on Main — which consults `VisibleComponent` — the text entity is
given the tag (the box/balloon/indicator meshes and option entities
already carry it); a screen running `CullingSystem` would strip it, so
anchored dialogue assumes the entity stays inside the camera view.

**Why:** an over-the-head balloon is a common dialogue presentation and
the same Yarn runtime / reveal / options / command path should drive it —
only *where* the chrome is drawn changes. Repositioning the root (and
letting `HierarchySystem`, which must run after `DialogueSystem`,
re-lay the children) keeps a single source of truth for layout.
**Breaks:** mutating the root each frame without `HierarchySystem`
downstream leaves the children at stale positions; omitting the
`VisibleComponent` on the text leaves the balloon framed but wordless on
Main; passing `anchorEntity` in texture mode has no show/hide path and
throws.
**Tests:** none yet.
**Depends on:** rendering — "Three render targets, two behaviors"
(Main consults `VisibleComponent`); foundation — hierarchy/transform
dirty propagation (root reposition cascades to children).

## `DialogueStartMessage` routes by node ownership

When more than one `DialogueSystem` is registered (the supported way to
run independent conversations — see "`DialogueRunner` is per-system"),
**every** instance receives each `DialogueStartMessage` (DefaultEcs
publishes to all subscribers). Each instance reacts only if its merged
Yarn program owns the requested node: `OnDialogueStart` guards on
`_yarnDialogue.NodeExists(message.StartNode)` and returns otherwise. So
a message addressed to a node only one system can play is delivered to
exactly that system; game code does not tag messages with a target.

**Why:** node names are already unique addresses into a runner's
program set, so they double as the routing key — no extra message field
or per-system identifier is needed. This is what lets the dialogue demo
run a cow conversation (node `Start`) and a bird conversation (node
`Bird`) from two `DialogueSystem` instances without cross-triggering.
**Breaks:** if two systems load the *same* node name, both react and
both activate — distinct node names per system are required. Publishing
a `DialogueStartMessage` for a node no system owns is silently ignored
(no active conversation, no error).
**Tests:** none yet.
**Depends on:** —

## `DialogueRunner` is per-system, not per-screen

A single `DialogueSystem` instance constructs one `DialogueRunner` and
loads all `YarnProgram`s passed to its constructor into one merged Yarn
runtime. Game code that wants multiple independent dialogues (e.g. a
"speak" dialogue vs an "examine" dialogue) needs multiple
`DialogueSystem` instances, each with its own root entity and Yarn
runtime.

**Why:** the merged-protobuf approach (`combinedProgram.MergeFrom(...)`)
lets a single runner address nodes across all loaded `.yarn` files by
name. That's the YarnSpinner-standard pattern for character-by-character
dialogue scripts. Multiple runners per game is allowed but uncommon.
**Breaks:** trying to start a node that lives in a `YarnProgram` not
passed to the constructor results in a YarnSpinner runtime error
(unknown node), not a framework error.
**Tests:** none yet.
**Depends on:** —

## Yarn commands are surfaced as `DialogueCommandMessage` and auto-advance

When the running Yarn script reaches a `<<command>>`, `DialogueSystem`'s
`OnYarnCommand` publishes a `DialogueCommandMessage` carrying the raw
command text, then sets an internal `_pendingContinue` flag. The next
`Update` clears the flag and calls `_yarnDialogue.Continue()` once —
*outside* the Yarn handler — so the conversation flows past the command
to the next line on the following frame. Game code reacts to the message
(emotes, SFX, flags) without owning any dialogue advancement.

**Why:** Yarn's `CommandHandler` fires synchronously inside `Continue()`;
calling `Continue()` again from within it would re-enter the Yarn VM. The
deferred single-frame continue keeps inline commands (e.g.
`<<emote npc happy>>` between lines) transparent — the player never has
to press advance to clear a command.
**Breaks:** if `_pendingContinue` is dropped, every `<<command>>` leaves
the conversation showing the *previous* line again and the player must
press the advance key an extra time to proceed. If `Continue()` is
instead called re-entrantly inside `OnYarnCommand`, the Yarn dialogue VM
can fault. The canonical consumer is
`MonoDreams/dialogue/demo/DialogueDemoScreen.cs` (emotes).
**Tests:** none yet.
**Depends on:** —

## Line + option text wrap to the box width; `sideInset` reserves side room

`DialogueSystem` word-wraps both the spoken line and each option to the box's inner width
before display (greedy wrap measured with the `BitmapFont` at the configured `textScale`,
inserting `\n`). The reveal animation slices the already-wrapped string, so embedded
newlines just advance instantly. The wrap width is `boxWidth − 2·textOffset.X − 2·sideInset`:
the optional `sideInset` constructor arg reserves symmetric horizontal zones on each side of
the text so a game can draw a speaker portrait there without the text colliding with it (the
continue indicator is likewise inset past the right zone). `textScale` and `indicatorSize`
are constructor args too (sensible small defaults) rather than hardcoded.

**Why:** at the previous hardcoded `0.5` scale with no wrapping, lines overflowed the box and
the screen, and fixed-pixel option spacing made options overlap. Wrapping + scale-aware
option stacking fixes both; `sideInset` is what lets the dialogue demo place its left/right
emote portraits inside the box.
**Breaks:** if `WrapText` is bypassed, long lines overflow again. If option stacking ignores
each option's wrapped line count, multi-line options overlap. If a portrait is drawn without
passing a matching `sideInset`, text renders on top of it. (In balloon mode — see the next
premise — `sideInset` is ignored; the wrap width comes from the balloon interior instead.)
**Tests:** none yet.
**Depends on:** rendering-text — "Use `DynamicTextComponent` for any text".

## Balloon mode: an inner talk balloon + a left portrait gutter (optional)

Passing a `talkBalloonTexture` (and usually a `talkBalloonNinePatch`) switches `DialogueSystem`
into *balloon mode*: it creates a fourth child entity — an inner nine-patch panel drawn at
`layerDepth + 0.005` (between the box and the text) — and lays the line text, options, and
continue indicator out **inside that balloon's interior** (inset by `balloonPadding`), not on
the box. A `portraitGutter` width reserves the box's left region for a game-drawn emote frame;
the balloon starts at `boxX + portraitGutter`. The system exposes that reserved region as
`PortraitGutterBounds` (a UI-space `Rectangle`) so game code places its frame against the real
layout instead of re-deriving box geometry. `boxHeight` and `boxNinePatch` are likewise
constructor args, so the box can be a different size and back onto a different panel texture
than the default 128×48 "dialog box medium" art. With `talkBalloonTexture == null` the system
stays in legacy mode (text on the box, symmetric `sideInset`, no balloon) and none of these
apply — the two modes are mutually exclusive.

**Why:** the text/options/indicator are engine-owned, so the panel that visually wraps them and
the inset that positions them must be owned in the same place — otherwise game code has to
duplicate the box/inset math (which the wrap premise warns desyncs). Balloon mode keeps the
single source of truth while letting a game render the two-layer "framed emote beside a talk
balloon" look (the dialogue demo's Sprout Lands box). `PortraitGutterBounds` is the seam.
**Breaks:** placing the emote frame from hand-computed box geometry instead of
`PortraitGutterBounds` drifts out of alignment when `boxHeight`/`portraitGutter`/margins change.
Forgetting that the balloon entity needs `VisibleComponent` toggled in tandem with the box (it
gates `SpritePrepSystem`, which fills the nine-patch texture) leaves the balloon invisible.
Passing both `sideInset` and a balloon expecting both to apply: only the balloon does.
**Tests:** none yet.
**Depends on:** rendering — "Three render targets, two behaviors" (SpritePrep requires
`VisibleComponent` even on UI to refill the nine-patch texture).

## Open questions

- **Localization integration** — `DialogueRunner.AddStringTable` and
  `GetLocalizedTextForLine` are exposed but the locale-switching
  workflow isn't documented yet.

(Multi-`DialogueSystem` coordination was an open question; it is now
settled — see "`DialogueStartMessage` routes by node ownership".)

## Aspirational direction

- Replace the hand-rolled box/text/indicator hierarchy with `ui`'s
  `AutoLayoutBuilder` so dialogue benefits from flexbox-driven
  positioning (especially for option lists).
- Standard set of `DialogueAdvanceMessage` / `DialogueChoiceMessage` /
  `DialogueEndMessage` in the module so games don't have to redefine
  the input bindings — these exist in
  `MonoDreams.Examples/Message/` today but should probably move into
  the module.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- Dialogue is started by `DialogueStartMessage`, not by the module itself
- Yarn content pipeline needs `CopyLocalLockFileAssemblies` +
  `EnableDynamicLoading`
- `DialogueSystem` constructs its own UI entity hierarchy
- Dialogue renders on a configurable target — default UI, Main when anchored
- Anchored dialogue floats above a world entity on the Main target
- `DialogueStartMessage` routes by node ownership
- `DialogueRunner` is per-system, not per-screen
- Yarn commands are surfaced as `DialogueCommandMessage` and auto-advance
- Line + option text wrap to the box width; `sideInset` reserves side room
- Balloon mode: an inner talk balloon + a left portrait gutter

`sideInset` (legacy symmetric reserve) and `portraitGutter` (balloon-mode left reserve) are
overlapping ways to make room for a portrait. They coexist for now to avoid touching the
Examples call sites; a future cleanup should unify them into one asymmetric-inset concept.
