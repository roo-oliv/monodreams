# foundation — premises

> Technical invariants the engine assumes about the foundation module:
> `TransformComponent`, `ChildOfComponent`, `HierarchySystem`,
> `TransformCommitSystem`, the `EntityHierarchy` resource, the input/replay
> scaffold, the `Logger`, the run-state model (`GameState.RunMode`,
> `EditTimeBehavior`, `GatedSystem`), and the engine-wide DefaultEcs
> component-publication contract. Read this before changing any of those
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

The propagation uses a signal SEPARATE from `IsDirty`: `NeedsHierarchyUpdate`.
`IsDirty` is the world-matrix **cache-validity** bit — the `WorldMatrix` getter
clears it as a side effect of *recomputing on read*. `NeedsHierarchyUpdate` is the
**"my descendants are stale" propagation** signal — set by every mutator (via
`SetDirty`) and cleared ONLY by `HierarchySystem` (via `ClearHierarchyDirty`) after
it has re-dirtied the subtree. `PropagateDirtyFlags` keys off `NeedsHierarchyUpdate`,
never `IsDirty`, so a `WorldMatrix` read that lands between a parent's edit and the
`HierarchySystem` pass cannot silently drop the child update. The child invalidation
itself still goes through the matrix-cache bit (the recursion calls `SetDirty` on
descendants).

**Why:** caching the world matrix avoids recomputing it on every read,
which would dominate hot paths in deep hierarchies (UI layouts, nested
entities). But a single flag doing both jobs is order-fragile: the level editor's
modal transform (`G`) edits a transform EARLY in the update pipeline, before
`ButtonMeshPrepSystem` reads the same transform's `WorldPosition` (which cleared the
one flag), so `HierarchySystem` saw a clean parent and never moved the button's label
child — while a gizmo drag (edits AFTER that reader) moved both. Splitting the
cache-validity bit from the propagation signal is what makes gizmo and modal edits
behave identically for any changed parent, and fixes the same latent staleness for
every consumer.
**Breaks:** if a system bypasses the dirty flag (e.g., mutates internal
fields directly via reflection), descendants render and collide at stale
world positions while their parents have moved. If propagation were keyed off
`IsDirty` again, any read between an edit and `HierarchySystem` re-opens the
gizmo-vs-modal divergence: the parent moves but its children lag one frame or freeze
in place.
**Tests:** `MonoDreams.Tests/Foundation/HierarchyDirtyPropagationTests.cs`
(`ChildFollowsParentMove_EvenWhenWorldPositionIsReadBeforeHierarchySystem`,
`ChildFollow_IsIdentical_ForGizmoOrderAndModalOrder`,
`GrandchildFollowsRootMove_WithInterveningRead`,
`PropagationSignal_IsClearedAfterEachHierarchyPass`).
**Depends on:** level-editor — "The modal transform (G/S/R) owns the pointer +
keyboard …" (the modal edit path this parity protects) and "The gizmo applies a
quantized … transform edit" (the gizmo path it matches).

## `ChildOfComponent` and `TransformComponent.Parent` are two intentional links

`TransformComponent.Parent` is the matrix link — it controls how
`WorldMatrix` cascades. `ChildOfComponent` is the structural link — it
controls lifecycle (cascade disposal). `HierarchySystem.SyncTransformParents()`
syncs the matrix link from the structural link each frame. Hierarchy logic
must read the link relevant to its concern.

**Why:** the split came from hierarchical UI like a dialogue panel, where
a banner, avatars, text, and a waiting-indicator move and dispose together
but may not share matrix scaling. The split is a known wart and is on the
refactor backlog (consolidation desired). Under colliders-as-entities the
structural link carries new weight: `ColliderBody.Resolve` walks the
`ChildOfComponent` chain to find a collider's physics body (a collider child of a
body), and lifecycle cascade disposes a body's collider children with it — so a
collider child must be `ChildOf`-parented to its body, not only matrix-linked.
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

## A suppressed `Logger` line costs nothing, and an emitted one is byte-identical

`Logger` exposes **two overloads per level**: the plain `Debug/Info/Warning/Error(string)`
and an interpolated-string-handler `Debug/Info/Warning/Error(ref Logger.Message<TLevel>)`,
where `TLevel` is one of the `Logger.AtDebug` / `AtInfo` / `AtWarning` / `AtError`
tag structs implementing `Logger.ILogLevelTag`. An interpolated string
literal at a call site — `Logger.Debug($"entity {id} at {x,6:F2}")` — binds
to the **handler** overload, whose constructor compares `TLevel.Value`
against `Logger.MinimumLevel` *before* a single interpolation hole is
evaluated; when the level is suppressed it reports `shouldAppend: false`
and the compiler skips the holes entirely (no `ToString`, no boxing, no
`StringBuilder`, no line). Anything that is already a `string` — a
variable, a concatenation such as `"literal " + $"interp {x}"`, a method
result — has no interpolation left to defer and binds to the plain
`string` overload, eagerly, exactly as it always did. `MinimumLevel` is a
public auto-property read **without** taking the logger's lock: it is
assigned once, inside `Initialize`, before any system exists to log.
The emitted line format — `[wallclock] [GT gametime] [LEVEL] message` —
is identical on both paths and is a **parsing contract**, not a
preference.

**Why:** before the handler existed, every interpolated call site (300+
across the engine and its reference games) formatted its message in full
and handed the finished string to a method whose first act was to discard
it — and the per-entity ones in level loading, culling and collision paid
that every frame, per entity. The two-overload pair is what buys the
deferral without touching a single call site. The format is fixed because
the input-replay / verification workflow, `GameTestRunner`'s log
assertions and the tooling greps all parse these lines.
**Breaks:** collapsing to a single `string` overload (or adding a
`string`-only convenience that shadows the handler) silently restores the
eager cost at every interpolated call site. Reading `MinimumLevel` under
the lock reintroduces a monitor per discarded line — more expensive than
the message it refuses to build. Moving the threshold check later in
`Write`, or reflowing the line format, breaks the contract for every log
consumer. Adding a hole with a side effect is now level-dependent
behaviour: at a suppressed level it never runs.
**Tests:** `MonoDreams.Tests/Foundation/LoggerInterpolationTests.cs` — a
`ToString()` that throws proves suppressed holes are never evaluated; the
same message logged through both call forms is asserted byte-identical
after the wall clock, and every emitted line is matched against the
format-contract regex.
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
web. See level-loading — "Native `.mdscene` levels are bundled by an MGCB
`/copy:` entry and read via `TitleContainer`".)
**Tests:** `MonoDreams.Tests/Platform/PlatformServicesTests.cs` (asserts
`Logger` and `InputReplayPlan.TryLoad` route through a fake
`IPlatformServices` with no real disk, and that `DesktopPlatformServices`
round-trips the real filesystem); the routed runtime sites are exercised
end-to-end on the desktop FS by the native `Blender_Level` boot (`BlenderLevelTests`) and `HeadlessDemoTests`.
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

## A gated system that owns transient entities tears them down through `ISuspendableSystem`

Skipping a system's `Update` removes its *behaviour*, not its *output*. For a system
that OWNS entities — a tooltip label, a drag ghost, a damage number, a hover
highlight — those two are different things: once the gate stops forwarding, that
system never gets another `Update` in which to dispose what it created, while the
draw stack (`RunNormally` by policy, since a frozen renderer is a black screen)
keeps rendering it for the rest of the session. `GatedSystem` therefore calls
`ISuspendableSystem.Suspend(state)` on the child **exactly once, on the running →
not-running edge** — for either reason a gate can stop: the policy excluding the
current `RunMode`, or the gate's own `IsEnabled` being switched off (the systems
panel's master toggle). A gate that has never forwarded suspends nothing, a frozen
gate does not call it again every frame, and a later resume + stop calls it again.
`Suspend` is a teardown, never a kill switch: implementations must be idempotent
and leave the system ready to rebuild on its next `Update`. A system that owns
transient entities is therefore only safe to `Freeze` if it implements the
interface — and must be registered as **its own entry**, since the gate reaches
only its immediate child (a DefaultEcs composite does not expose its children, so a
suspendable system buried inside a gated *group* is never reached).

**Why:** the policy stays data on the gate (previous premise) precisely so systems
know nothing about run modes — but that leaves nobody to clean up when a system
stops. Handing the child a mode-agnostic "you are no longer being run" callback
keeps the policy on the gate AND gives the owner its one chance to tear down; the
alternative (each system reading `GameState.RunMode` itself) would bake one game's
editor policy into an engine system type.
**Breaks:** `TooltipSystem` gated `Freeze` in the ui demo, before the hook existed:
Play → Pause with a tooltip on screen stranded its panel + label on the HUD pass —
`Update` never ran again to hide them, and nothing else in the engine knows they
exist (they are deliberately unparented and scene-marker-free). Same shape for any
future transient-owning system, and for the systems panel switching a running entry
off. A non-idempotent `Suspend` (one that assumes it is called once ever) breaks on
the second freeze; a `Suspend` that also disables the system turns a pause into a
permanent stand-down.
**Tests:** `MonoDreams.Tests/Foundation/RunStateGatingTest.cs`
(`Freeze_SuspendsTheChild_OnceOnEachPlayToEditEdge`, `AGateThatNeverRan_SuspendsNothing`,
`DisablingTheGate_SuspendsTheChild`, `ANonSuspendableChild_IsSkippedSilently`);
`MonoDreams.Tests/Ui/TooltipTests.cs::FreezingTheSystem_DespawnsTheTooltip` (the
real system through a real `Freeze` gate).
**Depends on:** ui — "The tooltip is a transient, system-owned, screen-space label
that despawns with its pick" (the first implementer).

## `GatedSystem`'s timing sink keeps the profiler out of foundation

`GatedSystem.TimingSink` — a static `Action<string, long>?` (profile name, elapsed
Stopwatch ticks) defaulting to `null` — is the **only** profiling hook in
`foundation`, and it is a socket, not an implementation: this module never
references the `debug` module, never names a profiler type, and contains no timing
logic beyond reading the sink once per `Update` and, when one is installed,
bracketing the child call with `Stopwatch.GetTimestamp()`. The plug comes from
outside — the optional `debug` module's `SystemProfiler` installs its `Record`
method as the sink when profiling is enabled and clears it when disabled — so with
nothing installed the entire feature is one null check per gated `Update` and no
profiler is reachable in the build's object graph at all. A gate whose
`ProfileName` is `null` (the default; the `EditorPipelineRegistrar` sets it to the
entry's full hierarchical name) is never timed even while a sink IS installed.

**Why:** timing at the gate is what makes per-system profiling total — every
pipeline entry passes through a `GatedSystem`, so one seam covers every screen's
pipelines with nothing opting in. But `foundation` is a sensitive domain that every
game depends on, and `debug` is optional: hooking the profiler by direct call would
invert the module dependency and make an optional overlay module a hard dependency
of the run-state model. An injectable sink buys the coverage while keeping the
dependency arrow pointing from `debug` to `foundation`.
**Breaks:** a direct call from `GatedSystem` into the debug module drags the
profiler into every build and makes `foundation` un-installable without `debug`.
Timing unconditionally (no sink check, or timing unnamed gates) puts two
`Stopwatch` reads and a delegate invocation in the hot path of every gated system
of every shipped game. Reading the static field twice instead of into a local lets
an uninstall between the two reads null-reference mid-frame.
**Tests:** `MonoDreams.Tests/Foundation/GatedSystemTimingSinkTests.cs`
(default-null sink, forwarding without a sink, recording with sink + name, and a
source scan asserting foundation never references `MonoDreams.Debug`).
**Depends on:** debug — "The profiler hooks the one seam every pipeline entry
passes through".

## `Logger.LineSink` is a single-owner tap that must not log

`Logger.LineSink` — a static `Action<LogLevel, string>?` defaulting to `null` — is the only
observation hook on the logger, and, exactly like `GatedSystem.TimingSink`, it is a **socket, not
an implementation**: `foundation` never names a consumer and the plug comes from the optional
`debug` module (`PointerReplaySystem` needs to see log lines in-process to satisfy a
`waitUntil log` predicate, without tailing a file that does not exist on web). It receives the RAW
message — no timestamp, no level prefix — and is invoked **after** the line has been written and
**outside** the writer lock. Three rules bind a sink: it is single-owner (assignment replaces, so
an owner installs on construction and restores `null` on dispose), it must be thread-safe (the
logger is written from background work too), and it must **never log**.

**Why:** the pointer channel's stage gating is only as good as what it can observe, and the log is
the one universal observable every module already produces. But `foundation` is a sensitive domain
every game depends on, so it must not reference an optional debug module to be observable — the
injected socket keeps the arrow pointing `debug → foundation` and costs one null check per
surviving line when nobody is plugged in. Invoking outside the lock is what keeps a slow or
blocking sink from serialising every logging thread behind it.
**Breaks:** invoking the sink inside the lock puts third-party code on the critical section every
thread contends for. A sink that logs re-enters `Write` and recurses without bound. A sink that is
not thread-safe corrupts its own state the first time a background task (the screenshot encoder)
logs. An owner that fails to clear the sink on dispose leaves a dead object receiving every line
for the rest of the process.
**Tests:** `MonoDreams.Tests/Debug/PointerReplaySystemTests.cs`
(`WaitUntilLog_IsSatisfiedByALineWrittenWhileTheDriverRuns` exercises the tap end to end;
`Dispose_ReleasesTheLoggerTap` pins the install/uninstall contract).
**Depends on:** debug — "A pointer plan gates on observables, times out, and drains into an exit".

## Screens declare editor-facing `ScreenInfo`; the shared `GameState` (and its `RunMode`) are the survivors of a screen switch

`ScreenController.RegisterScreen` has two overloads: the historical `(name, creator)` (which records a
default `ScreenInfo(name)` — display name = the screen name, no bound scene, not a scene host) and an
additive `(name, creator, ScreenInfo)`. `ScreenInfo(DisplayName, BoundSceneId, HostsSceneFiles)` is pure
foundation data: the human label, the scene id the screen loads from (null when it is not tied to one
file), and whether the screen is the level-parameterized host that loads whatever scene is requested.
`RegisteredScreens` enumerates the `(Name, Info)` pairs in **registration order** (a list, not the
creators `Dictionary`, whose enumeration order is not contractual). Duplicate-name registration throws,
unchanged. A screen switch (`LoadScreen` → the deferred swap in `Update`) disposes the outgoing screen's
**entire world**; the state that survives on the controller is the shared `GameState` — including its
`RunMode`, so the editor stays in `Edit` across a switch (the transport never has to re-assert it). **Under
the editor run flag there is now a SECOND host-scoped survivor beside `GameState`: the level-editor's
`EditorSession`** — created in the host's `Game1` and passed to every screen exactly like the shared
`GameState`, it owns the `ViewportContextStack` (the open scene/Game tabs + their `SceneData` snapshots). A
screen switch disposes the world, but the session (like `GameState`) survives, so the open tabs + the Game
sandbox ride cross-screen transitions. The editor module owns the session; `foundation` stays editor-free —
the host wires it, as it wires the overlay.

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
`DuplicateName_Throws_ForEitherOverload`, `RegisteredScreens_IsEmptyBeforeAnyRegistration`);
`MonoDreams.Tests/LevelEditor/EditorSessionTests.cs` (`TabList_SurvivesAScreenSwitch_ViaRebind`,
`Session_HoldsTheStack_SeedsTheBootSceneTab_PendingDefaultsOff` — the host-scoped editor session that
survives the switch beside `GameState`).
**Depends on:** this file — "Default `RunMode = Play` preserves all existing pipelines" (the `RunMode`
that survives the switch); level-editor — "The viewport context stack is the ONE tab-switching mechanism …
(PF-B/TB-A)" (the host-scoped `EditorSession`/`ViewportContextStack` that is the second survivor), "Game
screens declare their bound scene; the Scenes panel lists screens + scene files and selecting opens (or activates) its tab (TB-A)"
(the consumer).

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

## `WindowFit` is opt-in, and it is the ONLY thing allowed to size a game's window

`WindowFit` (in `MonoDreams.Platform`) computes and applies the largest **aspect-correct**
window that fits inside the display's **usable** bounds — the area left after the OS chrome
(macOS menu bar + dock, Windows taskbar, Linux panels) — snapped DOWN to a multiple of
`WindowFit.SnapTo` (16), with `WindowFit.ReservedChromeHeight` (28) points of usable height
held back for the window's own title bar, and **capped at the render resolution** (1:1 is the
sharpest a game can present; magnifying only blurs). `MONODREAMS_WINDOW=WxH` overrides the
computation **verbatim** — no fit, no snap, no cap — and is the only mode that leaves the
window non-resizable, because a scripted run asked for an exact size. Every call emits exactly
**one** `Logger.Info` line carrying render / display / usable / window / mode; that line is the
feature's entire observable and is what turns "the buttons are off-screen" into a diagnosable
report. The helper is **strictly opt-in**: nothing in the engine calls it, it has no system, no
component and no pipeline presence, so a game that never calls `WindowFit.Apply` keeps whatever
backbuffer it set, byte-for-byte. Because the boot line is written from the constructor, a head
that adopts it must call `Logger.Initialize` **before** `WindowFit.Apply` (see "`Logger`
requires `Initialize` before any write"); the CLI's scaffolded desktop head does exactly that.
Usable bounds come from `SDL_GetDisplayUsableBounds` through `SdlNative` — the engine's single
owner of "call an SDL export MonoGame never bound" — with a fixed-margin fallback
(`FallbackMarginWidth` / `FallbackMarginHeight`) when the export is missing, and an
`Unmeasured` mode that applies the render resolution unchanged when the display cannot be read
at all.

Both reference heads adopt it in their **windowed desktop branch only** (`MonoDreams.Examples.Desktop`
and `MonoDreams.Demos`, issue #115): a *headless* run keeps the backbuffer its own contract
requires — 1×1 off-screen for Examples (whose `Draw` early-returns), the virtual resolution for
Demos (whose capture reads it) — and says so in the log rather than fitting a window it does not
present through. A game head therefore has no window-size setting of its own: `WindowFit` computes
it and `MONODREAMS_WINDOW` overrides it (`GameSettings` no longer carries `WindowWidth`/`Height`,
and the head has no runtime "apply this resolution" method, because either would be a second
answer to the same question).

**Why:** MonoGame 3.8.4 DesktopGL does **not** let macOS clamp a *fixed* window (a resizable one
it does), so `PreferredBackBuffer = 1920x1080` on a 1512x982-point MacBook opens a window taller
than the screen: nothing crashes, nothing logs, and the bottom strip of the game — where the
Start button usually lives — renders below the physical display. Players do not report this
class of bug; they close the game. MonoGame never bound `SDL_GetDisplayUsableBounds` either
(only `GetBounds` / `GetCurrentDisplayMode`), so through the public API a game cannot even ask
for the menu-bar-aware area. Opt-in is what makes adding this to a sensitive module safe: a
game that does not call it is provably unchanged, exactly like the `RunMode = Play` default.
**Breaks:** setting `PreferredBackBufferWidth/Height` after `WindowFit.Apply` silently undoes
the fit and restores the offscreen window. Calling it before `Logger.Initialize` drops the boot
line, and the feature becomes unobservable — the failure it prevents returns to being
undiagnosable from a log. Making it non-opt-in (an engine system, or a call inside
`ScreenController`) would resize the window of every existing game that deliberately picked its
own size. Snapping the derived HEIGHT as well as the width would distort the aspect by up to
15 points instead of a rounding pixel.
**Tests:** `MonoDreams.Tests/Foundation/WindowFitTests.cs` (the fit geometry, the 1:1 cap, the
snap, the title-bar reservation, mode selection, the `WxH` parser, and the fallback probe);
`MonoDreams.Tests/IntegrationTests/ExamplesAdoptionTests.cs`
(`WindowedRun_FitsTheWindow_AndCapturesTheTargetAtItsOwnResolution` — a real windowed run of the
reference head honours `MONODREAMS_WINDOW` and logs the boot line;
`TheDesktopHead_LogsItsRenderSpaceAndPresentationPolicy` — a headless run logs the skip instead
and never calls it);
`MonoDreams.Cli.Tests/ScaffolderPlatformTests.cs::Scaffold_GameRoot_DesktopBranchFitsTheWindow_WebBranchUntouched`
(the scaffolded desktop head adopts it, the web branch is untouched, and the logger comes up first);
`MonoDreams.Cli.Tests/ScaffolderBuildTests.cs::Init_Desktop_ThenAdd_ProducesBuildableSolution`
(the scaffolded game with the call in it actually builds).
**Depends on:** this file — "`Logger` requires `Initialize` before any write"; "The platform
(backend + OS services) is selected by the head project, never by engine source" (the helper is
called by a head, inside its desktop branch, never by a module).

## On macOS DesktopGL every window number is in points — there is no Retina conversion in this path

Display mode (`GraphicsAdapter.DefaultAdapter.CurrentDisplayMode`), `SDL_GetDisplayUsableBounds`,
the SDL window, `Game.Window.ClientBounds`, and the GL backbuffer that `PreferredBackBufferWidth`
/ `Height` sizes are **all in logical points** on macOS DesktopGL — the same unit, end to end.
A 1512x982 "1512x982-point" MacBook display reports 1512x982 everywhere in this path even though
the panel is 3024x1964 physical pixels. Nothing in `WindowFit` multiplies or divides by a
backing scale factor, and nothing should: MonoGame creates its SDL window **without**
`SDL_WINDOW_ALLOW_HIGHDPI`, so the GL drawable is allocated at the window's point size and the
OS upscales it. The one place device pixels enter the engine is the level editor's opt-in
`EditorHiDpi`, which deliberately re-backs the surface at device resolution and reports the
scale so the editor's own chrome can render sharp — and even there the *window* stays in points.

**Why:** empirical, and expensive to re-derive. Every "should I multiply by the DPR here?"
question in windowing code has the same answer in this path — no — and getting it wrong once
produces a window twice the intended size (or half), which looks like a completely different
bug. Writing the unit down is what stops a future change from "fixing" a non-existent Retina
conversion.
**Breaks:** scaling the fitted window by `backingScaleFactor` opens a window ~2× the display on
Retina (the exact failure `WindowFit` exists to prevent, inverted); dividing by it opens a
postage stamp. Mixing the two spaces silently misplaces the mouse, since SDL reports pointer
coordinates in window points.
**Tests:** none yet (the unit is a platform fact, not a code path; `WindowFitTests` asserts the
arithmetic is scale-free by construction — no DPR term appears in it).
**Depends on:** rendering — "The viewport inset moves compositing and mouse mapping together"
(where `ViewportManager.DevicePixelRatio` enters); level-editor — "The editor shell insets the
game viewport and renders its chrome at native resolution" (`EditorHiDpi`, the one documented
device-pixel exception).

## A value-predicate `EntitySet` re-evaluates only when the component is published

DefaultEcs lets a query filter on a component's VALUE, not only on its presence, and
the engine uses that twice: `MasterRenderSystem.BuildDrawSet` builds each render
pass's draw set as `world.GetEntities().With((in DrawComponent d) => d.Target ==
source)`, and `GravitySystem`'s set is `.With((in TRigidBodyComponent b) =>
b.Gravity.active)`. Such a set runs its predicate only when the component is
**published** — `entity.Set(component)` or `entity.NotifyChanged<T>()` — and caches
the answer as set membership. Mutating the stored value instead publishes nothing:
neither `ref var c = ref entity.Get<T>(); c.Field = …` nor — because `DrawComponent`
and `RigidBodyComponent` are *classes*, so `Get<T>()` hands back the stored instance
— a plain `entity.Get<DrawComponent>().Target = …`. The entity therefore keeps
whatever membership its last publication earned it. Any code that edits a field a
predicate reads must follow the edit with `entity.Set(…)` or
`entity.NotifyChanged<T>()`. Editing fields no predicate reads needs neither — which
is why `SpritePrepSystem` rewrites a dozen `DrawComponent` fields in place every
frame and never notifies: `Target` is not one of them.

**Why:** publication is the only signal DefaultEcs has; it cannot observe a write
made through a `ref` or through a class reference. The failure mode is the worst kind
of silent one — retarget or re-layer an entity in place and it stays in the OLD
pass's set while every field on it inspects correct, so it renders where nobody is
looking, with no exception, no warning, and nothing to grep for. The gravity set
fails the same way: a body whose `Gravity.active` was switched off in place keeps
falling.
**Breaks:** an in-place retarget draws through the previous pass, or through none at
all if that pass no longer exists; a debugger shows the new value while the screen
shows the old behaviour. The opposite mistake is cheaper but real —
`NotifyChanged` per entity per frame re-runs every predicate set subscribed to that
component type, turning a cached membership test into a per-frame one.
**Tests:** none yet.
**Depends on:** rendering — "One `MasterRenderSystem` instance is one render pass"
(the `DrawComponent.Target` predicate set); physics — "`GravitySystem` affects only
entities with `RigidBodyComponent` + `VelocityComponent`" (the engine's second
value-predicate set).

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
- `ChildOfComponent` and `TransformComponent.Parent` are two intentional links
- `HierarchySystem` must run ahead of any system reading WorldPosition
- Children are disposed with their parents
- `WorldMatrix` is cached and computed lazily
- `Logger` requires `Initialize` before any write
- A value-predicate `EntitySet` re-evaluates only when the component is published
- On macOS DesktopGL every window number is in points — there is no Retina
  conversion in this path (a platform fact, not a code path; only assertable
  indirectly, by the absence of any DPR term in `WindowFit`)

Architectural tests (ArchUnit-style) protecting these are on the engine
backlog.
