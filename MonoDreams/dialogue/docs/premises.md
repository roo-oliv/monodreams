# dialogue — premises

> Technical invariants the engine assumes about the YarnSpinner dialogue
> block: `DialogueSystem`, `DialogueStateComponent`,
> `DialogueRunner`, the Yarn content pipeline
> (`YarnSpinnerImporter`/`Processor`/`YarnProgram`), and the
> `DialogueStartMessage` / `DialogueActiveMessage` contracts. Read this
> before changing any of those pieces or wiring a dialogue trigger.

## Dialogue is started by `DialogueStartMessage`, not by the block itself

The dialogue block does not own the *trigger* for a conversation.
`DialogueSystem` subscribes to `DialogueStartMessage` (carrying a
target node name) and reacts; it does not inspect collision zones, NPC
proximity, or scripted triggers. Game code is responsible for
publishing the message in response to whatever its triggering condition
is (collision zone, NPC interaction, scripted event).

**Why:** triggers are game-specific — a platformer fires dialogues on
proximity, a visual novel fires them on scene load, a roguelike fires
them on random encounter. Embedding any of those in the block would
foreclose the others. The message-passing seam is the engine's
extension point.
**Breaks:** installing this block without writing a trigger system
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
time. The block's manifest adds both properties automatically; a
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
entities (box, text, indicator) wired via `SetParent`. Game code does
not — and should not — create these entities; the constructor returns
a system that already owns its UI. Toggling the dialogue on/off swaps
the children's `VisibleComponent` and clears their textures, but the
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
**Tests:** none yet.
**Depends on:** rendering-text — "Use `DynamicTextComponent` for any
text"; ui — "`LayoutNodeComponent` is a pure C# tree, not an ECS
hierarchy" (dialogue does NOT use ui's layout; it positions children
by hand-rolled offsets).

## Dialogue UI renders on the UI render target

All four entities (root, box, text, indicator) target
`RenderTargetID.UI`. They live in screen-space at fixed coordinates
relative to the virtual resolution, are not subject to culling, and
sit between world (Main) and cursor (HUD) in z-order.

**Why:** dialogue UI is HUD-like in behavior (always visible when
active, fixed-position) but must sit *below* the cursor so the user
can click through the dialogue chrome without occlusion. UI target is
the right slot.
**Breaks:** putting the dialogue on Main subjects it to camera
transforms — the dialogue box would scroll off-screen when the camera
moves. Putting it on HUD would put it above the cursor.
**Tests:** none yet.
**Depends on:** rendering — "Three render targets, two behaviors".

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

## Open questions

- **Multi-`DialogueSystem` coordination** — if two instances are
  registered, both will react to the same `DialogueStartMessage`
  (DefaultEcs publishes to every subscriber). The intended interaction
  pattern (filter by an additional message field? add a target-system
  identifier?) is unsettled.
- **Localization integration** — `DialogueRunner.AddStringTable` and
  `GetLocalizedTextForLine` are exposed but the locale-switching
  workflow isn't documented yet.

## Aspirational direction

- Replace the hand-rolled box/text/indicator hierarchy with `ui`'s
  `AutoLayoutBuilder` so dialogue benefits from flexbox-driven
  positioning (especially for option lists).
- Standard set of `DialogueAdvanceMessage` / `DialogueChoiceMessage` /
  `DialogueEndMessage` in the block so games don't have to redefine
  the input bindings — these exist in
  `MonoDreams.Examples/Message/` today but should probably move into
  the block.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- Dialogue is started by `DialogueStartMessage`, not by the block itself
- Yarn content pipeline needs `CopyLocalLockFileAssemblies` +
  `EnableDynamicLoading`
- `DialogueSystem` constructs its own UI entity hierarchy
- Dialogue UI renders on the UI render target
- `DialogueRunner` is per-system, not per-screen
