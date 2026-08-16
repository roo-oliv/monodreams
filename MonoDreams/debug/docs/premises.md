# debug — premises

> Technical invariants the engine assumes about the debug overlays
> module: `ColliderDebugSystem`, `SpriteDebugSystem`,
> `ScreenshotCaptureSystem`, and `SystemProfiler` (per-system frame
> timing — not a pipeline system, a plug into `foundation`'s socket).
> (The `Logger` and the input-replay
> scaffold live in `foundation` because they're useful in production;
> this module adds the *visual* debug overlays and screenshot capture
> only.) Read this before changing any of those pieces or relying on
> the screenshot output for testing — including the headless Demos
> observe-and-self-verify path (`MonoDreams.Demos/Game1.cs`,
> `HeadlessOptions.cs`), which builds on `ScreenshotCaptureSystem`.
> Frame capture has two formats (encoded PNG and uncompressed raw RGBA) and
> one environment contract that selects between them — see the "Frame
> capture" section of [`overview.md`](overview.md) for the env table and the
> disk-cost table those premises refer to.

## This module is opt-in; nothing requires it

A screen's pipeline assembly never *needs* a debug system. Each system
in this module is registered only if the screen explicitly wants it,
and each respects a static `Enabled` flag plus an instance `IsEnabled`
flag so a registered system can be muted without removing it from the
pipeline. Tests and production-bound builds simply omit the
registrations.

**Why:** the framework-not-library tenet says required behavior lives
in `foundation`, optional behavior in its own module. Debug overlays
allocate per-frame (transient mesh entities), so making them mandatory
would impose a cost on every screen.
**Breaks:** if a future refactor moves any of these systems into a
required module, every game pays for debug rendering whether they
asked for it or not. The static `Enabled` toggles also become global
state that's harder to keep off across compositions.
**Tests:** none yet.
**Depends on:** —

## Debug overlays draw via the same `DrawComponent` path as everything else

`ColliderDebugSystem` and `SpriteDebugSystem` create ephemeral entities
with `DrawComponent { Type = Mesh }` and a high `LayerDepth` (e.g.
1.0, 0.98) each frame, then dispose them at the start of the next
frame. They do not call `SpriteBatch` directly and they do not fork
`MasterRenderSystem`.

**Why:** the rendering pipeline's "`MasterRenderSystem` is the sole
renderer" premise forbids a second SpriteBatch path. Generating
transient mesh entities is the engine-pure way to render overlays.
The high layer depth keeps them on top of the rest of the scene
without needing a separate target.
**Breaks:** if a debug system bypassed `MasterRenderSystem` to draw
directly, the next render-target switch would lose the overlay (the
SpriteBatch would be in the wrong state) — and the "no parallel
renderer" review lens would flag the PR.
**Tests:** none yet.
**Depends on:** rendering — "`MasterRenderSystem` is the sole renderer";
rendering-mesh — "Mesh entities use the same `DrawComponent` slot".

## Debug overlays must be prep'd before `MasterRenderSystem` runs

`ColliderDebugSystem` and `SpriteDebugSystem` register inside the
prep stage (between `SpritePrepSystem` and `MasterRenderSystem` in the
reference pipeline at
`MonoDreams.Examples/Screens/LoadLevelExampleGameScreen.cs`). They
must run before `MasterRenderSystem` so the transient mesh entities
are in the world when the renderer iterates draw queries; they should
run after `SpritePrepSystem` so the sprite bounds they visualize
reflect the same frame's `DrawComponent` data.

**Why:** the overlay entities have no draw data until the debug system
creates them. If `MasterRenderSystem` runs first, the entities aren't
in the world yet and the overlays render one frame late. Likewise,
`SpriteDebugSystem` reads `DrawComponent.Position` / `Origin` / `Size`
to compute bounds; reading before `SpritePrepSystem` writes those gives
stale data.
**Breaks:** overlays flicker, lag, or visualize last frame's bounds
during camera motion or animation.
**Tests:** none yet.
**Depends on:** rendering — "Rendering systems run last in the pipeline".

## Collider flash is caller-driven and the overlay is filterable

`ColliderDebugSystem` exposes two knobs for keeping the overlay readable in a
real level. `Filter` is a `Func<Entity, bool>` over collider entities: null
(the default) draws every one, and a predicate narrows the overlay to the
handful that matter — a tile world has hundreds of baked terrain colliders that
bury the three you are debugging. `Flash(Entity)` blinks one collider's outline
`Color.White` for `FlashSeconds` (0.12 by default), so an event that resolves
inside a single frame is still visible; it is **caller-driven on purpose** —
the system never flashes on its own from `CollisionMessage`. Flash timers age
by `state.Time` at the top of every `Update`, **before** the
`IsEnabled`/`Enabled` early-return, and entries whose entity died or whose
timer ran out are dropped; `Dispose` clears the table.

**Why:** flashing every contact would strobe continuously on the floor and
walls a body rests against, drowning out the one-frame events worth seeing —
so the game names the moments it cares about (damage landing, a trigger
firing) instead. Ageing while muted is what makes the mute honest: a flash
started just before the overlay is toggled off must not still be white when it
comes back on, possibly many seconds later.
**Breaks:** auto-flashing from collision messages makes the overlay a strobe
and hides real events. Ageing flashes *after* the enabled check (or only when
enabled) makes re-enabling show a stale blink pinned at full brightness.
Dropping the dead-entity check leaks recycled `Entity` keys, so a
newly-created entity that reuses the id inherits a phantom flash. Filtering
inside the draw helpers instead of at the query loop still allocates the mesh
lists for colliders nobody asked to see.
**Tests:** `MonoDreams.Tests/Debug/ColliderDebugSystemTests.cs` —
`Filter_Null_DrawsEveryCollider`,
`Filter_NarrowsTheOverlay_ToTheMatchingCollidersOnly`,
`Flash_TurnsTheOutlineWhite_ThenRevertsWhenTheTimerExpires`,
`Flash_AgesWhileDisabled_SoReEnablingShowsNoStaleBlink`,
`Flash_OnADeadColliderEntity_IsDroppedWithoutThrowing`.
**Depends on:** debug — "This module is opt-in; nothing requires it"
(`Flash` bookkeeping runs even when both toggles are off, which is what the
static/instance mute pair is allowed to skip and this is not); collision —
"A collider IS an entity (colliders-as-entities)".

## `ScreenshotCaptureSystem` is gated by `IsEnabled` set from `input_replay.json`

`ScreenshotCaptureSystem` is disabled by default. The integration test
pattern is to write `"screenshots": true` into `debug/input_replay.json`
and have the screen set `screenshotSystem.IsEnabled = replayPlan?.Screenshots
?? false` after constructing the system. Production builds either omit
the registration or leave `IsEnabled = false`.

**Why:** capturing PNGs of the backbuffer every 2 seconds allocates,
hits the disk, and stalls the render thread briefly. Default-off keeps
the cost out of any session that didn't explicitly ask for it. The
replay file is the natural carrier of "this run wants screenshots"
intent — it already lives in the debug dir.
**Breaks:** a screenshot system that captured unconditionally would
slow every run by a few % and fill the debug directory with stale
PNGs. The opt-in pattern keeps the system free in normal use.
**Tests:** none yet (the integration-test screens follow the pattern,
but no test asserts that `IsEnabled = false` produces no PNG output).
**Depends on:** foundation — "`Logger` requires `Initialize` before any
write" (screenshots respect `MONODREAMS_DEBUG_DIR` the same way logs
do — both default to `debug/` next to the executable, and both honor
the env-var override).

## `ScreenshotCaptureSystem.CaptureNow` is the synchronous, deterministic capture path

Alongside the time-interval async `Update`, `ScreenshotCaptureSystem`
exposes `CaptureNow(float gameTime)`: it reads the backbuffer, encodes a
PNG, **writes the file synchronously before returning**, logs a
`nonBlank=…`/`distinctColors=…` metric, and returns whether the frame is
non-blank. `CaptureNow` bypasses both `IsEnabled` and the interval gate —
the caller decides exactly which frame to capture. The headless Demos
host calls it on chosen frames so a captured frame is guaranteed flushed
to disk before the process exits.

**Why:** the async `Update` path can drop its final save when the process
exits immediately after (the `Task.Run` write never lands), and its
time-interval gate is non-deterministic under a variable headless frame
rate. A frame-driven, synchronous capture is what makes
"`--frames N --exit` always produces a PNG" hold.
**Breaks:** routing the headless capture through the async/interval path
reintroduces dropped final frames and timing-dependent flakes — the test
that asserts a non-blank PNG exists fails intermittently.
**Tests:** `MonoDreams.Tests/IntegrationTests/HeadlessDemoTests.cs`
(asserts a non-blank screenshot from a headless run).
**Depends on:** "Headless Demos renders every frame; capture reads the
backbuffer".

## `FromEnvironment` is the single owner of the capture env contract

`ScreenshotCaptureSystem.FromEnvironment(graphicsDevice, outputDirectory)` is the
only code in the repo that reads `MONODREAMS_SCREENSHOT`,
`MONODREAMS_SCREENSHOT_INTERVAL`, or `MONODREAMS_SCREENSHOT_MAX_FRAMES`. It maps
the whole protocol — mode (`1`/`png` → `CaptureFormat.Png` at 0.5s; `raw`/`rgba`
→ `CaptureFormat.Raw` every frame; `0`/`off`/unset → nothing; anything else →
`Logger.Error` + nothing), the invariant-culture interval override, and the frame
cap — onto either a ready-to-run instance with `IsEnabled = true` or `null`. A
host wires the capture in exactly one line (`_capture =
ScreenshotCaptureSystem.FromEnvironment(GraphicsDevice, debugDir)`) and decides
nothing itself; it reads no capture env var of its own. All env access goes
through `PlatformServices.Current.GetEnvironmentVariable`, never
`Environment.GetEnvironmentVariable`.

**Why:** it is one environment protocol, and a second reader of it would be a
second dialect of it. The variables are typed by convention only (a float in one,
an int in another, an enum-ish string in the third), so every additional reader is
an additional place `raw` could mean something subtly different, an interval could
parse under the ambient culture, or a missing cap could go unnoticed. Centralising
also makes the *refusal* uniform: an unrecognised mode captures nothing and says
so, rather than one host silently defaulting to PNG while another defaults to off.
**Breaks:** a screen or host that reads `MONODREAMS_SCREENSHOT` itself will drift
— it will accept values the factory rejects (or reject values it accepts), miss
the interval/cap variables entirely, and produce a run whose log line disagrees
with what the run actually captured. Reading the env directly (bypassing
`PlatformServices`) additionally breaks the web head, which has no process
environment, and makes the contract untestable without real env mutation.
**Tests:** `MonoDreams.Tests/Debug/ScreenshotCaptureSystemTests.cs` (the full
protocol against a fake `IPlatformServices`: off/invalid → null, format
selection, invariant-culture interval override, frame-cap parse);
`MonoDreams.Tests/IntegrationTests/RawFrameCaptureTests.cs` (the contract end to
end through the real desktop env into the Demos host).
**Depends on:** foundation — "`Logger` requires `Initialize` before any write"
(the factory's rejection path logs); "Debug output respects
`MONODREAMS_DEBUG_DIR`" (the output directory the host hands the factory).

## Raw capture writes synchronously on the main thread and never allocates per frame

`CaptureFormat.Raw` writes each frame from `Update` **synchronously, on the main
thread**: `GetBackBufferData` into a reused `Color[]`, one
`MemoryMarshal.AsBytes(...).CopyTo(...)` into a reused `byte[]`, then
`PlatformServices.Current.WriteAllBytes`. Both buffers are allocated once (and
only re-allocated when the backbuffer geometry changes), so a 60 fps capture
allocates nothing per frame. There is no encode, no staging `Texture2D` upload,
and no distinct-colour pass. `MONODREAMS_SCREENSHOT_MAX_FRAMES` and the
stop-on-write-failure branch are the two safety valves: both set `_stopped`,
log the frame and byte totals, and make every later `Update` a no-op.

**Why:** at 1280x720 the mode produces 3.5 MiB per frame, ~220 MB/s at 60 fps. A
background writer *cannot* keep up with a producer that fast, so handing the write
to a thread pool does not remove the cost — it converts it into a growing queue of
3.5 MiB buffers, and the capture ends in an OOM rather than a video. Writing
synchronously makes the disk the pacer, which is the honest behaviour: the capture
runs as fast as the disk allows and the frame set stays complete. The per-frame
allocation ban is the same argument from the GC's side — 3.5 MiB of fresh garbage
per frame would put a collection inside the capture loop and make the recording a
measurement of the GC. And because the producer can fill a disk inside a minute,
the frame cap is not optional decoration: without it a forgotten `raw` run is a
disk-space incident.
**Breaks:** moving the write to `RunBackground` reintroduces unbounded queueing
(OOM on a long take, and dropped tail frames when the process exits); allocating
the pixel or byte buffer per frame turns a steady ~59.8 fps into a GC-sawtooth and
inflates the live heap the headless heap-sample assertions watch; removing the
`_stopped` valve means a full disk logs one error per frame for the rest of the run
(or throws out of `Draw`), and an uncapped run silently consumes every remaining
byte on the volume.
**Tests:** `MonoDreams.Tests/IntegrationTests/RawFrameCaptureTests.cs` — a capped
headless run asserts exactly the cap's worth of `.rgba` blobs, contiguous frame
counters with no gaps (nothing dropped, no queue to drop from), full-size
uncompressed frames, forward-only embedded game time at full frame rate, and the
cap's stop-and-log line. The write-failure branch shares that `_stopped`
mechanism and is not separately exercised (faking a full disk under a live
`GraphicsDevice` is not honestly reachable in this test suite).
**Depends on:** "Headless Demos renders every frame; capture reads the
backbuffer"; "`FromEnvironment` is the single owner of the capture env contract";
"Debug output respects `MONODREAMS_DEBUG_DIR`" (a firehose must land in a scratch
directory, never the repo).

## Headless Demos renders every frame; capture reads the backbuffer

The Demos host's `Draw` must **not** early-return in headless mode
(unlike the Examples host, whose headless `Draw` is a no-op). Headless
Demos keeps a real `GraphicsDevice` backed by a full-virtual-resolution
backbuffer, and runs the full prep→`MasterRenderSystem`
→`FinalDrawSystem` pipeline every frame. `ScreenshotCaptureSystem` then
reads that composited backbuffer. The backbuffer must stay at the virtual
resolution (not 1×1) or the read-back is meaningless. The window is never
relied on for presentation and is **hidden via `HeadlessWindow.Hide`**
(`SDL_HideWindow` on the live SDL window, resolved through `foundation`'s
`SdlNative` — the engine's single owner of "call an SDL export MonoGame
never bound", so this module carries no SDL library-probing of its own;
the GL context and backbuffer
stay renderable): the old `(-2000, -2000)` position move alone is kept
only as the fallback, because macOS clamps off-screen positions back onto
the display, leaving visible windows a user could accidentally click
during a local test run. On a display-less CI runner the tests run under
`xvfb-run` (SDL still needs a video device to create the window at all). Hiding
alone does not stop the macOS FOCUS STEAL — that happens at app activation
during SDL video init, before any window exists to hide — so headless runs also
call `HeadlessWindow.PreventFocusSteal()` **before the `Game` is constructed**
(both heads' `Program.cs`; the SDL `SDL_MAC_BACKGROUND_APP` hint via env, which
`GameTestRunner` also sets on every spawn): the game launches as an accessory
app that never interrupts the user's typing. And because a hidden,
never-activated window makes `Game.IsActive` false, both heads zero
`InactiveSleepTime` in headless mode — MonoGame's inactive throttle (20ms/frame
≈ 50fps) would otherwise quietly break the headless max-speed contract.

**Why:** the entire point of the headless observe path (issue #28) is to
let an agent verify visual/runtime claims without a human. A no-op `Draw`
or a 1×1 backbuffer renders nothing to read back — exactly the gap in the
Examples headless mode. MonoGame DesktopGL 3.8.4 has no null graphics
device, so a hidden full-res window is the achievable form of "headless
render".
**Breaks:** early-returning `Draw`, or shrinking the headless backbuffer
to 1×1, makes every captured PNG blank and hides render-path memory
behaviour — the leak class #27 documents becomes unobservable again.
**Tests:** `MonoDreams.Tests/IntegrationTests/HeadlessDemoTests.cs`.
**Depends on:** rendering — "`MasterRenderSystem` is the sole renderer";
"Rendering systems run last in the pipeline"; foundation — "`WindowFit` is
opt-in, and it is the ONLY thing allowed to size a game's window" (the same
`SdlNative` seam this hide path resolves through).

## Headless heap samples measure the live set, not transient churn

The Demos headless host samples managed memory with
`GC.GetTotalMemory(forceFullCollection: true)` every K frames and logs it
as `Heap sample: frame=… gt=… bytes=…`. The forced collection makes each
sample the *retained* (live) heap, so a static scene yields a flat series
and a retained-object leak (e.g. the per-frame `EntitySet` leak from #27)
still shows growth. Tests assert flatness via
`GameTestResult.AssertHeapFlat`, dropping the first sample as warmup.

**Why:** `GC.GetTotalMemory(false)` returns currently-allocated memory
including uncollected per-frame garbage, which sawtooths upward over a
short run and is not assertable as "flat". Sampling the live set isolates
leaks (retained) from churn (collected), which is the signal the leak
class needs.
**Breaks:** switching the sample to `forceFullCollection: false` (or
removing the GC) makes `AssertHeapFlat` flake on ordinary churn and
stops distinguishing a real leak from normal allocation.
**Tests:** `MonoDreams.Tests/IntegrationTests/HeadlessDemoTests.cs`.
**Depends on:** rendering — "Per-target draw sets are built once, not per
frame" (the leak this sampling makes observable).

## Debug output respects `MONODREAMS_DEBUG_DIR`

`ScreenshotCaptureSystem` is constructed with an `outputDirectory`
parameter; the reference screen reads `MONODREAMS_DEBUG_DIR` from the
environment and falls back to `<AppDomain.BaseDirectory>/debug`. The
same convention lets `GameTestRunner` redirect a parallel test run's
screenshots into its own scratch directory without colliding with
others.

**Why:** parallel test execution is the load-bearing case. Without an
override, every concurrent test would write into the same `debug/`
folder, clobbering each other's PNGs and logs.
**Breaks:** if a new debug system is added that hardcodes `debug/`
instead of honoring the env var, parallel tests intermittently fail
with file-locking errors or with screenshots from the wrong test
attributed to the wrong run.
**Tests:** none yet (every `GameTestRunner` test depends on this
indirectly).
**Depends on:** foundation — "`Logger` requires `Initialize` before any
write".

## The profiler hooks the one seam every pipeline entry passes through

`SystemProfiler` measures per-system frame cost by timing `GatedSystem` —
the decorator every pipeline entry is wrapped in — and nothing else. One
stopwatch at that seam therefore covers both pipelines of every screen of
every host, groups included: a registrar group's gate reports the group's
own total while its children report individually under their nested names
(`logic.game` above `logic.game.enemies`), because the registrar stamps
each gate's `ProfileName` with the entry's full hierarchical registration
name. The wiring direction is inverted relative to the usual dependency:
`foundation` owns only the **socket** (`GatedSystem.TimingSink`, a static
`Action<string, long>?` that defaults to null and is read once per
`Update`), and this module owns the **plug** — setting
`SystemProfiler.Enabled` installs `SystemProfiler.Record` as that sink and
clearing it uninstalls the sink, so disabling mid-run stops recording
immediately, and with nothing installed no profiler exists in the object
graph at all, not even as a reference. A gate with a null `ProfileName` is
never timed even while a sink is installed. Reading the output: the host
calls `CountFrame()` once per Update and `ReportPeriodically(state, ref
timer)`, which every `ReportInterval` seconds logs a window through
`Logger` and resets it. A report is a header line — `[perf] N frames,
X.XXms/frame in profiled systems:` — followed by one indented row per
system, `<name> <ms>ms <share>%`, sorted by descending ms/frame; rows
under 0.01ms/frame are suppressed once 12 rows have been shown (the tail
is noise). **That format is a parsing contract, not a preference** — the
`[perf]` lines are grep-parsed by verification tooling, so the header
wording, the row shape, and the numeric precision must not change.

**Why:** the platform that matters most here is a browser, where attaching
a native profiler to wasm reports on the runtime rather than on which
system is heavy; timing at the ECS seam gives the same answer on desktop
and on web, and it rides `Logger`, which reaches the browser console on a
web head. Hooking the gate (rather than each system) is what makes the
coverage total for free — nothing has to opt in — and the injectable sink
is what keeps the dependency arrow pointing the right way: a sensitive
core module must not reference an optional debug module just to be
measurable.
**Breaks:** calling into this module from `GatedSystem` directly inverts
the module dependency and drags the profiler into every build that uses
the run-state model. Recording without a sink check (or timing unnamed
gates) puts a stopwatch in the hot path of every gated system in every
shipped game. Leaving the sink installed after `Enabled = false` keeps
recording — and growing the entry table — for a profiler that is
supposedly off. Reflowing the `[perf]` header or row format silently
breaks the tooling that greps it, with no compile error and no test
failure outside the format test.
**Tests:** `MonoDreams.Tests/Profiling/SystemProfilerTests.cs` (sink
install/uninstall, rows named per registration, disable mid-run stops
recording, format contract).
**Depends on:** foundation — "Edit-time behaviour is a per-system policy
honoured by `GatedSystem`".

## Open questions

- **`ColliderDebugSystem` / `SpriteDebugSystem` two-toggle design** —
  both have a static `Enabled` flag *and* the standard `IsEnabled`
  instance flag. The intended split (compile-time global vs runtime
  per-instance?) isn't documented. May simplify to one toggle.
- **Capture interval as a per-screen setting** — the constructor's
  interval is hardcoded per call site (2 seconds in the reference
  screen), and `MONODREAMS_SCREENSHOT_INTERVAL` overrides it only for
  the `FromEnvironment`-built instance. No reason the interval couldn't
  read from `input_replay.json` too, for the replay-driven instance.
- **Encoded clip capture is deliberately absent** — raw frames are the
  verification artefact; muxing them into an mp4/gif belongs outside the
  frame loop (desktop: the ffmpeg binary MGCB already bundles, on a
  background thread; web: `canvas.captureStream()` + `MediaRecorder`).
  Whether the engine should ship that tooling at all is open; what is
  settled is that it must not live inside the capture path being verified.

## Aspirational direction

- Debug HUD overlay (FPS, entity count, draw call count) as another
  opt-in system in this module, sharing the same transient-entity
  pattern.
- A `--capture-on-exit` mode for `ScreenshotCaptureSystem` that
  guarantees one final PNG at game shutdown, useful for replay
  post-mortems.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- This module is opt-in; nothing requires it
- Debug overlays draw via the same `DrawComponent` path as everything else
- Debug overlays must be prep'd before `MasterRenderSystem` runs
- `ScreenshotCaptureSystem` is gated by `IsEnabled` set from `input_replay.json`
- Debug output respects `MONODREAMS_DEBUG_DIR`
