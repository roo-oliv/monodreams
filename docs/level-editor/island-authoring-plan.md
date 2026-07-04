# Island authoring plan — from placeholder packs to a walkable island

> **Status: PROPOSAL for the gamedev's review, 2026-07-04.** This document re-anchors
> the editor's next work on **the actual game**: a cozy, playful top-down
> investigation game (Wytchwood / Don't Starve / Cult of the Lamb visual family) —
> free-roam island, NPCs, photographing evidence, a detective board. The immediate
> blocker it solves: *assembling a first real level* — placing big sprites
> (buildings), dressing ground that "feels real, fully texturized," laying a road,
> and authoring colliders/boundaries/triggers.
>
> It **supersedes the delivery order** of
> [`waves-b-f-design-review.md`](waves-b-f-design-review.md) (the wave repass): that
> doc's architecture remains the reference design (its §S1 tool modality, §S2/S3
> source/bake seams, and its Wave C placement design are used here nearly verbatim),
> but its ground-canvas (E) and spline-road (F) systems move from "next waves" to
> **named upgrade paths** — because the chosen art construction makes them
> unnecessary for v1 (§4).
>
> Decisions already made by the gamedev (banked, not re-litigated): ground =
> **overlapping painted patches** (not continuous blended painting); collision needs
> **all three** of freeform boundaries, prop footprints, trigger zones; delivery
> bias = **balanced** (real tools for repeated actions, one-off manual steps
> acceptable for rare ones); assets = **itch.io placeholder packs now, the gamedev's
> own art later** (the gamedev is the artist — future art can conform to the
> pipeline's specs).

---

## 0. Open decisions for you (ranked)

Everything else in this doc proceeds on recommendations. These change what gets
built — please rule on them:

1. **Asset intake mechanism (§2).** Recommended: **runtime file loading** — the
   editor scans an asset folder and loads PNGs directly (`Texture2D.FromStream`),
   no MGCB content-build round-trip; scenes reference them via a `file:` AssetKey.
   Alternative: MGCB wildcard include (uniform with the shipped content pipeline,
   but every art experiment costs a content rebuild). Runtime loading is the
   fastest possible iteration loop for "try placeholder packs", which is exactly
   your current phase. Accept the recommendation?
2. **Where placeholder packs live.** The repo is public and most itch packs
   **do not permit redistribution** — committing them is a license problem.
   Recommended: an untracked, gitignored `Content/Island/` folder + a committed
   `MANIFEST.md` listing pack names/URLs so a fresh checkout knows what to
   download. A scene file referencing missing assets loads with loud warnings +
   placeholder magenta boxes (not silently invisible). OK?
3. **Trigger identity editing, v1 (§5.3).** Triggers (evidence spots, talk zones)
   need a string identity the game reads. Recommended v1: the trigger palette
   offers **game-defined trigger types** (EvidenceSpot / TalkZone / Exit …) and
   auto-numbers instances (`evidence_01`); you rename later by editing the saved
   scene JSON (one-off manual step, per your balanced bias). The alternative — an
   in-editor text-input widget — is the first keyboard-capturing chrome widget
   (real work, and the seed of a future inspector panel). Accept palette-types +
   JSON-rename for v1?
4. **Buildings, v1 (§3.4).** Recommended: one sprite per building + a footprint
   collider (player walks *behind* the building via y-sort). The
   walk-behind-AND-under effect (player passes under an awning: base/roof split
   into two sprites on different layers) is deferred until a real building needs
   it. OK?
5. **Multi-stamp in v1 (§4.2)?** Single-click placement ships first. A hold-drag
   "stamp repeatedly with spacing" mode (the embryo of the scatter brush) makes
   dressing big areas with grass tufts much faster — include it in Slice 4, or
   defer entirely until the island proves it's needed?
6. **"Walkable island" acceptance (§8).** Proposed milestone: *island dressed with
   ≥2 buildings + ground patches + a road; coastline blocks the player; building
   footprints block; ≥1 evidence trigger and ≥1 NPC talk zone fire in Play mode;
   scene saves, reloads, and survives Restart.* Confirm or amend — this is the
   contract Slices 1–3 are judged against.

---

## 1. Pain → plan map

| Your pain | The plan | Where |
|---|---|---|
| "I only know how to work with single sprites" | Palette + click-to-place any asset from your packs — no code per prop | §2, §3 |
| Big sprites (buildings) | Same placement path + feet-origin y-sort convention + footprint collider | §3.4, §5.1 |
| "Create the ground… feels real, fully texturized" | Patch construction: base fill + overlapping soft-edged ground pieces, layered | §4 |
| A road | Road patches/segments placed like ground (spline tool = named upgrade) | §4.3 |
| "Define colliders, shapes, boundaries and positioning" | Footprint editing on entities + freeform boundary polyline + trigger zones | §5 |

The through-line: **your look (patches) turns four of the five pains into the same
tool** — placement — which the editor already half-has (create/undo/save exist;
what's missing is the palette, the asset intake, and layer ergonomics). Collision
authoring is the one genuinely new tool family.

---

## 2. Asset intake — from an itch.io zip to a placeable prop

### 2.1 The recommended pipeline (runtime file loading)

- **Drop folder:** `Content/Island/` (gitignored; see open decision 2), organized
  freely (`ground/`, `props/`, `buildings/`, `roads/`). The editor scans it at
  startup into an **asset catalog** (one entry per PNG; plus sliced regions, §2.2).
- **Loading:** `Texture2D.FromStream` over `TitleContainer.OpenStream` — the same
  content-stream seam the Blender parser and scene reader already use (verified:
  `BlenderLevelParserSystem`/`SceneReaderSystem` read JSON this way). No MGCB, no
  rebuild: drop a file, restart the editor (a refresh button is a later nicety),
  it's in the palette.
- **AssetKey scheme:** placed entities serialize `AssetKey = "file:Island/props/tree01.png"`
  (+ optional `#region` suffix for sliced sheets). `SceneReaderSystem`'s
  rehydration gains a `file:` branch beside the existing `content:` path. A missing
  file at load = loud warning + a visible magenta placeholder box, never an
  invisible entity (extends the existing fail-loud stance).
- **Shipping path:** when art finalizes, assets graduate into MGCB content and the
  AssetKey flips from `file:` to a content key — a mechanical, greppable migration
  recorded as a premise now so it isn't a surprise later. (Web note: TitleContainer
  streams work on KNI for bundled files; the `file:` path is desktop-editor-first
  and the graduation step is what makes a scene web-ready.)

*Why not MGCB-glob:* uniform with shipping, but every placeholder experiment costs
a content build; your current phase is exactly "experiment with packs." The
balanced bias picks the fast loop now + a defined graduation step.

### 2.2 Sprite sheets

itch packs often ship sheets. V1: a **sidecar JSON** next to the PNG
(`tree_sheet.png.slices.json`: named regions) → each region becomes its own
catalog/palette entry. Hand-written or AI-written (it's greppable data); a slicing
UI is a later tool. Packs that ship individual PNGs need nothing.

### 2.3 Asset spec brief — for the art YOU will draw

Because you're the artist, the pipeline can dictate specs and stay simple. When
you start replacing placeholders:

- **Ground patches:** large irregular pieces (~2–4 per material: grass, sand,
  dirt), **soft/rough alpha edges** (the overlap magic), sized ~256–512 px at your
  world scale, PNG with transparency.
- **Roads:** a few segment pieces — straight, gentle curve, end/fork blobs — same
  soft-edge treatment so they sit *on* the ground patches.
- **Props/buildings:** transparent PNGs with the **visual base at the bottom edge**
  (the feet-origin convention, §3.3, is what makes y-sort automatic).
- **Consistency:** pick one pixels-per-world-unit density and keep it (the virtual
  resolution is 800×600; a player sprite ~48–64 px tall implies the rest).

---

## 3. Palette & placement — the core loop

This adopts the wave repass's **Wave C design** (see
[`waves-b-f-design-review.md` §Wave C](waves-b-f-design-review.md)) with one
game-shaped addition (§3.2). Summary of what's reused verbatim: palette chrome in
the shell's reserved bottom strip; click-to-arm → ghost preview under the cursor →
click to place → **Escape/right-click disarms**; placement wraps the existing
`CreateEntityCommand` (one undo step, sub-graph snapshots, auto-tagged
`SceneObjectComponent`, round-trips through the Wave-A writer); snap rides the
existing `GizmoStateComponent.SnapEnabled/GridStep`; placed entity lands
**auto-selected** with the move gizmo active; repeated clicks keep placing until
disarmed. Tool modality follows the repass's §S1 (`EditorToolMode` on the shared
state entity; brushes/placement visibly deactivate the transform gizmo — the
Unity/Godot convention).

### 3.2 The game-shaped addition: a generic `SpritePropFactory`

The repass assumed palette items map to *game factories* (Player, Wall, NPC…).
Your island is mostly **art, not gameplay prototypes** — hundreds of props that
should be placeable with zero code. So the `level-editor` module ships one generic
factory: `SpritePropFactory(assetCatalogEntry, layerBand, origin)` → builds the
standard renderable stack (`EntityInfoComponent`, `TransformComponent`,
`SpriteInfoComponent` with the `file:` AssetKey + SOURCE sort fields,
`DrawComponent`, `SceneObjectComponent`). Every catalog entry is instantly
placeable; game factories (NPCs, interactables) join the same palette later via
the screen-supplied palette model (repass C2 — the editor module stays
game-agnostic).

*Uniquely ECS:* the authoring path and the runtime path are the same factory call;
a "prop" is not an editor concept, it's the standard component stack. *Parity:*
this is Unity's drag-a-sprite-into-the-scene / Godot's instance-a-scene gesture,
minus the prefab ceremony (which is deliberately deferred — see §9).

### 3.3 Layers & the feet-origin convention

The palette carries a **layer band selector** mapping to the existing
`GameDrawLayer` bands (verified): **Ground** → `Background`, **Ground detail**
(roads, scatter) → `Tiles`, **Props/actors** (y-sorted) → `Characters`,
**Overhead** → `Foreground`. Two conventions get codified as premises:

- *Y-sorted props use feet-origin:* `SpriteInfoComponent.Origin` at the sprite's
  bottom-center and `YSortOffset = 0` — the entity sorts by where it *stands*
  (player walks behind the tree when above it). The `SpritePropFactory` sets this
  automatically for the y-sorted band.
- *Ground bands are non-y-sorted:* patches never re-sort against actors; their
  within-band order is authored (§4.2).

### 3.4 Buildings

V1 (open decision 4): a building = one placed sprite on the y-sorted band with
feet-origin + a **footprint box collider** across its base (§5.1). The player
walks behind it (y-sort does the work) and is blocked by the base (the collider
does the rest). Multi-part buildings (separate roof layer for walk-under, door
triggers) compose from the same pieces later — placement + colliders + triggers
are all per-entity, so "a building" needs no new concept.

---

## 4. Ground & roads — the patch construction

### 4.1 Why patches (and what it buys us)

You chose the overlapping-patches look — which is also **how the reference games
are actually built**: a base fill, big irregular soft-edged ground pieces layered
over it, detail props scattered on top. Technical consequence: **ground = sprite
placement at ground bands.** No render targets, no shaders, no new render tech —
the repass's ground-canvas (its Wave E) is *not needed for this game's v1* and is
recorded as the upgrade path if you ever want continuous blending. Every patch is
an ordinary entity in `entities[]`: diffable saves, ordinary undo/selection/gizmo,
culled by the existing culler.

**Recipe** (what you'll actually do in the editor): clear-color or one huge water
sprite → island base patch(es) (sand) → grass patches overlapping the sand,
rotated/flipped for variety → dirt patches where paths wear through → road
segments (§4.3) → detail props (tufts, stones, flowers) on the detail band.

### 4.2 Within-band ordering

Overlap order inside a ground band must be authorable (grass over sand, dirt over
grass). Two toolbar actions on the selection — **Bring forward / Send back** —
adjust the SOURCE `SpriteInfo.LayerDepth` by a small step within the band, as an
undoable command. *Parity:* Unity's "Order in Layer" / painting-app layer nudges.
(Cheap; ships in Slice 2.)

### 4.3 Roads = patches too

V1 roads are **road patches placed like ground pieces** (segments + curves from
the packs; or blob patches for worn paths). This matches the painterly look and
needs nothing new. The **spline road tool** (repass Wave F: control-point proxies,
Catmull-Rom, bake-along-spline) is the named upgrade, triggered when you find
yourself hand-placing long winding roads repeatedly — note that without the ground
canvas, a spline bake would target **stamp entities** along the spline (the
repass's D+B machinery) rather than canvas pixels; the repass's F design carries
over with that one substitution.

---

## 5. Collision authoring — footprints, boundaries, triggers

All three confirmed as needed. All three build on the **existing proxy mechanism**
(Wave 8b): proxies are standalone handle entities bound to
`(entity, component, index)`, dragged through the same gizmo/undo path. Verified
seams: `BoxColliderComponent.Bounds` is **Transform-relative**
(`CollisionRect.FromBounds(bounds, transform.Position)`), `ConvexColliderComponent`
holds local-space `ModelVertices`, the collision system supports `Passive`
colliders, and `ZoneDialogueTriggerSystem` already demonstrates the trigger-zone
pattern in the Examples game.

### 5.1 Prop/building footprints (per-entity)

- **Add collider** action on the selection (toolbar): creates a
  `BoxColliderComponent` with a **footprint default** — full sprite width × the
  bottom ~25% of the sprite, anchored at the feet (the top-down convention; you
  then adjust). **Remove collider** likewise (both undoable commands).
- **Resize handles** on the existing box proxy (today it's move-only): edge/corner
  handles adjust `Bounds` size, through the same one-drag-one-undo path.
- **Polygon footprints** (irregular bases): `ConvexColliderComponent` with
  **per-vertex proxies** — this is the `(kind, index)` proxy generalization the
  repass's Wave F slice 1 specifies; we pull it forward (it serves colliders now
  and spline points later). Add/delete vertex via the selected proxy + Delete key.

### 5.2 Freeform world boundaries (coastline, cliffs)

Collision that belongs to the world, not to a sprite:

- A **Boundary tool** (toolbar mode): click to lay polyline vertices along the
  coast; Enter/double-click commits, Escape cancels (one undo step). Creates an
  authoring entity with a `BoundaryComponent { Points[], Thickness }` — ordinary
  serialized scene data, editable later via the same per-vertex proxies.
- **Bake, not per-frame:** a `BoundaryBakeSystem` reacts to the component being
  added/edited (message-driven, the repass's §S2 bake shape) and generates the
  collision: one **thin convex quad segment per polyline edge**, as `ChildOf`
  children of the boundary entity. This sidesteps concavity entirely (a coastline
  is deeply concave; the engine's SAT is convex-only — a segment chain is the
  standard, robust answer). Bake products are marked and **never serialized** (the
  polyline is the durable truth; children regenerate on load) — the repass's
  "bake products never scene-serialize" invariant, first applied here.
- The boundary renders as an editor overlay line (native-res, existing overlay
  path) and is invisible in Play.

*Uniquely ECS:* the boundary is a component + a bake system; nothing in the
collision module changes at all — it just sees ordinary convex colliders.
*Parity:* Godot's CollisionPolygon2D / Unity's EdgeCollider2D authoring feel.

### 5.3 Trigger zones (evidence spots, talk radius, exits)

- Same shapes, `Passive = true` (no physical block — verified the collision module
  supports passive colliders), placed via a **trigger palette** (same placement
  loop; visible as tinted outlines in Edit, invisible in Play).
- **Identity rides `EntityInfoComponent`** — the engine's existing string identity
  (`new EntityInfoComponent("evidence:tape_recorder")`), already serialized, and
  exactly what game systems pattern-match on (the existing
  `ZoneDialogueTriggerSystem` is the in-repo precedent for "zone + identity →
  game reaction"). No new component; the photo-evidence system you'll write
  subscribes to collision messages with these entities and reads the string.
- V1 identity editing per open decision 3 (palette types + auto-numbering).

---

## 6. Reuse vs build (the honest inventory)

**Reused as-is** (zero new work): create/delete/transform undo commands +
transaction coalescing; `SceneObjectComponent` + writer/reader round-trip;
selection + gizmo + snap; camera nav; transport (Play to test walking, Restart to
reload); systems panel (every new tool appears in it, toggleable); headless
editor-op channel (every new tool is scriptable + testable); shell chrome
infrastructure (the palette fills the reserved bottom strip); the S1 tool-modality
design from the repass.

**New, in dependency order:** asset catalog + `file:` AssetKey loading/rehydration
(§2) → `SpritePropFactory` + palette chrome + ghost/place + layer selector (§3) →
ordering affordance (§4.2) → proxy `(kind,index)` generalization + box resize
handles + add/remove collider actions (§5.1) → boundary tool + bake system (§5.2)
→ trigger palette + identity convention (§5.3). Deliberately **not** built: ground
canvas, spline tool, scatter brush, prefabs, inspector panel (§9).

---

## 7. Engine-parity map (familiar outside, ECS inside)

| A Unity/Godot/Unreal user expects | This plan's answer |
|---|---|
| Project panel → drag sprite into scene | Asset catalog → palette → ghost → click-place |
| Prefabs for repeated props | Deferred: `SpritePropFactory` + palette covers "place many of the same art"; true prefab semantics (edit-propagates) is a named later decision |
| Sorting layers + Order in Layer | `GameDrawLayer` bands + bring-forward/send-back within band |
| Pivot/feet conventions for top-down | Feet-origin convention, factory-applied, codified as a premise |
| BoxCollider2D edit handles | Proxy resize handles on `Bounds` (Transform-relative, verified) |
| PolygonCollider2D / CollisionPolygon2D | Convex vertex proxies; concave world boundaries via the segment-chain bake |
| Trigger colliders ("Is Trigger") | `Passive` colliders + `EntityInfoComponent` identity |
| Tilemap/terrain painting | Deliberately none — the game is non-tile by design; patches are the construction |
| Play/pause/restart to test | Already shipped (transport model) |

---

## 8. Delivery slices (dependency-ordered; each verifiable)

Per the repo's conventions: every slice lands with named tests; premises updated in
the same commit; headless-drivable where it touches interaction.

1. **See your assets, place your assets.** Asset catalog + `file:` AssetKey
   (loading, serialization, fail-loud missing-asset placeholder) + sidecar slicing
   + `SpritePropFactory` + palette chrome (text rows v1) + ghost/arm/place/disarm +
   layer selector + feet-origin convention. *Tests:* catalog scan; `file:` AssetKey
   round-trip incl. missing-file behavior; placement creates the standard stack
   with correct SOURCE sort fields per band; ghost lifecycle; headless op-plan
   place. **Outcome: you can dress the island visually, save it, reload it.**
2. **Order and shape.** Bring-forward/send-back command + proxy `(kind,index)`
   generalization + box **resize** handles + **add/remove collider** actions with
   footprint defaults. *Tests:* ordering undoable + persists; existing ProxyTests
   green after generalization; resize writes `Bounds` through one-drag-one-undo;
   footprint default geometry. **Outcome: buildings/props have correct footprints.**
3. **Walk the island.** Boundary tool (polyline lay/commit/cancel + vertex
   proxies) + `BoundaryBakeSystem` (segment-chain convex children; bake products
   never serialize; re-bake on load/edit) + trigger palette (`Passive` +
   `EntityInfoComponent` types, auto-numbered) + Play-mode verification. *Tests:*
   boundary bake geometry (pure math) + round-trip (polyline persists, children
   regenerate); a Play-mode walk blocked by coast + footprint (headless replay,
   log-asserted); trigger fires with the right identity string. **Outcome: the
   "walkable island" milestone (open decision 6).**
4. **Comfort (optional, cheap-first order).** Multi-stamp placement (open
   decision 5); palette thumbnails; road-segment placement conveniences; a
   refresh-catalog button; boundary thickness handle. Each independently
   droppable.

---

## 9. Deferred, with explicit triggers to revisit

| Deferred | Revisit when | Reference design |
|---|---|---|
| Ground canvas painting | You want continuous material blending patches can't give | Repass Wave E (canvas, chunked RTs, Reach-safe) |
| Spline road tool | Hand-placing long winding roads becomes a repeated pain | Repass Wave F, bake target = stamp entities (no canvas) |
| Scatter brush | Dressing large areas one click at a time is the bottleneck | Repass Waves B+D (multi-stamp in Slice 4 is its embryo) |
| Prefab semantics (edit-propagates) | You edit the same building's collider for the 5th time | Blindspot list item 2; needs stable GUIDs first |
| Inspector panel (free-text fields) | Trigger renaming via JSON gets old | First keyboard-capturing chrome widget |
| Authoring-vs-runtime save guard | First time you save mid-Play and bake weird state | Blindspot list item 1 — cheap guard: block Save while Playing |

One addition worth pulling forward from the blindspot list into Slice 1, because
it's nearly free: **block Save while the transport is Playing** (a one-line guard
+ a dimmed button) — it closes the "baked a mid-air player into the scene" trap
before it ever bites.

---

## See also

- [`waves-b-f-design-review.md`](waves-b-f-design-review.md) — the architecture
  this plan draws from (S1–S3, Wave C) and the upgrade-path designs (E, F).
- [`roadmap.md`](roadmap.md) — substrate history (Waves A + 6–8b + transport +
  HiDPI).
- [`MonoDreams/level-editor/docs/scene-format.md`](../../MonoDreams/level-editor/docs/scene-format.md)
  — `entities[]`/`sources[]`; this plan keeps everything in `entities[]` except
  boundary bake products (regenerated, never saved).
