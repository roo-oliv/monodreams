# debug — premises

> Technical invariants the engine assumes about the debug overlays
> module: `ColliderDebugSystem`, `SpriteDebugSystem`,
> `ScreenshotCaptureSystem`, `KeepAwake` (the opt-in macOS
> power-management assertion for unattended runs — not a system either),
> and `SystemProfiler` (per-system frame
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
exits immediately after (the `Task.Run` write never lands) — a race no
caller can order. Its time-interval gate accumulates `state.Time`, i.e.
the SIMULATED delta, so which frames it selects follows whatever clock
the host feeds: on a wallclock-dt host (a headless Examples run, which
has no injected clock) the selection varies run to run, while on headless
Demos the injected fixed-step clock makes it count simulated seconds and
land on the same frames every run. A frame-driven, synchronous capture is
what makes "`--frames N --exit` always produces a PNG" hold under either.
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
`MONODREAMS_SCREENSHOT_INTERVAL`, `MONODREAMS_SCREENSHOT_MAX_FRAMES`, or
`MONODREAMS_SCREENSHOT_TARGET`. It maps the whole protocol — mode (`1`/`png` →
`CaptureFormat.Png` at 0.5s of GAME time — the interval accumulates
`state.Time`, so it counts simulated seconds off whatever clock the host feeds,
which on headless Demos is the injected fixed step and elsewhere is the
wallclock delta; `raw`/`rgba` → `CaptureFormat.Raw` every frame;
`0`/`off`/unset → nothing; anything else → `Logger.Error` + nothing), the
invariant-culture interval override, the frame cap, and the capture source
(unset/`window` → the backbuffer; a `RenderTargetID` **name**, case-insensitive →
that target; anything else → `Logger.Error` + nothing) — onto either a
ready-to-run instance with `IsEnabled = true` or `null`. A host wires the capture
in exactly one line (`_capture =
ScreenshotCaptureSystem.FromEnvironment(GraphicsDevice, debugDir)`) and decides
nothing itself; it reads no capture env var of its own. All env access goes
through `PlatformServices.Current.GetEnvironmentVariable`, never
`Environment.GetEnvironmentVariable`. The effective mode, interval, cap **and
source** are reported on one init log line, which is the only record a directory
of frames has of what it is a picture of.

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
selection, invariant-culture interval override, frame-cap parse, source
selection including the name-only target parse);
`MonoDreams.Tests/IntegrationTests/RawFrameCaptureTests.cs` and
`RenderTargetCaptureTests.cs` (the contract end to end through the real desktop
env into the Demos host).
**Depends on:** foundation — "`Logger` requires `Initialize` before any write"
(the factory's rejection path logs); "Debug output respects
`MONODREAMS_DEBUG_DIR`" (the output directory the host hands the factory).

## Target capture reads a fixed-resolution render target, resolved from the passes that ran

`MONODREAMS_SCREENSHOT_TARGET` (or the `captureTarget` constructor argument) makes
`ScreenshotCaptureSystem` read a named `RenderTargetID`'s target instead of the window
backbuffer, at **that target's own resolution** — so the file geometry is independent of
window size, of a mid-run resize, and of letter/pillarboxing, and a single layer (just
`UI`) can be captured on its own. The target is not registered anywhere: screens own their
targets privately, so a capture with a named target subscribes to
`MasterRenderSystem.RenderedTargetSink` (a null-by-default socket owned by `rendering`) and
takes the **first live** target published for that id since its last read, clearing the slot
after every read. Subscribing happens only when a target was named, and `Dispose`
unsubscribes. A latched target the screen has since **disposed** counts as no target on both
sides of the slot: the publish path replaces it with the next pass's target (a plain `??=`
would refuse the replacement), and the read path drops it and re-resolves from the next pass.
When no pass has drawn that id — a screen without such a pass — the capture writes
**nothing** that tick (no counter consumed, no fallback to the window) and logs one warning
until a pass appears; a target that cannot be read back at all stops the capture with an
error rather than throwing out of `Draw` every frame. Capture still runs after the composite,
which is also what leaves the target unbound and readable. An unset target is the window
backbuffer, byte for byte the pre-existing behaviour.

**Why:** a backbuffer capture is a photograph of a window, so the same frame on another
machine (or after a resize) lands at another size and every pixel coordinate an agent noted
becomes wrong; the stable-evidence property is the whole feature. Reading the passes
instead of a registry is what makes it work with zero screen changes: no screen has to
announce a teardown, because a dead target simply loses to the next publisher. The
disposed check is the whole of that protocol, and it is **not** covered by clearing the
slot after a read: an interval capture (PNG's default 0.5s) latches a target and reads it
some thirty frames later, and a screen switch or a window resize — the editor chrome
rebuilds its target on one — happens in between. First-publisher-wins picks the primary pass
when a screen renders one id twice (the camera demo's world pass, then its minimap pass) —
the order screens composite in. Refusing to capture rather than falling back to the window is
the same rule the mode parse follows: evidence at the wrong geometry looks right and compares
with nothing.
**Breaks:** falling back to the backbuffer when a target is missing silently reintroduces
window-sized files in the middle of a target-sized set. Keeping the resolved target across
frames (not clearing after a read) makes last-publisher-wins read the minimap. Latching with
`??=` (or returning from the read path without clearing a disposed latch) pins the capture to
a dead target the first time a screen switches or the window resizes, and the run captures
nothing from there on — silently, since a warning is logged once. Leaving the sink subscribed
after `Dispose` keeps a dead screen's targets — and the capture — alive through a static
delegate for the rest of the process. Running the capture before `FinalDrawSystem` reads a
bound target, which throws.
**Tests:** `MonoDreams.Tests/Debug/ScreenshotCaptureSystemTests.cs`
(`WindowCapture_NeverTouchesTheRenderSocket`,
`TargetCapture_PlugsIntoTheRenderSocket_AndUnplugsOnDispose`,
`ResolvedTarget_IsReplaced_WhenTheLatchedOneWasDisposed`,
`ResolvedTarget_IsDropped_WhenItWasDisposedWithoutAReplacement`,
`ResolvedTarget_KeepsTheFirstPublisher_WhileItIsAlive`,
`ResolvedTarget_WarnsOncePerGap_WhenNoPassEverDrawsTheTarget`, plus the source-parse
theories); `MonoDreams.Tests/IntegrationTests/RenderTargetCaptureTests.cs` — a headless UI
demo run capturing `Scroll` (360x220) while the backbuffer is 1280x720, asserting the frame
names, the byte sizes and the untouched window-mode instance.
**Depends on:** rendering — "A render pass publishes its destination through a
null-by-default socket"; "`FinalDrawSystem` composites an explicit, ordered layer list" (it
is what unbinds the targets); "`ScreenshotCaptureSystem.CaptureNow` is the synchronous,
deterministic capture path" (the same source resolution serves it).

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
≈ 50fps) would otherwise quietly break the headless max-speed contract. For the same
reason a headless branch **never calls `WindowFit.Apply`**: fitting the window to the
display would resize the very backbuffer this contract pins (to the virtual resolution
here, to 1×1 in the Examples host), and `MONODREAMS_WINDOW` would then silently
re-geometry every captured frame. Window fitting belongs to the windowed branch only,
and a headless run logs that it skipped it.

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

## Headless Demos advance a deterministic fixed-step clock

Under `--headless` the Demos host never hands the pipeline MonoGame's own
`GameTime`. It advances an injected fixed-step clock
(`MonoDreams.Demos.HeadlessClock`) exactly once per `Update`, and `Draw` reads
the instant that `Update` advanced to rather than advancing again — the clock
ticks once per frame, never twice. The step is a constant
`Game.TargetElapsedTime` (1/60 s, the rate the windowed path runs at), and
`TotalGameTime` is recomputed from the frame COUNT (`step.Ticks * frames`,
integer arithmetic) instead of accumulated, so it carries no rounding drift:
frame N reports the same instant in every run. The host logs which clock
produced the run (`Headless clock: deterministic fixed step …`). This changes
the SIMULATED delta only, not the host's pacing: `IsFixedTimeStep` stays
`false` and VSync stays off, so the max-speed contract of the premise above is
intact — frames are still produced as fast as the machine can — and the
windowed path never constructs the clock, receiving MonoGame's `GameTime`
unchanged.

The clock makes a run's TIME deterministic; it does not by itself make its
PIXELS deterministic. A bare headless run still reads the hardware mouse
(`CursorInputSystem` without `SkipHardwareRead`), whose window-relative
position varies per launch; it still reads the hardware KEYBOARD, and the
headless window is hidden best-effort (an SDL hide plus a macOS-only
focus-steal hint), so a key held while a run happens to own focus moves the
camera demo's ball, advances the dialogue or types into the UI demo's text
field in that run only; and the physics demo still builds its scene from an
unseeded `Random`. Byte-identical captures therefore additionally require the
deterministic-input protocol the precheck below uses — an editor op plan
present (which is what engages it) plus final-frame-only capture — and any
pixel-identity gate must be stated under that protocol, never over a bare run.

The protocol has **two hardware legs, and both are pinned at one seam**.
`DemoKeyboard.Engage` (the Demos host) is called by every demo screen under
the same condition as the cursor line — `Overlay.HasEditorOpPlan` — and it
sets `CursorInputSystem.SkipHardwareRead` (the mouse), flips the shared
`DemoKeyboard.Read` gate that every demo screen's keyboard reader goes
through, and sets `SkipHardwareRead` on each `AKeyboardInputHandlingSystem`
in the screen (the demo's own action mapper and the editor's key surface).
The **editor overlay's own keyboard readers** are on the same gate, but not
through `Engage`: they are constructed with it. `EditorOverlay` takes one
`readKeyboard` seam and threads it to all six (both panels, the dialog, the
context menu, the modal transform, the shortcut chord tracker), and
`DemoEditor` passes `DemoKeyboard.Read` — because the editor flag is what the
protocol turns ON, those six run `RunNormally` in every precheck run and are
inert only while no editor UI is open. "Today's op plan opens no panel" is not
a property the protocol may rest on: one cursor op over the chrome plus a held
chord key is a byte diff in one run of two. Off the protocol `readKeyboard`
resolves to `Keyboard.GetState` exactly as before, so a windowed demo is
unchanged. Engaging is **observable**: the run logs
`Deterministic input: hardware reads skipped on '<screen>'`, the precheck
asserts that line, and a lint forbids `Keyboard.GetState()` / `Mouse.GetState()`
in any scanned demo source except the seam file itself, requires an engine
reader whose seam DEFAULTS to the hardware to be constructed with one
(`TextInputSystem.KeyboardStateProvider`, `KeyChordTracker`'s seam argument,
`EditorOverlay`'s `readKeyboard` — six readers behind one argument), and
requires every `AKeyboardInputHandlingSystem` subclass a screen constructs
to reach `Engage`'s argument list — `Engage` logs its line whether or not a
given system was handed to it, so the run-time assertion alone cannot see an
omission. Three properties make that lint the guarantee it reads as, each of
which was once absent while the sentence above was already written. It matches
the argument's **value**, not its presence: a second `KeyChordTracker` argument
that is `null`, or an `EditorOverlay` given `readKeyboard: Keyboard.GetState`,
defaults exactly as an omission does. Its subclass set is seeded from the
**engine's** declarations as well as the demos': `DefaultEditorKeys` is declared
in `level-editor` and constructed by `DemoEditor`, so a set built from
demo-declared subclasses alone never checked the editor key surface at all. And
the seam file's exemption is **one gated read, not a waiver for the file**: that
file must hold exactly one `Keyboard.GetState()`, on the `SkipHardwareRead` gate
line, and no `Mouse.GetState()`. Losing a leg is therefore a red test rather
than an intermittent byte diff blamed on the change under test.

The precheck's scope is itself pinned. Every screen the host can boot is
either run by the precheck or named — with the reason it cannot be — in the
precheck's own exclusion list, and a test compares that pair against the
host's screen registry. So "the demos are byte-reproducible" always says how
many screens it covers, and a screen added later (or dropped from the run
list) fails loudly instead of narrowing the claim in silence. Today the one
exclusion is the physics demo's unseeded `Random`, its cause is recorded as a
typed `ExclusionCause` rather than a sentence (so rewording the entry cannot
disable the converse check that reads it), and that "one" is checked rather
than believed: a second test scans every `.cs` file of every covered screen's
demo DIRECTORY (not one file per screen — a demo split in two would leave half
unscanned) PLUS the demo-owned sources every screen composes —
`MonoDreams.Demos/UI` and the host root (`Game1`, `DemoKeyboard`, `DemoEditor`,
the headless clock) — and fails on a value the next run will not reproduce,
even when nothing currently consumes it. That root list is **enumerated, not
trusted**: every top-level directory of the demos host holding C# source must
lie inside a root a COVERED screen's scan reads, so a new
`MonoDreams.Demos/Systems/` (or a `ShapeBuilder` moved out of `UI/`) cannot be
scanned by nothing while the tests stay green.

The census resolves names at the **set's** scope, not one file's, and each kind
of name on its own terms. A `Random`-typed member declared in one scanned file
and target-typed-constructed from another (`ShapeBuilder.Jitter = new();`) is an
RNG in both — a per-file name set sees one in neither. A seed CONSTANT resolves
qualified across the set and bare only within the file that uses it, which is
how C# resolves it: pooling bare names would accept `new Random(Seed)` because
an unrelated file declares a `const int Seed` while the local `Seed` is computed
at runtime, and a qualified name binds to the type that DECLARES it, so `B.Seed`
does not resolve against sibling class `A`'s constant. RNGs are matched on the
type, not on one syntactic shape: `new Random()`, `Random.Shared`, a
target-typed `new()` reaching a `Random` in any of its shapes (nullable,
constructor body, property initialiser, `??=`, expression body, collection
expression — including the multi-line forms, since the type is read from the
enclosing STATEMENT and not from one line), and any seed that
is not a compile-time integer constant — an `Environment.TickCount` seed is no
better than none, and a seed qualified by a type the scan never saw declared is
not resolvable and counts as unpinned. The census is not RNG-only: reading the
wallclock (`DateTime.Now`, `Stopwatch.GetTimestamp()`/`StartNew()`, an instance
stopwatch's `.Elapsed`, `TimeProvider`), per-process identity (`Guid.NewGuid()`,
`Environment.TickCount`/`ProcessId`/`CurrentManagedThreadId`) or the
per-process-randomised `GetHashCode()` makes scene content per-process in
exactly the same way and fails the same test. A dormant source (the camera demo's hit-shake
jitter, seeded since) reds a later run the moment an op plan reaches it, while
every record still names physics. Seeding an excluded screen without widening
the covered set fails the same test from the other side. The claim's scope is
exactly that: the sources the demos OWN. The engine systems a demo composes are
outside it — they carry no RNG today, and a lint covering them belongs to the
engine, not to this precheck.

**Why:** the whole point of the headless Demos path (issue #28) is to let an
agent verify its own work without a human, which requires that re-running the
same scene produce the same output. With the wallclock dt MonoGame hands a
max-speed host, `GameState.Time`/`TotalTime`, the `[GT …]` stamp on every log
line, the `gt=` field of a screenshot filename and anything integrating over dt
all differ between two runs of the same demo, so "did my change alter the
output?" was unanswerable in principle. The ECS-backend migration's
screenshot-identity gate (issue #119) rests on this precheck.
**Breaks:** passing the host `GameTime` through in headless returns every
derived value to the wallclock and turns screenshot/log comparison into a
measurement of machine load; advancing the clock in `Draw` too doubles its rate
and makes the drawn frame report a different instant than the `Update` it
follows; accumulating `TotalGameTime` (`total += step`) reintroduces drift, so a
frame's instant depends on the path taken to it; extending the clock to the
windowed path would change the player-visible game to serve a testing aid;
throttling the host to the step would break the max-speed contract (a 600-frame
run would cost ten wall-clock seconds); and a demo screen that reads the
hardware directly (either `Keyboard.GetState()` or a cursor pipeline it never
engages) reopens the input leg — every run stays green on a machine with no key
held, so the byte-identity claim quietly becomes conditional on the developer's
hands and the first red run is blamed on the change under test.
**Tests:** `MonoDreams.Tests/IntegrationTests/HeadlessClockTests.cs` — one test
pins the fixed step against the wallclock (game time between two heap samples is
exactly the frame gap × the step), one pins run-to-run determinism (two runs
observe an identical printed game-time series), and
`HeadlessClock_IsConstructedOnlyOnTheHeadlessBranch_AndTheWindowedPathFallsBackToGameTime`
source-scans `Game1.cs` for the headless-only half — which has no runtime
observable, because every test spawns `--headless` — pinning the single
construction site inside the constructor's `if (_headless.Enabled)` branch and
the `?? gameTime` fallback at both read sites;
`MonoDreams.Tests/IntegrationTests/DeterministicClockTests.cs`
(`Demo_RunTwiceHeadless_ProducesByteIdenticalPngs`) carries it to pixels — five
demo screens, each run twice under the deterministic-input protocol, compared
byte for byte via `GameTestRunner.AssertScreenshotsByteIdentical`, each run
asserting the protocol's own `Deterministic input: hardware reads skipped` line;
`Precheck_CoversEveryDemoScreen_OrNamesTheExclusionAndWhy` in the same file
holds the scope, failing when a registered demo screen is neither run nor
excluded-with-a-reason (and cross-checking the registry against `Game1`'s
`RegisterScreen` call sites, so a screen registered from a raw literal cannot be
bootable-but-unscanned);
`Precheck_CoveredScreensPinEveryNondeterministicSource_AndTheExclusionReasonStillHolds`
holds the exclusion's content, failing on an unpinned `Random` or entropy read
in a covered screen's scanned sources (dormant included) or on an excluded
screen whose OWN sources — the shared roots excluded, since they belong to every
screen alike — no longer justify its typed cause;
`Precheck_ScansEveryDirectoryOfTheDemosHost` holds the root list against the
host's directory tree;
`NondeterminismCensus_MatchesOnTheType_NotOnOneSyntacticShape` and
`NondeterminismCensus_ResolvesNamesAcrossTheScannedSet_WithoutPoolingBareOnes`
are the census's own contract, pinning both directions on synthetic sources —
single-file (each escaping shape must be caught; each properly pinned seed must
not be flagged) and cross-file (a `Random` declared in a sibling source and
constructed here is caught; a bare seed name is NOT resolved by a sibling's
constant, and a qualified one is not resolved by a sibling TYPE); and
`Precheck_EveryDemoScreenRoutesHardwareInputThroughTheProtocol` holds the input
legs, failing on a direct `Keyboard.GetState()`/`Mouse.GetState()` outside the
seam file, on a seam file that holds more than the one gated read (or any mouse
read), on a `TextInputSystem`/`KeyChordTracker`/`EditorOverlay` built without the
demos' gate as its seam VALUE, on an `AKeyboardInputHandlingSystem` subclass the
screen constructs but never hands to `Engage` (engine-declared subclasses
included — `DefaultEditorKeys`), or on a screen that builds a cursor pipeline
without calling `DemoKeyboard.Engage`.
**Depends on:** "Headless Demos renders every frame; capture reads the
backbuffer" (there are pixels to compare only because `Draw` is not a no-op);
"Headless heap samples measure the live set, not transient churn" (the `Heap
sample:` line is what makes the clock readable from outside the process);
cursor — "`SkipDerivation` lets an injection channel own the cursor's derived
positions" (the `SkipHardwareRead` half of the input protocol); level-editor —
"The overlay reads the keyboard at ONE injected seam; the per-system default is
the hardware" (the editor half of the keyboard leg, which the protocol turns on
by requiring the editor run flag); foundation —
"A suppressed `Logger` line costs nothing, and an emitted one is
byte-identical" (the `[GT …]` stamp the clock feeds).

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

## Keep-awake is opt-in, macOS-only, and never fatal

`KeepAwake.FromEnvironment()` holds an `NSProcessInfo` activity
(`NSActivityUserInitiated | NSActivityIdleDisplaySleepDisabled` — the in-process
`caffeinate -disu`) for as long as the returned token lives, and returns `null`
when `MONODREAMS_KEEP_AWAKE` is unset/`0`/`off`. It is the only reader of that
variable, through `PlatformServices.Current.GetEnvironmentVariable`. Off macOS it
returns `null` after logging that it is a no-op; if the Objective-C runtime cannot
be reached it logs a warning and returns `null`. It **never throws** — the
Objective-C entry points are resolved through `NativeLibrary`/`GetDelegateForFunctionPointer`
(the `HeadlessWindow` idiom), not `DllImport`, so a platform without libobjc is a
caught miss rather than an unresolved import, and a WASM publish has nothing to
bind. The activity token is `retain`ed on begin and `release`d on end, and
`Dispose` is idempotent. Hosts hold it for the process lifetime and dispose it
before `Logger.Shutdown` so both lines land in the run's own log.

**Why:** an unattended macOS run is suspended by App Nap (every headless run has a
hidden window) or by display/idle sleep, and the failure is invisible — a
three-hour run was found hung inside `Cocoa_GL_SwapWindow` with no log line and a
frame set that just stops. Opt-in is the boundary: a game must not assert anything
about a user's power management because it happens to link this module. Not
throwing is the other half — a keep-awake is a comfort, the run is the point, so
an unavailable runtime must degrade to a logged no-op.
**Breaks:** making it default-on quietly overrides the power settings of everyone
who installs `debug`. Returning an autoreleased token without `retain` ends the
activity at the next run-loop turn — silently, so the run looks protected and is
not. Throwing (or a `DllImport` that fails to resolve) takes down a run that would
otherwise have completed, and would surface first on the platform that cannot
possibly support the feature.
**Tests:** `MonoDreams.Tests/Debug/KeepAwakeTests.cs` (off-by-default, off/invalid
values, macOS holds a real activity while other platforms log the no-op, idempotent
release); `MonoDreams.Tests/IntegrationTests/KeepAwakeHostTests.cs` (the host
wiring: held before the run and released after it, and silent when unasked).
**Depends on:** foundation — "`Logger` requires `Initialize` before any write"
(hosts call this straight after `Logger.Initialize`, and dispose it before
`Logger.Shutdown`).

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

## `PointerReplaySystem` injects into the real cursor component; it never simulates a click

The scripted-pointer channel drives a screen by writing the same `CursorInputComponent` a real
mouse fills — position, the button LEVELS *and* their press/release edges, the scroll
accumulator — and by placing the cursor entity through `Cursor.ApplyPose`, the same
per-render-target pose rule `CursorPositionSystem` uses. It never calls a button's handler, sets a
hover flag, or publishes an interaction message itself. A screen that composes the driver stands
the hardware path down on both halves (`CursorInputSystem.SkipHardwareRead = true` **and**
`CursorPositionSystem.SkipDerivation = true`) and registers the driver immediately after the
cursor-input stage, so every consumer downstream reads the injected pointer the same frame it
would read a real one. The button edges are derived from the DRIVER's own previous levels, never
from the mutable level fields on the component, so a consumer that clears them to consume a click
cannot poison the next frame's edges.

**Why:** the value of a scripted pointer is that it exercises picking, focus, hit-testing, hover
and UI arbitration — the parts most likely to be wrong. A driver that shortcut to the handler
would verify the driver and leave exactly the interesting layer untested, and it would drift the
moment the UI's own pick rule changed. Injecting one frame upstream of everything is what makes
"an agent drove the menu" mean "the menu works".
**Breaks:** setting only `SkipHardwareRead` lets `CursorPositionSystem` recompute the derived
positions from the injected `ScreenPosition` and clobber the injection with
`OutsideViewport = true`, so every world-space consumer treats the click as "over chrome, ignore
it" — the click silently does nothing. Registering the driver late (after the UI systems) delays
every scripted interaction by a frame. Deriving the edges from `CursorInputComponent.LeftButton`
reintroduces the consumed-click bug the cursor module's edge premise exists to prevent.
**Tests:** `MonoDreams.Tests/Debug/PointerReplaySystemTests.cs`
(`Move_WritesVirtualWorldAndTransform_ThroughTheRealPoseRule`,
`Move_OnAMainTargetCursor_PlacesTheTransformInWorldSpace`,
`Click_ProducesAPressEdgeThenAReleaseEdge_ThenNothing`,
`Click_WithHold_KeepsTheButtonDownForThatManyFrames`, `Click_Right_DrivesTheRightButtonOnly`,
`Wheel_PulsesTheDeltaForOneFrame_AndAccumulatesTheValue`);
`MonoDreams.Tests/IntegrationTests/PointerReplayTests.cs`
(`ScriptedClick_OnAMenuButton_DrivesTheRealInteractionPipeline` — a spawned run where the injected
release edge travels the menu's real pick → `UIFocusActivated` → `ButtonInteractionSystem` path and
really changes screens); `MonoDreams.Tests/IntegrationTests/ExamplesAdoptionTests.cs`
(`PointerDwellOnALevelButton_ShowsItsTooltip_AndTheClickLoadsTheLevel` — the injected pointer also
RESTS, so the ui module's dwell-gated tooltip appears for it exactly as for a hand on the mouse).
**Depends on:** cursor — "`SkipDerivation` lets an injection channel own the cursor's derived
positions"; cursor — "Button press/release edges derive from CursorInputSystem's own
previous-state, immune to consumers clearing the level fields"; cursor — "Cursor
`TransformComponent.Position` depends on render target".

## Pointer coordinates are authoring space, and time is frames

A `PointerReplayPlan` addresses points in **authoring space** — the virtual-resolution coordinates
the game's UI is laid out in — and the driver derives world coordinates from them through the
screen's `Camera`. It never speaks window pixels and never inverts the letterbox mapping. Its
clock is likewise a **frame counter it owns**, not `GameState.TotalTime` and not the wall clock:
each command occupies whole frames (a `move` one, a `click` `hold`+1, a `type` two per character,
a `waitUntil` as many as its predicate needs).

The one cursor field that is **not** in authoring space is `CursorInputComponent.ScreenPosition`,
which is backbuffer pixels by contract, so the driver maps the authored point *forward* through
`ViewportManager.ScaleVirtualToScreenCoordinates` — the exact inverse of the mouse mapping
`CursorPositionSystem` applies — rather than writing an authoring-space number into it. A direct
consequence, and the honest limit of the channel: an authored point always lands inside the game
viewport, so the editor shell's chrome (toolbar, panels, tabs — laid out and hit-tested in screen
space, in the inset margins) is **not addressable from a pointer plan**. Scripting the editor's own
controls is `EditorOpReplaySystem`'s job, by action name.

**Why:** a script that named window pixels would break on every resize, window-mode change and
resolution bump — the exact fragility the two-space model exists to remove — and it could not run
at all on a headless host, whose 1x1 backbuffer has no meaningful window-to-virtual mapping to
invert. Frame counting is what makes two runs of the same plan identical: under a variable
timestep (headless runs at max speed, a loaded CI machine does not) a time-based script executes
a different number of frames per command every run, which is how a scripted scenario becomes a
flaky test. And `ScreenPosition` has exactly one meaning across the engine — `CursorInputSystem`
multiplies the raw OS mouse by `DevicePixelRatio` to hold it, and every chrome hit-test reads the
field raw — so a channel that fills it owes it that space.
**Breaks:** authoring in window pixels makes every scripted scenario a resolution-specific
artifact and makes headless scripting impossible. Scheduling on `TotalTime` makes stage boundaries
land on different frames run to run, so a click can arrive before the frame that laid the button
out. Writing the authored virtual point straight into `ScreenPosition` puts two spaces in one
field: on a device-resolution backbuffer (macOS Retina under the editor run flag, `DevicePixelRatio
= 2`) every chrome hit-test then reads half the intended point, and at ratio 1 a game click can
spuriously land on whatever chrome happens to sit at those screen coordinates.
**Tests:** `MonoDreams.Tests/Debug/PointerReplaySystemTests.cs`
(`Move_WritesVirtualWorldAndTransform_ThroughTheRealPoseRule` asserts the camera-derived world
position differs from the authored one; the click/hold/type tests pin the per-frame cadence;
`ScreenPosition_IsMappedIntoBackbufferPixels_NotTheAuthoredVirtualPoint` and
`ScreenPosition_WithoutAViewportManager_IsTheAuthoredPoint` pin the screen-space half);
`MonoDreams.Tests/Rendering/ViewportInsetTests.cs`
(`VirtualToScreen_IsTheInverseOfTheMouseMapping`,
`VirtualToScreen_FollowsADeviceResolutionBackbuffer`).
**Depends on:** rendering — the camera's virtual-resolution contract; cursor —
"`CursorInputComponent.ScreenPosition` is backbuffer pixels, on the injected path too".

## A pointer plan gates on observables, times out, and drains into an exit

`waitUntil` is the reason a scripted pointer is usable at all: a stage waits for something
*observable from outside the game* — an entity with a given `EntityInfoComponent` exists, a log
line has appeared, N frames have passed — before the next command runs. Every wait carries a
`timeoutFrames` (600 by default): on expiry the driver logs an `ERROR` naming the predicate and
**continues** rather than blocking forever. When the last command finishes, the plan drains and,
after `tailFrames`, the driver invokes `requestExit` exactly once. The log-line predicate reads a
bounded ring fed by `Logger.LineSink`, and that ring deliberately **excludes the driver's own
`[pointer]` lines**.

A log wait also **consumes** the line it matched: the driver holds a watermark over the ring and
moves it past that line, so no later wait can be satisfied by it. The watermark advances only on a
match — never to "now" when a wait starts — because the line a wait gates on is normally written by
the command before it, downstream of the driver in a frame that has already finished by the time
the wait first runs.

**Why:** without stage gating a script races the game it drives ("click Submit before the dialog
exists") and flakes; that is the single lesson the game-side original contributed. Continuing on
timeout instead of hanging is what turns a broken scenario into a diagnosable log line rather than
a CI timeout with no artifacts. Auto-exit on drain is the input replay's contract, and it is what
makes an unattended agentic run terminate on its own. Excluding the driver's own lines is not
tidiness: the announcement `waitUntil log="level ready"` *contains* `level ready`, so recording it
would satisfy every log predicate on the frame it starts — a wait that always passes is worse than
no wait. The consuming watermark exists for the same reason one step further out: a scan over
everything since construction makes the second of two identical waits pass instantly on the first
one's line, so the command it gates fires ungated.
**Breaks:** an un-timed-out wait hangs the run and the harness kills the process, losing the exit
code and often the log tail. A plan that never requests exit leaves an unattended run alive until
the harness timeout. Recording the driver's own narration makes `waitUntil log` a no-op that still
looks like it worked. An unwatermarked (or start-of-wait-snapshotted) log predicate breaks the two
common shapes in opposite directions: repeated waits pass instantly, or every wait sits until its
timeout because the line it wanted arrived one frame before it started.
**Tests:** `MonoDreams.Tests/Debug/PointerReplaySystemTests.cs`
(`WaitUntilEntity_BlocksTheScriptUntilTheEntityExists`,
`WaitUntilEntity_TimesOut_AndTheScriptContinues`,
`WaitUntilLog_IsSatisfiedByALineWrittenWhileTheDriverRuns`,
`WaitUntilLog_DoesNotReuseTheLineAnEarlierWaitAlreadyMatched`,
`WaitUntilLog_MatchesALineWrittenBeforeTheWaitStarted`,
`DrainedPlan_RequestsExitOnce_AfterTheTail`);
`MonoDreams.Tests/IntegrationTests/PointerReplayTests.cs`
(`WaitUntilThatNeverComesTrue_TimesOutAndTheRunStillEndsItself`).
**Depends on:** foundation — "`Logger.LineSink` is a single-owner tap that must not log".

## The pointer channel is file-gated and single-owner

`PointerReplaySystem.TryLoad` returns `null` when `pointer_replay.json` is absent, unparseable or
empty, so a screen that wires the channel is byte-identical to one that never had it whenever the
file is missing — the same gate `input_replay.json` uses, in the same `MONODREAMS_DEBUG_DIR`-aware
directory. A driver also OWNS the cursor while it lives: exactly one pointer-injecting channel may
run per session (the `level-editor`'s `EditorOpReplaySystem` is the other one), and it owns
`Logger.LineSink` from construction until `Dispose`, which it must release.

**Why:** the channel is wired into shipped screens, so its cost when unused has to be exactly zero
constructed objects — that is what makes leaving it wired defensible rather than a debug branch
someone has to remember to strip. Two channels stamping the same `CursorInputComponent` in one
frame is last-writer-wins on the position AND on the edges, which produces clicks that land
nowhere and is very hard to read from a log. And a driver that keeps the log tap after its screen
is disposed keeps a dead object taping every line for the rest of the process.
**Breaks:** an eagerly-constructed driver changes the composition of every screen that wires it.
Two live channels fight over the cursor. A leaked `Logger.LineSink` keeps a disposed driver's ring
growing (and its `EntitySet`s referenced) for the remainder of the run.
**Tests:** `MonoDreams.Tests/Debug/PointerReplaySystemTests.cs` (`TryLoad_WithoutAPlanFile_BuildsNoDriver`,
`TryLoad_ReadsAHandWrittenPlan_WithItsDefaults`, `Dispose_ReleasesTheLoggerTap`);
`MonoDreams.Tests/IntegrationTests/PointerReplayTests.cs` (`WithoutAPointerPlan_TheMenuComposesNoDriver`).
**Depends on:** "Debug output respects `MONODREAMS_DEBUG_DIR`".

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
- **A pointer plan is per-screen, and a screen transition ends it** — the driver belongs to the
  screen that composed it, so navigating away disposes it mid-plan (the destination screen's own
  driver or input replay owns what happens next). A scenario that has to span screens currently
  needs a plan per screen; whether a session-scoped pointer channel (owned by the host, surviving
  `ScreenController` swaps) is worth the extra lifetime is open.
- **Dragging is not expressible yet** — `click` is press-hold-release at one point, so a
  press → move → move → release drag (a gizmo drag, a card drag, a slider) cannot be scripted. The
  obvious extension is `down`/`up` primitives with `click` as sugar over them; it was left out of
  the first cut to keep the command set exactly the one proven game-side.
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
