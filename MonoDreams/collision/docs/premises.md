# collision — premises

> Technical invariants the engine assumes about the collision module:
> `BoxColliderComponent`, `ConvexColliderComponent`, `ColliderTagComponent`,
> `IColliderComponent`, `CollisionMessage`, `TransformCollisionDetectionSystem`,
> and the two resolution systems. Read this before changing any of those
> pieces or any system that emits or consumes `CollisionMessage`.

## `ColliderTagComponent` is the canonical query target

`TransformCollisionDetectionSystem` queries entities by
`ColliderTagComponent`, not by `BoxColliderComponent` or
`ConvexColliderComponent`. The tag is auto-applied to any entity that
gains either collider component.

**Why:** the unification lets detection treat box and convex colliders
through the same query, dispatching to the right narrowphase test by
type at iteration time. Two separate queries would duplicate the
broadphase loop.
**Breaks:** game code that adds `ColliderTagComponent` manually (or
removes it while a collider remains) desyncs the tag from collider
presence — the entity is queried but has no collider to test, or has a
collider detection ignores.
**Tests:** none yet.
**Depends on:** —

## Swept collision reads `TransformComponent.Delta`

Dynamic collision tests (e.g., `DynamicRectVsRect`,
`DynamicConvexVsConvex` in `SATCollision`) read
`TransformComponent.Delta` to compute the entity's movement vector.
Correct `Delta` requires `TransformCommitSystem` ran for the previous
frame.

**Why:** swept tests prevent tunneling — they check whether the movement
segment crosses the other collider, not just whether the endpoints
overlap.
**Breaks:** with a missing or stale `Delta`, fast-moving entities pass
through colliders. The bug is silent (no exception) and intermittent
(depends on speed).
**Tests:** none yet (the SAT primitives in
`MonoDreams.Tests/Collision/SATCollisionTests.cs` exercise the
narrowphase without going through a Delta-driven swept test).
**Depends on:** foundation — "`TransformComponent.Delta` is meaningful
only after `TransformCommitSystem` ran".

## `ConvexColliderComponent.BroadPhaseAABB` must be refreshed when vertices change

Detection's broadphase filters on `ConvexColliderComponent.BroadPhaseAABB`.
If the world vertices change (entity moved or rotated) without the AABB
being refreshed, the broadphase rejects an entity pair whose real
geometry would overlap.

**Why:** the broadphase is an early-exit fast filter. Out-of-date AABBs
defeat the filter's purpose by producing false negatives instead of
false positives.
**Breaks:** a real collision is invisible to the broadphase and never
reaches the narrowphase — entities silently pass through each other
even though their actual shapes overlap.
**Tests:**
`MonoDreams.Tests/Collision/SATCollisionTests.cs::ConvexCollider_BroadPhaseAABB_UpdatedAfterTransform`
verifies the detection system's own update path keeps the AABB fresh.
A custom system that mutates vertices outside detection has no
protection.
**Depends on:** —

## Collider world geometry derives from the entity's WORLD transform

Both narrowphase shapes are placed in the world from the entity's **world**
transform, not its local one: `ConvexColliderComponent.UpdateWorldVertices`
scales/rotates/translates `ModelVertices` by
`TransformComponent.WorldScale`/`WorldRotation`/`WorldPosition`, and every box
AABB / box-polygon (detection broadphase + narrowphase, resolution, and
`SATCollision.BoxToPolygon`) is anchored at `TransformComponent.WorldPosition`.
So a collider authored on a **child** entity — the first real case being a
prefab instance's child (`house` → `House2` with a convex collider) — sits at
its world location once placed, exactly where the editor's proxy / debug
outlines (which already read `WorldPosition`) draw it. This **closes the former
root-level-entity limitation** the convex derivation carried. For a root entity
the world transform equals the local one (`WorldPosition == Position`,
`WorldRotation == Rotation`, `WorldScale == Scale`), so every flat-hierarchy
scene is **byte-identical** to the pre-change behaviour; the resolution
position write-backs still mutate the *local* `Position`, correct only for a
root dynamic mover (a dynamic child would need a world→local map — out of
scope, interim).

**Why:** a prefab collider authored on a child used to land at its
parent-relative local position when the instance was placed — "more to the top
and right" of where it belonged — because the derivation read local `Position`.
Reading the world transform makes authoring-in-prefab == placed-in-scene.
**Breaks:** if a system mutates a moved ancestor without the world matrix being
refreshed (foundation — "HierarchySystem must run ahead of any system reading
WorldPosition"), a child collider tests against a stale world position; a baked
child collider that ALSO copies the parent's world position onto its own local
field would double-count once the matrix link is synced (see level-editor's
`BoundaryBakeSystem` — segments sit at local origin, parented).
**Tests:**
`MonoDreams.Tests/Collision/SATCollisionTests.cs::ConvexCollider_ChildEntity_WorldVertices_IncludeParentWorldPosition`
and `::BoxCollider_Root_WorldPosition_ByteIdenticalToLocal`;
`MonoDreams.Tests/LevelEditor/PrefabMilestoneTests.cs` (author-collider-on-child
→ instance → world-correct collider).
**Depends on:** foundation — "HierarchySystem must run ahead of any system
reading WorldPosition"; foundation — "`WorldMatrix` is cached and computed
lazily". *CE (colliders-as-entities) will re-derive collider world geometry via
the owning entity's `WorldMatrix` natively, subsuming this.*

## Reference physics pipeline order: Movement → Velocity → Detection → Resolution → Commit

Each stage owns one job and the next depends on the previous stage's
output. The screen's pipeline assembler is responsible for registering
these five stages in this order. Skipping or reordering them silently
degrades collision quality.

**Why:** Movement sets intent (game code writes `VelocityComponent`).
`VelocitySystem` applies intent to `TransformComponent.Position`.
`TransformCollisionDetectionSystem` finds contacts using the new
position + the previous frame's `Delta`. Resolution corrects
positions/velocities to honor contacts. `TransformCommitSystem` closes
the frame so next frame's `Delta` is meaningful.
**Breaks:** skip Commit → next frame's swept collision tunnels. Move
Resolution before Detection → resolution corrects against a stale
contact set. Mix gameplay position-writes with `VelocitySystem` → see
"`VelocitySystem` is the primary mover" in the physics module.
**Tests:** none yet (the integration tests exercise this order
implicitly —
`MonoDreams.Tests/IntegrationTests/InfiniteRunnerTests.cs::PlayerFallsOffLeftEdge`
depends on the full chain).
**Depends on:** physics — "`VelocitySystem` is the primary mover of
physics entities"; foundation — "`TransformComponent.Delta` is
meaningful only after `TransformCommitSystem` ran".

## `TransformCollisionDetectionSystem` is single-threaded by design

The detection system holds instance-level polygon buffers
(`_boxPolyBufA`, `_boxPolyBufB`). It is not thread-safe; only one
instance should be registered and invoked at a time.

**Why:** the polygon buffers avoid per-collision allocations in the hot
path. Making them thread-local would re-introduce allocation pressure or
require an explicit pool.
**Breaks:** parallel invocations corrupt the buffers, producing nonsense
intersection tests — false positives, false negatives, or NaN-cascaded
entity positions.
**Tests:** none yet.
**Depends on:** —

## One collider of each type per entity

The framework assumes an entity has at most one `BoxColliderComponent`
and at most one `ConvexColliderComponent`. Multiple colliders of the
same type on a single entity is undefined behavior.

**Why:** the assumption simplifies queries and narrowphase dispatch.
The use case for multi-collider entities (e.g., one body, multiple
hitboxes) hasn't appeared, so the framework hasn't designed for it.
**Breaks:** queries pick one collider non-deterministically; detection
may test against the wrong one. If the use case appears, this becomes
a framework change, not a workaround.
**Tests:** none yet.
**Depends on:** —

## Layer-based filtering is the semantic pair filter

Colliders with non-overlapping `ActiveLayers` sets are never tested
against each other. Layer membership is the "should these ever collide?"
filter that gameplay code uses to express groupings like "player vs
world", "player vs enemy", "projectiles vs everything".

**Why:** layers express intent, not geometry. They are applied per
candidate pair *after* the spatial-grid broadphase has narrowed the
field by position (see "Broadphase is a uniform spatial grid"); together
they keep detection near O(n) for evenly distributed colliders rather
than all-pairs O(n²).
**Breaks:** a missing layer membership makes two colliders silently
not collide — the dev tunes the game logic for hours before realizing
the layer wasn't set.
**Tests:** none yet.
**Depends on:** "Broadphase is a uniform spatial grid".

## Broadphase is a uniform spatial grid, and it is behavior-preserving

`TransformCollisionDetectionSystem` narrows candidate pairs with a
uniform spatial grid rebuilt every frame, not an all-pairs sweep. Each
enabled collider's world AABB — **expanded by its
`TransformComponent.Delta`** — is bucketed into every grid cell it
overlaps, and only colliders sharing a cell are pair-tested (deduped on
the *ordered* pair). The grid changes performance, not results: it emits
exactly the `CollisionMessage` set the old all-pairs loop did. Two
properties make that hold: (1) inserting into *all* overlapping cells
means any two colliders whose AABBs overlap always share a cell, so no
real contact is pruned; (2) expanding by `Delta` means a fast mover
shares a cell with anything along its swept path, so the swept
box-vs-box test is never starved. The dedup key is ordered — (A,B) and
(B,A) stay distinct — so the symmetric double-message that resolution may
rely on is preserved.

**Why:** all-pairs detection is O(n²) and stutters in the low hundreds of
colliders; the grid is ~O(n) for evenly distributed colliders (measured
~6× faster at 360 balls in the physics demo, and the gap widens with
count). Cell size adapts to the average collider AABB so small colliders
occupy ~one cell while the few large ones span several.
**Breaks:** bucketing by the un-expanded AABB lets a fast collider tunnel
(its swept path leaves the cells it was bucketed into) — the swept box
test silently misses contacts. Deduping on the *unordered* pair drops one
of the two symmetric messages, halving the impulse for resolvers that
expect both. Inserting into only one cell (e.g. the min-corner cell)
prunes real contacts whose colliders straddle a cell boundary.
**Tests:** behavior parity is covered indirectly — `SATCollisionTests`,
`InfiniteRunnerTests`, and `HeadlessDemoTests` pass unchanged after the
grid replaced the all-pairs loop.
**Depends on:** "Swept collision reads `TransformComponent.Delta`";
"`TransformCollisionDetectionSystem` is single-threaded by design".

## Collision today couples to `TransformComponent` directly

`TransformCollisionDetectionSystem` and the resolution systems read
`TransformComponent` by type — `TransformComponent.Position`,
`TransformComponent.Delta`, and `ConvexColliderComponent.WorldVertices`
(refreshed from `TransformComponent.WorldMatrix`). *Status:
implementation debt.*

**Why:** the eventual end-state is loose coupling — collision against
any Transform-shaped contract — so a developer can swap in
`MyTransform` without re-implementing collision. The framework
doesn't enforce this today; the coupling is named here so the gap is
visible.
**Breaks:** a developer with `MyTransform` has to fork the collision
stack. The framework can't honestly call itself plug'n'play for
spatial substitutes until this loosens.
**Tests:** none yet.
**Depends on:** foundation — "Don't mix two Transform-shaped components
in one project".

## `CollisionMessage` is the contract between detection and consumers

Detection emits `CollisionMessage` (or a custom message that satisfies
`ICollisionMessage` — `TransformCollisionDetectionSystem` is generic on
the message type via a `CreateCollisionMessageDelegate`). The message
carries the entity pair, contact point, contact normal, contact time,
penetration depth, layer, and collision type. Resolution systems and
game systems are the consumers.

**Why:** the generic message type lets games extend with custom fields
(damage, knockback strength, sound cue ID) without modifying the
framework. The base fields are the minimum contract for any contact.
**Breaks:** a custom message that drops a base field silently breaks
the resolution systems' assumption about what's available.
**Tests:** none yet.
**Depends on:** —

## Known limitations (acknowledged gaps)

- **Tunneling at very high velocity** — swept collision prevents most
  tunneling, but at sufficiently extreme speed-to-collider-size
  ratios it can still occur. The only mitigation today is keeping
  gameplay velocities and collider sizes reasonable. *Velocity-cap
  safety net is on the backlog.*

## Open questions

- **`Passive` flag on `BoxColliderComponent` / `ConvexColliderComponent`** —
  the field exists but the semantic isn't yet documented (probably
  "detect but don't apply response"). Needs confirmation.
- **`ColliderTagComponent` on disposal** — is it auto-removed when the
  collider is removed, or only auto-added?

## Aspirational direction

- Loose coupling to `TransformComponent` so the collision stack works
  against any Transform-shaped contract.
- Multi-collider entities, if the use case appears — needs a defined
  combination semantic (intersection? union? per-layer override?).
- Velocity-cap safety net to prevent tunneling regardless of gameplay
  speed.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `ColliderTagComponent` is the canonical query target
- Swept collision reads `TransformComponent.Delta`
- Reference physics pipeline order
- `TransformCollisionDetectionSystem` is single-threaded by design
- One collider of each type per entity
- Layer-based filtering is the semantic pair filter
- Collision today couples to `TransformComponent` directly
- `CollisionMessage` is the contract between detection and consumers

The `ConvexColliderComponent.BroadPhaseAABB` premise is the only one
with test protection. The pipeline-order premise is the highest-leverage
gap — an architectural test would catch screens that omit
`TransformCommitSystem`.
