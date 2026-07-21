# Editor shell UI/UX phase 3 (UX3) — Game-mode integrity, overlays, shortcuts, modal transforms

> Follow-up to [`editor-shell-ui-ux-2.md`](editor-shell-ui-ux-2.md) (UX2-A..G). The five
> asks (2026-07-09), mapped: (1) Game-mode blank-scene bug → §1; (2) explicit mode
> labels + auto-play → §1; (3) icon polish → §2; (4) viewport Overlays menu → §3;
> (5) keyboard shortcuts + combo input + modal transforms + status bar → §4/§5.

## 1. Game-mode integrity + explicit modes (wave UX3-A)

**The bug (mechanism confirmed in code).** `EditorCameraRig`'s ctor initializes the rig
to the camera *at overlay-construction time* — which precedes the scene load — and
`SyncFromScene(null)` keeps that default. Every existing scene persists `camera: null`
(the UX2-E audit), so on a fresh launch the rig sits at the pre-load view (origin,
zoom 1) while the content sits wherever auto-framing found it. Entering Game mode does
`Camera := rig` → the view lands on empty world → "the entire scene disappears". The
"returning to Scene mode doesn't help" half must be pinned by a **failing integration
repro first** (fresh boot → `mode:game` → `mode:scene` → assert content visible AND
world intact); prime suspects: `CaptureView` unwired on some screens (`?? default`
restores a zeroed view — note `Camera.Zoom`'s setter clamps to 0.1, so a default
restore lands at origin/0.1, still blank), or the snapshot restore failing after the
sweep (check the fail-loud log path).

**Fixes:**
- **Sane authored default**: when a load carries `camera: null`, the rig re-syncs to
  the **post-load view** (after auto-framing) — "the authored camera starts on the
  content", not on the pre-load origin. First Save then persists it (`scene.camera`
  is written from the rig), so the null-camera class evaporates as scenes get saved.
- **Never-blank entry**: entering Game mode with a rig that still equals the
  never-authored default must land somewhere sane by construction (the re-sync above
  achieves this; assert it in the repro).
- **Exit always restores**: whatever the repro pins (unwired capture / failed
  restore), exiting Game mode must restore both the world and a usable view. A
  `default` `CameraViewSnapshot` must never be applied — treat unwired/zeroed capture
  as "keep the current view".
- **Explicit labels + auto-play (ask 2)**: the toggle segments read **"Scene mode" /
  "Game mode"** (width recomputed; tooltips keep the short names if space is tight —
  the labels are the ask). Toggling INTO Game mode now **auto-plays**
  (`EnterGameMode` → snapshot/capture (unchanged order guarantees) → `RunMode = Play`
  — the same composition Play-in-Scene-mode already uses). Play/Pause inside Game
  mode unchanged; exiting to Scene mode lands Paused (unchanged).

## 2. Icon polish (wave UX3-C)

Per the user's screenshots (Blender reference + the marked-up icons):

- **Arrowheads everywhere**: the filled triangle heads on move / rotate / scale /
  undo / redo / restart / refresh are too subtle — enlarge to ≥22% of the icon box
  and thicken strokes so the heads read at 16pt logical.
- **Snap/grid**: the `#` reads as a hashtag — draw a **closed square border** with
  the inner 3×3 lines (2 vertical + 2 horizontal, inset) so it reads as a grid.
- **Camera-view**: replace the frustum glyph with the classic **video-camera**: a
  rounded body rect (left ~55–60% of the box) with a small top tab, plus a triangle
  on the right whose apex touches the body's right edge (per the reference image).
- **Save**: floppy per the reference — outer square with a **beveled top-RIGHT
  corner**, the top shutter rectangle displaced **slightly LEFT** of centre, the
  bottom label rectangle centred.
- Pure-geometry tests updated (inside-rect, the bevel/displacement asserted as
  relative coordinates); everything else about the icon pipeline unchanged.

## 3. Viewport Overlays menu + grid (wave UX3-D)

Blender's per-viewport Overlays dropdown, adapted:

- **Menu machinery**: `EditorContextMenuModel` gains **checkable items**
  (`Kind = Toggle`, `Checked`) rendered with a small check/box mark; the Overlays
  content is a menu model opened from a new **Overlays** header button (icon: two
  overlapping circles, Blender-style), anchored below it — the same one-model
  primitive, now with toggles.
- **Settings**: a new pure-data `ViewportOverlaySettingsComponent` on the editor
  state entity: `ShowGrid` (default **off** — preserves the current look),
  `GridSpacing` (world units), `OutlineSelected` (default on), `ShowCameraGlyph`
  (default on). Session-scoped v1; per-project persistence is named terrain.
- **Grid spacing = snap step, one value.** The engine has ONE grid quantum (the
  gizmo's snap step); the displayed grid MUST be the grid things snap to, or the
  overlay lies. The menu's "Spacing ▸" presets (8 / 16 / 32 / 64) edit the shared
  value. (Blender separates them; we deliberately don't until there's a reason.)
- **Grid renderer**: world-space line mesh through the existing overlay-projection
  path (clipped to the game viewport, no `VisibleComponent`), lines at `GridSpacing`
  across the visible world range, every 5th line stronger (new theme roles
  `GridMinor`/`GridMajor` — subtle, darker than content). Regenerated on view/spacing
  change; bounded line count (spacing clamps so a zoomed-out view can't explode the
  mesh — degrade by switching to major-only, documented).
- `OutlineSelected` gates the selection outline emit; `ShowCameraGlyph` gates the
  rig glyph (divergence rule unchanged when on). Ops: `overlay:grid on|off`,
  `overlay:spacing <n>`, `overlay:outline on|off`, `overlay:camera on|off`.

## 4. Combo input — an ENGINE feature, not editor plumbing (wave UX3-E)

The user's constraint: future game features must be able to use combo inputs too. So
the chord layer lives in **foundation** (input), game-agnostic:

- **`KeyChord`** (pure struct): a `Key` + modifier flags (`Ctrl`, `Shift`, `Alt`,
  `Meta`) + the virtual **`PlatformCommand`** modifier that resolves to `Meta` on
  macOS and `Ctrl` elsewhere (resolved once at chord-match time via an injected
  platform flag — no `#if` in the module).
- **`KeyChordTracker`** (pure): given previous + current `KeyboardState`, a chord
  **fires on the press edge of its key while exactly the required modifiers are
  held** (extra non-modifier keys don't block; extra modifiers DO — `Ctrl+Shift+Z`
  must not also fire `Ctrl+Z`). Keyboard state arrives through the same injectable
  `Func<KeyboardState>` seam the editor dialog uses; the tracker is testable with
  hand-built states.
- **Replay caveat (documented in the foundation premise)**: the input-replay channel
  synthesizes `AInputState` actions, not raw keyboard chords — chord-driven features
  are exercised headlessly through their op channels (the editor's `menu:*`/`mode:*`/
  etc.), not through replay. A future replay-v2 could record raw keyboard.
- **Editor bindings this wave** (a single `EditorShortcuts` table — one place to read
  the bindings): `Shift+A` → open the **Add** menu at the cursor (the Entities-panel
  add items — Empty Entity now, more later); `Ctrl/Cmd+Z` → Undo; `Ctrl/Cmd+Shift+Z`
  → Redo. Any pre-existing bare-key undo/redo bindings are REMOVED (Blender parity;
  bare keys are reserved for tools).
- **Context gate**: editor chords fire only when the cursor is **over the game
  viewport**, no dialog/menu open, no modal text focus — one predicate
  (`ViewportShortcutContext`) shared by every binding, so panel typing or a dialog
  never triggers tools. Existing editor keys (delete, frame) route through the same
  table + gate for consistency.

## 5. Modal transforms + the status bar (wave UX3-F — depends on E)

Blender's G/S/R modal behavior, on the same coalesced-undo machinery:

- **`ModalTransform`** (pure state machine) + `ModalTransformSystem` (Edit-guarded,
  woven with the early input-owners): `G`/`S`/`R` over the viewport with a selection
  **enters modal mode** — the mouse drives the transform WITHOUT a button held
  (grab: world-space delta from the entry cursor; scale: factor from distance ratio
  to the pivot; rotate: swept angle), applied live through the SAME
  `BeginTransaction`-coalesced command path as a gizmo drag (one modal session = one
  undo step).
  - **Axis constraint**: `X` / `Y` toggle axis lock (press again to clear; the other
    axis replaces). Constrained grab zeroes the other component; constrained scale
    scales one axis (`Transform.Scale` per-axis); rotate ignores axis keys.
  - **Numeric entry**: digits / `-` / `.` / backspace build a number that OVERRIDES
    the mouse (units for grab, factor for scale, degrees for rotate), exactly
    Blender's typed-transform.
  - **Confirm / cancel**: left-click or `Enter` commits the transaction; right-click
    or `Escape` reverts it (the modal owns the pointer + keyboard while active — the
    dialog's consume pattern + `ShouldSuppressInput` OR'd with `Modal.IsActive`; the
    gizmo/selection/palette stand down via the same viewport-press-ownership rules).
  - The rig composes: `G` moves it, `S` edits zoom (the UX2-G mapping), `R` disabled
    for the rig.
- **The status bar** (ask: "like Blender and IntelliJ"): a thin window-bottom strip
  (~22pt, full width, BELOW the assets shelf; shell state + the ONE viewport inset
  gain its height; DPR-covered). Content: while a modal transform is active, the
  live readout — `Move ΔX 12.0 ΔY -3.5 [X] · type = exact · LMB/Enter confirm ·
  RMB/Esc cancel` (values from the modal state, axis tag when locked, the typed
  buffer shown as typed); otherwise contextual status — current scene id + dirty
  dot, mode, selected entity name, entity count. Left-aligned modal/status text,
  right-aligned scene/mode. Plain labels + `Bg0` band — no interaction v1.
- Ops: `modal:grab|scale|rotate`, `modal:axis x|y`, `modal:digits <text>`,
  `modal:confirm|cancel` — the full Blender flow headless-testable.

## 6. Wave plan

| Wave | Scope | Depends on |
|---|---|---|
| **UX3-A** | Game-mode blank-scene bug (repro-first) + rig null-camera default + exit-restore hardening + "Scene mode"/"Game mode" labels + auto-play on entry | — |
| **UX3-C** | icon polish: arrowheads, closed grid, video-camera, beveled floppy | — |
| **UX3-D** | checkable menu items + Overlays dropdown + settings component + grid renderer (spacing = snap step) + outline/camera gates | — |
| **UX3-E** | foundation `KeyChord`/`KeyChordTracker` (+ premise) + `EditorShortcuts` table + Shift+A / Ctrl(Cmd)+Z / Ctrl(Cmd)+Shift+Z + the viewport context gate | — |
| **UX3-F** | `ModalTransform` G/S/R + axis locks + numeric entry + confirm/cancel + the window status bar with the live readout | UX3-E |

Verify gate per wave: `dotnet build MonoDreams/MonoDreams.csproj && dotnet test
--configuration Release` (full solution).

## 7. Pre-mortem

1. **The repro must come first in UX3-A** — fixing the rig default without pinning
   the exit-path failure risks shipping "blank on entry fixed, world-loss latent".
2. **A `default` view snapshot applied to the camera** (zoom 0 → clamped 0.1) is a
   silent blank — the restore path must treat unwired/zero capture as keep-current.
3. **Chord over-matching** — `Ctrl+Shift+Z` firing `Ctrl+Z` too. Exact-modifier
   matching is the rule; test the superset/subset cases both ways.
4. **Modal + existing ownership** — a modal session must claim the pointer the way
   the gizmo claims presses, or a confirm-click also re-picks selection. Reuse the
   ownership rules; test click-confirm over another entity does NOT re-pick.
5. **Grid mesh explosion** — a zoomed-out view over a small spacing must clamp/degrade
   (bounded vertex count asserted in a test), never allocate unbounded.
6. **Status bar joins the ONE inset** — a second bottom rect source desyncs mouse
   mapping; it must flow through the existing shell state → inset derivation.
7. **macOS Cmd vs Ctrl** — the `PlatformCommand` resolution is injected, not `#if`'d;
   both mappings unit-tested.
