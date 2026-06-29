---
flow: level-blender
covers:
  - MonoDreams/level-blender/**
  - Tools/blender_level_export.py
sensitive: false
---

# Blender level load

A Blender scene becomes engine entities along one straight path: an artist designs a level in
Blender, the bundled add-on `Tools/blender_level_export.py` walks the scene and writes a JSON
document, that JSON ships as content (`<ContentRoot>/blender_level.json`), and at runtime
`BlenderLevelParserSystem` reads it on a `LoadLevelRequest` and builds entities. The JSON is the
seam — its field names and coordinate conventions are a contract the Python exporter and the C#
parser must agree on, because nothing else binds the two halves at compile time.

The trigger is the load-bearing contrast with LDtk. `BlenderLevelParserSystem` subscribes
**directly to the `LoadLevelRequest` message** (`_world.Subscribe<LoadLevelRequest>`), processing
it only when `request.LevelIdentifier.StartsWith("Blender_")`. The LDtk parsers are
**component-driven** — they react to `CurrentLevelComponent` being added by `LevelLoadRequestSystem`.
Both `LevelLoadRequestSystem` and this parser see the same message; the `Blender_` prefix is the
only signal that routes a load here (and makes the LDtk path fail-and-clean-up). This asymmetry is
an acknowledged wart, not a design — see the premises and CORE_TENETS §6.

Unlike LDtk, this parser does **not** emit `EntitySpawnRequest`s through the shared
`level-loading` spawn seam. It creates entities directly (`_world.CreateEntity()` + `entity.Set(…)`),
deriving sprite source rects from UV data and components from Blender collection membership. Game
code customizes results through `RegisterCollectionHandler(collectionName, handler)` rather than
`IEntityFactory`. (The module overview's "emits EntitySpawnRequests" wording is stale.)

## Entities & lifecycle

On a matching `LoadLevelRequest`, in one synchronous pass:

1. **Cleanup** — `CleanupEntities()` disposes every entity from the previous Blender load
   (`_blenderEntities`) and clears the name→entity map. There is no `EntitySpawnRequest`; the
   parser owns its entities' lifetime.
2. **Read** — `TitleContainer.OpenStream(<ContentRoot>/blender_level.json)` → `JsonSerializer`
   (case-insensitive) → `BlenderLevelData`. Hardcoded filename; the identifier selects nothing.
3. **Pre-scan** — build `nameToObj`, and collect any object whose `name` ends `-collider` into
   `colliderChildMap` keyed by parent name (these are skipped as standalone entities).
4. **Pass 1 — create** — per object by `type`: `MESH`/`GREASEPENCIL` → `ProcessMesh` (loads the
   texture, computes a source rect from UV bounds, sets `TransformComponent` + `SpriteInfoComponent`
   + `DrawComponent` + `VisibleComponent`, then `ProcessCollections`); `EMPTY` → `ProcessEmpty`
   (transform + `EntityInfoComponent` only); `CAMERA` → `ProcessCamera` (mutates the shared `Camera`,
   creates no entity). `ProcessCollections` adds `BoxColliderComponent` for the `Collision`
   collection, the physics+`CameraFollowTargetComponent` stack for a root `Player`, and tags for
   `Enemy`/`Trigger`; then fires registered collection handlers.
5. **Post-pass 1 — colliders** — `ApplyColliderChild` bakes each collider child's vertices (scaled
   by the parent's Blender scale) into a `ConvexColliderComponent` on the parent, **replacing** any
   `BoxColliderComponent` (inheriting its layer/passive), and sets the parent's `YSortOffset`.
6. **Pass 2 / post-pass 2 — hierarchy** — link `Transform` parents via `SetParent`, then propagate
   a non-zero parent `YSortOffset` onto sprite children.

## Invariants

Authoritative list in [`MonoDreams/level-blender/docs/premises.md`](../../MonoDreams/level-blender/docs/premises.md);
the ones this flow leans on (see also `level-loading` premises for the shared `Blender_` dispatch):

- The `Blender_` prefix is the parser's sole opt-in; without it the load falls to the LDtk path and fails.
- Message-driven, not component-driven — a test that sets `CurrentLevelComponent` triggers LDtk but **bypasses this parser**; Blender tests must publish `LoadLevelRequest`.
- The JSON schema is the exporter↔parser contract; a field renamed on one side deserializes to default on the other, silently.
- `Tools/blender_level_export.py` is in-module — schema changes must move both halves together.
- The level JSON is read as content via `TitleContainer`, never via `File`/`IPlatformServices` (else zero entities on web).
- A `-collider`-suffixed child mesh becomes a `ConvexColliderComponent` on its parent, not its own entity.

## Load-bearing quantities

- **Coordinate mapping** — exporter: Blender X → game X; Blender **Z → game Y, negated**
  (`y = -location.z`, Z-up → Y-down screen space); Blender Y is discarded. Get this wrong and every
  object lands mirrored vertically or on the wrong axis.
- `scaleFactor` — Blender-units-to-pixels multiplier, exporter default **16.0**. Multiplies position,
  dimensions, and collider vertices on export; the parser reads it back only to convert camera
  `ortho_scale` → zoom (`gameZoom = VirtualWidth / (ortho_scale * scaleFactor)`). Positions/sizes are
  already baked in pixels in the JSON.
- **Origin Y-flip** — `originOffset` is normalized (0,0 = Blender bottom-left, 0.5 = center). The
  parser flips Y when building the sprite origin: `origin = (srcW * offX, srcH * (1 - offY))`, in
  **source-texture** pixels (what `SpriteBatch.Draw` expects), and the same `1 - offY` flip aligns
  the collision box to the rendered sprite.
- **UV → source rect** — `CalculateSourceRect` takes min/max U,V over the layer and flips V
  (`y = (1 - maxV) * texHeight`) because UV origin is bottom-left, texture origin top-left.
- **Schema `version`** — exporter writes a hardcoded `"1.0"` (note: *not* `bl_info.version` `(1,8,1)`);
  `BlenderLevelData.Version` is deserialized but never checked. Drift is currently silent.

## Failure modes

- **Exporter/parser field drift** — a field renamed or added on one side only. `System.Text.Json`
  populates the missing property with its default and ignores the unknown one — no exception. The
  classic instance: `originOffset` renamed, every sprite silently centers at (0.5, 0.5). Highest-risk
  failure because the `version` field exists but is never enforced.
- **Wrong-axis / flipped placement** — a change to the export coordinate mapping (Z→Y negation) or to
  any of the parser's Y-flips (origin, UV-V, collider top edge) that isn't mirrored on the other side;
  objects render mirrored or offset, and collision boxes drift off the sprite.
- **Missing `Blender_` prefix** — a Blender level requested without the prefix is ignored here and
  fails down the LDtk path, which also logs an error and removes `CurrentLevelComponent`; that error
  can mask the real cause.
- **Component-driven test bypass** — a test that adds `CurrentLevelComponent` instead of publishing
  `LoadLevelRequest` exercises nothing here; assertions on Blender entities read as "no entities found."
- **`-collider` typo / non-convex collider** — a misspelled suffix spawns the mesh as a stray visible
  sprite and leaves the parent with its default box; the exporter also drops vertices for a non-convex
  mesh (SAT requires convex), so the collider silently vanishes.
- **Content-path regression** — reverting the JSON read to `File`/`IPlatformServices`, or shipping it
  as a processed `.xnb` instead of `/copy:`, makes the web build load the level with zero entities (no
  error). Texture paths additionally rely on a literal `/Content/` segment (`ExtractContentPath`).
