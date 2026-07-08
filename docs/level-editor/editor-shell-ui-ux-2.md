# Editor shell UI/UX phase 2 (UX2) — panel modularity, modes, camera rig

> Follow-up to [`editor-shell-ui-ux.md`](editor-shell-ui-ux.md) (UX0/UX-A..D, landed
> `fe4ac63..4988173`). User-confirmed decisions (2026-07-08): **left tab strip +
> dedicated right Inspector** (Unity-style arrangement); **Game-mode sandbox edits are
> DISCARDED on exit** (Unity's snapshot model). UX-E (toolbar de-crowding into the
> Scene tab) is **superseded** by this phase's panel-local bars + context menus.
>
> The eight asks, mapped: (1) layout → §1; (2) renames → §1; (3) Scene/Game mode →
> §5; (4) icons + tooltips → §3; (5) context menus + Entity menu → §4; (6) panel-local
> top bars → §2; (7) camera as entity + view split → §6; (8) placement centering +
> the scale bug → §7.

## 1. Layout: left tabs, right Inspector, renames (wave UX2-B)

```
┌─────────────────────────────────────────────────────────────┐
│ window top bar: Save · Undo · Redo · Refresh   (thin, global)│
├──────────┬───────────────────────────────────┬──────────────┤
│ Entities │ Scene panel header: [Scene|Game]  │  Inspector   │
│ Systems  │  ▶⏸ ↺ · tools · Entity ▾ · [cam] │  (dedicated) │
│ Scenes   ├───────────────────────────────────┤  selected    │
│  [tabs]  │                                   │  entity's    │
│          │        game viewport              │  components  │
│          │                                   │  + members   │
├──────────┴───────────────────────────────────┴──────────────┤
│ Assets ─ card grid                                          │
└─────────────────────────────────────────────────────────────┘
```

- The **left region activates** (UX-B reserved it at width 0): it hosts the tab group,
  renamed — **Entities** (was "Scene": the entity tree; the Inspector LEAVES this tab),
  **Systems** (unchanged), **Scenes** (was "Project": the scene catalog + project
  info). The **right region becomes the dedicated Inspector panel** (no tabs; the
  selection-bound component list + members, exactly the current Inspector section).
- Shell state gains `LeftWidthPt` (same clamp/splitter treatment as right/bottom; the
  left splitter sits on its viewport-facing edge). `ViewportManager`'s 4-tuple inset
  already supports left ≥ 0 — compositing and mouse mapping follow for free.
- Ops rename with the tabs: `panel:tab <entities|systems|scenes>` (left region),
  the Inspector needs no tab op. This is a clean break (ops are test plumbing, not
  user API); update tests + premises, note the rename in the premise text.
- Enum/type renames follow the user-facing names (`EditorPanelSection.Scene` →
  `Entities`, `Project` → `Scenes`, `EditorRightTab` → a region-agnostic
  `EditorPanelTab`, etc.) — code reads like the UI speaks.

## 2. Panel-local top bars (wave UX2-B framework, populated by later waves)

Blender's philosophy, adopted: **every panel owns its header** — its tools, tabs,
menus, and its own right-click surface. Concretely:

- The **Scene panel** (the center viewport region — its header is chrome INSIDE that
  region; the game viewport insets below it): hosts, by end of phase, the
  **[Scene | Game] mode toggle** (§5), the **transport** (Play/Pause single toggle +
  Restart — relocated from the window top bar), the **tool icon cluster**
  (move/rotate/scale/boundary/snap, §3), the **Entity menu** (§4), and the
  **camera-view button** (right corner, §6).
- The **left panel / bottom shelf**: their tab strips ARE their headers (already true
  after UX-B); they gain panel context menus (§4).
- The **window top bar** slims to global actions: Save, Undo/Redo, RefreshCatalog
  (future home of a File menu). Transport moves out (it is a Scene-panel concern).
- UX2-B ships the header FRAMEWORK (a header band per region in the layout model +
  the Scene-panel header rect carved out of the viewport inset) with the transport
  relocated as plain buttons; UX2-C swaps buttons to icons; §4/§5/§6 add their
  controls into it. Pre-mortem: the Scene-panel header changes the inset → the
  DPR-2 layout tests must cover it; every consumer keeps re-reading layout per frame.

## 3. Icons + tooltips (wave UX2-C)

**Decision — procedural mesh icons, Lucide as the visual reference.** Rationale:
Lucide's SVGs/TTF would need a content-pipeline step (the user's `Content.mgcb` is a
guarded file, and the level-editor module is source-distributed — shipping binary
atlases with it complicates `monodreams add`), while the editor already has a
font-independent, DPR-crisp, theme-colored screen-baked mesh path (disclosure arrows,
gizmos). So:

- `EditorIcons` — a pure geometry library: each icon = line/triangle primitive lists
  in a unit box (Lucide's shapes as reference: move = cross-arrows, rotate =
  circular arrow, scale = corner-arrows box, snap = grid/magnet, boundary = polygon,
  play ▶ / pause ⏸ / restart ↺, save, undo/redo curved arrows simplified to angular
  polylines, camera = the frustum glyph, plus/dots for menus). Rendered through the
  existing mesh `DrawComponent` pattern; fills/strokes from `EditorTheme` roles
  (icon = `Text1`, hovered/active = `Text0`/`Accent`).
- **Tooltips**: hovering any icon button ~0.45s shows a one-line label (`Bg2` box +
  `Border` outline + `Text0` label) near the cursor on the Editor target — ONE pooled
  tooltip entity set, parked when idle; instant hide on move-off. Tooltip text = the
  action name the old text button carried (discoverability preserved).
- Applies to: Scene-panel header tools + transport, window top bar (Save/Undo/Redo/
  Refresh). Text stays where text is content (tabs, menus, rows, dialog actions).

## 4. Context menus + the Entity menu (wave UX2-D — supersedes UX-E)

- **`EditorContextMenu` primitive**: a popup list on the Editor target — items,
  separators, one-level submenus ("Order ▸") — opened at the cursor (or anchored to a
  header button), closed by item click / click-away / Escape. While open it consumes
  the pointer like the dialog does (same weave-early guarantee); it is lighter than
  the dialog (no keyboard field, no backdrop). Items render with the UX-A state model;
  destructive items in `Danger`.
- **Viewport (Scene panel) right-click** — in `SelectTransform` mode only (the
  existing tool-modality premise keeps right-click-as-disarm when a tool is armed):
  picks the entity under the cursor (same selection rules) if none selected, then
  opens the entity menu: **Order ▸ Bring Forward / Send Backward** (the existing
  order actions), **Delete** (`Danger`; the existing snapshotting delete command).
  Right-click on empty viewport: no menu (click-empty semantics unchanged).
- **The fixed "Entity" menu** — a dropdown button in the Scene panel header exposing
  the SAME items (menu = the discoverable twin of the context menu; both dispatch the
  same actions).
- **Entities panel right-click** → **Add Empty Entity**: an undoable
  `CreateEntityCommand` producing `TransformComponent` (at the current view centre) +
  `EntityInfoComponent("Empty")` + `SceneObjectComponent` — it appears in the tree,
  selectable, inspectable, serializes as an empty-ish root.
- **Scenes panel right-click** → **Create Empty Scene**: a small modal (the dialog
  machinery: name field prefilled `untitled`, Create/Cancel) that writes a minimal
  valid `.mdscene` (empty `entities[]`, default camera/layers, canonical bytes) into
  `LevelsPath`, then switches to it through the NORMAL dirty-gated switch flow. This
  is the deferred "new scene UI", landing where the user asked for it.
- The seven selection-context toolbar actions: **Order** moves into these menus (and
  OFF the toolbar); the collider/vertex buttons stay on the toolbar this phase (their
  natural home is a future Inspector "add component" surface — noted, not built).
- Ops: `menu:open <viewport|entities|scenes|entity-menu>`, `menu:pick <item-path>`
  (e.g. `order/forward`), headless-testable like everything else.

## 5. Scene mode / Game mode (wave UX2-F — depends on §6)

The Scene panel header carries the **[Scene | Game] toggle** (two-segment control,
active segment `Accent`-underlined, mirroring the tab visuals).

- **Scene mode** (default): you edit the actual scene through the free editor view
  (§6). Everything as today: dirty tracking, Save enabled (Paused), Scenes-panel
  switching.
- **Game mode**: the viewport looks through the **game camera** and you may poke
  entities while Paused "just to test" — a sandbox. Semantics (user-confirmed —
  Unity's model):
  - **Enter** (toggle, or automatically when **Play** is hit in Scene mode): snapshot
    the scene in memory — `SceneWriter.BuildScene(world, cameraRig, layers)` → a held
    `SceneData` (no file) — plus the current `EditorHistory` save-point/dirty state
    and the Scene-mode view transform. Then the shared `Camera` adopts the game
    camera state (§6).
  - **While in Game mode**: Play/Pause freely (the transport is unchanged — Play is
    ONE toggle that relabels, as today). Paused edits use the normal tools/history.
    **Save is blocked in Game mode** (a third `SaveBlockReason.GameMode` — sandbox
    changes are expressly not-to-be-saved), and the dirty `●` reflects the SNAPSHOT's
    state, not sandbox churn.
  - **Exit to Scene mode** (toggle): land Paused, dispose scene entities and restore
    the snapshot through the SAME reader path a `LoadSceneRequest` uses (an in-memory
    `SceneData` overload — so re-tag, texture rehydration, `DrawComponent` restore,
    and camera-rig re-materialization are all shared, not re-implemented), clear
    `EditorHistory` (restored entities invalidate old commands — same rule as
    Restart), restore the save-point/dirty state and the saved Scene view. Sandbox
    edits vanish; **Scene mode always shows exactly what Save would write**.
  - **Restart in Game mode**: unchanged semantics (disk reload, lands Paused) and
    additionally lands in **Scene mode** with the snapshot dropped — Restart's
    premise already declares "discards unsaved edits"; the snapshot is exactly an
    unsaved edit. One rule, no special case.
  - **Scenes-panel switch while in Game mode**: exits Game mode first (snapshot
    restore), then the normal dirty gate runs. No second gate flavor.
- `EditorTransport` owns the mode (alongside `RunMode` — it is transport state);
  ops: `mode:scene` / `mode:game`.

## 6. The camera rig: view/camera split + camera-as-entity (wave UX2-E)

Today the editor **drives the one `Camera` directly** in Edit (`CameraNavSystem`),
and Save captures that live camera into `scene.camera` — so panning the editor
LITERALLY moves the game camera. The user's ask ("camera visible when you're not in
camera view", Blender's bounds + X glyph, a back-to-camera-view button) requires the
split:

- **The persisted form stays `scene.camera`** (no scene-format change). The editor
  **materializes a camera entity** from it on load/bind — the rig: a standalone
  editor-materialized entity carrying a `TransformComponent` (+ the zoom/virtual-size
  camera state in a small `CameraRigComponent`), selectable and gizmo-movable like a
  proxy (border pick on its frustum rect; move tool v1 — zoom editing via inspector
  later). It is NOT `SceneObjectComponent`-tagged (never enters `entities[]`);
  `SceneWriter` reads `scene.camera` FROM the rig at save; `SceneReaderSystem`
  re-materializes it on every load/restore. It is not deletable (delete = loud
  no-op). It survives nothing it shouldn't: Restart/reload rebuild it from the file.
- **The shared `Camera` object becomes "whatever the viewport looks through"** — no
  rendering/culling/cursor plumbing changes at all:
  - **Scene mode**: `CameraNavSystem` keeps driving the shared `Camera` as the free
    VIEW (pan/zoom/frame — unchanged code). The game camera's authored state lives on
    the rig. When `Camera` state ≠ rig state (epsilon), the overlay draws the rig's
    **frustum bounds + the X of crossing corner lines** (Blender-style) through the
    existing overlay-projection path; when they match, the glyph hides (you ARE the
    camera).
  - **Back-to-camera-view button** (Scene panel header, right corner — the Blender
    nav-corner affordance): `Camera := rig state`. Op: `view:camera`.
  - **Game mode / Play**: `Camera := rig state` on entry; `CameraFollowSystem`
    (unfrozen in Play) takes over from there, exactly as the game would.
  - Load-time auto-framing frames the VIEW only (the rig holds the authored state).
- CameraNav premise update: "in Edit the editor drives the camera" becomes "the
  editor drives the **view**; the **rig** owns the authored game-camera state; Save
  serializes the rig". `CameraFollowTargetComponent` semantics untouched.

## 7. Placement centering + the scale bug (wave UX2-A — first, independent)

- **Scale bug (diagnosed)**: `MasterRenderSystem.DrawElement`'s Sprite case computes
  the draw scale as `Size / SourceRectangle` **whenever a source rect exists**,
  discarding `element.Scale` — which `SpritePrepSystem` sets from
  `transform.WorldScale`. Placed props always have a source rect ⇒ gizmo scaling
  grows the selection outline/colliders (transform math) but never the sprite. Fix:
  compose — `scale = (Size / source) * element.Scale` — after verifying no call site
  pre-bakes `WorldScale` into `Size` (audit `SpritePrepSystem` + `SpritePropFactory` +
  the reader's `DrawComponent` restore; add the audit result to the rendering
  premise). Regression test: a world-scaled sprite's drawn quad matches its hit-test
  quad (`GizmoTransform.SpriteWorldQuad`).
- **Placement centering**: the palette ghost and the placed stamp must land with the
  sprite's **visual centre at the cursor**. The feet-origin convention (Y-sort) is
  untouched — `Origin` stays feet; only the placement POSITION offsets by the
  centre↔origin delta. Ghost preview and committed stamp must agree exactly (one
  shared position function; test both).

## 8. Wave plan

| Wave | Scope | Depends on |
|---|---|---|
| **UX2-A** | scale-composition fix + placement centering (+ tests, rendering premise touch) | — |
| **UX2-B** | left region + tab move + renames (Entities/Scenes) + dedicated right Inspector + panel-header framework + transport relocation (text buttons) + `LeftWidthPt`/splitter + ops renames | — |
| **UX2-C** | `EditorIcons` mesh library + icon buttons + tooltips (header tools + window bar) | UX2-B |
| **UX2-D** | `EditorContextMenu` + viewport/Entities/Scenes menus + Entity header menu + Add Empty Entity + Create Empty Scene + Order relocation | UX2-B (uses UX2-C icons if landed) |
| **UX2-E** | camera rig: materialized camera entity + view/camera split + frustum glyph + back-to-camera-view | UX2-B |
| **UX2-F** | [Scene|Game] toggle + snapshot/restore + Play-auto-Game + `SaveBlockReason.GameMode` + Restart/switch composition | UX2-E |

Verify gate per wave: `dotnet build MonoDreams/MonoDreams.csproj && dotnet test
--configuration Release` (full solution).

## 9. Pre-mortem

1. **Scale-fix double-apply** — if any path already bakes `WorldScale` into `Size`,
   composing multiplies twice. The audit in §7 is mandatory before the one-line fix.
2. **Snapshot restore must reuse the reader** — a second restore implementation
   forgets re-tag or rehydration and produces the blank-screen / empty-save class of
   bug. The in-memory `SceneData` overload is the only new surface.
3. **History across mode toggles** — restoring a snapshot with a live undo stack
   dangles commands against disposed entities (the Restart rule). Clear + restore the
   save-point marker; test undo-after-exit is a no-op.
4. **The rig is not scene membership** — tagging it `SceneObjectComponent` would
   serialize a camera entity into `entities[]` and break the format. The writer reads
   it explicitly; the membership tests must assert it never appears.
5. **Right-click double-duty** — armed tools keep right-click-as-disarm; only
   `SelectTransform` opens menus. A stray menu during boundary-lay would eat the
   cancel gesture.
6. **Header inset** — the Scene-panel header shrinks the game viewport; mouse mapping
   and `OutsideViewport` must follow via the ONE inset source (never a second rect).
7. **Mode/transport interleave** — Play in Scene mode must snapshot BEFORE
   `RunMode=Play` (a frame of simulation before the snapshot corrupts the restore
   point). Order is load-bearing; test it.
8. **Icon legibility at DPR 1** — mesh icons must stay readable at ~16pt logical;
   keep shapes ≤3 strokes (Lucide's simplicity is the reference, not its detail).
