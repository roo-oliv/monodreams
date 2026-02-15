# Session Context

## User Prompts

### Prompt 1

[Request interrupted by user for tool use]

### Prompt 2

Implement the following plan:

# Fix CullingSystem — Entities Never Culled After Becoming Visible

## Context

CullingSystem correctly adds/removes the `Visible` tag on entities as they
enter/leave camera bounds. However, entities that leave the bounds remain
rendered on screen. The debug green rectangle shows the correct boundary, but
sprites beyond it are still drawn.

## Root Cause

`MasterRenderSystem` implements `ISystem<GameState>` (not
`AEntitySetSystem<GameState>`). The `[With(typeof(D...

### Prompt 3

Ok, this seems to work better, just one issue: on the main level selection screen now I don't see any of the buttons (they're still clickable though)

### Prompt 4

[Request interrupted by user for tool use]

