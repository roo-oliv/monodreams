# Viewport tabs 2 (TB phase) — session-scoped tabs, cross-screen Game, named scene tabs

> User feedback (2026-07-10, hands-on on Level Selection): (1) Play spawns the Game
> tab, but clicking "Level 1" IN the played game vanishes the Game tab and lands in a
> confused "Scene tab in game mode" — the Game tab should FOLLOW gameplay across
> screen transitions while the Scene tab keeps holding the level-selection scene;
> each scene opens its own tab titled by SCENE NAME (not "Scene"); the tools move a
> row BELOW the tabs so many tabs don't fight the buttons; Play/Restart join Save on
> the far right. Applies to all Demos too. (2) Level-Selection buttons are
> unclickable in the viewport and behave inconsistently under gizmo vs G — buttons
> may be reworked freely ("no fear", everything in Examples/Demos is testing code).

## 1. Why the Game tab vanishes today (the architectural gap)

The `ViewportContextStack` + tab strip are owned by the per-screen `EditorOverlay`.
A gameplay screen transition (`ScreenController.LoadScreen`) disposes the screen,
its world, its overlay, and therefore the whole stack — every tab, every snapshot.
The new screen builds a fresh overlay with a fresh `[Scene]` tab while `RunMode`
(shared `GameState`) is still `Play` → the user's "Scene tab in game mode".

## 2. The model (wave TB-A)

- **`EditorSession`** — a host-scoped object (created once in each host's `Game1`
  beside the `ScreenController`, passed to every screen like the project context):
  owns the `ViewportContextStack`, the tab descriptors, and the per-context state.
  The overlay BINDS to the session instead of owning a stack; screen disposal no
  longer destroys tabs. (The exact pattern `GameState` already proves: session
  state survives switches.)
- **Contexts record their screen**: a context = `{ Kind, SceneId, ScreenName,
  Snapshot, View, dirty/save-point }`. Scene tabs are **titled by scene id**
  (`level_selection`, `island2`, …).
- **Scene tabs are per-scene, many may be open**: the Scenes panel's select now
  **opens (or activates) that scene's tab** instead of switching in place; ≥1 scene
  tab always exists (the last one refuses to close); closes are dirty-gated as
  today. The old "switch discards/gates in place" premise is superseded — switching
  TABS never discards (contexts persist); only CLOSING gates.
- **Cross-screen tab activation**: activating a tab whose `ScreenName` differs from
  the live screen: snapshot the active context → set the session's **pending
  activation** → `LoadScreen(target.ScreenName)` (with the requested-level hand-off
  for the scene-hosting screen) → the new screen's overlay binds the session, sees
  the pending activation, and **restores that context through the reader** instead
  of (after) its default fresh load. Same-screen activation stays the in-place
  swap it is today.
- **The Game tab follows gameplay**: entering Game (Play / `tab:game`) snapshots
  the active scene context exactly as today. While the Game tab is active, a
  gameplay `LoadScreen` (the menu's Level 1 button) keeps the session + tabs
  alive — the new screen boots with `RunMode = Play`, the overlay rebinds, the
  **Game tab remains active**, and NO scene restore happens (gameplay owns the
  world). Scene tabs are untouched. Exiting Game (click a scene tab, ×, or Pause?
  — exit stays click/×; Pause pauses in place as today): land Paused → activate
  the target scene tab → cross-screen activation if its screen differs. The Game
  context still never persists a snapshot of its own (discard semantics verbatim).
  Restart inside Game: unchanged (current screen's recorded load).
- **Prefab tabs ride the session too** — they were world-self-contained already;
  with the session they survive screen switches for free.

## 3. The header (two rows)

Row 1 — **the tab strip, full width** (many tabs, no tool collision; the ×/dirty
affordances as today). Row 2 — tools: left cluster = Move/Rotate/Scale/Boundary/
Snap · Overlays · Entity ▾; **far right** = camera-view · **Play/Pause · Restart ·
Save** (the user's ask: transport joins Save on the right). Header height doubles
(~2×32pt rows); the ONE viewport inset follows; DPR tests updated. Both hosts
(Examples.Desktop, Demos) wire the session — "fix them all".

## 4. Buttons become editable citizens (wave TB-B)

Findings to verify then fix (free rein granted on Examples/Demos code):
- **Unclickable**: menu buttons are `SimpleButtonComponent` mesh + `DynamicText`
  entities with NO `SpriteInfoComponent` — the selection's candidate sources are
  sprites, collider shapes, and the rig, so buttons never candidate. Fix: button
  visuals (UI-target quads) join selection as a candidate source (virtual-space
  hit-test like UI sprites), OR the rework below makes the button root carry a
  pickable surface — pick the cleaner and document.
- **Gizmo-vs-G divergence** (suspected ENGINE bug — verify first): the modal
  transform's live apply may write `TransformComponent` without the
  changed-notification (`NotifyChanged`) the layout system's changed-filter
  listens to, while the gizmo path fires it — so `AutoLayoutSystem` repositions
  the label under a gizmo drag but not under G. If confirmed: fix at the modal
  path (parity with the gizmo), regression-test with a changed-filter consumer.
- **The rework**: rebuild the menu buttons as proper hierarchies — a button ROOT
  entity (Transform, the pickable surface, the button behavior) with the mesh +
  label as `ChildOf` children — so select/move/G/S operate on the root and
  children follow through the ordinary hierarchy (the collider-entity model's
  sibling). Layout (AutoLayout) composes: verify moving a laid-out button in Edit
  either sticks (layout respects manual placement) or is honestly refused —
  decide with the layout premises and document. Demos' menus get the same shape.

## 5. Waves

| Wave | Scope | Depends on |
|---|---|---|
| **TB-A** | `EditorSession` + session-scoped stack/tabs, named scene tabs, per-scene open/activate, cross-screen activation + pending-activation restore, Game-follows-gameplay, two-row header + transport-right, both hosts | — |
| **TB-B** | button pickability + the modal NotifyChanged parity fix + menu-button hierarchy rework (Examples + Demos) | TB-A landed (header/selection churn) |

Gate per wave: full-solution Release, zero skips.

## 6. Pre-mortem

1. **The session must not leak worlds** — contexts hold `SceneData` snapshots
   (data), never live `World`/`Entity` refs across screens; a context restored on
   a different screen instance must rebuild cleanly through the reader.
2. **Pending activation vs the screen's own Load** — the Game screen publishes its
   recorded `LoadLevelRequest` in `Load`; a pending restore must not double-load
   (restore REPLACES the fresh load's content or preempts it — one deterministic
   order, tested).
3. **RunMode across gameplay transitions** — the new screen must keep Playing when
   the Game tab rides a transition, and land Paused when a scene tab is activated
   cross-screen; the transport stays the ONE RunMode owner.
4. **Dirty state per tab** — each scene context keeps its own save-point; closing
   gates on ITS dirty, not the live world's.
5. **Demos hosts** — a second `ScreenController` + session; the wiring must be
   symmetric or the Demos tabs silently regress ("this also happens in all Demos").
   **CLOSED (wave TD).** TB-A gave the Demos host a session but left it with a NULL project
   context and NO screen-scene bindings, so its Scenes panel read "(unresolved) … (no scenes)",
   its boot tab read "untitled", and there were no bound scenes to activate cross-screen. TD makes
   the Demos host resolve an `EditorProjectContext` (with the `MonoDreams.Demos` multi-manifest
   disambiguation hint), commits `MonoDreams.Demos/Content/game.mdproj` + its MGCB `/copy:`, seeds
   the session with the launcher's scene, and binds every demo screen to a scene id (launcher /
   camera-demo / physics-demo / dialogue-demo / ui-demo) via `DemoEditor.BindScene`. Cross-screen tab
   activation between demo scenes now works (`DemosSessionCrossScreenTests`). TD also fixes a latent
   bug the whole model shared — the **Game-tab-exit blank screen** on a code-built screen (report 2):
   the `Reload` seam SPLITS into `RebuildCodeContent` + `ReloadSceneContent`, and the tab-exit sweep
   now runs `RebuildCodeContent` between the sweep and the snapshot restore (`ViewportContextStack`),
   so a menu / demo launcher / physics demo comes back instead of a blue void.
6. **Modal NotifyChanged** — if confirmed, the fix is engine-level (foundation/
   editor command path); every changed-filter consumer benefits — do not patch it
   menu-locally.
