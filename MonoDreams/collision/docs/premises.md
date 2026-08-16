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

## A `ChildOf`-parented collider costs hierarchy work every frame; mass-produced statics go unparented

Parent a collider when it must ride a body or share its lifecycle — `ColliderBody.Resolve`
walks the `ChildOf` chain, so that link IS the model. A collider with no physics ancestor is
already its own body (static geometry, trigger zones), so parenting it buys nothing but cost,
and the cost is per parented entity per frame: `HierarchySystem.Update` makes **three full
passes over the `ChildOf` set** every frame — `DisposeOrphans`, then
`EntityHierarchy.Rebuild` (which also clears and repopulates two dictionaries, allocating a
`List<Entity>` per parent), then `SyncTransformParents` — on top of the **three passes over
the `TransformComponent` set** in `PropagateDirtyFlags` (build the parent→children map,
collect the changed roots, clear every propagation flag). A parented collider is therefore
visited six times a frame plus a dictionary insert; an unparented one, three. That is fine for
a handful and dominant for thousands: when a view's worth of mass-produced tile children
stopped being parented, the measured hierarchy cost fell from **1.42 ms to 0.23 ms per
frame**. So a producer that mass-produces static colliders should spawn them UNPARENTED with
their pose written directly in world space — and then owns their disposal itself, since
nothing will cascade for it. (The live counterweight: `TileGridBakeSystem` still
`SetParent`s its greedy-merged tile colliders to the grid entity, deliberately, so deleting a
grid cascade-disposes them. The merge keeps them few by design, but a large painted world
bakes thousands, so that is a named trade, not a free choice.)

**Why:** the hierarchy passes are unconditional — they cost whether or not anything moved —
and a baked-collider producer is exactly the code that can turn thousands of entities into
`ChildOf` members in one bake. The escape is safe only because a standalone collider needs no
parent to be found by detection (the auto-`ColliderTagComponent` is what detection queries)
and no parent to resolve its body (it resolves to itself).
**Breaks:** parenting mass-produced static colliders puts them in six per-frame set walks and
a per-frame dictionary rebuild, showing up as a flat frame-time tax that profiles inside
`HierarchySystem` and looks nothing like a collision problem. Unparenting them without moving
the disposal responsibility to the producer leaks every collider the producer forgets — there
is no cascade to catch them — and unparenting a collider that was riding a body silently
freezes it in world space while its body moves away.
**Tests:** none yet.
**Depends on:** foundation — "`HierarchySystem` must run ahead of any system reading
WorldPosition" and "Children are disposed with their parents" (the cascade the unparented
producer gives up); this file — "A collider's body is resolved via `ColliderBody.Resolve`"
(why a standalone collider needs no parent); level-editor — "Tile sprites stream per chunk;
colliders bake whole" (the shipping producer, and the sprite side that already took this
escape).

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

## Overlapping bodies depenetrate; only separated ones sweep

Box-vs-box resolution dispatches on `dynamicRect.Intersects(targetRect)` — `CollisionRect`
compares with strict `<`/`>`, so a pair that merely TOUCHES does not intersect. An
already-overlapping pair takes `ResolvePenetration`: translate the BODY along the minimum
translation vector — the shortest of the four exits out of the target rect — and zero the
body's velocity on that axis. A separated or strictly-touching pair takes the swept solve
(`DynamicRectVsRect`, then the correction that lands the collider's centre on the contact
point). Penetration and approach are different problems; only the approach has a meaningful
contact time.

**Why:** a swept solve whose START is inside the target returns a NEGATIVE contact time, and
its contact point then sits arbitrarily far back along the motion — proportional to the
TARGET's size, because the ray is cast against a target expanded by the mover's own extent.
Any overlap (knockback, a spawner or teleport placing a body inside geometry, a corner clip
of one big merged collider) therefore hurls the body through the target and out the far side;
at a world edge that means clean out of the world, where it falls forever. The MTV correction
is independent of both the overlap depth and the target's size, so a body stuck inside
terrain self-heals in a frame or two.
**Breaks:** routing overlap through the sweep reintroduces the teleport-across-terrain bug.
Making touching count as intersecting (relaxing `CollisionRect`'s bounds from strict `<`/`>`
to `<=`/`>=`) routes every resting contact through depenetration and jitters bodies that are
merely standing on the floor. Handling one message twice in a frame also breaks it — see
"Each collision message is handled exactly once per resolution system".
**Tests:** `MonoDreams.Tests/Collision/PenetrationResolutionTests.cs` —
`OverlappingBody_ExitsByTheNearestFace` (all four faces of a big wall),
`OverlappingBody_WithVelocityIntoTheWall_ExitsNearestFace_NotThroughTheFarSide` (the knockback
shape), `TouchingBodyAtRest_IsNotDepenetrated_AndDoesNotJitter`, and
`ApproachingBody_IsBlockedAtTheWallFace_SweepPathUnchanged` (the sweep path unchanged).
**Depends on:** this file — "Resolution corrects the BODY's Transform/Velocity, never the
collider child" (the depenetration write-back lands on the body, exactly like the swept one).

## Each collision message is handled exactly once per resolution system

`TransformCollisionResolutionSystem` annotates its `virtual On(in TCollisionMessage)` with
`[Subscribe]`, and `World.Subscribe(this)` registers every `[Subscribe]` method DefaultEcs
finds walking the type hierarchy. A subclass that filters or extends the handler (as
`TransformPhysicalCollisionResolutionSystem` does for `CollisionType.Physics`) therefore
overrides `On` WITHOUT re-applying `[Subscribe]`: the base registration already dispatches
virtually to the override, so annotating both registers the same handler twice and every
message is resolved twice per frame.

**Why:** resolution is stateful across a frame — each `Resolve*` re-validates against the
CURRENT positions and reads the body's `TransformComponent.Delta`, which by then already
contains the earlier correction. A second pass over the same message therefore re-solves
against a delta that includes its own answer. For a near-face swept block that re-solve is a
zero-length correction (the mover's centre lands exactly on the expanded target's face),
which is why a duplicate registration can hide indefinitely; after a depenetration that exits
ALONG the motion, the same re-solve back-projects the body clean across the target.
**Breaks:** duplicate handling silently undoes depenetration (the body ends up on the far side
of the collider it was just pushed out of), publishes `RigidBodyTouchMessage` twice per
contact, and doubles resolution's per-frame work.
**Tests:** `MonoDreams.Tests/Collision/PenetrationResolutionTests.cs` (every case drives the
shipping `TransformPhysicalCollisionResolutionSystem`, so all of them fail if the override is
re-annotated).
**Depends on:** this file — "Overlapping bodies depenetrate; only separated ones sweep";
"Multi-collider bodies are legal; resolution accumulates sequentially with re-validation".

## `TransformPhysicalCollisionResolutionSystem` gates on the message TYPE, not on physics components

The "Physical" resolution system is a subclass of `TransformCollisionResolutionSystem<CollisionMessage>`
whose *only* difference is its `On` override: it admits a message when
`CollisionMessage.Type == CollisionType.Physics` and drops every other type. The resolution math it
then runs is the base class's, unchanged — a positional correction (swept snap, or shortest-exit
depenetration when the pair already overlaps) plus zeroing the velocity component moving into the
contact when the body has a `VelocityComponent`. There is **no impulse solver and no mass term**:
`RigidBodyComponent` is never read by resolution at all (only by `ColliderBody.Resolve`, to pick the
body), and `RigidBodyComponent.Mass` is not read anywhere in this module. The type is stamped
upstream by the game's `CreateCollisionMessageDelegate` and defaults to `CollisionType.Generic`, so a
game that never classifies its contacts gets *nothing* resolved by this system.

**Why:** the split is a policy filter, not a second solver — which is what makes the same detection
pass serve blocking contacts and non-blocking triggers (a `Dialogue`-typed zone contact reaches the
game's trigger system and is ignored by resolution, so the zone senses without blocking). Documenting
it as "impulse/mass resolution that acts on bodies carrying `RigidBodyComponent` + `VelocityComponent`"
was false on both halves and shipped in `monodreams add collision`'s output (issue #82 review).
**Breaks:** a dev who believes the gate is component-based attaches `RigidBodyComponent` and waits for
blocking that never comes (their messages are still `Generic`); one who believes mass is honored tunes
`Mass` for heavier pushback and sees no effect. Conversely, registering BOTH resolution systems over
one entity stack corrects each `Physics`-typed contact twice — the filter is what keeps them disjoint,
so it only works if exactly one of the two is registered per stack.
**Tests:** `MonoDreams.Tests/Collision/PhysicalResolutionFilterTests.cs` (a fully equipped
RigidBody+Velocity body is ignored at `Generic`/`Collectible`/`Dialogue` and resolved at `Physics`; a
`Physics` contact resolves identically with no `RigidBodyComponent` and at any `Mass`);
`MonoDreams.Tests/Collision/CollisionConsumerAuditTests.cs` —
`RealPipeline_PlayerBodyWithColliderChild_EntersZone_DialogueFires_AndZoneDoesNotBlock` (the
sense-without-blocking consequence over the shipping pipeline).
**Depends on:** this file — "Each collision message is handled exactly once per resolution system"
(the `[Subscribe]`-free override that carries the filter); physics — "`RigidBodyComponent.IsKinematic`
selects the resolution path" (which system a stack is subject to is a registration choice).

## A one-way platform is a resolution FILTER plus a half-thickness collider drop

The engine has no one-way-platform primitive and needs none: the seam already exists as the
resolution system's `protected virtual void On(in TCollisionMessage)` — the same override
`TransformPhysicalCollisionResolutionSystem` uses to admit only `CollisionType.Physics`
contacts. Game code builds a one-way platform by subclassing a resolution system (or by
classifying upstream in its `CreateCollisionMessageDelegate`) and admitting a platform contact
only when BOTH conditions hold: the body is **falling** (`VelocityComponent.Current.Y > 0` —
y grows down) and its feet were **above the platform's top face** before the motion (the
body's collider bottom versus `SATCollision.BoxWorldRect(plate…).Top`, backed out by the
body's `TransformComponent.Delta` to get the pre-move edge). A rejected message is simply not
added to `Collisions`, so the body passes through, upward or sideways, with no correction. The
override must NOT re-apply `[Subscribe]` — see "Each collision message is handled exactly once
per resolution system".

**And the plate's collider must be dropped by half its thickness.** A `BoxColliderComponent`
is CENTERED on its collider entity's `WorldPosition` (there is no offset field), so a collider
entity placed on the visible surface line puts the box's top face half a thickness ABOVE the
art. The body is then stopped short of the pixels it should stand on, and — worse — while
rising through the plate its feet are inside the box, which the falling-and-above test can read
as a legitimate landing on the next descending frame. Offset the collider entity DOWN by half
the box's effective world-space thickness so the top face is flush with the surface the player
sees — `Size.Y / 2` for an unscaled collider; `SATCollision.BoxWorldRect` multiplies `Size` by
`WorldScale`, so a scaled plate drops by `Size.Y * WorldScale.Y / 2`.

**Why:** one-way behaviour is a *policy* about which contacts count, not a new geometry or a
new component, so it belongs in the resolver's message filter — the framework's
general-over-specialized rule. Putting it anywhere else (a flag consulted inside
`ResolveBoxVsBox`, a second resolution system) forks a path every game would then have to
opt out of. The half-thickness drop is a consequence of the centered-box model, not of the
filter: it applies to any thin plate authored against a visible surface line.
**Breaks:** filtering on "moving downward" alone lets a body that clipped a corner get snapped
up onto the plate from below; filtering on "feet above the top" alone re-blocks a body jumping
through (its feet cross the line mid-arc). Re-annotating the override with `[Subscribe]`
registers the handler twice and every admitted contact resolves twice. Centring the collider
on the surface line leaves the box's top face standing half a thickness proud of the art, so
the body is blocked in the air above the surface and its feet occupy that band while rising —
which is what makes a thin plate read as a wall instead of a floor ("I should land on it and
instead I bounce off nothing above it").
**Tests:** none yet (no one-way platform ships in the engine or its reference games; the
filter seam itself — the `On` override — is covered by
`MonoDreams.Tests/Collision/PenetrationResolutionTests.cs`, which drives
`TransformPhysicalCollisionResolutionSystem`).
**Depends on:** this file — "Each collision message is handled exactly once per resolution
system" (the override seam and its `[Subscribe]` trap); "A collider IS an entity
(colliders-as-entities)" (the centered `Size`, which is why the drop is needed); "Resolution
corrects the BODY's Transform/Velocity, never the collider child" (the delta and velocity the
filter reads belong to the body); "Overlapping bodies depenetrate; only separated ones sweep"
(a body admitted while already overlapping the plate takes the depenetration path, not the
sweep).

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

## The collision module compiles against `physics`, and `module.json` declares it

`collision` has a **hard, compile-time** dependency on the `physics` module: two files open
`MonoDreams.Component.Physics`. `ColliderBody.cs` reads `RigidBodyComponent` and
`VelocityComponent` as the markers body resolution walks for, and
`TransformCollisionResolutionSystem.cs` reads `VelocityComponent` to zero the velocity of the
body it just corrected. That is the entire surface — no collision code reads
`RigidBodyComponent` outside `ColliderBody`, and none reads `RigidBodyComponent.Mass` at all —
but two `using` directives are already enough to break the build. So
`MonoDreams/collision/module.json` lists `physics` in `dependencies`, and `monodreams add
collision` installs `physics` with it. The coupling is *compile*-time, not *pipeline*-time:
installing `physics` does not mean registering `GravitySystem`/`VelocitySystem`. A
trigger-only game registers neither, carries the physics source dormant, and still compiles —
which is why the split into two modules stays worth having.

**Why:** the engine ships shadcn-style, so a manifest is a recipe a stranger cooks on a
machine that has nothing else installed. Every dev machine has all 14 modules present, so an
undeclared cross-module `using` can never fail locally — it fails on a fresh user's first
`dotnet build`, with an error naming a namespace from a module they never installed. This
manifest claimed `foundation` only and its `description` advertised a "soft" couple to
physics, so `monodreams add collision` produced a project that did not compile (issue #82).
**Breaks:** dropping `physics` from `dependencies` (or adding a new cross-module `using` to
collision source without adding the module that owns it) silently reintroduces the same
first-run build failure — invisible in this repo, fatal for a user.
**Tests:** `MonoDreams.Cli.Tests/CollisionModuleRegistryTests.cs` (declared deps, resolver
order, and a source scan asserting every engine namespace collision imports is covered by a
declared dependency) and
`MonoDreams.Cli.Tests/ScaffolderBuildTests.cs::Init_ThenAddCollision_InstallsPhysicsAndBuilds`
(scaffold + `add collision` + `dotnet build`).
**Depends on:** physics — "`GravitySystem` affects only entities with `RigidBodyComponent` +
`VelocityComponent`" (the body markers); this file — "A collider's body is resolved via
`ColliderBody.Resolve`".

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
**Tests:** `MonoDreams.Tests/Collision/ColliderEntityTests.cs::CollisionMessage_CarriesColliderAndBody_ForBothSides`
(the message shape); `MonoDreams.Tests/Collision/CollisionConsumerAuditTests.cs` — the **completed
consumer audit** (CE-D, pre-mortem #4): each shipping consumer is wired and asserted to read the correct
side — `GameCollisionHelper` (identity collider-first-then-body), `ZoneDialogueTriggerSystem` (`ColliderB`,
+ a negative that it never falls back to `BodyB`), `RunnerCollisionHandlerSystem` (`BodyA` state / whole
`BodyB` dispose), `NPCInteractionSystem` (the collider-CHILD proximity path); the resolution systems are
covered by `ColliderEntityTests`, and the physics-demo `BallBounceSystem` (collider==body) by
`MonoDreams.Tests/IntegrationTests/HeadlessDemoTests.cs::HeadlessPhysicsDemo_…`. The milestone suites
(`IslandMilestoneTests`, `TriggerPlacementTests`, `PrefabMilestoneTests`) additionally exercise the
identity end-to-end through the real pipeline.
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
- A `ChildOf`-parented collider costs hierarchy work every frame
- A one-way platform is a resolution FILTER plus a half-thickness collider drop

The `BroadPhaseAABB`, the colliders-as-entities model, body resolution, the write-back
rule, multi-collider bodies, the four-entity message, and the perf smoke now carry test
protection (`SATCollisionTests`, `ColliderEntityTests`, the milestone suites). The
pipeline-order premise is the highest-leverage remaining gap — an architectural test would
catch screens that omit `TransformCommitSystem`.
