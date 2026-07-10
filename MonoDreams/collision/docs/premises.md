# collision — premises

> Technical invariants the engine assumes about the collision module:
> `BoxColliderComponent`, `ConvexColliderComponent`, `ColliderTagComponent`,
> `IColliderComponent`, `CollisionMessage`, `ColliderBody`,
> `TransformCollisionDetectionSystem`, and the two resolution systems. Read this
> before changing any of those pieces or any system that emits or consumes
> `CollisionMessage`.

## A collider IS an entity (colliders-as-entities)

A collider is its own entity: a shape component (`BoxColliderComponent` or
`ConvexColliderComponent`) + its own `TransformComponent` (its pose, relative to a
parent body via the ordinary `ChildOf`/`Transform.Parent` hierarchy, or standalone)
+ an auto-applied `ColliderTagComponent`. The shape carries no embedded pose:
`BoxColliderComponent` is `{ Vector2 Size, ActiveLayers, Passive, Enabled }` and is
**centered** on the collider entity's `WorldPosition` with extent `Size` scaled by
`WorldScale` (rotation is intentionally ignored — the box stays axis-aligned; use a
convex for a rotated hitbox). `ConvexColliderComponent` keeps `ModelVertices` in the
collider entity's local space; `WorldVertices`/`BroadPhaseAABB` derive from that
entity's own `WorldMatrix`. `SATCollision.BoxWorldRect` is the single source of the
box's world pose (detection, resolution, debug outlines, and proxy geometry all route
through it).

**Why:** the RFC's authoring-model fix — a collider authored off-center (a prefab
child, a feet-anchored footprint) places by moving/parenting the collider ENTITY, not
by an embedded offset that diverged authored-in-prefab vs placed-in-scene (the class of
bug PF-G patched). A flat scene where the collider sat at the entity's origin is
byte-identical.
**Breaks:** deriving a box world rect anywhere but `BoxWorldRect` (e.g. re-adding a
`Bounds` offset) reintroduces the offset divergence; putting two shape components on one
entity makes it a single collider entity with undefined shape (give each its own child
entity instead).
**Tests:** `MonoDreams.Tests/Collision/SATCollisionTests.cs` (box/convex world geometry,
centered box corners, child-entity world shape); `MonoDreams.Tests/Collision/ColliderEntityTests.cs`
(child-collider world shape under a moved/scaled parent).
**Depends on:** foundation — "`HierarchySystem` must run ahead of any system reading
WorldPosition"; foundation — "`WorldMatrix` is cached and computed lazily".

## `ColliderTagComponent` tags collider ENTITIES and is the canonical query target

`TransformCollisionDetectionSystem` queries by `ColliderTagComponent`, not by
`BoxColliderComponent` or `ConvexColliderComponent`. The tag is auto-applied (by the
detection system's component-added subscriptions) to any entity that gains either
collider component — under the entity model, that IS the collider entity.

**Why:** the unification lets detection treat box and convex colliders through the same
query, dispatching to the right narrowphase test by type at iteration time. Two separate
queries would duplicate the broadphase loop.
**Breaks:** game code that adds `ColliderTagComponent` manually (or removes it while a
collider remains) desyncs the tag from collider presence. **The auto-tag is added on
the component-added event, so the detection system must be constructed BEFORE the
collider entities it should see** (or the entities re-add their collider) — a detection
system created after a collider entity never tags it, and the collider is invisible to
detection. The reference pipeline (and the tests) construct detection first.
**Tests:** `MonoDreams.Tests/Collision/ColliderEntityTests.cs` (the behavior tests rely
on detection-before-entities auto-tagging).
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

## A collider's body is resolved via `ColliderBody.Resolve` (nearest RigidBody, else Velocity, else self)

`ColliderBody.Resolve(collider)` is the one body-resolution primitive shared by
detection, resolution, and message construction. It walks the `ChildOf` chain up from
the collider entity (including the collider entity itself) and returns the nearest
ancestor carrying `RigidBodyComponent`; failing that, the nearest carrying
`VelocityComponent`; failing that, the collider entity itself. RigidBody wins outright
over a nearer Velocity (the `else` is a fallback, not a nearest-wins race). A standalone
collider — no physics ancestor — is therefore its own body: static geometry and trigger
zones reduce to collider == body.

**Why:** a "body" is the physical thing a contact acts on; resolution write-back and the
swept movement delta belong to it, not to a collider child riding it. One shared helper
keeps detection/resolution/messages from each inventing a different answer.
**Breaks:** resolving to the collider child instead of the body corrects the wrong entity
(pre-mortem #1); a body query that stops at the first Velocity would pick a nearer
kinematic proxy over the real RigidBody.
**Tests:** `MonoDreams.Tests/Collision/ColliderEntityTests.cs` (the Resolve matrix:
standalone / RigidBody ancestor / Velocity ancestor / RigidBody-wins-over-nearer-Velocity /
plain-parent-falls-back-to-self).
**Depends on:** foundation — "`ChildOfComponent` and `TransformComponent.Parent` are two
intentional links"; physics — "`GravitySystem`/`VelocitySystem`" (the body markers).

## Resolution corrects the BODY's Transform/Velocity, never the collider child

The resolution systems apply the position correction (box: translate the body so its
collider's world centre lands at the swept contact point; convex/SAT: translate the body
by the MTV) and the velocity zeroing/damping to the **body** (`CollisionMessage.BodyA`),
read the shapes from the **colliders** (`ColliderA`/`ColliderB`), and read the swept
movement delta from the body's `TransformComponent.Delta`. The correction is a
world-space vector applied to a root body via `Translate`; for a root body world delta ==
local delta and the collider child follows via its parent's world matrix.

**Why:** pre-mortem #1 — correcting a collider CHILD would drift it inside its parent
while the body sails on. The body is the mover; the collider only describes where it is.
A collider child's own local `Delta` is ~0 (it rides the body), so the swept test must
read the body's delta or it never sweeps.
**Breaks:** writing the correction to the collider entity de-syncs collider from body;
reading the collider's own `Delta` for the sweep starves fast movers of contacts. A
dynamic body that is itself a non-root child would need a world→local map for the
correction — the same interim limitation as before CE (documented, out of scope).
**Tests:** `MonoDreams.Tests/Collision/ColliderEntityTests.cs::Resolution_CorrectsTheBody_NotTheColliderChild`
(the child's local position is untouched while the body is blocked);
`MonoDreams.Tests/LevelEditor/IslandMilestoneTests.cs` (a player BODY with a CHILD collider
walks the island — the pre-mortem #1 tripwire).
**Depends on:** this file — "A collider's body is resolved via `ColliderBody.Resolve`";
foundation — "`TransformComponent.Delta` is meaningful only after `TransformCommitSystem` ran".

## Multi-collider bodies are legal; resolution accumulates sequentially with re-validation

A body may own N collider children (the former one-collider-of-each-type-per-entity
assumption is RETIRED — detection iterates collider entities, so it falls out naturally).
When two of a body's colliders both contact in one frame, resolution processes the
messages in contact-time order and each `Resolve*` **re-runs the narrowphase at the
current positions**: once an earlier correction has separated the body, the later
message's re-validation finds no penetration and no-ops. Sequential correction with
per-message re-validation is the accumulation rule — a body is never double-corrected
into an explosion.

**Why:** the entity model makes multi-collider bodies trivial to author (a hitbox per
limb, a wide + a narrow footprint); the re-validation the resolver already did for a
single contact is exactly what keeps N contacts stable.
**Breaks:** applying every message's precomputed penetration blind (no re-validation)
double-counts overlapping corrections and flings the body.
**Tests:** `MonoDreams.Tests/Collision/ColliderEntityTests.cs::TwoColliderBody_BothChildrenContact_ResolvesWithoutExploding`.
**Depends on:** this file — "Resolution corrects the BODY's Transform/Velocity".

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

## One collider of each type per entity — RETIRED (colliders-as-entities)

The former assumption (at most one `BoxColliderComponent` and one
`ConvexColliderComponent` per entity) is gone. A collider is its own entity, and a body
owns N collider children — see "A collider IS an entity" and "Multi-collider bodies are
legal; resolution accumulates sequentially with re-validation". Authoring two shapes on
ONE entity is still not a thing (that entity would be a single collider entity with an
ambiguous shape); give each shape its own child collider entity.

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
enabled collider's world AABB — **expanded by its BODY's
`TransformComponent.Delta`** (the collider rides its body; its own local delta
is ~0) — is bucketed into every grid cell it
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

## `CollisionMessage` carries both collider and body granularities

Detection emits `CollisionMessage` (or a custom message satisfying `ICollisionMessage` —
`TransformCollisionDetectionSystem` is generic via `CreateCollisionMessageDelegate`). The
message names FOUR entities: `ColliderA`/`ColliderB` (the collider entities — where the
shape and identity live) and `BodyA`/`BodyB` (the resolved bodies, via `ColliderBody`).
A is the initiator (the active, non-passive mover); B is the other side. Plus contact
point/normal/time, penetration depth, layer, and collision type. The delegate receives
all four entities, so a game classifier can key on identity (collider) or physics (body)
without re-walking the hierarchy.

**Each consumer reads the side it needs (the pre-mortem #4 audit):**
| Consumer | side | why |
|---|---|---|
| resolution systems | `ColliderA/B` (shapes) + `BodyA` (write-back) + `BodyB` (touch msg) | geometry on the collider, correction on the body |
| `GameCollisionHelper` / runner classifier | identity, collider-first-then-body | "Player" rides the body, "Zone"/"Collectible" ride the collider |
| `ZoneDialogueTriggerSystem` | `ColliderB` | the zone's `DialogueZoneComponent` is on the collider entity |
| `RunnerCollisionHandlerSystem` | `BodyA` (player state), `BodyB` (dispose) | dispose the whole collectible/obstacle body, not a collider child |
| physics-demo `BallBounceSystem` | `BodyA` (write), `BodyB` (`FloorTag`) | collider == body there, so both resolve to the standalone entity |

**Why:** identity and physics live on different entities now (a player's identity on its
body, its collider on a child; a zone's identity on the collider entity itself), so a
single "the other entity" field could not serve both dialogue zones and physics.
**Breaks:** a consumer reading the wrong side misses identity (a dialogue zone read via
`BodyB` when the body is a physics parent) or disposes/queries a collider child instead of
the game object; a custom message dropping a base field breaks resolution.
**Tests:** `MonoDreams.Tests/Collision/ColliderEntityTests.cs::CollisionMessage_CarriesColliderAndBody_ForBothSides`;
the consumer sides are exercised by `IslandMilestoneTests`, `TriggerPlacementTests`,
`PrefabMilestoneTests`, and `InfiniteRunnerTests`.
**Depends on:** this file — "A collider's body is resolved via `ColliderBody.Resolve`".

## Colliders-as-entities perf is parity-or-better (RFC criterion)

The entity model reads an already-computed `WorldMatrix`/`WorldPosition` per collider
instead of doing per-frame local-offset math, so it is expected to be parity-or-better —
the RFC's cache-hop concern applies only at scales this game does not approach. Coarse,
non-gating smoke measurement (in-process, one detection pass over 500 convex collider
entities, Debug build):
`ColliderEntityTests.PerfSmoke_ManyConvexColliders_OneDetectionPass_Completes` measured
**~0.3 ms** per pass. The test asserts only a generous 2000 ms ceiling (catches a
catastrophic regression, not a micro-regression).

**Why:** the RFC gated CE on "no material perf regression"; this records the informal
number so a later change that regresses it is visible.
**Breaks:** a change that reintroduces per-collider per-frame allocation or an O(n²) pair
loop would blow past the ceiling.
**Tests:** `MonoDreams.Tests/Collision/ColliderEntityTests.cs::PerfSmoke_ManyConvexColliders_OneDetectionPass_Completes`.
**Depends on:** "Broadphase is a uniform spatial grid, and it is behavior-preserving".

## Known limitations (acknowledged gaps)

- **Tunneling at very high velocity** — swept collision prevents most
  tunneling, but at sufficiently extreme speed-to-collider-size
  ratios it can still occur. The only mitigation today is keeping
  gameplay velocities and collider sizes reasonable. *Velocity-cap
  safety net is on the backlog.*
- **Dynamic collider body that is itself a non-root child** — the resolution
  write-back applies a world-space correction to the body via `Translate`, which equals a
  local move only when the body is a root. A dynamic body nested under another moving/
  scaled/rotated transform would need a world→local map. No such case exists today (bodies
  are roots); documented as interim, same limitation as before CE.

## Open questions

- **`Passive` semantic (now documented, confirmed):** `Passive = true` means "does not
  INITIATE a collision" — a passive collider is never the resolver's moved body (it is
  never side A), but an active body IS resolved out of it, so passive static geometry
  BLOCKS while staying put (the `WallEntityFactory`/footprint/boundary idiom). Whether a
  passive collider reads as a physical blocker or a fire-only sensor is the game's
  `EntityInfoComponent` classification, not this flag.
- **`ColliderTagComponent` on disposal** — is it auto-removed when the collider component
  is removed, or only auto-added? (Auto-add is on the component-added event; there is no
  auto-remove.)

## Aspirational direction

- Loose coupling to `TransformComponent` so the collision stack works
  against any Transform-shaped contract.
- Velocity-cap safety net to prevent tunneling regardless of gameplay
  speed.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- Swept collision reads `TransformComponent.Delta`
- Reference physics pipeline order
- `TransformCollisionDetectionSystem` is single-threaded by design
- Layer-based filtering is the semantic pair filter
- Collision today couples to `TransformComponent` directly

The `BroadPhaseAABB`, the colliders-as-entities model, body resolution, the write-back
rule, multi-collider bodies, the four-entity message, and the perf smoke now carry test
protection (`SATCollisionTests`, `ColliderEntityTests`, the milestone suites). The
pipeline-order premise is the highest-leverage remaining gap — an architectural test would
catch screens that omit `TransformCommitSystem`.
