# Prefab workflow (PF phase) — assets → prefabs → entities, viewport tabs, a real Inspector

> The user's brief (2026-07-09): prefabs are classes ("from which we'll instantiate
> objects") built from assets + colliders + components; creatable via code AND via
> config files; designed in a dedicated viewport tab over an empty world; the
> Scene/Game **toggle becomes tabs** (Play spawns a "Game" tab; default = just
> "Scene"); the Inspector becomes editable (values / add / delete components) with
> **Chrome DevTools** as the UI north star — "this is a code-first game engine, take
> this seriously." Main goal: build NPCs, dialogue zones, the Player as prefabs.
>
> **User-confirmed decision:** instances are **LINKED with whole-component
> overrides** — the scene stores a compact reference; prefab edits propagate; an
> instance can replace whole components locally (per-field overrides and nested
> prefabs are named terrain, not v1).

## 1. The prefab model (wave PF-C)

- **`.mdprefab`** = the `SceneData` schema reused verbatim (same `CanonicalJson`,
  same component-serializer registry, same stable ids) with prefab rules: exactly
  ONE root (validation: every other entity parent-chains to it — save refuses
  otherwise), the root's Transform position is **normalized to origin** on save,
  `camera` is absent/ignored. Lives in `Content/Prefabs/<id>.mdprefab`, bundled by
  the same MGCB `/copy:` mechanism as levels (source-first in the editor, shipped
  via `TitleContainer`).
- **The scene instance entry**: an `entities[]` entry gains a `"prefab": "<id>"`
  field; its `components{}` map holds ONLY `core.Transform` (always
  instance-owned) plus **overridden components** (whole-component replacement,
  same keys as the prefab root). Instance children are NEVER serialized in the
  scene — they come from the prefab (the writer excludes them from the membership
  closure; a premise + test guards this).
- **Override detection is diff-based, not bookkept**: at save, the writer
  serializes the instance root's components and omits any whose canonical bytes
  EQUAL the prefab root's — byte-different ⇒ override, byte-equal ⇒ inherited.
  Canonical serialization makes equality reliable (pre-mortem #1); no
  per-edit override flags to maintain or desync.
- **Runtime marker**: `PrefabInstanceComponent { PrefabId }` on the instance root
  (structurally captured like `SceneEntityIdComponent` — the `prefab` field, never
  in `components{}`); children get nothing prefab-specific (they're ordinary
  entities parented to the root).
- **Expansion (load)**: the scene reader resolves `prefab` entries — loads the
  `.mdprefab` (cached per load pass), instantiates root + children through the
  normal deserialize path, applies the instance's `components{}` over the root,
  stamps the marker + scene id. A missing/cyclic prefab **fails loud** (the
  unknown-component policy's sibling); self/circular references are refused at
  save AND capped at expansion (pre-mortem #7).
- **The code path**: a generic `PrefabFactory : IEntityFactory` registered with
  `EntitySpawnSystem` — game code spawns any prefab via the existing
  `EntitySpawnRequest("prefab:<id>", …)` channel (the "of course via code" half,
  unified with the one factory authority).
- **Propagation**: instances refresh from the prefab (a) on every scene load
  (expansion IS propagation) and (b) **live on prefab-save** — every instance of
  that prefab in the open world re-expands (children rebuilt, root overrides
  re-applied). v1 limitation (pre-mortem #2): a live propagation that rebuilt
  entities **clears the undo history** (the Restart rule — stale commands would
  dangle), with a status-bar note; no instances in the open world ⇒ history
  untouched.
- **Instance-children guardrails**: children of an instance are selectable but
  not editable in the scene (gizmo/inspector edits refused with the status hint
  "open the prefab, or Unpack") — Godot's editable-children is terrain. **Unpack
  Prefab** (entity context menu, `Danger`): dissolves the link into ordinary
  entities (undoable) — the escape hatch.

## 2. Viewport tabs (wave PF-B)

The center region's mode TOGGLE is retired; a **tab strip** takes its place:

```
[ Scene ] [ ▶ Game × ] [ npc-boldo × ] [ house × ]        …Scene-header tools…
```

- **One mechanism**: `ViewportContextStack` — the UX2-F Game-mode
  snapshot/restore machinery **generalized** (pre-mortem #4: ONE implementation;
  the Game tab consumes it too). Each background tab holds an in-memory context:
  a `SceneData` snapshot + view camera + history save-point/dirty + scene id.
  Switching tabs = snapshot the active context → sweep → reader-restore the
  target (the in-memory `LoadSceneRequest` path — shared retag/rehydrate/
  DrawComponent/rig logic).
- **Scene tab**: always present, never closable — the scene being edited (the
  Scenes panel switches WHICH scene it shows; scene switching from another tab
  first activates the Scene tab).
- **Game tab**: spawned by **Play** (from the Scene tab), auto-switches +
  auto-plays — the existing sandbox semantics wholesale: leaving the tab (or ×)
  **discards** the sandbox and restores the Scene context; it never persists a
  context of its own. Restart inside it: unchanged. Play while a Game tab exists
  just activates it.
- **Prefab tabs**: one per open prefab — an empty world + the prefab's content
  loaded from its file, auto-framed, neutral backdrop. Its own dirty state + save
  target. **No camera rig** in prefab contexts (pre-mortem #8 — the rig, its
  glyph, and the "Camera" tree row are scene-context-only). Play is disabled in a
  prefab tab v1 (status hint; prefab playgrounds are terrain). × close is
  dirty-gated (the Save & Close / Discard / Cancel confirm — the switch-gate
  machinery).
- **Context-aware chrome**: the Save dialog in a prefab tab offers **Save
  Prefab** (writes the `.mdprefab`, triggers propagation); the Entities tree /
  Inspector / tools operate on whatever the active context's world holds (they
  already just read the world — the swap does the work). The status bar's right
  side shows the active tab's id + dirty.

## 3. The Inspector, DevTools-grade (wave PF-A)

Chrome DevTools as the design north star — element pane semantics over ECS:

- **Filter box** at the top (the DevTools search): filters component rows AND
  member rows as you type (`EditorTextField`; Esc clears; the keyboard focus
  composes with `ShouldSuppressInput`).
- **Value editing**: click a member value → inline edit field in place; `Enter`
  commits through an undoable `MemberEditCommand` (reflection read → parse →
  write-back — components are structs: get-modify-`Set()`, pre-mortem #5);
  `Escape` cancels. v1 editable types: `float`, `int`, `bool` (click toggles —
  no field), `string`, enums (value cycles or a small menu), `Vector2` (a
  `"x, y"` field). Everything else stays read-only muted (DevTools grey).
  Edits coalesce sensibly (one commit = one undo step) and dirty-track.
- **Type-colored values** (DevTools syntax coloring via the existing intent
  roles, documented mapping): numbers `Info`, strings `Warning`-warm, `true`
  `Success` / `false` `Danger`, enums `Accent`, null/default `TextMuted`.
- **Add component**: a trailing `+ Add component` row → a filterable popup (the
  context-menu machinery + a filter field — the DevTools command-palette idiom):
  the candidate list is **the serializer registry's registered types** (engine +
  game — the honest "what can this scene persist" set) minus already-present;
  selection adds a default-constructed component through an undoable
  `AddComponentCommand`. A per-type default-initializer table covers components
  whose zero value is unusable (e.g. a collider gets the standard footprint).
- **Delete component**: per-row affordance (`Danger`) → undoable
  `RemoveComponentCommand`. Guardrails: `TransformComponent` is not removable;
  removing `SpriteInfoComponent` also removes the transient `DrawComponent`
  (the pairing premise, pre-mortem #6); structural components
  (`SceneEntityId`, `PrefabInstance`, `ChildOf`) never appear as rows to delete.
- Works identically in scene and prefab contexts — this IS the prefab assembly
  surface ("plus a collider here, plus these Components").

## 4. Building prefabs (wave PF-D)

- **Create Prefab from Selection…** (entity context menu + Entity header menu):
  name modal → captures the selection's subtree closure into
  `Content/Prefabs/<id>.mdprefab` (root position normalized) → **replaces the
  selection with a linked instance** (one undoable composite). The primary
  workflow: assemble in the scene, extract.
- **Create Empty Prefab…** (Prefabs shelf context menu): name modal → opens an
  empty prefab tab to assemble from scratch (palette placement + boundary +
  colliders + Add Component all work there).
- **The Prefabs shelf tab**: the bottom shelf gains a **Prefabs** tab (next to
  Assets) listing `Content/Prefabs/*.mdprefab` as cards (generic prefab glyph
  v1; rendered thumbnails are terrain). Click arms placement — placing stamps a
  **linked instance** at the cursor (undoable); context menu per card: **Edit
  Prefab** (opens its tab), **Delete** (`Danger`, file delete, confirm).
  Double-click = Edit.
- **Editing flow**: open tab → edit exactly like a scene (same tools) → Save
  Prefab → instances in the open scene re-expand live (§1 propagation).

## 5. The goal walkthrough (wave PF-E — the acceptance test)

An ops-driven integration test (plus the docs/premises sweep) that builds the
user's actual targets end-to-end: an **NPC prefab** (sprite + Passive collider +
`DialogueZoneComponent`), a **dialogue-zone prefab** (trigger collider + zone
component), and a **Player prefab** (sprite + RigidBody + `PlayerState` +
`CameraFollowTarget`) — created from selection / from empty, placed as
instances, overridden per-instance (a different yarn node on one NPC), prefab
re-edited with propagation verified, scene saved/reloaded byte-stable, then
booted and played. What the walkthrough surfaces gets fixed in-wave.

## 6. Wave plan

| Wave | Scope | Depends on |
|---|---|---|
| **PF-A** | DevTools Inspector: filter, value editing, add/delete component, type colors, guardrails | — |
| **PF-B** | viewport tab strip + `ViewportContextStack` (Game mode → Game tab; toggle retired) | — |
| **PF-C** | `.mdprefab` format + reader expansion + diff-based writer compaction + `PrefabInstanceComponent` + `PrefabFactory` + bundling + fail-loud/cycle rules (core, test-first — no UI) | — |
| **PF-D** | Prefabs shelf tab + placement + Create-from-Selection + Create-Empty + prefab editing tabs + Save Prefab + live propagation + Unpack + instance-children guardrails | PF-A, PF-B, PF-C |
| **PF-E** | the NPC/dialogue/Player walkthrough + fixes + docs/premises/roadmap sweep | PF-D |

Verify gate per wave: `dotnet build MonoDreams/MonoDreams.csproj && dotnet test
--configuration Release` (full solution).

## 7. Pre-mortem

1. **Diff-based overrides need canonical equality** — a serializer emitting
   nondeterministic bytes turns inherited components into phantom overrides.
   The canonical-bytes suite already guards determinism; add the equality test
   at the prefab layer.
2. **Propagation vs undo** — rebuilding instance children under a live history
   dangles commands; v1 clears history on propagation-with-instances (status
   note), tested. Smarter merging is terrain.
3. **Writer closure** — instance children serialized into the scene = silent
   bloat + double-expansion on load. Membership tests assert exclusion.
4. **One context stack** — the Game tab MUST ride `ViewportContextStack`, not
   keep a parallel snapshot path; two implementations will drift (the UX2-F
   code becomes the stack's first consumer).
5. **Struct write-back** — reflection edits on components must get-modify-`Set()`
   or they silently vanish; the `MemberEditCommand` tests cover a struct field
   round-trip.
6. **Component pairing** — add/remove must respect `SpriteInfo ⇒ DrawComponent`
   and the Transform-required rule, or the blank-sprite class of bug returns.
7. **Prefab cycles** — `a.mdprefab` containing an instance of `a` (directly or
   transitively) must be refused at save and capped at load (fail loud).
8. **The rig is scene-only** — a camera rig materializing in a prefab tab would
   serialize a camera into the prefab (format violation) and confuse the tree.
9. **Tab-switch data safety** — every switch path goes through the dirty-gated
   confirm; the × on a dirty prefab tab must never silently discard.
