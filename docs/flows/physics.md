---
flow: physics
covers:
  - MonoDreams/physics/**
sensitive: false
---

# Physics tick

Each frame, force becomes velocity becomes position, in that order, for every entity that
opts in. `GravitySystem` reads each participating entity's `RigidBodyComponent.Gravity`
(active flag + per-entity `factor`) and adds `worldGravity * factor * dT` to
`VelocityComponent.Current.Y`, optionally clamped at a terminal `maxFallVelocity`. Then
`TransformVelocitySystem` (the class behind what the docs call "VelocitySystem") integrates
`Transform.Position += Current * dT` and snapshots `Last = Current` at the end of its pass.
This is **semi-implicit (symplectic) Euler**: gravity must mutate `Current` *before* the
integrator reads it, so the order `GravitySystem → TransformVelocitySystem` within the frame
is load-bearing, not incidental. The module is deliberately decoupled from `collision` — it
never reads colliders — but it produces the two values collision depends on: the post-move
`Transform` (and its `Delta`) that swept detection reads, and the `Current`/`Last` history
that impulse resolution writes back into.

## Entities & lifecycle

A physics entity carries `TransformComponent` + `VelocityComponent`, and `RigidBodyComponent`
if it participates in gravity/resolution. Per frame, in pipeline order:

1. **Gravity** — `GravitySystem` adds to `Current.Y` for entities whose `RigidBodyComponent.Gravity.active` is true (entities without an active rigid body skip this stage; game systems may still write `Current` directly).
2. **Integrate** — `TransformVelocitySystem` advances `Position` by `Current * dT`, then sets `Last = Current`. `Delta` (`Current - Last`) is therefore zero immediately after this pass and reflects last frame's change before it.
3. **Detect/Resolve (downstream, `collision` module)** — swept detection reads `Transform.Delta`; resolution corrects `Position` and may zero/reflect `Current`. (The `RigidBodyComponent` freeze flags are *intended* to constrain this step but are not yet read — see the physics premise.)

Velocity is mutated by many writers in one frame (gravity, input/movement systems, dampening); `Current` is the shared accumulator, and only `TransformVelocitySystem` advances `Last`.

## Invariants

Authoritative list in [`MonoDreams/physics/docs/premises.md`](../../MonoDreams/physics/docs/premises.md); the ones this flow's ordering leans on:

- `GravitySystem` runs before `TransformVelocitySystem` (semi-implicit Euler). Reorder and gravity lags one frame.
- Motion on physics entities is expressed as writes to `VelocityComponent.Current`, **never** direct `Transform.Position` mutation — direct mutation teleports, leaving `Delta` empty so swept collision misses the contact and resolution can't reverse it.
- Freeze flags (`FreezePositionX/Y`, `FreezeRotation`) are **not yet consumed** by resolution — setting one has no effect today (known gap, see physics premise); don't rely on them to pin an axis.
- `Last` is advanced by `TransformVelocitySystem` only; readers of `Delta` must know which side of that pass they're on.

## Load-bearing quantities

- `worldGravity` — acceleration, units/s²; multiplied per-entity by `RigidBodyComponent.Gravity.factor` (dimensionless). Effective gravity = `worldGravity * factor`.
- `maxFallVelocity` — terminal-velocity cap on `Current.Y`, units/s. **Only applied when `> 0`**; the default `0` means *uncapped*, not *frozen*.
- `Current * state.Time` — `state.Time` is the frame `dT` in seconds; `Current` is units/s, so the product is a position delta. A change that feeds a non-`dT` value here silently rescales all motion.
- `Delta = Current - Last` — per-frame velocity change, consumed by impulse/dampening behaviors.

## Failure modes

- **Teleport instead of move** — a system sets `Transform.Position` directly on a physics entity. Swept collision sees no `Delta`, the entity tunnels through colliders, and resolution has no impulse history to reverse. Highest-frequency real bug in this flow.
- **One-frame-late gravity** — gravity stage reordered after integration; fall feels mushy and contact timing drifts. Subtle, survives casual testing.
- **Frozen-axis vibration** — a movement system pushes a frozen axis; resolution reverts it every frame; the entity jitters and burns CPU.
- **Uncapped fall read as capped** — code assumes `maxFallVelocity` is enforced but the system was constructed with the default `0`; entities accelerate without bound and overshoot thin colliders.
