# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# Configure macOS Notifications for Claude Code

## Context
You want to be notified when Claude Code needs your attention — whether it's asking a question, requesting permission approval, or finished executing. This avoids having to constantly watch the terminal.

## Approach
Add a **`Notification`** hook to your **global** settings (`~/.claude/settings.json`) so it works across all projects.

The `Notification` event fires for:
- **`idle_prompt`** — Claude is ...

