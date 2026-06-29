# Session Context

## User Prompts

### Prompt 1

This repo already have some skills, but they might need updating too. Bring the skills from https://github.com/roo-oliv/skills to here so we can use `bootstrap` to also revise if we have all the files and structure needed.

### Prompt 2

Base directory for this skill: /Users/rodrigooliveira/.warp/worktrees/monodreams/feat/skills/.claude/skills/setup

# setup

The engineering skills (`deep-review`, `deep-plan`, `refine`, `implement`, `review-fix-loop`)
adapt to a repo by reading one file: `docs/agents/skills-config.md`. This skill writes that
file by looking at the repo and asking you a few questions. Run it once per repo; edit the
file by hand afterwards whenever something changes.

This is prompt-driven, not a script. Explore â...

### Prompt 3

Do both, update CONTRIBUTING.md and commit changes

### Prompt 4

Base directory for this skill: /Users/rodrigooliveira/.warp/worktrees/monodreams/feat/skills/.claude/skills/bootstrap

# bootstrap

`deep-review`, `deep-plan`, `refine`, and `implement` are far more useful when the repo
carries two kinds of agent-facing docs:

- **`CORE_TENETS.md`** â€” the handful of cross-cutting invariants and design principles that
  hold across the whole codebase. "Most of what looks surprising in the code is consistent
  with one of these." Business rules and architectural...

