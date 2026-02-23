# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Plan: Blender Parent-Child Export and Collection Hierarchy

## Context

The Blender level exporter currently creates flat, independent entities — no parent-child
relationships are expressed in the exported JSON. The engine already has a working hierarchy
system (`ChildOf` + `HierarchySystem` + `Transform.Parent`), so we just need to bridge the
gap in the export/import pipeline.

Additionally, draw layer resolution currently requires manual `WithAlias("NPC", Cha...

### Prompt 2

Checkout a new branch, commit, push and open a pull request

