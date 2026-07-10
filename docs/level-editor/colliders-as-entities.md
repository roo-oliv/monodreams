# Colliders as entities (CE phase) — the RFC, resolved

> User directive (2026-07-10): "It's time to turn colliders into proper entities and
> no longer proxies. You're expected to update current examples and demos with this
> concept and no need for backwards compatibility." This resolves the roadmap's
> deferred engine RFC ("colliders as child entities with their own Transform") —
> and permanently fixes the class of bug PF-G patches (collider world-shape derived
> from LOCAL position — authored-in-prefab vs placed-in-scene divergence).

## 1. The model

- **A collider IS an entity**: `TransformComponent` (position/rotation/scale —
  relative to its parent body via the ordinary hierarchy) + a reshaped shape
  component + `ColliderTagComponent` (now tagging collider ENTITIES for the
  detection loop):
  - `BoxColliderComponent` → `{ Size, ActiveLayers, Passive }` — the embedded
    `Bounds` offset dies; position/rotation/scale come from the entity's Transform.
  - `ConvexColliderComponent` → `{ ModelVertices (collider-entity-local),
    ActiveLayers, Passive }` — `WorldVertices`/`BroadPhaseAABB` derive from the
    entity's `WorldMatrix` (which `HierarchySystem` already maintains, in both
    run modes — the derivation cost is a read, not new math).
- **Body resolution**: a collider's body = its nearest ancestor carrying
  `RigidBodyComponent` (else `VelocityComponent`), else itself. A collider entity
  MAY stand alone — a **trigger zone is now just a root collider entity** with an
  `EntityInfoComponent` (the trigger palette's output simplifies to exactly this).
  A body may have N collider children (the one-collider-of-each-type-per-entity
  premise is retired).
- **`CollisionMessage`** carries both granularities: `ColliderA/B` (the collider
  entities — dialogue zones read identity here) and `BodyA/B` (the resolved bodies
  — player/physics consumers read here). Resolution write-back (position/velocity
  corrections) applies to the BODY's Transform/Velocity.
- **Perf (the RFC's criterion)**: measured in-wave and recorded in the collision
  premise. Expectation: parity-or-better — today's code does local-position offset
  math per collider per frame; the new code reads an already-computed WorldMatrix.
  The RFC's cache-hop concern applies at scales this game does not approach; a
  coarse non-gating smoke test documents the frame cost at ~500 colliders.

## 2. Serialization + migration (NO backwards compatibility, by directive) — LANDED (CE-B)

- The shape components keep their registry keys (`core.BoxCollider`,
  `core.ConvexCollider`) with the NEW shapes; **`SceneData.Version` bumps to 2**.
  The reader REFUSES a version-1 file that contains collider components, loudly:
  "legacy embedded colliders — run `monodreams migrate-colliders`". (Version-1
  files WITHOUT colliders load fine and re-save as v2.)
- **The migrator is a CLI command** (`monodreams migrate-colliders <path|dir>`)
  — it lives where `CanonicalJson` does, so output is byte-canonical: for each
  entity with an embedded collider → strip the component, append a collider CHILD
  entity (Transform from the old offset/Bounds centre; vertices re-based to the
  new entity's local space), stamp version 2. Idempotent; refuses non-v1 input.
- **Committed content migrates in-repo** (sample, Blender_Level, the milestone
  fixtures) via the migrator — dogfooding it. The **user's untracked WIP**
  (island2/island3/untitled + their prefabs) is theirs: the editor fails loud
  with the migrator hint; I run the migrator on their files only on their say-so.
- Parsers (LDtk, Blender), `SpritePropFactory` footprints, `BoundaryBakeSystem`
  products (still never serialized), and trigger placement all PRODUCE collider
  child entities. The Blender parser's flattening unbends — the exporter's
  collider-objects now map 1:1 to entities (the RFC's authoring-model motivation).

> **Landed in CE-B (2026-07-10).** `SceneData.Version = 2` (default for scenes AND
> prefabs); `SceneVersionGuard.CheckFileLoad` fail-loud refuses a v1 file with any
> collider on a FILE read (in-memory snapshots are version-agnostic); the
> `monodreams migrate-colliders` command + the `ColliderMigration` core (byte-canonical,
> idempotent, `--dry-run`, dir recursion; box `bounds`→centered `size` on the collider
> entity, convex verbatim to a child, dialogue/trigger ZONES reshape in place so the
> zone identity stays on the collider — pre-mortem #4); committed `sample.mdscene` +
> `Blender_Level.mdscene` migrated in-repo. **Deviation:** the Blender parser's box-offset
> seam was deliberately NOT closed with new collider-child code — the parser is import-only
> and off the live boot path (PS5), and the committed `Blender_Level.mdscene` boots as a
> migrated NATIVE scene (not via the parser). LDtk factory colliders were already child
> entities from CE-A. **The Blender importer is RETIRED (user directive 2026-07-10, given
> to the CE-B wave directly and re-confirmed to the orchestrator: "we are getting rid of
> it", no compatibility owed).** The unclosed box-offset import seam is therefore moot;
> the full module deletion is **wave BR** (after CE-D): parser + data types, `Blender_`
> dispatch, the exporter plugin under `Tools/`, Examples import wiring, the CLI registry
> entry + module-count docs, and the Blender-shaped test fixtures.

## 3. The editor (proxies die for colliders) — LANDED (CE-C)

> **Landed in CE-C (2026-07-10).** `ProxySyncSystem`'s box/convex-shape bindings, `GizmoProxyComponent`'s
> `BoxColliderBounds`/`ConvexColliderShape` kinds, `ColliderEditCommand.ForBox`, `ColliderComponentCommand`,
> and `Proxy/BoxResize` are DELETED (no stubs). A collider is a first-class editor entity: **border-picked
> on its world shape** (`SelectionSystem` folds collider entities in as a spriteless candidate source at
> `ProxyBorderPickDepth`, the camera-rig precedent; `ProxyGeometry.TryGetColliderWorldShape` derives the
> shape from the entity's own WorldMatrix), moved/scaled by the ordinary gizmo + modal G/S/R
> (`TransformEditCommand`; Scale composes `Transform.Scale`, **no resize command**), and edited in the
> Inspector. **Decisions:** a BOX collider **refuses Rotate** (axis-aligned — `ResolveTool`/`Enter` fall
> back to Move with a status hint, the rig's precedent); a convex rotates. **Bake products** (boundary
> segments) are **pickable but move/delete-refused** at a lower `BakedProductPickDepth` (they regenerate).
> **Add Collider ▸ Box / Polygon** (entity menu + Entity header + toolbar `+Box`/`+Poly` + the
> `collider:addBox`/`addConvex` + `collider/add-box`/`add-polygon` menu paths) creates a footprint-shaped
> CHILD collider entity via `CreateEntityCommand` (auto-named, passive, selected; box = the parent sprite
> footprint / a 32×32 fallback, polygon = a footprint hexagon / a small fallback); **−Col** deletes the
> selected collider entity. Convex VERTEX grips survive as `ProxyBindingKind.ConvexVertex` proxies retargeted
> at the collider entity's own `ModelVertices`. Premises: the two collider-proxy premises rewritten into "A
> collider is a first-class editor entity…" + "A convex collider entity's vertices are edited through
> (kind, index) grip proxies…"; selection / inspector / context-menu / boundary / trigger / camera-rig
> premises updated. 7 skipped tests un-skipped + rewritten; `PrefabMilestoneTests` extended with the
> author-collider-child-in-a-prefab-tab story.

- `ProxySyncSystem`'s collider bindings, `GizmoProxyComponent`'s box/convex kinds,
  `ColliderEditCommand`, and the box-resize proxy path are RETIRED. A collider
  child is selected (border-pick on its world shape — the shape IS the pick
  surface for spriteless entities), moved/scaled/rotated by the normal gizmo and
  modal G/S/R, and edited numerically in the Inspector, like any entity.
- **Convex vertex grips remain proxy-style** (a vertex is not an entity — the
  camera-rig precedent: first-class entity + transient grips): the (kind,index)
  vertex mechanism retargets to the collider entity's own component.
- **Add-collider flows**: the Inspector's add-candidates DROP the collider
  components (a shape component lives only on a collider entity); the entity
  context/Entity menu gains **Add Collider ▸ Box / Polygon** (creates a child
  collider entity, undoable, auto-named); the toolbar `+Box`/`+Poly` buttons and
  ops retarget to it; the trigger palette places standalone collider entities.
- Debug outlines (`ColliderDebugSystem`) and editor overlays read collider
  entities' world shapes. Prefab guardrails compose unchanged (a collider child
  of an instance is prefab-owned; the PF-G pick-redirect applies).

## 4. Waves

| Wave | Scope | Depends on |
|---|---|---|
| **CE-A** | collision+physics core: collider entities, WorldMatrix-derived shapes, body resolution, message shape, resolution write-back to bodies, ColliderTag retarget, premises (collision/physics/foundation) | PF-G landed |
| **CE-B** | serialization v2 + fail-loud v1 refusal + the `monodreams migrate-colliders` CLI command + parsers/factories/boundary/trigger production + committed-content migration in-repo | CE-A |
| **CE-C** | editor: collider-proxy retirement, shape border-pick, Add Collider flows, vertex-grip retarget, Inspector candidates, debug/overlay readers, ops | CE-B |
| **CE-D** | Examples + Demos sweep (player, ground, NPC zones, dialogue zones) + milestone/walkthrough updates + docs/roadmap close | CE-C |

Gate per wave: `dotnet build MonoDreams/MonoDreams.csproj && dotnet test
--configuration Release` (full solution).

> **Landed in CE-D (2026-07-10).** The phase-closing sweep. **Consumer audit COMPLETE
> (pre-mortem #4):** every shipping-code `CollisionMessage` consumer is proven to read the
> correct side by `MonoDreams.Tests/Collision/CollisionConsumerAuditTests.cs` — `GameCollisionHelper`
> (identity collider-first-then-body), `ZoneDialogueTriggerSystem` (`ColliderB`, + a negative that it
> never falls back to `BodyB`), `RunnerCollisionHandlerSystem` (`BodyA` state / `BodyB` dispose),
> `NPCInteractionSystem` (a proximity consumer resolving the collider off a CHILD entity); the two
> resolution systems are cited to `ColliderEntityTests`, and the physics-demo `BallBounceSystem`
> (collider==body) gains a live render-path smoke (`HeadlessDemoTests.HeadlessPhysicsDemo_…`, non-blank
> + heap-flat over 600 frames of perpetual collision + grid rebuild). **Examples/Demos sweep:** no
> pre-CE collider construction remained (`PlayerEntityFactory` = body + child collider; the physics
> demo's walls = centered-`Size` box entities); `ColliderEditCommand.ForBox` and the whole-shape
> proxies are gone, only `ForConvex` (vertex grips) survives. **Milestones** (`IslandMilestoneTests`,
> `PrefabMilestoneTests`) already told the current authoring story end-to-end (palette → Add Collider ▸
> → boundary → trigger → prefab → save v2 → boot → play). **WIP migration readiness (item 6):**
> `PrefabColliderMigrationTests` proves `migrate-colliders` handles a `.mdprefab` with an embedded box
> on the root + a convex on a child — each becomes a new collider CHILD (the box parents to the root),
> the result satisfies one-root through `PrefabData.FromScene`, and expands + places world-correct.
> Docs closed (this block, the roadmap CE section, `CORE_TENETS` §5, the `level-editor` overview).

## What changed for game code (the CE model, for a gamedev's AI agents)

If you write systems or factories that touch colliders, this is the whole contract:

- **A collider is its own entity.** A shape component (`BoxColliderComponent` = a centered `Size`, no
  offset; `ConvexColliderComponent` = entity-local `ModelVertices`) + its own `TransformComponent` +
  an auto-applied `ColliderTagComponent`. Its world shape derives from the collider entity's own
  `WorldMatrix` (via `SATCollision.BoxWorldRect` / `UpdateWorldVertices`) — never from an embedded
  `Bounds` offset (that field is gone).
- **Construction pattern.** A physics entity (player, mover) is the BODY (`RigidBody`/`Velocity`); its
  collider is a `ChildOf` CHILD entity centered on it (see `PlayerEntityFactory`). Static geometry / a
  trigger zone is a STANDALONE collider entity — a trigger is exactly `{ EntityInfoComponent (+ maybe a
  game zone component), TransformComponent, a passive collider }`. A body may own N collider children;
  two shape components on ONE entity is undefined — give each its own child.
- **Body resolution.** `ColliderBody.Resolve(collider)` walks up `ChildOf` to the nearest `RigidBody`
  (else `Velocity`) ancestor, else the collider itself. A standalone collider is its own body.
- **The message names FOUR entities.** `CollisionMessage(ColliderA, ColliderB, BodyA, BodyB, …)`.
  **Read the collider side for IDENTITY** (a zone's `EntityInfoComponent` / `DialogueZoneComponent`
  lives on the collider entity) and **the body side for PHYSICS / gameplay state** (player state,
  disposing the whole game object, resolution write-back). A is the initiator; B is the other side.
- **Resolution writes to the BODY** (`BodyA`), reads shapes from the colliders, reads the swept delta
  from the body's `TransformComponent.Delta` — never correct a collider child (it would drift inside
  its parent). Custom collision messages implement `ICollisionMessage` and receive all four entities
  via `CreateCollisionMessageDelegate`, so a game classifier keys on identity or physics without
  re-walking the hierarchy.
- **Serialization.** Native scenes/prefabs are `SceneData.Version = 2`. A legacy v1 file with an
  embedded collider is refused loud on read — run `monodreams migrate-colliders <path|dir>` (handles
  both `.mdscene` and `.mdprefab`).

## 5. Pre-mortem

1. **Resolution write-back to the wrong entity** — contacts must correct the BODY,
   never the collider child (a corrected child drifts inside its parent). The
   player-walks-island milestone is the tripwire.
2. **Version-guard leniency** — silently loading a v1 collider file with the new
   deserializer produces plausible-but-wrong shapes; the refusal must trigger on
   ANY collider component in a v1 file.
3. **Migrator float drift** — vertex re-basing must round-trip through
   `CanonicalJson`'s invariant shortest-float policy or migrated files churn on
   next save; migrate → load → save must be a byte fixed point (tested).
4. **Trigger identity consumers** — dialogue zones read the trigger's EntityInfo;
   after CE the identity lives ON the collider entity — audit every
   `CollisionMessage` consumer for which side (collider vs body) it needs.
5. **Spriteless picking** — collider entities have no sprite; if border-pick
   misses them, they're unselectable orphans. The pick path must treat the world
   shape as the surface, tested at rotated/scaled parents.
6. **Prefab byte-stability** — collider children inside prefabs flow through the
   diff-based override machinery; the PF byte-fixed-point suites must stay green
   over v2 content.
