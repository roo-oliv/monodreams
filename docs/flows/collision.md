---
flow: collision
covers:
  - MonoDreams/collision/**
sensitive: false
---

# Collision detection & resolution

Collision is the stage that runs *after* the physics tick has already moved everything. By the
time `TransformCollisionDetectionSystem` runs, `TransformVelocitySystem` (physics) has integrated
`Velocity.Current` into `Transform.Position`, and `Transform.Delta` carries last frame's committed
move — the swept input. Detection makes one O(n) pass over every `ColliderTagComponent` entity:
it refreshes each `ConvexColliderComponent`'s world vertices, snapshots each enabled collider's
world AABB **expanded by `Transform.Delta`** so a fast mover's swept path can't be pruned, then
buckets it into a per-frame uniform spatial grid. Same-cell ordered pairs that share a layer reach
the narrowphase: box-vs-box goes through `DynamicRectVsRect` (swept ray-vs-expanded-rect, returns
contactPoint/normal/`contactTime`); anything involving a convex collider goes through `TestSAT`
(AABB reject → `SATCollision.PolygonVsPolygon`, returns normal + `penetrationDepth`). Each contact
publishes a `CollisionMessage` per shared layer. Detection writes nothing to the world but the
messages.

Resolution consumes those messages. `TransformCollisionResolutionSystem` and
`TransformPhysicalCollisionResolutionSystem` (the same resolution, in a subclass whose only
override filters to `CollisionType.Physics`) both buffer the frame's collisions, **sort them by
`ContactTime`** so the earliest contact resolves first, then re-validate each against current
positions and correct `Transform`: box-vs-box snaps the axis with a non-zero normal
(`TranslateX/Y` onto the swept contact point) and zeros that component of `Velocity.Current`; an
already-overlapping box pair depenetrates along the shortest exit instead; SAT pushes out along the MTV
(`Translate(-normal * penetration)`) and removes the velocity component moving into the contact.
Neither system applies an impulse and neither reads `Mass`. This is where the **coupling to
`physics`** lives — hard at compile time (resolution and `ColliderBody` open
`MonoDreams.Component.Physics`, so `collision` declares `physics` in its `module.json`), soft at
runtime: resolution reads/writes `Velocity.Current` *if present* (no `VelocityComponent` →
position-only correction, fine for trigger colliders), and publishes `RigidBodyTouchMessage` for
grounded-state and audio. `TransformCommitSystem` then closes the frame so next frame's `Delta` is
meaningful again.

## Entities & lifecycle

A collidable entity carries `TransformComponent` + one collider (`BoxColliderComponent` or
`ConvexColliderComponent`); `ColliderTagComponent` is auto-added by detection's component-added
hooks and is the *only* query target. Per frame, in pipeline order, downstream of the physics tick:

1. **Detect** — `TransformCollisionDetectionSystem` rebuilds entries + grid, narrowphase-tests
   same-cell shared-layer ordered pairs, publishes one `CollisionMessage` per shared layer. A
   collider with `Passive = true` is tested as a *target* but never initiates; `Enabled = false`
   drops it from the grid entirely.
2. **Resolve** — one or both resolution systems drain their buffered messages (sorted by
   `ContactTime`), re-validate, correct `Transform.Position`, and zero/clip `Velocity.Current`.
   `RigidBodyTouchMessage` is published per resolved contact side.
3. **Commit** — `TransformCommitSystem` finalizes `Last`, restoring a meaningful `Delta` for the
   next detection pass.

`CollisionMessage` is the only record this flow creates; game systems are equal consumers (pickups,
zones) alongside resolution — same detection serves trigger and physical contacts.

## Invariants

Authoritative list in [`MonoDreams/collision/docs/premises.md`](../../MonoDreams/collision/docs/premises.md); the ones this flow's ordering leans on:

- Pipeline order **Movement → Velocity → Detection → Resolution → Commit**. Detection reads the
  position the physics tick just wrote and the `Delta` the *previous* `TransformCommitSystem` left;
  resolution must run after detection, and Commit must close the frame or next frame's swept tests
  tunnel.
- Detection queries `ColliderTagComponent`, expands each AABB by `Transform.Delta` before
  bucketing, and dedups on the **ordered** pair — both symmetric `(A,B)`/`(B,A)` messages survive.
- `CollisionMessage` is the contract: `ContactTime` is populated by box-vs-box (resolution sorts on
  it), `PenetrationDepth` by SAT; the two are mutually exclusive per message.

## Load-bearing quantities

- `Transform.Delta` — the swept displacement, world units/frame. Detection expands AABBs by it and
  box-vs-box uses it as the ray. Empty (stale) `Delta` → no sweep, contact missed.
- `contactTime` — fraction of `Delta` to first contact, `[0,1)`; `DynamicRectVsRect` rejects `>= 1`.
  Resolution sorts ascending so the nearest contact wins. SAT leaves it `0`.
- `penetrationDepth` × `contactNormal` — SAT minimum-translation vector, world units; resolution
  applies `Translate(-normal * depth)` to separate. Box-vs-box uses `contactPoint` + collider
  bounds instead (depth `0`).
- Velocity write-back — resolution zeros the contacted axis (box) or subtracts `dot(Current,
  normal) * normal` when positive (SAT). Units/s, no cap; only applied if `VelocityComponent` present.

## Failure modes

- **Missed swept contact from an empty `Delta`** — a system teleported the entity (direct
  `Transform.Position` write instead of `Velocity`), or `TransformCommitSystem` was omitted, so
  `Delta` is zero. Detection expands the AABB by nothing and box-vs-box's `displacement == 0` path
  falls back to a static overlap test — fast movers tunnel. Silent and speed-dependent; the
  highest-frequency real bug in this flow (see `physics` flow — teleport-instead-of-move).
- **Double resolution** — registering both resolution systems for the same entity stack applies two
  corrections to one contact (the physical system only filters to `CollisionType.Physics`, so a
  `Generic`/`Physics` mix can have each message handled once *per* system). Pick one per stack.
- **Freeze axis is not enforced — known gap.** `RigidBodyComponent.FreezePositionX/Y` and
  `FreezeRotation` exist but **no collision resolution system reads them** — `SetPositionX/Y` and
  `Translate` move unconditionally. A frozen axis is only "frozen" if no resolvable contact pushes
  it; a contact along that axis silently wins. See the physics freeze-flag premise (a documented gap,
  not yet implemented).
- **Layer omission** — a collider with no overlapping `ActiveLayers` is never pair-tested; the
  contact never reaches the narrowphase and the dev tunes gameplay for hours before noticing.
- **Stale `BroadPhaseAABB`** — vertices mutated outside detection without refreshing the convex
  collider's AABB: the broadphase false-negatives and the real overlap never reaches SAT.
