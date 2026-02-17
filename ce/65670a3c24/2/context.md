# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Fix: Player movement broken on Blender levels after refactor

## Context

After the `ro/core-examples-alignment` refactor, clicking Level 2 (a Blender level) loads all entities visually, but the player doesn't respond to WASD/arrow keys.

**Root cause**: When `BlenderLevelParserSystem` was moved from Examples → core, two game-specific components were dropped from the "Player" collection handler:
- `PlayerState` (lives in `MonoDreams.Examples.Component` — core...

