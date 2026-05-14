# Collision — premises

> Technical invariants the engine assumes about the collision system.
> Each entry: title, paragraph, **Why** / **Breaks** / **Tests** / **Depends on**.
> Aspirational items at the bottom (intended end-state, not yet enforced).

## `ColliderTag` is the canonical query target

`TransformCollisionDetectionSystem` queries entities by `ColliderTag`,
not by `BoxCollider` or `ConvexCollider`. The tag is auto-applied to
any entity that gains either collider component.

**Why:** the unification lets detection treat box and convex colliders
through the same query, dispatching to the right narrowphase test by
type at iteration time. Two separate queries would duplicate the
broadphase loop.
**Breaks:** game code that adds `ColliderTag` manually (or removes it
while a collider remains) desyncs the tag from collider presence — the
entity is queried but has no collider to test, or has a collider
detection ignores.
**Tests:** none yet.
**Depends on:** —

## Swept collision reads `Transform.Delta`

Dynamic collision tests (e.g., `DynamicRectVsRect`,
`DynamicConvexVsConvex`) read `Transform.Delta` to compute the entity's
movement vector. Correct `Delta` requires `TransformCommitSystem` ran
for the previous frame.

**Why:** swept tests prevent tunneling — they check whether the
movement segment crosses the other collider, not just whether the
endpoints overlap.
**Breaks:** with a missing or stale `Delta`, fast-moving entities pass
through colliders. The bug is silent (no exception) and intermittent
(depends on speed).
**Tests:** none yet (the SAT primitives in `SATCollisionTests`
exercise the narrowphase without going through a Delta-driven swept
test).
**Depends on:** Hierarchy & Transform — "`Transform.Delta` is
meaningful only after `TransformCommitSystem` ran".

## `ConvexCollider.BroadPhaseAABB` must be refreshed when vertices change

Detection's broadphase filters on `ConvexCollider.BroadPhaseAABB`. If
the world vertices change (entity moved or rotated) without the AABB
being refreshed, the broadphase rejects an entity pair whose real
geometry would overlap.

**Why:** the broadphase is an early-exit fast filter. Out-of-date AABBs
defeat the filter's purpose by producing false negatives instead of
false positives.
**Breaks:** a real collision is invisible to the broadphase and never
reaches the narrowphase — entities silently pass through each other
even though their actual shapes overlap.
**Tests:**
`SATCollisionTests.ConvexCollider_BroadPhaseAABB_UpdatedAfterTransform`
verifies the detection system's own update path keeps the AABB fresh.
A custom system that mutates vertices outside detection has no
protection.
**Depends on:** —

## Reference physics pipeline order: Movement → Velocity → Detection → Resolution → Commit

Each stage owns one job and the next depends on the previous stage's
output. The screen's pipeline assembler is responsible for registering
these five stages in this order. Skipping or reordering them
silently degrades collision quality.

**Why:** Movement sets intent (game code writes `Velocity`). Velocity
applies intent to `Transform.Position`. Detection finds contacts using
the new position + the previous frame's `Delta`. Resolution corrects
positions/velocities to honor contacts. Commit closes the frame so
next frame's `Delta` is meaningful.
**Breaks:** skip Commit → next frame's swept collision tunnels. Move
Resolution before Detection → resolution corrects against a stale
contact set. Mix gameplay position-writes with `VelocitySystem` → see
"`VelocitySystem` is the primary mover" (physics premises).
**Tests:** none yet (the integration tests exercise this order
implicitly — `InfiniteRunnerTests.PlayerFallsOffLeftEdge` depends on
the full chain).
**Depends on:** Physics — "`VelocitySystem` is the primary mover of
physics entities"; Hierarchy & Transform — "`Transform.Delta` is
meaningful only after `TransformCommitSystem` ran".

## `TransformCollisionDetectionSystem` is single-threaded by design

The detection system holds instance-level polygon buffers
(`_boxPolyBufA`, `_boxPolyBufB`). It is not thread-safe; only one
instance should be registered and invoked at a time.

**Why:** the polygon buffers avoid per-collision allocations in the
hot path. Making them thread-local would re-introduce allocation
pressure or require an explicit pool.
**Breaks:** parallel invocations corrupt the buffers, producing
nonsense intersection tests — false positives, false negatives, or
NaN-cascaded entity positions.
**Tests:** none yet.
**Depends on:** —

## One collider of each type per entity

The framework assumes an entity has at most one `BoxCollider` and at
most one `ConvexCollider`. Multiple colliders of the same type on a
single entity is undefined behavior.

**Why:** the assumption simplifies queries and narrowphase dispatch.
The use case for multi-collider entities (e.g., one body, multiple
hitboxes) hasn't appeared, so the framework hasn't designed for it.
**Breaks:** queries pick one collider non-deterministically; detection
may test against the wrong one. If the use case appears, this becomes
a framework change, not a workaround.
**Tests:** none yet.
**Depends on:** —

## Layer-based filtering is a coarse first filter

Colliders with non-overlapping `ActiveLayers` sets are never tested
against each other. Layer membership is a fast first filter that
gameplay code uses to express groupings like "player vs world",
"player vs enemy", "projectiles vs everything".

**Why:** without a layer cut, every entity tests against every other
entity — O(n²) detection. Layers reduce that to O(n²) within each
layer pair.
**Breaks:** a missing layer membership makes two colliders silently
not collide — the dev tunes the game logic for hours before realizing
the layer wasn't set.
**Tests:** none yet.
**Depends on:** —

## Collision today couples to `Transform` directly

`TransformCollisionDetectionSystem` and the resolution systems read
`Transform` by type — `Transform.Position`, `Transform.Delta`,
`Transform.WorldVertices` for `ConvexCollider`. *Status:
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
**Depends on:** Hierarchy & Transform — "Don't mix two
Transform-shaped components in one project".

## `CollisionMessage` is the contract between detection and consumers

Detection emits `CollisionMessage` (or a custom message that satisfies
`ICollisionMessage` — the detection system is generic on the message
type). The message carries the entity pair, contact point, contact
normal, contact time, penetration depth, layer, and collision type.
Resolution systems and game systems are the consumers.

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

- **`Passive` flag on `BoxCollider` / `ConvexCollider`** — the field
  exists but the semantic isn't yet documented (probably "detect but
  don't apply response"). Needs confirmation.
- **`ColliderTag` on disposal** — is it auto-removed when the collider
  is removed, or only auto-added?

## Aspirational direction

- Loose coupling to `Transform` so the collision stack works against
  any Transform-shaped contract.
- Multi-collider entities, if the use case appears — needs a defined
  combination semantic (intersection? union? per-layer override?).
- Velocity-cap safety net to prevent tunneling regardless of gameplay
  speed.

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `ColliderTag` is the canonical query target
- Swept collision reads `Transform.Delta`
- Reference physics pipeline order
- `TransformCollisionDetectionSystem` is single-threaded by design
- One collider of each type per entity
- Layer-based filtering is a coarse first filter
- Collision today couples to `Transform` directly
- `CollisionMessage` is the contract between detection and consumers

The `ConvexCollider.BroadPhaseAABB` premise is the only one with
test protection. The pipeline-order premise is the highest-leverage
gap — an architectural test would catch screens that omit
`TransformCommitSystem`.
