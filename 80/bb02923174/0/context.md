# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# ImGui.NET ECS Debug Inspector

## Context

MonoDreams needs a runtime debug inspector for examining entity/component state during gameplay. The goal is a JetBrains-style variable watcher: a **World Hierarchy panel** showing the full ECS tree (World → Entities → Components → Fields) with tri-state checkboxes to pin items, and a **Watchers panel** that displays only the pinned items with live-updating values.

ImGui.NET (1.89.7.1) and MonoGame.ImGuiNet (1.0.5...

### Prompt 2

How do I enable/disable the debugger?

### Prompt 3

Does this implementation uses reflection? If yes, we have to ensure there is a way to disable this so it works with AOT for console builds or find a way to avoid reflection entirely.

### Prompt 4

Would it be reasonable to reimplement this avoiding reflection at all by requiring that all components derive from a base class or interface so we get some sort of control?

### Prompt 5

[Request interrupted by user for tool use]

