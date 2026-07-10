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

## 2. Serialization + migration (NO backwards compatibility, by directive)

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

## 3. The editor (proxies die for colliders)

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
