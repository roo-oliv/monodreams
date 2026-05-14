# Physics — premises

> Technical invariants the engine assumes about physics components and
> systems. Each entry: title, paragraph, **Why** / **Breaks** /
> **Tests** / **Depends on**. Aspirational items at the bottom.

## `GravitySystem` affects only entities with `RigidBody` + `Velocity`

`GravitySystem` queries on entities that have both `RigidBody` (with
gravity active) and `Velocity`. Missing either component means gravity
does not apply.

**Why:** gravity needs both a participant flag (`RigidBody.Gravity`)
and a target field to write to (`Velocity.Current`). Falling back to
defaults would hide configuration bugs where a dev expected gravity
but never added the required components.
**Breaks:** a "floating" entity that was supposed to fall sits still;
the dev assumes gravity is broken, when in fact one of the two
components is missing.
**Tests:** `InfiniteRunnerTests.PlayerFallsOffLeftEdge` exercises this
integration indirectly (player has both components and falls as
expected).
**Depends on:** —

## `VelocitySystem` is the primary mover of physics entities

Game systems should express motion as writes to `Velocity.Current`;
`VelocitySystem` applies that velocity to `Transform.Position` each
frame. Game systems should not mutate `Transform.Position` directly
on physics entities.

**Why:** writing to `Velocity` is what makes `Transform.Delta`
meaningful for swept collision and what feeds the resolution systems'
impulse computations. Direct position mutation bypasses both.
**Breaks:** entities teleport instead of move — swept collision sees
no `Delta` and misses contacts; resolution can't reverse the move
because the impulse history is wrong.
**Tests:** none yet (indirectly exercised by `InfiniteRunnerTests`).
**Depends on:** Collision — "Swept collision reads `Transform.Delta`".

## `RigidBody.FreezePositionX/Y` and `FreezeRotation` are honored by resolution

The resolution systems read the freeze flags and zero out the
corresponding correction. Movement systems that mutate a frozen axis
directly will desync from resolution, because resolution will keep
reverting it.

**Why:** freeze flags are the single source of truth for "this axis
doesn't move." Splitting that authority between game code and the
flag produces inconsistent behavior depending on which system ran last.
**Breaks:** a game system pushes a frozen entity along the frozen
axis; resolution un-pushes it; the entity vibrates or stays put with
wasted CPU.
**Tests:** none yet.
**Depends on:** —

## `Velocity.Delta` is `Current - Last`, updated by `VelocitySystem`

`Velocity.Delta` reports the change between the current and previous
velocity. `VelocitySystem` updates `Last` from `Current` at the end of
its pass. Systems reading `Delta` get the previous frame's effective
velocity change.

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

## `RigidBody.IsKinematic` selects the resolution path

`TransformCollisionResolutionSystem` is the lighter, kinematic-style
response (move-and-stop without mass effects).
`TransformPhysicalCollisionResolutionSystem` is the mass- and
velocity-aware response. Which resolution system an entity is subject
to is determined by **which resolution systems the screen registers**,
not by the `RigidBody` flag alone.

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
  `Velocity.Current` to prevent tunneling regardless of gameplay
  velocity. Gameplay code is responsible for not exceeding
  reasonable speed-to-collider-size ratios. *Velocity-cap safety net
  is on the backlog.*

## Open questions

- **`RigidBody.Gravity` factor field** — confirmed semantic: per-entity
  gravity scaling. Heavier multipliers fall faster, lighter
  multipliers float. Not yet captured as a premise — promote when
  more gameplay code uses it.
- **`RigidBody.Mass`** — used by `…PhysicalCollisionResolutionSystem`
  today. Nothing prevents other systems from reading it for
  mass-dependent gameplay (push strength, AI weight class).
  Acknowledged: usage may expand.

## Aspirational direction

- Velocity-cap safety net to prevent tunneling at extreme speeds.
- Broader use of `RigidBody.Mass` in gameplay-facing systems
  (knockback, push, AI weight classes).

## Follow-up debt

The following premises currently have **Tests: none yet**:

- `GravitySystem` affects only entities with `RigidBody` + `Velocity`
  *(indirectly exercised by `InfiniteRunnerTests.PlayerFallsOffLeftEdge`)*
- `VelocitySystem` is the primary mover of physics entities
- `RigidBody.FreezePositionX/Y` and `FreezeRotation` are honored by
  resolution
- `Velocity.Delta` is `Current - Last`, updated by `VelocitySystem`
- `RigidBody.IsKinematic` selects the resolution path
