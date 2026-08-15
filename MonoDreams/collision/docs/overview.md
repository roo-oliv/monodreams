# collision — overview

AABB and SAT collision: tag entities with `BoxColliderComponent` or `ConvexColliderComponent`, install the detection system, and pairs of overlapping colliders publish `CollisionMessage` for resolution and game systems to consume. Depends on `physics` at compile time — a collider's *body* is a `RigidBodyComponent`/`VelocityComponent` owner, so `monodreams add collision` installs `physics` too. Which physics *systems* you register stays a pipeline choice: a trigger-only game can skip `GravitySystem`/`VelocitySystem` entirely.

## Purpose

This module adds spatial collision detection and resolution to entities. Two collider types share the same query target (`ColliderTagComponent`) so detection can broadphase-filter both polymorphically; the narrowphase dispatches to AABB-vs-AABB, SAT, or AABB-vs-SAT as needed. Detection emits `CollisionMessage`, and two resolution systems consume them — the same move-and-stop correction, differing only in *which* messages each admits: one takes every message, the other only those tagged `CollisionType.Physics`. Game systems also subscribe to `CollisionMessage` for trigger logic — pickups, doorways, dialogue zones — so the same detection serves both physical and trigger collision without forking the pipeline.

## What ships

### Components

- `BoxColliderComponent` — AABB collider with a centered `Size` (no offset — the collider is its own entity; pose comes from its `TransformComponent`), layers, passive flag
- `ConvexColliderComponent` — SAT convex polygon collider with vertex list and cached `BroadPhaseAABB`
- `ColliderTagComponent` — canonical query target; auto-attached when either collider component is added
- `IColliderComponent` — interface implemented by both colliders for polymorphic narrowphase dispatch

### Systems

- `TransformCollisionDetectionSystem` — single-threaded; queries `ColliderTagComponent`, broadphase-filters on layers + AABB, narrowphase-tests with AABB or SAT, emits `CollisionMessage` per pair. Generic on the message type via `CreateCollisionMessageDelegate`
- `TransformCollisionResolutionSystem` — resolves every `CollisionMessage` it receives: corrects the **body's** position (swept snap, or shortest-exit depenetration when the pair already overlaps) and zeros the velocity moving into the contact when that body has a `VelocityComponent`. No impulses, no `Mass`
- `TransformPhysicalCollisionResolutionSystem` — the same resolution narrowed by message *type*: a subclass whose only override admits a message when `CollisionMessage.Type == CollisionType.Physics`. The type is game-set in the `CreateCollisionMessageDelegate` (default `CollisionType.Generic`, which this system ignores), so "Physical" names the filter, not a different solver

### Messages

- `CollisionMessage` — the two collider entities (`ColliderA`/`ColliderB`) AND their resolved bodies (`BodyA`/`BodyB`, via `ColliderBody.Resolve`), contact point, normal, contact time, penetration, layer, type
- `ColliderBody` — the shared body-resolution helper (nearest `RigidBody` ancestor, else `Velocity`, else the collider itself)
- `ICollisionMessage` — interface for custom message types (extend with game-specific fields like damage, knockback)
- `RigidBodyTouchMessage` — emitted when rigid bodies make contact (for sound, particles, etc.)

### Utilities

- `Extensions/Monogame/SATCollision.cs` — narrowphase SAT primitives (`StaticConvexVsConvex`, `DynamicConvexVsConvex`, etc.)
- `Extensions/Monogame/CollisionRect.cs` — AABB helpers

## Pipeline wiring

1. Attach `BoxColliderComponent` or `ConvexColliderComponent` to the entity. The `ColliderTagComponent` marker is auto-applied; don't add or remove it manually.
2. In your update pipeline, register these in order **after** `VelocitySystem` (from `physics`) and **before** `TransformCommitSystem` (from `foundation`):
   - **`TransformCollisionDetectionSystem`** — queries colliders, computes overlaps, publishes `CollisionMessage`.
   - **`TransformCollisionResolutionSystem`** — corrects positions and clips velocity for every message.
   - **`TransformPhysicalCollisionResolutionSystem`** — the same correction, admitting only `CollisionType.Physics` messages. Register **one** of the two per entity stack, not both: each handles a given message once, so a `Physics`-tagged contact reaching both is corrected twice.
3. In game systems, subscribe to `CollisionMessage` for trigger logic — pickups, doorways, dialogue zones.

The reference pipeline order is **Movement → Velocity → Detection → Resolution → Commit**; skipping or reordering silently degrades collision quality (most commonly: missing `TransformCommitSystem` produces no `Transform.Delta`, so swept tests miss fast-moving contacts).

Layer-based filtering on `BoxColliderComponent.ActiveLayers` / `ConvexColliderComponent.ActiveLayers` is the coarse first cut — two colliders with non-overlapping layer sets are never tested against each other.

## Cross-module dependencies

- `foundation` — reads `TransformComponent.Position` for AABB world bounds and `Transform.Delta` for swept (CCD-style) tests.
- `physics` — a **hard, compile-time** dependency, declared in `module.json`. Two files `using MonoDreams.Component.Physics`: `ColliderBody.Resolve` reads `RigidBodyComponent` and `VelocityComponent` as the markers that pick a collider's body, and `TransformCollisionResolutionSystem` reads `VelocityComponent` to zero the velocity of the body it just corrected. That is the whole surface — no collision code reads `RigidBodyComponent` outside `ColliderBody`, and none reads `Mass` at all — but it is enough that the collision source does not compile without the `physics` module present. Installing it is not the same as running it: a trigger-only game registers no physics system and pays only the dormant source.

## Extension points

- **Custom collision messages.** Implement `ICollisionMessage` with your own fields (damage, knockback strength, sound cue ID) and pass a `CreateCollisionMessageDelegate` to `TransformCollisionDetectionSystem`'s constructor. The base fields stay the contract; custom fields are additive.
- **Custom layer schemes.** `ActiveLayers` is a bitmask; define your game's layer enum and bitwise-or the membership flags.
- **Trigger zones.** A non-physical collider just emits messages — subscribe in a game-specific system and act (pickup, zone entry, dialogue start). No resolution needed.
- **New collider shapes.** Extending `IColliderComponent` would also require adding a narrowphase test in `TransformCollisionDetectionSystem`. Not exercised today; circle colliders are the obvious candidate.

## See also

- [Premises](premises.md) — load-bearing invariants for this module (`ColliderTagComponent` canonical query, swept-collision `Delta` dependency, single-threaded detection, the reference pipeline order)
- Related modules: `physics` (declared dependency — supplies the `RigidBodyComponent`/`VelocityComponent` body markers this module compiles against), `foundation` (provides `Transform.Delta` via `TransformCommitSystem`), `debug` (`ColliderDebugSystem` overlays the collider shapes for visual debugging)
