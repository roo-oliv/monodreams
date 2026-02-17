# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# MonoDreams Housekeeping: Core/Examples Alignment

## Context

MonoDreams.Examples has evolved well beyond the core library in rendering, camera, cursor, entity spawning, and collision handling. The core still carries dead/broken/legacy code (commented-out systems, broken render calls, superseded components). This refactor:
1. Deletes all dead weight from core
2. Promotes mature Examples patterns into core (new namespaces under `MonoDreams.*`)
3. Updates Examples ...

