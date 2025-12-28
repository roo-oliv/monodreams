# About this project
This project is a code-first and ECS-purist 2D game engine on top of MonoGame.
The core concept is that this engine provides a set of components one can just
add to their game entities and include the provided systems in the update
calls, so you get easy access to plug’n play features. Like, just add a
Transform and a RigidBody component to an entity and a GravitySystem to your
systems’ pipeline to have gravity applied to all RigidBodies.

The MonoDreams solution is supposed to be the core of the engine and the
MonoDreams.Examples is currently the test playground since this is still in
alpha. In the current state, MonoDreams already contains some legacy code and
MonoDreams.Examples has more up to date and mature features. Your starting
point to understand the current state is looking at files under
MonoDreams.Examples/Screens. The most recent work is being done in the
LoadLevelExampleGameScreen.cs file.

# Workflow
 - After planning but before coding, build the MonoDreams.Examples solution so
   that you know how to build and not question the build process after you
   commit your changes and test the build.
 - Eval between using the LoadLevelExampleGameScreen as a starting point for
   your own work or creating a new screen.
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
