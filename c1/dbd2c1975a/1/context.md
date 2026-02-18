# Session Context

## User Prompts

### Prompt 1

Implement the following plan:

# CLAUDE.md Update + Headless Replay Tests

## Context

We just implemented a debug infrastructure (Logger, ScreenshotCapture, InputReplay)
and tested it successfully. Two follow-ups:

1. **Document it in CLAUDE.md** so future Claude Code sessions know how to use it
2. **Add a headless test mode** so replay scenarios can run as xunit tests without
   a visible window — just logic + log assertions

---

## Part 1 — Update CLAUDE.md

Add a new `## Debug infrastru...

### Prompt 2

Delete branch main, fetch origin, checkout main (that has all the commits of this branch already), then checkout on a new branch, git add ., commit, push and open a pull request

### Prompt 3

The recurring screenshots should be off by default and enabled only when needed (most often by Claude Code).

