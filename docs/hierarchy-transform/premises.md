# Hierarchy & Transform — premises

> Technical invariants the engine assumes about hierarchy and transform.
> Each entry: title, paragraph, **Why** / **Breaks** / **Tests** / **Depends on**.
> Aspirational items at the bottom (intended end-state, not yet enforced).

## Don't mix two Transform-shaped components in one project

A project may define its own `MyTransform` with different trade-offs, but
mixing `Transform` and a custom transform in the same world breaks the
framework's bundled consumers (collision, rendering, camera, culling),
which all read `Transform` directly.

**Why:** the bundled modules currently couple to `Transform` by type
rather than by shape (see Aspirational below). Until that loosens,
consistency within a project is the only thing preventing silent
breakage in modules the developer didn't write.
**Breaks:** colliders, cameras, and the render pipeline silently
operate on the framework `Transform` while game systems mutate the
custom one. Entities appear in the wrong place, collide based on stale
positions, or fail to render.
**Tests:** none yet.
**Depends on:** —

## `Transform.Delta` is meaningful only after `TransformCommitSystem` ran

`Transform.HasMoved` and `Transform.Delta` reflect the change between
two committed positions. They are valid only after
`TransformCommitSystem` ran for the *previous* frame. Reading them
mid-frame or before commit returns stale data with no error or warning.

**Why:** the collision system's swept tests depend on a meaningful
`Delta`. The framework intends to enforce consistency via interaction
methods on `Transform`, but does not today — `Delta` is a public field.
**Breaks:** swept collision detection sees an empty or wrong `Delta`
and misses dynamic contacts; objects pass through walls. With no
warning, the dev hunts for hours in the collision system before
finding the missing `TransformCommitSystem` in the pipeline.
**Tests:** none yet (indirectly exercised by
`InfiniteRunnerTests.PlayerFallsOffLeftEdge`, which depends on swept
collision working).
**Depends on:** Collision — "Swept collision reads `Transform.Delta`".

## `Transform.IsDirty` cascades through the parent chain

Mutating a parent's position, rotation, or scale marks every descendant
dirty. `WorldMatrix` is a cached property whose getter re-walks the
chain when next read.

**Why:** caching the world matrix avoids recomputing it on every read,
which would dominate hot paths in deep hierarchies (UI layouts,
nested entities).
**Breaks:** if a system somehow bypasses the dirty flag (e.g., mutates
internal fields directly), descendants render and collide at stale
world positions while their parents have moved.
**Tests:** none yet.
**Depends on:** —

## `ChildOf` and `Transform.Parent` are two intentional links

`Transform.Parent` is the matrix link — it controls how `WorldMatrix`
cascades. `ChildOf` is the structural link — it controls lifecycle
(cascade disposal). `HierarchySystem` syncs both. Hierarchy logic must
read the link relevant to its concern.

**Why:** the split came from hierarchical UI like a dialogue panel,
where a banner, avatars, text, and a waiting-indicator move and
dispose together but may not share matrix scaling. The split is a
known wart and is on the refactor backlog (consolidation desired).
**Breaks:** code that reads only `Transform.Parent` misses the
disposal cascade; code that reads only `ChildOf` misses the matrix
behavior. A future consolidation will collapse both into one concept.
**Tests:** none yet.
**Depends on:** —

## `HierarchySystem` must run ahead of any system reading WorldPosition

Per the ECS-purity tenet, systems are pure functions and ordering is
the screen's responsibility. The reference pipeline places
`HierarchySystem` after physics (so parent-child movement is composed
from the latest local positions) and before camera/render/culling.

**Why:** any system reading `Transform.WorldPosition` /
`WorldRotation` / `WorldScale` gets the cached world transform; the
cache is only fresh after `HierarchySystem` has processed dirty
descendants this frame.
**Breaks:** a child entity renders at last frame's world position, a
follow-camera tracks a stale target, a collider tests against stale
world vertices.
**Tests:** none yet.
**Depends on:** Rendering — "Rendering systems run last in the
pipeline".

## Children are disposed with their parents

`HierarchySystem` cascade-disposes any entity whose parent (via
`ChildOf`) has been disposed. There is no supported way today for a
child to outlive its parent.

**Why:** complex hierarchical entities (dialogue UI, composed
characters) need a single lifecycle handle. Cascade disposal makes
this the default.
**Breaks:** a system that disposes a parent without `ChildOf`-linked
children expects the children to die; if `ChildOf` is missing on a
visually-attached child, the child orphan-renders at world origin.
**Tests:** none yet.
**Depends on:** —

## `WorldMatrix` is cached and computed lazily

`Transform.WorldMatrix` is a cached property. The getter walks the
parent chain on demand only when the cached value is dirty; otherwise
it reuses the cached matrix.

**Why:** matrix recomputation is the dominant cost of deep
hierarchies; caching cuts it to once per dirty span per frame.
**Breaks:** any logic that bypasses the cache (recomputes the matrix
manually or mutates `Transform` fields without flagging dirty) breaks
the contract — downstream systems read a stale `WorldMatrix` without
knowing.
**Tests:** none yet.
**Depends on:** —

## Open questions

- **Entity disposed mid-frame:** convention not yet established —
  what happens if a system queries an entity after a prior system
  disposed it this frame? *Status: open; settle when use case appears.*
- **Mid-frame re-parenting:** if a system re-parents an entity (changes
  `Transform.Parent` or `ChildOf`), do consumers in the same frame see
  the new parent or the old one? Behavior depends on
  `HierarchySystem`'s position in the pipeline.

## Aspirational direction

- Consolidate `ChildOf` and `Transform.Parent` into one hierarchical
  concept.
- `Transform` exposes interaction methods that enforce `Delta`
  consistency, so `Delta` is meaningful when read regardless of
  pipeline order.
- Collision, rendering, camera, and culling decouple from `Transform`
  specifically and operate against any Transform-shaped contract.

## Follow-up debt

The following premises currently have **Tests: none yet** — they are
documented but not programmatically protected:

- Don't mix two Transform-shaped components in one project
- `Transform.Delta` is meaningful only after `TransformCommitSystem` ran
- `Transform.IsDirty` cascades through the parent chain
- `ChildOf` and `Transform.Parent` are two intentional links
- `HierarchySystem` must run ahead of any system reading WorldPosition
- Children are disposed with their parents
- `WorldMatrix` is cached and computed lazily

Architectural tests (ArchUnit-style) protecting these are on the
engine backlog.
