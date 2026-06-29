---
flow: dialogue
covers:
  - MonoDreams/dialogue/**
sensitive: false
---

# Running a Yarn conversation

A conversation is a Yarn node played one beat at a time, driven entirely by `DialogueSystem`
pulling the YarnSpinner VM forward. At build time a `.yarn` file passes through the content
pipeline (`YarnSpinnerImporter` → `YarnSpinnerProcessor`) into a `YarnProgram` (compiled
protobuf + a CSV string table); at construction the system merges every `YarnProgram` into one
`Yarn.Dialogue` runtime and registers `DialogueRunner` as the string-table/markup resolver. Game
code starts a conversation by publishing `DialogueStartMessage` with a node name; the system that
owns that node calls `_yarnDialogue.SetNode(...).Continue()`. Each `Continue()` synchronously
fires exactly one Yarn callback — `OnYarnLine`, `OnYarnOptions`, `OnYarnCommand`, or
`OnYarnDialogueComplete` — which mutates `DialogueStateComponent` and the system's owned UI
entities. The *player* (not the VM) drives advancement: a line reveals character-by-character via
the rendering-text pipeline, waits for an advance press, then the next `Continue()` pulls the
following beat. The whole UI hierarchy (box, text, indicator, option arrow, optional balloon) is
constructed and owned by the system — game code never assembles or toggles it directly.

## Entities & lifecycle

`DialogueStateComponent.CurrentPhase` is the line/option state machine; it lives on the root
entity and is mutated only by `DialogueSystem`.

- **None** → on `DialogueStartMessage` (node owned), `StartYarnDialogue` sets `IsActive`, shows
  the box, consumes the opening interact edge (so the same press doesn't advance line 1), and
  `Continue()`s into the first beat.
- **Line** (`OnYarnLine`): the localized, speaker-split, word-wrapped text is written to the text
  entity's `DynamicTextComponent` with `VisibleCharacterCount = 0`. `TextUpdateSystem` (rendering-
  text) advances the reveal each frame; `UpdateLinePhase` shows the continue indicator only once
  `IsRevealed`. Advance press **mid-reveal** saturates `VisibleCharacterCount` (skip to full
  text); advance press **after reveal** calls `Continue()` → next beat.
- **Options** (`OnYarnOptions`): available options are copied into `CurrentOptions` /
  `CurrentOptionIDs`, `SelectedOptionIndex = 0`, the line text is cleared, and `ShowOptions`
  spawns one instant-revealed text entity per option plus the selection arrow. Up/Down or live
  cursor hover moves `SelectedOptionIndex`; interact or a left-button release on the hovered
  option calls `ConfirmSelectedOption` → `SetSelectedOption(id).Continue()` → next beat.
- **Command** (`OnYarnCommand`): not a `CurrentPhase` value — it publishes `DialogueCommandMessage`
  and sets `_pendingContinue`. The *next* `Update` clears the flag and `Continue()`s once, outside
  the Yarn handler, so the conversation flows past the command without a player press.
- **Complete** (`OnYarnDialogueComplete`): `DeactivateDialogue` hides all chrome, clears text,
  drops `_pendingContinue`, resets to `None`, and publishes `DialogueActiveMessage(false)`.

Per-line UI entities (the option text entities) are the only ones created/disposed each beat;
box, text, indicator, and arrow persist for the system's lifetime and are shown/hidden in place.

## Invariants

Authoritative list in [`MonoDreams/dialogue/docs/premises.md`](../../MonoDreams/dialogue/docs/premises.md); the ones this flow's ordering leans on:

- A `<<command>>` defers its post-command `Continue()` to the next `Update` (`_pendingContinue`) — never re-enters the Yarn VM from inside `OnYarnCommand`. ("Yarn commands are surfaced as `DialogueCommandMessage` and auto-advance.")
- `DialogueStartMessage` is broadcast to every `DialogueSystem`; only the one whose merged program `NodeExists` reacts. ("`DialogueStartMessage` routes by node ownership"; "`DialogueRunner` is per-system, not per-screen.")
- The reveal slices an already-wrapped string; advancing mid-reveal saturates the count rather than calling `Continue()`. ("Line + option text wrap to the box width.")
- The opening interact press is consumed in `StartYarnDialogue` so it can't advance the first line on the same frame.
- The system owns its UI hierarchy; external `VisibleComponent` toggles are overwritten on the next transition. ("`DialogueSystem` constructs its own UI entity hierarchy.")

## Load-bearing quantities

- `RevealingSpeed` — characters/second of the line reveal (default 20 on the line entity), consumed by `TextUpdateSystem` as `(TotalTime − RevealStartTime) * RevealingSpeed`. Options use `0` (instant). See rendering-text — "Text pipeline order".
- `VisibleCharacterCount` — int index into the wrapped string; `0` = nothing shown, saturated to length = fully revealed. The seam the reveal animation and the mid-reveal skip both write.
- `SelectedOptionIndex` — int, clamped to `[0, CurrentOptions.Count − 1]`; the index `ConfirmSelectedOption` maps through `CurrentOptionIDs` to a Yarn option ID.
- `_textAreaWidth` — rendered-pixel wrap width (`BitmapFont.MeasureString * textScale`); in anchored mode it is recomputed per beat as the balloon resizes to its final wrapped text.

## Failure modes

- **Re-entrant `Continue()` on a command** — calling `Continue()` inside `OnYarnCommand` instead of deferring faults the Yarn VM; dropping `_pendingContinue` instead leaves every `<<command>>` re-showing the previous line and forces an extra advance press. Highest-risk bug in this flow.
- **Runner/state desync** — mutating `DialogueStateComponent` without driving the Yarn VM (or vice versa) leaves the UI showing one beat while the VM sits on another; the next `Continue()` skips or repeats content.
- **Advancing mid-reveal treated as next-beat** — if the `IsRevealed` guard in `UpdateLinePhase` is bypassed, the first advance press skips the line entirely instead of completing the reveal.
- **Option index drift** — confirming against `SelectedOptionIndex` without clamping to `CurrentOptionIDs`, or caching option hit-bounds at show-time instead of reading live world bounds, sends the wrong Yarn option ID or breaks hover after a resize.
- **Silent no-start** — publishing `DialogueStartMessage` for a node no registered system owns is ignored with no error; two systems owning the same node name both activate. Distinct node names per system are required.
- **Content-pipeline build failure** — a csproj that loses `CopyLocalLockFileAssemblies` / `EnableDynamicLoading` breaks MGCB's load of `YarnSpinnerImporter`'s transitive deps at build time, not runtime.
