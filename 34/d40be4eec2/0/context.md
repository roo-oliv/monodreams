# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Plan: Review PR #21

## Context
PR #21 ("Add rotation wobble and Y-sort depth bias") introduces two features:
1. A `RotationWobble` component + system that snaps entity rotation between two states on a global timer
2. A `YSortDepthBias` field on `SpriteInfo` for deterministic front/back ordering of entities at the same Y

The user wants a review comment posted via `gh` (comment only, no approve/request-changes).

## Review findings

### Positive
- Component/syste...

