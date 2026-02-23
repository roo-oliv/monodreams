# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Plan: Add Rotation Wobble to Character Entities

## Context

Add a discrete two-state rotation wobble to all entities in the Characters collection (both NPC and Player subcollections). Every 2 seconds, entities snap between a rotated state (+2° clockwise) and their base rotation. Entities ending with "shilhouette" rotate counterclockwise instead, with parent compensation since `elephant-kid-shilhouette` is a child of `elephant-kid`.

**Rotation pipeline status**...

### Prompt 2

Also, can you think a way for the base entity always be on top of the shilhouette counterpart when it exists? But we still want those to be YSorted with the rest.

### Prompt 3

[Request interrupted by user for tool use]

