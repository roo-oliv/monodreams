# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Plan: Y-Sort Depth Bias for Parent/Child Rendering Order

## Context

Rotation wobble is already implemented. `elephant-kid` and `elephant-kid-shilhouette` share the same world Y position (child inherits parent's transform), so `YSortSystem` computes the same interpolated depth for both. The only tiebreaker is entity creation index in `MasterRenderSystem` (`.ThenBy(x => x.index)`), which depends on Blender export order — fragile and implicit.

We can't use `Get...

### Prompt 2

Checkout to a new branch, commit, push and open a pull request

