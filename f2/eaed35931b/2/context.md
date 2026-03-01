# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Fix NPCInteractionSystem to Support ConvexCollider

## Context

The Blender pipeline replaces `BoxCollider` with `ConvexCollider` on entities that have a collider child mesh (`*-collider`). The Player entity goes through this path, so it ends up with a `ConvexCollider` instead of a `BoxCollider`. However, `NPCInteractionSystem` hardcodes `BoxCollider` in its ECS queries — when the player has a `ConvexCollider`, it silently drops out of the query and all NPC dia...

