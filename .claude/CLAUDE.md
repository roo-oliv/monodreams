# About this project
This project is a code-first and ECS-purist 2D game engine built on MonoGame
(rendering) and DefaultEcs (ECS framework). The core concept is plug'n play
components and systems: add a `Transform` and `RigidBody` to an entity, include
`GravitySystem` in your pipeline, and gravity just works.

MonoDreams is still in alpha. The core library (`MonoDreams/`) is the
authoritative source for all engine infrastructure — rendering, camera, cursor,
collision, physics, entity spawning, level loading, and screen management.
`MonoDreams.Examples/` contains game-specific logic only. Your starting point to
understand how to wire up a game screen is
`MonoDreams.Examples/Screens/LoadLevelExampleGameScreen.cs`.

# Project structure
- `MonoDreams/` — core engine library: components, systems, rendering, camera,
  cursor, collision, physics, entity spawning, level loading, screen management
- `MonoDreams.Examples/` — game-specific logic: screens, input mapping,
  dialogue, movement, entity factories, settings
- `MonoDreams.YarnSpinner/` — YarnSpinner dialogue integration (standalone, not
  referenced by core)
- `MonoDreams.Tests/` — tests
- `Tools/` — Blender exporter plugin for level design

# Key conventions

## Component design
- Components are pure data containers — no logic, no methods beyond simple
  helpers. All behavior lives in systems.
- Before creating a new component, check if an existing one can be extended
  with a field or flag. This project avoids component sprawl.
- `EntityInfo` is string-based in core (`new EntityInfo("Player")`). Game code
  can define its own enum for convenience.
- `Transform` is the single spatial component — there is no separate Position
  component.
- `CameraFollowTarget` lives in `MonoDreams.Component` (not a Camera
  subdirectory) to avoid namespace conflicts with the `Camera` class.
- `DrawComponent` (class) is the unified rendering component — it supports
  Sprite, Text, NinePatch, and Mesh via the `DrawElementType` enum. **Do not
  create new draw/render components.**
- `SpriteInfo` (struct) holds sprite source data (texture, source rect, color,
  layer depth). Both `SpriteInfo` and `DrawComponent` are set on entities that
  render.
- `Visible` is an empty tag struct added/removed by `CullingSystem`. Entities
  need it to be rendered.

## Entity creation
- Factory-based: implement `IEntityFactory` (in `MonoDreams.EntityFactory`),
  register it with `EntitySpawnSystem` (in `MonoDreams.System.EntitySpawn`) by
  string identifier. The system listens for `EntitySpawnRequest` messages and
  dispatches to the right factory.
- Namespace choice: `MonoDreams.EntityFactory` (not `.Entity`) to avoid
  shadowing `DefaultEcs.Entity`.
- Direct creation: `BlenderLevelParserSystem` creates entities from exported
  Blender level data.
- Standard component stack for a renderable entity: `EntityInfo`, `Transform`,
  physics components as needed, `SpriteInfo`, `DrawComponent`, `Visible`.

## Rendering pipeline
- All rendering infrastructure lives in `MonoDreams.Component.Draw` and
  `MonoDreams.System.Draw`.
- Three render targets defined by `RenderTargetID`: Main (game world, camera
  transform applied), UI, HUD.
- Draw pipeline: prep systems (`SpritePrepSystem`, `TextPrepSystem`,
  `MeshPrepSystem`) populate `DrawComponent` from source data →
  `MasterRenderSystem` renders everything.
- `MasterRenderSystem` is game-agnostic and handles all draw types. Do not
  create parallel render systems.
- `CullingSystem` adds/removes the `Visible` component based on camera view
  bounds.

## Collision and physics
- `BoxCollider`, `RigidBody`, `Velocity` components in
  `MonoDreams.Component.Collision` / `MonoDreams.Component.Physics`.
- Transform-based collision detection and resolution in
  `MonoDreams.System.Collision`.
- `GravitySystem` in `MonoDreams.System.Physics`.

## Level loading
- LDtk and Blender parsers in `MonoDreams.System.Level`.
- `LoadLevelRequest` message triggers the pipeline.
- `LevelLoadRequestSystem` → parser systems → `EntitySpawnSystem`.

# Workflow
- After planning but before coding, build the MonoDreams.Examples solution so
  that you know how to build and not question the build process after you
  commit your changes and test the build.
- Eval between using the LevelSelectionScreen as a starting point for
  your own work or creating a new screen.
- When adding new functionality, first check `MonoDreams/` (core) for existing
  components and systems that can be extended. Prefer adding fields to existing
  components over creating new component types.
- This project should behave as a framework, so avoid having multiple ways to
  do the same thing, meaning that you should not create many new components
  and systems when you can just add functionality to existing ones.
- Refactorings are fine since this project is still in alpha. Just be sure to
  align with the user your plan first.

# Building the Project
Before making changes, ensure the project builds successfully.

## Build Commands
```bash
# Build core engine
dotnet build MonoDreams/MonoDreams.csproj

# Build examples (includes MonoDreams)
dotnet build MonoDreams.Examples/MonoDreams.Examples.csproj

# Build YarnSpinner integration (includes MonoDreams)
dotnet build MonoDreams.YarnSpinner/MonoDreams.YarnSpinner.csproj

# Build tests
dotnet build MonoDreams.Tests/MonoDreams.Tests.csproj

# Build everything
dotnet build
```
