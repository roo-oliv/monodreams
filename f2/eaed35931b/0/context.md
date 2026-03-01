# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Fix ConvexCollider misalignment with sprites from Blender pipeline

## Context

The store's ConvexCollider (red debug outline) appears tiny and offset. The previous fix attempt (origin offset) made things worse — the root cause is actually **missing parent Blender scale** on collider child vertices.

### Why the origin offset theory was wrong

Both the sprite and collider are anchored at the entity position (= Blender origin). `SpriteBatch.Draw` uses `SpriteInf...

