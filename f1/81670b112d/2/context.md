# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Fix Collision Detection Bugs (Movement Snapping & Bounding Box Misalignment)

## Context

When colliding with obstacles in Level 2, the player experiences movement "snapping" (getting locked to one axis while touching certain edges) and visual misalignment between debug bounding boxes and actual collision behavior. Three interrelated bugs cause these issues.

## Root Cause Analysis

### Bug 1: Integer truncation in collision rectangle positioning
`RectangleExtens...

### Prompt 2

Well, snapping is gone but so it collision resolution. Detection seems to work but it identifies ghost collisions (at 0;0) as well: Collision detected: Entity 2:15.0 vs Entity 2:11.0 at {X:0 Y:0}
Collision detected: Entity 2:11.0 vs Entity 2:15.0 at {X:0 Y:0}
Collision detected: Entity 2:15.0 vs Entity 2:11.0 at {X:0 Y:0}
Collision detected: Entity 2:11.0 vs Entity 2:15.0 at {X:0 Y:0}
Collision detected: Entity 2:15.0 vs Entity 2:11.0 at {X:0 Y:0}
Collision detected: Entity 2:11.0 vs Entity 2:15...

### Prompt 3

[Request interrupted by user for tool use]

