# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Add Debug Overlays panel to ImGui DebugInspector

## Context
Debug visualization systems (ColliderDebugSystem, SpriteDebugSystem, CullingSystem debug bounds) exist but are toggled by commenting/uncommenting code or flipping static fields. We want runtime toggles via the existing ImGui DebugInspector (F1) so developers can enable/disable debug overlays without recompiling.

## Approach
- Unconditionally add debug systems to the draw pipeline (they default to `Enab...

### Prompt 2

/Users/rodrigooliveira/Desktop/Captura de Tela 2026-02-28 às 22.53.15.png
/Users/rodrigooliveira/Desktop/Captura de Tela 2026-02-28 às 22.52.00.png
The game entities are rendering a little bit differently than what I arrange in Blender. Especially the colliders. The collider for the store is way off in terms of place and scale. Maybe it's something to do with the origin of each in Blender? I don't know.

### Prompt 3

[Request interrupted by user for tool use]

