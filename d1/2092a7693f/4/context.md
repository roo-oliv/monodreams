# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Fix: Y-sort parent-child gap still allows entities to slip between parent and child

## Context

The PostUpdate pass in `YSortSystem` (added in the previous iteration) correctly derives the
child's depth from the parent's final depth. However, **it still uses the original
`YSortDepthBias` value (-0.0005f) as the offset**, creating a gap of 0.0005 in depth space.

### Why the gap is too large

The Characters Y-sort range spans 0.098 (from `DrawLayerMap`: 11 enum m...

