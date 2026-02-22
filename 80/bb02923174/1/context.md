# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Y-Sorting Implementation Plan

## Context

The engine currently assigns a **fixed depth** per draw layer (e.g., Characters = 0.6). All entities on the same layer render at the same depth, with creation order as the tiebreaker. This breaks down in top-down or isometric-style games where entities lower on screen should render in front of entities higher on screen. Y-sorting solves this by dynamically adjusting entity depth within a layer based on Y position.

The c...

### Prompt 2

I want to be able to debug in-game the Y-sorting of each entity. Can you think of a way to accomplish this?

### Prompt 3

[Request interrupted by user for tool use]

