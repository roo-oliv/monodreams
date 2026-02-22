# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Fix: Player and NPC not Y-sorted in Blender Level

## Context

When loading Level 2 (Blender level), Player ("Pete") and NPC ("Boldo") are not Y-sorted with each other. The root cause is a name mismatch: `BlenderLevelParserSystem.ResolveLayerDepth()` tries to match Blender collection names against `DrawLayerMap`, but "Player" and "NPC" don't match any `GameDrawLayer` enum value — the correct one is "Characters". Both entities fall through to `DEFAULT_LAYER_DEPT...

### Prompt 2

commit this

