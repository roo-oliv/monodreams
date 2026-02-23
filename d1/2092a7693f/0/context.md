# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Plan: Fix XY-plane collider mesh export in Blender exporter

## Context
The `elephant-kid-collider` mesh is authored on the XY plane in Blender. The exporter's `get_mesh_vertices()` always maps `(X, -Z)` to game coords, so Z=0 for all vertices → all game Y=0 → degenerate collider. The exporter should transparently handle meshes on either XY or XZ plane, producing the same result the artist sees from the camera/top view.

**Evidence from exported data:**
- `el...

