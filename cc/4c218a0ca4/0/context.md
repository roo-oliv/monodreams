# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Draw Layer Configuration System

## Context

Layer depths are hardcoded magic numbers scattered across 6+ entity factories (0.1f for player, 0.09f for tiles, 0.991f for orbs, etc.) and the `BlenderLevelParserSystem` uses a single constant (0.5f) for everything. There's no centralized place to see or manage the depth ordering for a game. The existing `DrawLayerDepth` utility (which auto-maps any enum to evenly-spaced floats) is elegant but only used in one place (...

### Prompt 2

git add ., commit, push and open a pull request

