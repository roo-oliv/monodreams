# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Fix: `contactTime < 0f` filter blocks wall-pinning collisions

## Context

The previous fix moved the `contactTime >= 0f` check from `DynamicRectVsRect` into the detection `Update()` as `contactTime < 0f`. Ghost collisions stopped, but collision resolution is still non-functional — entities pass through walls.

## Root Cause

The `contactTime < 0f` check in detection is **inverted in intent**. It blocks exactly the collisions that keep entities pinned to surfac...

### Prompt 2

It's working now. Checkout to a new branch, commit everything (staging with git add .) and create a Pull Request.

