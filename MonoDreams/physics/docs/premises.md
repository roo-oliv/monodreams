# physics — premises

> Technical invariants the engine assumes about the physics module:
> `RigidBodyComponent`, `VelocityComponent`, `GravitySystem`, and
> `VelocitySystem`. Read this before changing any of those pieces or any
> system that writes velocity / reads `Transform.Delta` for movement.

## `GravitySystem` affects only entities with `RigidBodyComponent` + `VelocityComponent`

`GravitySystem` queries on entities that have both `RigidBodyComponent`
(with gravity active) and `VelocityComponent`. Missing either component
means gravity does not apply.

**Why:** gravity needs both a participant flag (`RigidBodyComponent.Gravity`)
and a target field to write to (`VelocityComponent.Current`). Falling back
to defaults would hide configuration bugs where a dev expected gravity
but never added the required components.
**Breaks:** a "floating" entity that was supposed to fall sits still;
the dev assumes gravity is broken, when in fact one of the two
components is missing.
**Tests:**
`MonoDreams.Tests/IntegrationTests/InfiniteRunnerTests.cs::PlayerFallsOffLeftEdge`
exercises this integration indirectly (player has both components and
falls as expected).
**Depends on:** —

## `VelocitySystem` is the primary mover of physics entities

Game systems should express motion as writes to `VelocityComponent.Current`;
`VelocitySystem` applies that velocity to `TransformComponent.Position`
each frame. Game systems should not mutate `TransformComponent.Position`
directly on physics entities.

**Why:** writing to `VelocityComponent` is what makes
`TransformComponent.Delta` meaningful for swept collision and what feeds
the resolution systems' impulse computations. Direct position mutation
bypasses both.
**Breaks:** entities teleport instead of move — swept collision sees no
`Delta` and misses contacts; resolution can't reverse the move because
the impulse history is wrong.
**Tests:** none yet (indirectly exercised by `InfiniteRunnerTests`).
**Depends on:** collision — "Swept collision reads `TransformComponent.Delta`".

## A physics body owns its colliders as child entities; it is the collision write-back target

Under the colliders-as-entities model a collider is its own entity (a shape + its own
`TransformComponent`), typically a `ChildOf` child of the physics BODY — the entity
carrying `RigidBodyComponent`/`VelocityComponent`. `VelocitySystem` moves the BODY; its
collider children ride it via the world-matrix cascade, so a collider child's own local
`Delta` is ~0. Collision detection resolves each collider's body with
`ColliderBody.Resolve` (nearest `RigidBody` ancestor, else `VelocityComponent`, else the
collider itself), reads the swept movement from the BODY's `Delta`, and resolution writes
the position/velocity correction back to the BODY. A body with no collider children of its
own (the collider on the same entity) is its own body — the pre-CE flat case, unchanged.

**Why:** motion and mass live on the body; the collider only describes where the body is.
Keeping `VelocityComponent` on the body (not the collider child) is what makes
`ColliderBody.Resolve` find it and what keeps the correction on the mover (collision
pre-mortem #1). `GravitySystem`/`VelocitySystem` query the body's components directly, so
a collider child never needs them.
**Breaks:** putting `VelocityComponent` on a collider child instead of the body makes the
child its own body — resolution then corrects the child (it drifts inside its parent) and
gravity pulls the collider, not the body.
**Tests:** `MonoDreams.Tests/Collision/ColliderEntityTests.cs` (body resolution matrix +
resolution-corrects-the-body); `MonoDreams.Tests/LevelEditor/IslandMilestoneTests.cs`
(a player body with a child collider walks the island).
**Depends on:** collision — "A collider's body is resolved via `ColliderBody.Resolve`";
collision — "Resolution corrects the BODY's Transform/Velocity, never the collider child".

## `RigidBodyComponent.FreezePositionX/Y` and `FreezeRotation` are NOT yet honored by resolution

The freeze flags exist on `RigidBodyComponent` and are *intended* to be the single
source of truth for "this axis doesn't move," but **no system reads them today** —
neither `TransformCollisionResolutionSystem` nor
`TransformPhysicalCollisionResolutionSystem`, nor any `TransformComponent` mutator,
consults them (they call `SetPositionX/Y` / `Translate` unconditionally). Setting a
freeze flag currently has no effect: a contact that pushes along a "frozen" axis still
moves the entity. Treat this as an unimplemented contract, not a guarantee.

**Why:** the flags were added ahead of the resolution support that would honor them —
the field set is the intended API, the read side is the gap.
**Breaks:** a dev sets `FreezePositionX` expecting the axis to be pinned during
resolution; the entity moves anyway, and the bug reads like a resolution error rather
than a missing feature.
**Tests:** none yet — the gap itself is unguarded. Once the read side lands, a test
should assert a frozen axis is not displaced by a contact.
**Depends on:** collision (the resolution systems are where the freeze-flag read would live).

## `VelocityComponent.Delta` is `Current - Last`, updated by `VelocitySystem`

`VelocityComponent.Delta` reports the change between the current and
previous velocity. `VelocitySystem` updates `Last` from `Current` at the
end of its pass. Systems reading `Delta` get the previous frame's
effective velocity change.

**Why:** `Delta` is useful for impulse-based behaviors (a character
that reacts to its own deceleration, dampening systems that fade
energy). The "end of pass" update keeps `Delta` meaningful for the
next frame regardless of what else writes to `Current` mid-frame.
**Breaks:** code that reads `Delta` before `VelocitySystem` runs sees
last frame's value; code that reads after sees zero (or whatever fresh
zero-update produces). Both are valid mid-frame; the bug is reading
without understanding which side of the update you're on.
**Tests:** none yet.
**Depends on:** —

## `RigidBodyComponent.IsKinematic` selects the resolution path

`TransformCollisionResolutionSystem` resolves every `CollisionMessage` it
receives; `TransformPhysicalCollisionResolutionSystem` is the same
move-and-stop response narrowed to `CollisionType.Physics` messages (neither
applies impulses, and neither reads `Mass` — see collision —
"`TransformPhysicalCollisionResolutionSystem` gates on the message TYPE, not
on physics components"). Which resolution system an entity is subject to
is determined by **which resolution systems the screen registers**, not
by the `RigidBodyComponent` flag alone.

**Why:** consistent with ECS purity — systems don't dispatch on entity
state, they query and act. The flag is a hint to the screen which
resolution system to register for the relevant entity stack.
**Breaks:** a dev sets `IsKinematic = false` expecting a physical
response, but the screen only registers the kinematic resolution
system; the entity behaves kinematically anyway.
**Tests:** none yet.
**Depends on:** —

## Known limitations (acknowledged gaps)

- **No high-speed velocity cap** — there is no built-in clamp on
  `VelocityComponent.Current` to prevent tunneling regardless of gameplay
  velocity. Gameplay code is responsible for not exceeding reasonable
  speed-to-collider-size ratios. *Velocity-cap safety net is on the
  backlog.*

## Open questions

- **`RigidBodyComponent.Gravity` factor field** — confirmed semantic:
  per-entity gravity scaling. Heavier multipliers fall faster, lighter
  multipliers float. Not yet captured as a premise — promote when more
  gameplay code uses it.
- **`RigidBodyComponent.Mass`** — read by **no system today**: collision
  resolution corrects positions without a mass term, and the only other
  reader is the level-editor serializer round-tripping the field. It is a
  declared-but-unconsumed knob, like the freeze flags. Nothing prevents
  game systems from reading it for mass-dependent gameplay (push strength,
  AI weight class). Acknowledged: usage may expand.

## Aspirational direction

- Velocity-cap safety net to prevent tunneling at extreme speeds.
- Broader use of `RigidBodyComponent.Mass` in gameplay-facing systems
  (knockback, push, AI weight classes).

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `GravitySystem` affects only entities with `RigidBodyComponent` +
  `VelocityComponent` *(indirectly exercised by
  `InfiniteRunnerTests.PlayerFallsOffLeftEdge`)*
- `VelocitySystem` is the primary mover of physics entities
- `RigidBodyComponent.FreezePositionX/Y` and `FreezeRotation` are
  **not yet honored** by resolution (known gap — no read side exists)
- `VelocityComponent.Delta` is `Current - Last`, updated by `VelocitySystem`
- `RigidBodyComponent.IsKinematic` selects the resolution path
