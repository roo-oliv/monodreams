# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Fix: Dialogue text invisible (drawn behind box)

## Context

After the CullingSystem and JustPressed fixes, the dialogue box now renders at the bottom of the screen. However, the text is invisible — it's being drawn **behind** the box due to inverted LayerDepth values.

## Root Cause

`MasterRenderSystem` (`MasterRenderSystem.cs:88-95`) sorts all entities by ascending LayerDepth — lower values draw first (behind), higher values draw on top.

Current values in...

### Prompt 2

Commit this to a new branch and open a Pull Request

