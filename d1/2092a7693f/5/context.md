# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Fix: Y-sort parent-child grouping + Inspector array display

## Context

Two issues remain after the YSortOffset propagation fix:

1. **Y-sort corner case**: When the player (Pete) is at the exact Y where sorting flips, the player can render *between* the NPC (`elephant-kid`) and its silhouette (`elephant-kid-shilhouette`). This happens because the silhouette has `YSortDepthBias = -0.0005f` (set in the NPC collection handler), creating a tiny depth gap between pa...

### Prompt 2

(/Users/rodrigooliveira/Desktop/Captura de Tela 2026-02-23 às 15.06.48.png) still happening

### Prompt 3

[Request interrupted by user for tool use]

