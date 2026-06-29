---
name: verify
description: Run after any code change. Runs the repo's configured format/lint/build/test pipeline with a fix loop until it's green, then reports honestly. Use when the user asks to verify, build, run tests, or confirm a change is sound — and proactively after editing code.
---

# verify

Run the repo's verification pipeline after a change and drive it to green. The exact commands
are **not hardcoded** — they come from `docs/agents/skills-config.md` › Verify. Execute the steps
in order, stopping on failure to fix before moving on.

## 0. Read the verify config

Read **`docs/agents/skills-config.md`** › Verify:

- **Full** — the format/lint/build/test command (e.g. `./gradlew spotlessApply detekt clean build`,
  `bash .claude/scripts/check-all.sh`, `dotnet test`, `npm run lint && npm test`).
- **Incremental** (optional) — a faster scoped variant for checking just what changed.
- **Always-run gates** (optional) — cheap checks to append every run (e.g. architecture tests).

If the config or the Verify section is absent, **ask the user** for the command that formats,
lints, builds, and tests this repo (and any always-run gate), then proceed. Do not guess a build
tool from the file tree and run it silently.

## 1. Incremental pass (when the change is scoped)

If config provides an **Incremental** command, run it against what you changed — plus the
**always-run gates** — first. Fixing failures here is cheaper than after a full build. If the
change is broad, or there's no incremental command, skip straight to the full pass.

Read the conventions doc (`docs/agents/skills-config.md` › Conventions › test conventions) before
writing or editing any test — glob-scoped rules don't auto-load, and the patterns they forbid are
often exactly what the always-run gates reject.

## 2. Full pass with a fix loop

Run the **Full** command (plus always-run gates if they aren't already part of it). Then:

- **Green** → report success.
- **Red** → extract the failing items (test names, lint violations, compile errors) and their
  messages. Fix each. Re-run **only the failures** to confirm they pass, then run the Full command
  again. Repeat until green or until you've made **3 fix attempts** without progress — then stop
  and report what's still red (don't loop forever).
- **Output too large** → tee it to a file and Read that file to find the failures, rather than
  scrolling truncated output.

## 3. Report honestly

State the outcome plainly: green (and what ran), or the specific failures that remain after the
fix attempts. If you skipped the full build (incremental only) or couldn't run something, say so —
a "passed" that skipped a step is worse than an honest partial.
