# foundation — premises

> Technical invariants the engine assumes about the foundation module:
> `TransformComponent`, `ChildOfComponent`, `HierarchySystem`,
> `TransformCommitSystem`, the `EntityHierarchy` resource, the input/replay
> scaffold, the `Logger`, and the run-state model (`GameState.RunMode`,
> `EditTimeBehavior`, `GatedSystem`). Read this before changing any of those
> pieces or any system that depends on them.

## Don't mix two Transform-shaped components in one project

A project may define its own `MyTransform` with different trade-offs, but
mixing `TransformComponent` and a custom transform in the same world breaks
the bundled consumers (collision, rendering, camera, culling), which all
read `TransformComponent` directly.

**Why:** the bundled modules currently couple to `TransformComponent` by
type rather than by shape. Until that loosens, consistency within a
project is the only thing preventing silent breakage in modules the
developer didn't write.
**Breaks:** colliders, cameras, and the render pipeline silently operate
on the framework `TransformComponent` while game systems mutate the
custom one. Entities appear in the wrong place, collide based on stale
positions, or fail to render.
**Tests:** none yet.
**Depends on:** —

## `TransformComponent.Delta` is meaningful only after `TransformCommitSystem` ran

`TransformComponent.HasMoved` and `TransformComponent.Delta` reflect the
change between two committed positions (`Position - LastPosition`). They
are valid only after `TransformCommitSystem` ran for the *previous* frame.
Reading them mid-frame or before commit returns stale data with no error
or warning.

**Why:** the collision system's swept tests depend on a meaningful
`Delta`. The framework intends to enforce consistency via interaction
methods on `TransformComponent`, but does not today — `Delta` is a public
property whose backing is the difference of two mutable fields.
**Breaks:** swept collision detection sees an empty or wrong `Delta` and
misses dynamic contacts; objects pass through walls. With no warning, the
dev hunts for hours in the collision system before finding the missing
`TransformCommitSystem` in the pipeline.
**Tests:** none yet (indirectly exercised by
`MonoDreams.Tests/IntegrationTests/InfiniteRunnerTests.cs::PlayerFallsOffLeftEdge`,
which depends on swept collision working).
**Depends on:** collision — "Swept collision reads `TransformComponent.Delta`".

## `TransformComponent.IsDirty` cascades through the parent chain

Mutating a parent's position, rotation, or scale marks every descendant
dirty. `WorldMatrix` is a cached property whose getter re-walks the chain
when next read. `HierarchySystem.PropagateDirtyFlags()` is the system that
propagates the flag through the descendant tree each frame.

**Why:** caching the world matrix avoids recomputing it on every read,
which would dominate hot paths in deep hierarchies (UI layouts, nested
entities).
**Breaks:** if a system bypasses the dirty flag (e.g., mutates internal
fields directly via reflection), descendants render and collide at stale
world positions while their parents have moved.
**Tests:** none yet.
**Depends on:** —

## `ChildOfComponent` and `TransformComponent.Parent` are two intentional links

`TransformComponent.Parent` is the matrix link — it controls how
`WorldMatrix` cascades. `ChildOfComponent` is the structural link — it
controls lifecycle (cascade disposal). `HierarchySystem.SyncTransformParents()`
syncs the matrix link from the structural link each frame. Hierarchy logic
must read the link relevant to its concern.

**Why:** the split came from hierarchical UI like a dialogue panel, where
a banner, avatars, text, and a waiting-indicator move and dispose together
but may not share matrix scaling. The split is a known wart and is on the
refactor backlog (consolidation desired).
**Breaks:** code that reads only `TransformComponent.Parent` misses the
disposal cascade; code that reads only `ChildOfComponent` misses the
matrix behavior. A future consolidation will collapse both into one
concept.
**Tests:** none yet.
**Depends on:** —

## `HierarchySystem` must run ahead of any system reading WorldPosition

Per ECS purity, systems are pure functions and ordering is the screen's
responsibility. The reference pipeline places `HierarchySystem` after
physics (so parent-child movement is composed from the latest local
positions) and before camera/render/culling.

**Why:** any system reading `TransformComponent.WorldPosition` /
`WorldRotation` / `WorldScale` gets the cached world transform; the cache
is only fresh after `HierarchySystem` has processed dirty descendants
this frame.
**Breaks:** a child entity renders at last frame's world position, a
follow-camera tracks a stale target, a collider tests against stale world
vertices.
**Tests:** none yet.
**Depends on:** rendering — "Rendering systems run last in the pipeline".

## Children are disposed with their parents

`HierarchySystem.DisposeOrphans()` cascade-disposes any entity whose
parent (via `ChildOfComponent`) is no longer alive. There is no supported
way today for a child to outlive its parent.

**Why:** complex hierarchical entities (dialogue UI, composed characters)
need a single lifecycle handle. Cascade disposal makes this the default.
**Breaks:** a system that disposes a parent without `ChildOfComponent`-linked
children expects the children to die; if `ChildOfComponent` is missing on
a visually-attached child, the child orphan-renders at world origin.
**Tests:** none yet.
**Depends on:** —

## `WorldMatrix` is cached and computed lazily

`TransformComponent.WorldMatrix` is a cached property. The getter walks
the parent chain on demand only when the cached value is dirty (either
the transform itself or any ancestor is dirty); otherwise it reuses the
cached matrix.

**Why:** matrix recomputation is the dominant cost of deep hierarchies;
caching cuts it to once per dirty span per frame.
**Breaks:** any logic that bypasses the cache (recomputes the matrix
manually, or mutates `TransformComponent` fields via reflection without
flagging dirty) breaks the contract — downstream systems read a stale
`WorldMatrix` without knowing.
**Tests:** none yet.
**Depends on:** —

## `Logger` requires `Initialize` before any write

`Logger` is a static, lock-protected singleton in `MonoDreams.State`.
`Logger.Initialize(outputDirectory)` must be called once before the first
`Info` / `Debug` / `Warning` / `Error` call; otherwise the write
silently drops. `Logger.Shutdown()` must be called before process exit to
flush the buffered writer. The `MONODREAMS_DEBUG_DIR` environment variable
overrides the directory for test isolation.

**Why:** the lock-protected initializer is the only thing serializing
writes across threads; an uninitialized logger has no `StreamWriter` to
write to, so the early-return is a no-op rather than a crash.
**Breaks:** debug output from systems registered before `Initialize` is
lost. In tests, missing the env var means parallel test runs clobber each
other's log file (same default `debug/` path).
**Tests:** none yet (every `GameTestRunner` test relies on
`MONODREAMS_DEBUG_DIR` working correctly, so any test failure under
parallel execution will surface this indirectly).
**Depends on:** —

## Engine source is backend/OS-agnostic — non-portable calls go through `IPlatformServices`

MonoDreams engine modules never touch `System.IO.File` / `Directory`,
`System.AppDomain`, `System.Environment`, or `System.Console` directly.
Every such call is routed through `PlatformServices.Current` (an
`IPlatformServices` in `MonoDreams.Platform`): storage read/write
(`FileExists`/`ReadAllText`/`WriteAllText`/`WriteAllBytes`/`CreateDirectory`),
base path (`BaseDirectory`), env/config lookup (`GetEnvironmentVariable`),
path joining (`CombinePath`), the `Logger` sink (`OpenLogWriter` +
`WriteLineToConsole`), and best-effort background work (`RunBackground`).
The holder defaults to `DesktopPlatformServices` (real filesystem / process
environment — the historical behaviour), so a desktop head and every test
behave exactly as before with no setup. A non-desktop head (web/WASM)
assigns its own implementation to `PlatformServices.Current` at the very
start of startup. The build-time content-pipeline importers
(`dialogue/YarnSpinnerImporter.cs`) are exempt: they run on the developer's
desktop at build time, never in the shipped runtime.

**Why:** KNI/BlazorGL recompiles MonoDreams source unchanged against the
same `Microsoft.Xna.Framework` namespace, but a browser has no process
filesystem / environment / `Console`. Hard-coding those APIs into engine
modules would make the source un-runnable on web. The seam keeps the
platform a head-level choice, never baked into a module.
**Breaks:** a module that calls `File`/`AppDomain`/`Environment`/`Console`
directly compiles for web but throws (or silently no-ops) at runtime in the
browser — e.g. `GameSettings` reading a save file off a disk that doesn't
exist, or `Logger` writing to a `StreamWriter` that can't open. (Read-only
*game content* is the exception: it is not a host-filesystem concern — it
goes through `ContentManager`/`TitleContainer`, which serves it over HTTP on
web. See level-blender — "Blender level JSON is read as content, not host
filesystem".)
**Tests:** `MonoDreams.Tests/Platform/PlatformServicesTests.cs` (asserts
`Logger` and `InputReplayPlan.TryLoad` route through a fake
`IPlatformServices` with no real disk, and that `DesktopPlatformServices`
round-trips the real filesystem); the routed runtime sites are exercised
end-to-end on the desktop FS by `BlenderLevelTests` and `HeadlessDemoTests`.
**Depends on:** —

## The platform (backend + OS services) is selected by the head project, never by engine source

Which graphics backend a build links — MonoGame `DesktopGL` or
KNI/BlazorGL `nkast.Xna.Framework.*` — and which `IPlatformServices`
implementation runs, are decided **outside** the engine modules, by the
consuming head project. The mechanism is the `$(MonoDreamsPlatform)`
MSBuild property (`desktop` | `web`, default `desktop`, defined in
`Directory.Build.props`): a head flows it into the shared game library via
`AdditionalProperties="MonoDreamsPlatform=…"` so the *same* engine source
compiles once per backend, with no assembly-identity collision. The `web`
value also defines the `MONODREAMS_WEB` compile symbol, the only thing that
flips head-level platform conditionals (`GraphicsProfile.Reach` instead of
`HiDef`, dropping `Window.Position` / `Window.ClientSizeChanged`). A web
head additionally installs the web `WebPlatformServices` (via the shared
`MonoDreams.Web.Hosting` host layer's `WebHost.RunAsync`, which every web head's
one-line `Program.Main` calls) to `PlatformServices.Current` as the very first
startup step. MonoDreams
modules contain **no** `#if MONODREAMS_WEB`, no framework-package choice,
and no `GraphicsProfile` literal — every such decision lives in the head or
in `Directory.Build.props`.

**Why:** a single shared game library must produce two distinct backend
builds from one source tree (desktop + web), which only works if the
backend choice is an external MSBuild input, not a baked-in reference. If
engine modules picked the backend or the platform services, a project
could not be desktop-only, web-only, *and* multi-platform from the same
modules — and the assembly identities (`Microsoft.Xna.Framework` provided
by two different packages) would collide in one build.
**Breaks:** a module that hard-codes `GraphicsProfile.HiDef`, references a
framework package directly, or branches on `#if MONODREAMS_WEB` makes the
web head fail (HiDef is rejected by WebGL/Reach) or makes the desktop and
web builds impossible to produce from the same source. A head that forgets
to set `PlatformServices.Current` before `Logger.Initialize` silently runs
the desktop FS services in the browser (see the backend/OS-agnostic premise
above).
**Tests:**
`MonoDreams.Cli.Tests/ScaffolderPlatformTests.cs::Scaffold_Desktop_EmitsCoreAndDesktopHeadOnly`,
`::Scaffold_Web_EmitsCoreAndWebHeadWithHostWiring`,
`::Scaffold_Multi_EmitsBothHeads_WebExcludedFromDefaultSolutionBuild`, and
`::Scaffold_Core_CarriesBackendGateButNoPreDeclaredFrameworkPackages`
(the scaffolded Core carries only the `$(MonoDreamsPlatform)` gate, the
heads own the backend);
`MonoDreams.Tests/IntegrationTests/KniBackendBuildTests.cs::EngineCoreCompilesAgainstKniWebBackend`
(the unchanged engine source recompiles under the web backend selected by
an external property).
**Depends on:** "Engine source is backend/OS-agnostic — non-portable calls
go through `IPlatformServices`".

## Default `RunMode = Play` preserves all existing pipelines

`GameState.RunMode` defaults to `RunMode.Play`. The run state changes behaviour
**only** for systems explicitly wrapped in a `GatedSystem`; an ungated system is
run by the pipeline regardless of the mode, exactly as before the run-state model
existed. A screen that never wraps a system in a `GatedSystem`, or never sets
`RunMode = Edit`, is byte-identical to its pre-run-state behaviour. The editor run
flag (`--editor` / `MONODREAMS_EDITOR=1`) does **not** change this default: the
host applies its boot-Paused (`RunMode = Edit`) as an explicit opt-in mutation of
`ScreenController.State.RunMode` **after** construction (the property exists for
exactly this seam), and the flag itself defaults off. After boot, ONLY the editor
transport (`EditorTransport` — the toolbar's Play/Pause + Restart buttons and the
headless transport ops) flips `RunMode`; there is no in-game toggle key, so with
the flag off nothing ever leaves `Play` (see level-editor — "The editor run flag
composes the always-on editor and the transport owns RunMode").

**Why:** the run-state model was added to `foundation` (a sensitive domain) so the
in-game level editor can freeze the game pipeline without forking it (see
`docs/CORE_TENETS.md` — "The editor is part of the game"). Adding a property to
`GameState` that every screen across all 14 modules carries is only safe if the
default leaves every existing screen untouched. Opt-in-only gating is what makes
that true.
**Breaks:** if `RunMode` defaulted to `Edit`, or if a system consulted `RunMode`
without being opted in, an existing screen would silently freeze part of its
pipeline (a black screen, or physics that no longer runs) with no code change at
the call site.
**Tests:** `MonoDreams.Tests/Foundation/RunStateGatingTest.cs::GameState_RunMode_DefaultsToPlay`
(asserts the default; the same file's gating tests assert ungated behaviour is
unchanged); `MonoDreams.Tests/LevelEditor/EditorRunFlagTests.cs` (the boot-in-Edit
flag defaults off and mutates only after a Play-constructed `GameState`).
**Depends on:** —

## Edit-time behaviour is a per-system policy honoured by `GatedSystem`

`GatedSystem` is the one mechanism by which the run mode gates a system. It wraps a
child `ISystem<GameState>` plus an `EditTimeBehavior` policy and, each `Update`,
reads `GameState.RunMode` to decide whether to forward to the child:
`RunNormally` runs in both modes; `Freeze` runs in `Play` only (skipped in `Edit`);
`RunPartial` and `RuntimeEditable` are reserved and, for now, run in both modes. The
gate also honours its own `IsEnabled` and forwards `Dispose` to the child. The
fixed policy assignment the level editor relies on: render / input / cursor and
`HierarchySystem` are `RunNormally` (live while editing); movement / velocity /
physics / collision / AI / dialogue and `CameraFollowSystem` are `Freeze`; editor
systems are `RunNormally` and Edit-guarded. Under the transport model the mode is
flipped exclusively by the editor transport (Paused = `Edit`, Playing = `Play`);
the gate semantics are unchanged — only WHO flips `RunMode` changed when the F1
mode-toggle was retired.

**Why:** cornerstone of the editor design (cornerstone C2) — editor tooling is ECS
systems over a run-state-gated game pipeline, not a separate renderer. The policy
must be data on the gate (not baked into each system) so the same engine system can
be live in one screen and frozen in another purely by how the screen wraps it (ECS
purity — behaviour lives in the system, the *decision to run* lives in the
assembler's gate).
**Breaks:** a render system wrapped in `Freeze` is a black screen the instant the
designer enters `Edit`; a physics system left ungated keeps moving entities while
they are being placed; a `Freeze`-gated `HierarchySystem` shows editor transform
edits at last frame's world position. All three fail silently.
**Tests:** `MonoDreams.Tests/Foundation/RunStateGatingTest.cs` (a `Freeze`-wrapped
fake runs in `Play` and is skipped in `Edit`; a `RunNormally`-wrapped fake runs in
both; the gate honours its own `IsEnabled`).
**Depends on:** rendering — "Rendering systems run last in the pipeline".

## Screens declare editor-facing `ScreenInfo`; the shared `GameState` (and its `RunMode`) is the only survivor of a screen switch

`ScreenController.RegisterScreen` has two overloads: the historical `(name, creator)` (which records a
default `ScreenInfo(name)` — display name = the screen name, no bound scene, not a scene host) and an
additive `(name, creator, ScreenInfo)`. `ScreenInfo(DisplayName, BoundSceneId, HostsSceneFiles)` is pure
foundation data: the human label, the scene id the screen loads from (null when it is not tied to one
file), and whether the screen is the level-parameterized host that loads whatever scene is requested.
`RegisteredScreens` enumerates the `(Name, Info)` pairs in **registration order** (a list, not the
creators `Dictionary`, whose enumeration order is not contractual). Duplicate-name registration throws,
unchanged. A screen switch (`LoadScreen` → the deferred swap in `Update`) disposes the outgoing screen's
**entire world**; the only state that survives is the shared `GameState` on the controller — including
its `RunMode`, so the editor stays in `Edit` across a switch (the transport never has to re-assert it).

**Why:** the editor's Scenes panel (level-editor UX-C) needs code to declare which configuration file a
screen loads from, and needs the list in a stable order. Keeping `ScreenInfo` a pure foundation record
(no editor dependency) means a plain game registering info never pulls the editor in; keeping the
enumeration a registration-order list makes the panel deterministic. `RunMode` surviving the switch is
why clicking a Scenes-panel row lands the new screen still in `Edit` with a fresh overlay.
**Breaks:** enumerating the creators `Dictionary` would make the panel order implementation-defined;
resetting `RunMode` on a switch (or storing it per-screen) would drop the editor back to `Play` every
time the designer opened another scene; a default overload that recorded no info would make every
pre-UX-C screen invisible to the panel.
**Tests:** `MonoDreams.Tests/Foundation/ScreenRegistrationTests.cs`
(`DefaultOverload_RecordsDefaultInfo`, `ExplicitInfo_IsEnumeratedInRegistrationOrder`,
`DuplicateName_Throws`, `RegisteredScreens_IsReadOnlySnapshotOfRegistrationOrder`).
**Depends on:** this file — "Default `RunMode = Play` preserves all existing pipelines" (the `RunMode`
that survives the switch); level-editor — "Game screens declare their bound scene; the Scenes panel
lists screens + scene files and switching IS selecting" (the consumer).

## Key chords fire on an exact-modifier press edge; `PlatformCommand` resolution is injected, never `#if`'d

`KeyChord` (a `Keys` trigger + a `KeyModifiers` set) is a pure, platform-blind value type; `KeyChordTracker`
fires a chord on the **press edge** of its key — down this frame, up last frame — while **exactly** the
required modifiers are held. "Exactly" is load-bearing: extra held *non-modifier* keys do NOT block a match,
but extra held *modifiers* DO (`Ctrl+Shift+Z` must not also fire `Ctrl+Z`). Left/right variants of a modifier
both count. The virtual `KeyModifiers.PlatformCommand` resolves to `Meta` (⌘) on macOS and `Ctrl` elsewhere
**at match time** from an injected `commandIsMeta` flag — the chord layer never reads the OS (no `#if`, no
`OperatingSystem` call inside `foundation`); the composing layer injects the flag. Keyboard state arrives
through the same injectable `Func<KeyboardState>` seam the editor dialog uses (default
`Keyboard.GetState`), and the pure static `KeyChordTracker.Matches` is testable with hand-built
`KeyboardState`s. The layer is game-agnostic: any feature can bind combo inputs, not only the editor.

**Replay caveat.** The input-replay channel (`InputReplayPlan` / `InputReplaySystem`) synthesizes
`AInputState` *actions*, not raw keyboard chords, so chord-driven features are **not** exercised through
replay — they are tested through their own op channels (the editor's `menu:*` / `view:frame` / toolbar ops)
and, for the matching itself, through `KeyChordTracker.Matches` with hand-built states. A future replay-v2
that records the raw keyboard is the named terrain that would make chords replayable.

**Why:** the user's requirement that combo inputs be an ENGINE feature (any future game can use them), plus
the macOS-vs-Windows/Linux accelerator split. Baking the OS choice into the struct (or a `#if`) would break
the source's platform-neutrality (a head-level choice, never a module one — see "The platform … is selected
by the head project"); resolving at match time from an injected flag keeps one table correct on both. The
exact-modifier rule is what stops `Ctrl+Shift+Z` (Redo) from also firing `Ctrl+Z` (Undo).
**Breaks:** a superset-tolerant match makes every modified chord also fire its unmodified prefix (Redo also
undoes); a `#if MONODREAMS_WEB`/OS query inside the module re-bakes the platform into the source; reading the
level (not the edge) fires every frame a chord is held; assuming replay drives chords leaves chord features
untested (replay carries actions, not keys).
**Tests:** `MonoDreams.Tests/Foundation/KeyChordTests.cs` (`ResolveModifiers_*` — the PlatformCommand
injection; `Matches_FiresOnlyOnThePressEdge_NotWhileHeld`; `Matches_ExtraHeldModifier_Blocks_SoCtrlShiftZ_DoesNotFireCtrlZ`
+ `Matches_MissingRequiredModifier_Blocks_*` + `Matches_ExtraHeldNonModifierKey_DoesNotBlock` — the exact-modifier
matrix; `Matches_LeftAndRightModifierVariants_BothCount`; `Matches_PlatformCommand_ResolvesToMeta_OnMac` +
`_ResolvesToCtrl_Elsewhere` — pre-mortem #7 both resolutions; `Tracker_PrimesOnFirstUpdate_*` +
`Tracker_FiresOnTheFrameTheChordIsPressed_*` — the seam + priming). The editor consumer + the replay caveat's
op channels are protected by `MonoDreams.Tests/LevelEditor/EditorShortcutTests.cs`.
**Depends on:** this file — "The platform (backend + OS services) is selected by the head project, never by
engine source" (why the OS fact is injected); level-editor — "The editor's keyboard shortcuts are ONE chord
table, gated by a single viewport context" (the first consumer).

## Open questions

- **Entity disposed mid-frame:** convention not yet established —
  what happens if a system queries an entity after a prior system
  disposed it this frame? *Status: open; settle when use case appears.*
- **Mid-frame re-parenting:** if a system re-parents an entity (changes
  `TransformComponent.Parent` or `ChildOfComponent`), do consumers in
  the same frame see the new parent or the old one? Behavior depends on
  `HierarchySystem`'s position in the pipeline.

## Aspirational direction

- Consolidate `ChildOfComponent` and `TransformComponent.Parent` into one
  hierarchical concept.
- `TransformComponent` exposes interaction methods that enforce `Delta`
  consistency, so `Delta` is meaningful when read regardless of pipeline
  order.
- Collision, rendering, camera, and culling decouple from
  `TransformComponent` specifically and operate against any
  Transform-shaped contract.

## Follow-up debt

The following premises currently have **Tests: none yet** — they are
documented but not programmatically protected:

- Don't mix two Transform-shaped components in one project
- `TransformComponent.Delta` is meaningful only after `TransformCommitSystem` ran
- `TransformComponent.IsDirty` cascades through the parent chain
- `ChildOfComponent` and `TransformComponent.Parent` are two intentional links
- `HierarchySystem` must run ahead of any system reading WorldPosition
- Children are disposed with their parents
- `WorldMatrix` is cached and computed lazily
- `Logger` requires `Initialize` before any write

Architectural tests (ArchUnit-style) protecting these are on the engine
backlog.
