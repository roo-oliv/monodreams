# Editor shell UI/UX + project model — the UX phase (UX0 design)

> **North star.** The editor's *structure* converges on Blender: a windowed shell of
> regions/panes that scroll, tab, resize, and (eventually) rearrange, so any new tool
> has a home. The editor's *visual identity* converges on interfaces like Claude Code:
> a strict dark palette, color used for **intent** (action / success / warning /
> danger), clear actionable controls, and minimal-but-intentional animation. We will
> not have Blender from day 1 — this doc marks the terrain: what ships now, and what
> the architecture must already permit.
>
> The second half re-founds the **project model**: game screens declare which scene
> file they load from; the editor lists them in a Scenes panel; selecting one loads it
> (there is **no Load action**); Save becomes a three-action dialog (Save Scene / Save
> Project / Save Backup As) and the file-system navigator is **removed**.
>
> Substrate maps this design is built on: the chrome-internals map and the
> screens/persistence map (2026-07-08, in-session). Read together with
> [`MonoDreams/level-editor/docs/premises.md`](../../MonoDreams/level-editor/docs/premises.md)
> and [`roadmap.md`](roadmap.md).

## 0. Constraints that shape everything (from the maps)

- **No scissor/clipping in the render stack.** `MasterRenderSystem` is `CullNone`
  everywhere; `GraphicsDevice.Viewport` is deliberately unset; the only CPU clipper
  (`OverlayMeshClip`) clips meshes, not text/sprites. ⇒ scrollable panels stay
  **row-granular** (park whole rows), as `SystemsPanelLayout` already documents.
  Pixel-clipped scrolling is a named future `rendering` change (scissor-enabled
  `RasterizerState` inside `MasterRenderSystem`'s batches), not a level-editor patch.
- **One Editor target, one depth stack, device pixels.** All chrome is meshes/text/
  sprites on `RenderTargetID.Editor` at native resolution; every metric goes through
  `Px(points, DevicePixelRatio)`; chrome entities carry `EditorInfrastructureComponent`
  and **no** `VisibleComponent`; **mesh fills must be opaque** (premultiplied-alpha
  rule) — "translucent" styling must be precomputed opaque blends.
- **Modal consume is ordering-dependent.** `editor.dialog` is woven before every other
  mouse consumer and clears cursor edges+levels; edges survive because
  `CursorInputSystem` derives them from its own previous hardware state. Any new
  interactive chrome must be woven after the dialog.
- **A screen switch is a full world teardown** (`ScreenController.Update` disposes the
  outgoing screen, no hooks); `GameState` (and `RunMode=Edit`) is the only survivor.
- **Nothing tags code-spawned entities as scene-owned** — only loaded/placed/editor-
  created entities carry `SceneObjectComponent`. Saving on a code-built screen today
  would write an **empty** scene file.

## 1. EditorTheme — the strict palette (wave UX-A)

One static class `MonoDreams/level-editor/UI/EditorTheme.cs` becomes the **single
source of every color and depth** in the level-editor module (chrome *and* viewport
overlays). The de-facto palette scattered across `EditorChromeBuilder`,
`EditorDialogSystem`, `PalettePlacementSystem`, and `EditorPanelSystem.RowColor`
migrates into it. Layout *metrics* stay in the layout classes (geometry ≠ style).

### 1.1 Roles (v1 values — warm dark, Claude-coral accent)

| Role | Value (RGB) | Used for |
|---|---|---|
| `Bg0` | 20,19,18 | dialog backdrop, tab-strip background, deepest chrome |
| `Bg1` | 30,29,27 | panel bodies (right strip, bottom shelf, top bar, dialog panel) |
| `Bg2` | 45,43,40 | raised controls at rest: buttons, cards, field |
| `Bg3` | 58,55,51 | hovered controls / hovered rows |
| `Bg4` | 70,66,60 | pressed controls |
| `BgDisabled` | 36,35,33 | disabled control fill |
| `Border` | 62,58,53 | panel edges, splitter idle, scrollbar track |
| `BorderStrong` | 96,90,82 | control outlines, splitter hover/drag, scrollbar thumb |
| `Text0` | 240,238,230 | primary labels (ivory) |
| `Text1` | 178,172,162 | secondary labels, subtitles, headers |
| `TextMuted` | 122,117,108 | placeholders, de-emphasized values |
| `TextDisabled` | 100,96,90 | disabled labels |
| `Accent` | 217,119,87 | **selection + primary action** (Claude coral): selected rows/cards edge+border, active-tab underline, primary dialog action |
| `AccentSoft` | 66,45,39 | selected-row/card fill (precomputed opaque blend of Accent into Bg1) |
| `Success` | 107,166,113 | **on/enabled** semantics: checkbox-on, Play affordance |
| `Warning` | 224,164,88 | destructive-adjacent notes ("discards unsaved edits"), dirty marker |
| `Danger` | 229,72,77 | destructive actions (Discard & Switch, Remove collider) |
| `Info` | 108,169,216 | informational status text |
| `GhostTint` | White × 0.55 | placement ghost sprite tint (sprite alpha is fine; the opaque rule is for mesh fills) |
| `OverlayAccent` | current gizmo cyan | viewport overlays (proxies/outlines) — migrated, unchanged visually |

Depth constants (`PanelDepth 0.1 … LabelDepth 0.6`, dialog band `0.70–0.86`) move
into `EditorTheme.Depths` so the one Editor-target depth stack is declared in one
place. Semantic rule: **Accent = "this is selected / the primary thing to do"; Success
= "this is on"; Danger = "this destroys something"** — never decorative.

### 1.2 Interaction states — every interactive widget

`Idle(Bg2) → Hover(Bg3, ~120ms fade) → Pressed(Bg4, instant) → Selected(AccentSoft
fill + Accent edge) → Disabled(BgDisabled + TextDisabled, no hover)`.

- **Hover fades on buttons** (toolbar, dialog actions, tabs, band buttons, cards):
  per-widget progress advanced with the engine's standard framerate-independent ease
  (`Lerp(current, target, clamp(speed·dt))`, speed ≈ 18 — the `ButtonVisualSystem`
  recipe; chrome systems all have `GameState.Time`). Toolbar buttons store progress on
  `ToolbarButtonComponent`; card/tab widgets store it on their own components.
- **Rows highlight instantly** (right-strip rows, dialog list rows): pooled row
  visuals are repurposed per frame (animation state would smear across scroll), and
  instant row highlight is what Blender does anyway.
- **Selected** rows/cards get `AccentSoft` fill + a 3pt `Accent` left edge bar (rows)
  or `Accent` border (cards/tabs). The palette's "armed" card moves from green to
  Accent (armed = selection semantics, not on/off semantics).
- Existing intentional animations stay: dialog caret blink, hover fade is the one new
  motion. Nothing else animates in v1.

### 1.3 Palette lint (regression guard)

A source-scan test (the `SceneLint`/ship-lint pattern): no `new Color(` literal in
`MonoDreams/level-editor/**/*.cs` outside `EditorTheme.cs`. Every color is a theme
role; adding a color means adding a role, consciously.

## 2. Shell structure — regions, tabs, scroll (wave UX-B)

### 2.1 Day-1 shell vs marked terrain

```
┌───────────────────────────────────────────────┬──────────┐
│ Top bar: transport │ tools │ undo/redo │ save │  (44pt)  │
├───────────────────────────────────┬─┬─────────┴──────────┤
│                                   │s│ Tabs: Scene│Systems│Project
│                                   │p│ ┌────────────────┐ │
│         game viewport             │l│ │ active tab body│ │
│      (inset, real pipeline)       │i│ │  sections +    │ │
│                                   │t│ │  rows + scroll ▐ │
│                                   │ │ └────────────────┘ │
├─┬─────────────────────────────────┴─┴────────────────────┤
│s│ Assets ── card grid ─────────────────────────▐ (bottom)│
└─┴────────────────────────────────────────────────────────┘
```

**Ships in UX-B:**
- **`EditorShellStateComponent`** (pure data, editor-infra entity): `RightWidthPt`,
  `BottomHeightPt` (runtime-adjustable, clamped), active tab per region, splitter drag
  state. `EditorChromeLayout` methods take this state instead of constants — the
  region rects and the `ViewportInset` derive from ONE model, so compositing, mouse
  mapping, and every chrome system keep agreeing (the existing invariant).
- **Splitters**: 4pt-wide drag zones on the viewport-facing edges of the right strip
  and bottom shelf; dragging resizes the region (device-px → pt), `EditorShellSystem`
  re-applies the inset (it already relayouts on change). Rendered as a `Border` line,
  `BorderStrong` while hovered/dragging.
- **Right-strip tabs**: **Scene | Systems | Project**. Scene = entity tree + inspector
  (today's sections, still collapsible inside the tab). Systems = the pipeline panel.
  Project = NEW (§3): the Scenes list + project info. Tab bar = per-tab buttons; the
  active tab merges into the body (`Bg1` fill + `Accent` underline). The bottom shelf
  gets the same tab strip with a single **Assets** tab (marks the terrain; future:
  more shelves).
- **Scrollbar affordance**: scrollable panel bodies (right strip, asset shelf) draw a
  slim track+thumb (proportional, `Border`/`BorderStrong`) when content overflows;
  wheel scrolls as today (row-granular); the thumb is draggable (same drag-tracker
  helper as splitters). No pixel clipping — rows still park whole.
- **Toolbar de-crowding**: the top bar keeps transport │ Move/Rotate/Scale/Boundary/
  Snap │ Undo/Redo │ Save │ Refresh. The seven selection-context actions (order
  forward/back, collider add-box/add-convex/remove, vertex add/delete) move into the
  **Scene tab** as small action buttons under the Inspector section — contextual UI
  where Blender would put it, and the top bar stops overflowing narrow windows.
  *(Droppable to a follow-up if the wave runs long; the top bar must then note the
  overflow risk.)*

**Marked terrain (architecture must permit, day-1 does not build):**
- Panels as **data** (`EditorPanelKind` + region→panels assignment in the shell
  state) so future drag-rearrangement/docking is a state mutation, not a rearchitect.
- A left region and a menu-bar region (both reserved in the layout model at size 0).
- Pixel-clipped scrolling via a scissor `RasterizerState` in `MasterRenderSystem`
  (a `rendering` framework change — named here, out of scope).
- Pointer feedback on splitters (resize cursor) — needs chrome cursor-swap plumbing.

### 2.2 Ops

`panel:tab <scene|systems|project|assets>`, `shell:right <pt>`, `shell:bottom <pt>`
join the grammar (headless tests drive tabs + resize). Existing `panel:*` ops keep
working against whichever tab hosts their section (they activate it).

## 3. The project model — screens declare scenes (wave UX-C)

### 3.1 Screen↔scene binding (code is the source of truth)

The user's rule: *"we create game screens on code and need a clear way to indicate
from which configuration files they load from."* So the binding is declared at
**screen registration** (foundation seam):

```csharp
// foundation — ScreenController
public sealed record ScreenInfo(string DisplayName, string? BoundSceneId, bool HostsSceneFiles);
RegisterScreen(name, creator, ScreenInfo info);       // new overload; old one = info with nulls
IReadOnlyList<(string Name, ScreenInfo Info)> RegisteredScreens { get; }  // NEW enumeration
```

Examples wiring:
- `ScreenName.Game` → `("Game", null, HostsSceneFiles: true)` — the level-parameterized
  host: it loads whatever scene is requested.
- `ScreenName.LevelSelection` → `("Level Selection", "level_selection", false)`.
- `ScreenName.InfiniteRunner` → `("Infinite Runner", "infinite_runner", false)`.

A screen with a `BoundSceneId` publishes an **optional scene load** in `Load` (a small
helper: probe source-first, then `TitleContainer`; file absent ⇒ silently skip — the
menu keeps its code UI either way) and passes its scene id to the overlay explicitly.
This makes the menu screen genuinely editable: place props → Save Scene → they load
under the code UI on every boot. Code-spawned UI stays **code-owned** (untagged, never
serialized); scene-owned entities are exactly the loaded/placed/tagged set — the
existing membership policy, now doing ownership work.

This also kills the current bug-in-waiting where every screen's overlay falls back to
`manifest.startScene` and **all three screens would save to `island.mdscene`**: each
overlay now gets an explicit id (the Game screen sets it from the requested level in
`Load`; the others from their declared binding).

### 3.2 The Scenes panel (Project tab)

A pure `SceneCatalog` (injected scene lister, like the deleted browser's — the module
never reads the filesystem) merges:
1. every registered screen with a `BoundSceneId` → one entry (label = DisplayName);
2. every `.mdscene` under `LevelsPath` not claimed by (1) → one entry hosted by the
   `HostsSceneFiles` screen (label = scene id) — dangling **backups appear here**, so
   "open a scene not tied to a screen" falls out for free;
3. unresolved project context ⇒ screens only (fail-safe, matching the Save guard).

The current entry (active screen + current scene id) renders selected (`AccentSoft` +
`Accent` bar) with a `Warning`-colored `●` dirty marker when unsaved. Ops:
`scenes:select <label>`.

### 3.3 Switching = selecting (there is no Load)

Clicking an entry (or `scenes:select`):
- same entry → no-op;
- **dirty** → a modal confirm (the dialog machinery, new mode): *"Unsaved changes in
  \<sceneId\>"* → **[Save & Switch] [Discard & Switch (Danger)] [Cancel]**;
- clean (or confirmed) → the overlay invokes a host-supplied `SwitchScene(entry)`
  callback; Examples implements it as the existing hand-off (set the requested level,
  `ScreenController.LoadScreen(entry.ScreenName)`). The world tears down wholesale;
  `RunMode=Edit` survives on the shared `GameState`; the new screen composes a fresh
  overlay bound to the right scene id. The editor module gains **no** dependency on
  game screen types — the callback is the seam, exactly like `Transport.Reload`.

The toolbar **Load button and the Load dialog are deleted** (UX-D removes the code).

### 3.4 Dirty tracking (the missing signal)

`EditorHistory` gains a monotonic `EditVersion` (incremented on push / undo / redo /
transaction-commit / clear) and a `MarkSavePoint()`; `IsDirty = EditVersion !=
savePointVersion`. Every world mutation in Edit flows through the history (gizmo,
palette, boundary, collider, delete commands), so the signal is complete. Save Scene /
Save Project mark the save point; Restart's `Clear()` resets it. Known conservative
edge: undoing back to the save point still reads dirty — acceptable v1, documented.

### 3.5 The empty-save guard (the footgun from the map)

`SaveCurrentSceneTo` **refuses** (loud warning + status line) when the world has zero
`SceneObjectComponent` roots AND the target file exists with entities AND no scene was
actually loaded into this world this session (the reader sets a `SceneWasLoaded` flag
on the overlay). A designer who deliberately emptied a loaded scene can still save it
empty; a mis-bound code-built screen can never wipe a real level with a blank file.

## 4. Save semantics (wave UX-D)

The toolbar Save button (and `dialog:save-open`) opens the **Save dialog** — a modal
with three stacked full-width actions (title + `Text1` subtitle each — the
Claude-Code-style "clear actions" list), replacing the navigator:

1. **Save Scene** *(primary, Accent)* — `<sceneId>.mdscene` — the existing guarded
   write (source tree + zero-touch bundling + ship-lint warning), now also
   `MarkSavePoint()`.
2. **Save Project** — "every unsaved scene + project files (currently: \<sceneId\>)" —
   v1 saves the current scene (the only one in memory) through the same path; it is
   the terrain for multi-scene sessions. It must **never** blanket-write scenes that
   are not in memory.
3. **Save Backup As…** — a name field (prefill `<sceneId>-backup`, `Sanitize`d) —
   writes `<name>.mdscene` to `LevelsPath` **without** rebinding the scene id, without
   marking the save point, and **without** bundling (a backup is dangling by design;
   logged "not bundled"), then **reloads the bound scene from disk** via the
   transport's existing Restart (teardown + screen-recorded reload + history clear) —
   the user-specified semantics: the edits went to the backup file; the working scene
   returns to its on-disk truth. Subtitle carries the `Warning`-colored "then reloads
   \<sceneId\> from disk (discards unsaved edits)".

Escape/Cancel closes. Enter = Save Scene (or confirm-backup while the name field is
active). The Save-blocked guard (Playing / no project root) is unchanged and dims the
toolbar button; the dialog actions re-apply it (defense-in-depth, as today).

### 4.1 Source-first reload (fixes a real latent bug)

Restart's reload re-publishes `LoadLevelRequest` → the native probe reads the
**bundled** copy via `TitleContainer` — which is stale the moment the editor saves to
the source tree (saves only reach the bundle at the next build). The probe
(`NativeLevelLoader`) gains the resolved `EditorProjectContext`: **when resolved and
the source file exists, it publishes `LoadSceneRequest(sourcePath, fromContent:false)`
instead**. Shipped builds (unresolved context) keep the `TitleContainer` path,
byte-identical. This makes Restart-after-Save honest and is what backup-reload
requires.

### 4.2 Deletions + grammar

- Deleted: `EditorFileBrowser` + `EditorFileBrowserTests`, the Load dialog mode, the
  toolbar `Load` action/button, ops `dialog:load-open|cd|up|pick|load`.
- New ops: `dialog:scene`, `dialog:project`, `dialog:backup <name>` (one-shot);
  `dialog:save-open|name|confirm|cancel` keep working (confirm = the focused/default
  action).

## 5. Premise + test delta (the docs-layer contract)

Rewritten: *"The editor's Save/Load dialog is a modal file-system navigator…"* → the
three-action Save dialog premise; *"The editor right strip is a stack of collapsible
sections"* → the tabbed shell premise (+ Scenes panel); toolbar premise (Load removed,
context actions relocated); Save-blocked premise (+ empty-save guard); Restart premise
(+ source-first reload note). New: screen↔scene binding (foundation + level-editor);
EditorTheme single-source + lint; shell state/splitters/tabs; dirty save-point.
level-loading: native-first premise gains the source-first-in-editor variant + the
"backups are not bundled" note. foundation: ScreenController enumeration + the
RunMode-survives-switch note. Tests: `EditorFileBrowserTests` deleted;
`EditorDialogTests`/`ToolbarTests` rewritten; new `EditorThemeLintTests`,
`EditorShellStateTests` (splitter/tab/scrollbar math), `SceneCatalogTests`,
`SceneBindingTests` (screen registration + optional load), `DirtyTrackingTests`,
`SaveDialogTests` (three actions, backup+reload, empty-save guard, source-first).

## 6. Wave plan

| Wave | Scope | Depends on |
|---|---|---|
| **UX-A** | `EditorTheme` (roles/depths), migrate every color site, interaction states + hover fades, palette lint test | — |
| **UX-B** | shell state + splitters + right-strip/bottom tabs + scrollbars + toolbar de-crowding (context actions → Scene tab; droppable) | UX-A |
| **UX-C** | `ScreenInfo` binding + registry enumeration (foundation), optional scene load, `SceneCatalog` + Project tab, switch flow + dirty gate + confirm mode, per-screen scene ids, empty-save guard, `EditorHistory` save-point | UX-B |
| **UX-D** | three-action Save dialog, backup-as + Restart reuse, source-first reload, navigator deletion, op grammar, premises/roadmap/ledger sweep | UX-C |

Verify gate per wave: `dotnet build MonoDreams/MonoDreams.csproj && dotnet test
--configuration Release` (full solution — the CLI tests only run there).

## 7. Pre-mortem (what kills this if ignored)

1. **A translucent-looking panel that is actually opaque-blended** — someone "fixes"
   `AccentSoft` to `Accent × alpha` and the premultiplied rule turns it near-white.
   The theme documents blends as precomputed opaque values.
2. **Splitter resize desyncs hit-tests** — a chrome system caches its region rect
   across frames while the splitter moves the layout. Every consumer already re-reads
   the layout per frame; the shell-state refactor must keep that property.
3. **The dialog-order invariant** — the confirm-on-switch mode and any new interactive
   region must be woven after `editor.dialog` or modality silently leaks clicks.
4. **Save-empty via mis-binding** — §3.5's guard is the defense; its test must cover
   the "bound screen, never-loaded scene, existing file" case explicitly.
5. **Backup reload resurrects stale state** — without §4.1, backup-reload reads the
   bundled copy and silently reverts to the *last build*, not the last save. The
   source-first probe test must assert the source bytes win.
6. **Pooled-row animation smear** — hover fades keyed by entity on pooled rows smear
   across scroll; the design confines fades to per-widget-component controls.
7. **Screen-switch data loss** — the dirty gate must intercept *every* switch path the
   panel offers; `LoadScreen` itself stays hookless (foundation stays lean), so the
   gate lives in the one place that initiates switches (the catalog click handler).
8. **DPR regressions** — every new metric (tab strip, splitter, scrollbar, action
   buttons) goes through `Px(points, scale)`; the DPR-2 layout tests must cover the
   new widgets.
