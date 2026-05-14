# MonoDreams docs

Routing index. Start here, then load the specific docs your task needs.

## Principles

| Doc | What's in it |
|---|---|
| [`CORE_TENETS.md`](CORE_TENETS.md) | Engine-wide invariants: framework-not-library, ECS purity, hierarchy, rendering, physics, level loading, reference pipeline, debug, refactor backlog. **Load this first** for any non-trivial task. |

## Domain premises (V1)

Premises are technical invariants downstream code silently depends on.
Each file uses the format **Why / Breaks / Tests / Depends on**.

| Domain | File |
|---|---|
| Hierarchy & Transform | [`hierarchy-transform/premises.md`](hierarchy-transform/premises.md) |
| Rendering | [`rendering/premises.md`](rendering/premises.md) |
| Collision | [`collision/premises.md`](collision/premises.md) |
| Physics | [`physics/premises.md`](physics/premises.md) |
| Level loading & entity spawning | [`level-loading/premises.md`](level-loading/premises.md) |

## Deferred to V2

These domains have premises worth capturing but were out of scope for the
V1 bootstrap. The reconnaissance notes for each exist in source.

- Camera — see `MonoDreams/Component/Camera*.cs` + `MonoDreams/System/Camera/`
- Cursor — see `MonoDreams/Component/Cursor/` + `MonoDreams/System/Cursor/`
- Input — see `MonoDreams/Input/` + `MonoDreams/System/Input/`
- Debug systems — see `MonoDreams/System/Debug/`
- Screen management — see `MonoDreams/Screen/` + `MonoDreams/State/`
- Entity spawning *(currently bundled into level-loading)*
- Messages / state contracts

## Conventions and workflow

| Doc | What's in it |
|---|---|
| [`../.claude/CLAUDE.md`](../.claude/CLAUDE.md) | Coding conventions, build commands, debug workflow, project structure. Loaded automatically by Claude Code; humans should also read it. |
| [`../.claude/skills/deep-review/SKILL.md`](../.claude/skills/deep-review/SKILL.md) | Multi-agent review skill. Run `/deep-review` on a PR, branch, or commit. |
