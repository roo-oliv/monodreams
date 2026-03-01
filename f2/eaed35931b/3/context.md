# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Rewrite ColliderDebugSystem for per-frame collider visualization

## Context
We need visual debug overlays for colliders (BoxCollider + ConvexCollider) to diagnose collision issues at a glance. A `ColliderDebugSystem` already exists but is non-functional: it only fires on component addition (subscription-based), creates static entities that don't follow moving entities, and doesn't set `Visible` (required for Main render target). It needs a full rewrite.

## Appr...

### Prompt 2

Make a way to enable disable this and other debugging features via the ImGUI debugging UI that exists for tracking entities and their components and shows when you hit F1.

### Prompt 3

[Request interrupted by user for tool use]

