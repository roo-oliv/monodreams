# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Fix: Player child entities receiving movement components (2x speed bug)

## Context
When a child entity (e.g., `Pete-shilhouette`) is placed under Pete in Blender and both belong to the "Player" collection, the child gets physics/movement components it shouldn't have. `MovementSystem` moves both parent and child independently. Since the child's world position = parent position + child local position, and both positions receive the same input delta, the child move...

### Prompt 2

Can you enable or if necessary code a way to visually see the colliders in a debug mode (use different colors for active vs passive colliders)

### Prompt 3

[Request interrupted by user for tool use]

