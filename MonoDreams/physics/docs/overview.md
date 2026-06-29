# physics — overview

Velocity-driven motion with optional gravity, decoupled from collision. Add a `VelocityComponent` and motion is integrated into `TransformComponent.LocalPosition` each frame; add a `RigidBodyComponent` and gravity accumulates into velocity. Install this for anything that moves — players, projectiles, parallax backgrounds, falling decoration.

## Purpose

This module is the engine's source of motion. Gameplay systems express intent as writes to `VelocityComponent.Current`; `VelocitySystem` is the single system that converts intent into position changes; `GravitySystem` adds the universal downward acceleration to anything tagged with `RigidBodyComponent`. The split lets a game use physics *without* collision (parallax backgrounds, particle drift, falling decorative leaves) and exposes freeze flags (`FreezePositionX/Y`, `FreezeRotation`) intended as the single source of truth for "this axis doesn't move" (the read side is not yet implemented — see premises). Without this module, every system that wants to move an entity has to write `TransformComponent.LocalPosition` directly and lose the `Delta` swept-collision input.

## What ships

### Components

- `VelocityComponent` — `Current` (the velocity to apply this frame), `Last` (previous frame's value), `Delta` (`Current - Last`, updated by `VelocitySystem`)
- `RigidBodyComponent` — `Mass`, `IsKinematic`, `Gravity` (per-entity gravity scaling), `FreezePositionX`, `FreezePositionY`, `FreezeRotation`

### Systems

- `GravitySystem` — adds gravitational acceleration to `VelocityComponent.Current` for entities with `RigidBodyComponent` (where gravity is active). Runs after gameplay/input writes velocity, before `VelocitySystem`
- `VelocitySystem` — applies `VelocityComponent.Current` to `TransformComponent.LocalPosition` and updates `Delta`/`Last`. Runs after gameplay writes and before collision detection

## Pipeline wiring

Each frame, in order:

1. Gameplay / input / AI systems write to `VelocityComponent.Current` (e.g., a movement-input system sets `Current.X` based on left/right keys).
2. **`GravitySystem`** accumulates gravity into `Current` for entities with `RigidBodyComponent`.
3. **`VelocitySystem`** integrates `Current` into `TransformComponent.LocalPosition` and bookkeeps `Last`/`Delta`.
4. **Collision systems** (if `collision` is installed) read positions and the resulting `Transform.Delta`/`Velocity.Current`, publish `CollisionMessage`, and may correct positions via the resolution systems.
5. **`HierarchySystem`** (from `foundation`) propagates dirty flags for any moved entity.
6. **`TransformCommitSystem`** (from `foundation`) closes the frame so next frame's `Transform.Delta` is meaningful.

For motion *without* collision: install only this module. `VelocitySystem` writes positions; no collision message is generated.

## Cross-module dependencies

- `foundation` — writes to `TransformComponent.LocalPosition`; relies on `HierarchySystem` and `TransformCommitSystem` to close the frame correctly.

## Extension points

- **Custom gravity sources.** Write a system that reads `RigidBodyComponent` and adds to `VelocityComponent.Current` before `VelocitySystem` runs (e.g., wind, magnetism, planetary gravity). The single rule: write to `VelocityComponent.Current`, not `Transform.LocalPosition` directly.
- **Per-entity drag / damping.** Same pattern — a system that runs before `VelocitySystem` scales `Current` down by a damping coefficient.
- **New `RigidBodyComponent` fields.** Mass is currently consumed by `TransformPhysicalCollisionResolutionSystem`; nothing prevents game systems from reading mass for knockback strength, AI weight classes, etc.

## See also

- [Premises](premises.md) — load-bearing invariants for this module (`VelocitySystem` is the primary mover, `Delta` semantics, freeze flag authority)
- Related modules: `collision` (consumes `Transform.Delta` for swept tests; reads `RigidBodyComponent` for impulse resolution), `foundation` (provides `Transform` and the commit/hierarchy systems physics integrates with)
