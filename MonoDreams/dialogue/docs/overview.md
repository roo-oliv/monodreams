# dialogue — overview

YarnSpinner-based dialogue: author `.yarn` files in Blender or VSCode, load them via the bundled MGCB content pipeline, and run them with `DialogueSystem` which owns its own dialogue-box / text / waiting-indicator UI hierarchy. Trigger conversations by publishing `DialogueStartMessage` from game-side systems (collision zones, NPC interactions, scripted events). Install for narrative games — adventures, RPGs, visual novels.

## Purpose

YarnSpinner is the de-facto open-source dialogue runtime for indie games; this module makes it work cleanly with MonoGame and the MonoDreams ECS. Two threads of integration: (1) a content-pipeline importer/processor loads `.yarn` files as `YarnProgram` content assets at build time; (2) a `DialogueSystem` constructs its own three-entity UI hierarchy (box, text, indicator), drives a `DialogueRunner` per system instance, and reveals lines character-by-character using `DynamicTextComponent` from `rendering-text`. The module is deliberately game-agnostic about *when* a dialogue starts — game code publishes `DialogueStartMessage` from its own trigger system (proximity, scene-load, scripted event). The canonical trigger pattern is in `MonoDreams.Examples/System/Dialogue/ZoneDialogueTriggerSystem.cs`.

## What ships

### Components

- `DialogueStateComponent` — runtime state for the active dialogue: current line, options, advance phase, owned UI entity handles

### Systems

- `DialogueSystem` — subscribes to `DialogueStartMessage`, owns the runner, advances dialogue per input, manages its own UI hierarchy (box + text + indicator)

### Messages

- `DialogueStartMessage` — game code publishes to start a conversation; carries the target Yarn node name
- `DialogueActiveMessage` — emitted while a dialogue is active (other game systems can suppress input, pause AI, etc.)

### Content pipeline

- `YarnSpinnerImporter` / `YarnSpinnerProcessor` — MGCB-side importer + processor for `.yarn` files
- `YarnSpinnerReader` / `YarnSpinnerWriter` — content-pipeline serialization
- `YarnProgram` — runtime asset type loaded via `content.Load<YarnProgram>(...)`
- `YarnSpinnerFile`, `YarnTranslation` — auxiliary types for translation/CSV workflows
- `InMemoryVariableStorage` — default variable storage for the runner

### Runtime

- `DialogueRunner` — wraps the YarnSpinner runtime, merges multiple `YarnProgram`s into one, exposes line-by-line advance API

## Pipeline wiring

**Content pipeline.** YarnSpinner's importer needs the project DLL plus dynamic-loading enabled for MGCB to load YarnSpinner's transitive dependencies. The module manifest sets these automatically when installed:

```xml
<PropertyGroup>
  <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  <EnableDynamicLoading>true</EnableDynamicLoading>
  <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
  <MonoGameMGCBAdditionalArguments>$(MonoGameMGCBAdditionalArguments) /reference:$(MSBuildThisFileDirectory)bin/$(Configuration)/net8.0/$(AssemblyName).dll</MonoGameMGCBAdditionalArguments>
</PropertyGroup>
```

Without these three properties the YarnSpinner transitive dependencies aren't copied next to the importer DLL at content-build time and MGCB fails with `Could not load file or assembly`.

**Yarn assets.** Author `.yarn` files in `Content/Dialogues/`. Add to your `.mgcb`:
```
#begin Dialogues/intro.yarn
/importer:YarnSpinnerImporter
/processor:YarnSpinnerProcessor
/build:Dialogues/intro.yarn
```

**Runtime wiring.**
1. Load yarn programs: `var program = content.Load<YarnProgram>("Dialogues/intro");`
2. Construct `DialogueSystem` with the loaded programs, font, dialog-box texture, indicator texture, and input bindings.
3. Add it to your update pipeline **after input** and **before rendering**.
4. **Trigger dialogues from a game-side system.** This module does not own *when* a conversation starts. Write a system that publishes `DialogueStartMessage { TargetNode = "..." }` in response to your trigger (collision zone, NPC interaction, scripted event). See `MonoDreams.Examples/System/Dialogue/ZoneDialogueTriggerSystem.cs` for the canonical collision-zone pattern.

The dialogue UI lives on `RenderTargetID.UI` (between Main and HUD) and is owned by the system — don't try to manipulate the entities directly.

## Cross-module dependencies

- `rendering-text` — dialogue lines render via `DynamicTextComponent`; the reveal animation drives the typewriter effect.
- `ui` — declared dependency for the UI render-target conventions; the dialogue currently uses hand-rolled positioning rather than `AutoLayoutBuilder` (aspirational migration — see premises).

## Extension points

- **Multiple dialogue contexts.** Construct multiple `DialogueSystem` instances (e.g. one for "speak" interactions, one for "examine" interactions, or one per NPC). Each owns its own runner, UI, and Yarn-program set. All instances receive every `DialogueStartMessage`, but each reacts only to nodes its own program owns (`NodeExists` routing) — give each context distinct node names. The dialogue demo runs two: a bottom-box cow (node `Start`) and an over-the-head balloon bird (node `Bird`).
- **Anchored (over-the-head) balloons.** Pass `renderTarget: RenderTargetID.Main`, an `anchorEntity`, an `anchorOffset`, and mesh chrome (`chromeFill`) to draw the dialogue as a compact tailed speech balloon that floats above a world entity and tracks it each frame, instead of the fixed bottom panel. See `MonoDreams/dialogue/demo/DialogueDemoScreen.cs` (the bird).
- **Localization.** `DialogueRunner.AddStringTable` and `GetLocalizedTextForLine` are exposed; the locale-switching workflow is open (see premises).
- **Custom Yarn commands and functions.** Hook into the underlying YarnSpinner `Dialogue` exposed by `DialogueRunner` to register your own `<<commands>>` and `Function`s for game-side script integration.
- **Custom dialogue UI.** The module owns its UI hierarchy today; replacing it means building a parallel system or refactoring `DialogueSystem` to take a UI-builder callback (aspirational direction).

## See also

- [Premises](premises.md) — load-bearing invariants (`DialogueStartMessage` as trigger seam, content-pipeline csproj property requirements, UI hierarchy ownership, render target placement, per-system runner)
- Related modules: `rendering-text` (lines reveal via `DynamicTextComponent`), `ui` (dialogue is on UI render target; aspirational to use `AutoLayoutBuilder`), `foundation` (input bindings flow through the input handler for advance/choice keys)
