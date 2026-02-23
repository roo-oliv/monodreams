# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Convex Polygon Collision Detection & Resolution

## Context

The engine currently only supports AABB collision via `BoxCollider`. The goal is to
add arbitrary convex polygon collision support so entities can have triangular, hexagonal,
or any convex shape as colliders — enabling sloped surfaces, angled walls, irregular
platforms, etc.

javidx9's implementation demonstrates two approaches: SAT and Diagonals. After analysis,
**SAT (Separating Axis Theorem)** is t...

