# Agent skills config

Per-repo configuration for the engineering skills (`deep-review`, `deep-plan`, `refine`,
`implement`, `review-fix-loop`). **The skills read this file to adapt to this repo — they
hardcode nothing about stack, paths, domains, or conventions.** The `setup` skill writes it;
edit it by hand anytime. If a section is missing, the skill that needs it falls back to the
default noted here and says so in its output.

## Stack

`C# + .NET (dotnet), MonoGame DesktopGL (desktop) / KNI-BlazorGL (web), DefaultEcs`

A code-first, ECS-purist 2D game engine shipped as 14 self-contained source modules under
`MonoDreams/<module>/`, distributed shadcn-style via the `monodreams` CLI. Used only for idioms
(test style, file naming) — never as a hard gate.

## Verify

The command sequence the skills run to format, lint, build, and test before committing or
opening a PR. A non-zero exit is a failure the skill must fix before proceeding.

**Hard ordering rule:** core (`MonoDreams/MonoDreams.csproj`) MUST build before the solution or
any head. The MGCB content step references `MonoDreams.dll` by absolute path (not as an MSBuild
dependency), so the core dll must already exist or the content build fails with
`Failed to create importer 'YarnSpinnerImporter'`.

- **Full:** `dotnet build MonoDreams/MonoDreams.csproj && dotnet test --configuration Release`
  - Builds + tests the desktop solution (the `.sln` excludes the web head by design). This is
    the required gate.
- **Web head (optional — only when the `wasm-tools` workload is installed):**
  `dotnet build MonoDreams.Examples.Web/MonoDreams.Examples.Web.csproj -p:MonoDreamsPlatform=web`
  - Run this in addition to **Full** when a change touches the `platform` flow or the
    rendering/content paths. Install the workload with `dotnet workload install wasm-tools`;
    **skip this step entirely if it is not installed** — it hard-fails without the workload and
    is deliberately kept out of the core Full gate so Full never fails on a missing workload.
- **Incremental** (faster, scoped per-wave — desktop only, skips the heavy web build):
  `dotnet build MonoDreams/MonoDreams.csproj && dotnet test MonoDreams.Tests/`
  - Use `dotnet test MonoDreams.Cli.Tests/` instead when the change is CLI-only.
- **Always-run gates:** none. The repo ships no format/lint gate (no `.editorconfig`-enforced
  style, no architecture tests). Match the surrounding code's style; the codebase's own
  consistency is the style guide.

## Docs layout

Where the docs the skills read and produce live. `{module}` is substituted per change.

- **Core tenets:** `docs/CORE_TENETS.md` (engine-wide invariants — read first for any non-trivial change)
- **Premises:** `MonoDreams/{module}/docs/premises.md` (**colocated** — per-module technical invariants in Why/Breaks/Tests/Depends-on format)
- **Overview** (optional): `MonoDreams/{module}/docs/overview.md` (per-module purpose + wiring tour)
- **Schema:** none.
- **Planning:** none (no plan-contract spec / recurring-failure-modes docs).
- **Rules dir:** none (no `.claude/rules/`).
- **Other agent-facing docs:** `docs/index.md` (routing index → per-module docs),
  `docs/web-targeting.md` (platform-targeting deep dive), `MonoDreams/MODULES.md` (module
  manifest schema + authoring guide), `CONTRIBUTING.md` (build/test/contribution workflow).

## Domains

The bounded contexts of this repo (the 14 engine modules + cross-cutting platform/tooling
contexts), and how to detect which one a changed file belongs to. A file may match more than one
domain (e.g. a web head matches both `examples` and `platform`); the skills load every matched
domain's premises and lenses.

| Domain | Detect (path globs) |
|---|---|
| `foundation` | `MonoDreams/foundation/**` |
| `rendering` | `MonoDreams/rendering/**` |
| `rendering-text` | `MonoDreams/rendering-text/**` |
| `camera` | `MonoDreams/camera/**` |
| `physics` | `MonoDreams/physics/**` |
| `collision` | `MonoDreams/collision/**` |
| `level-loading` | `MonoDreams/level-loading/**` |
| `level-ldtk` | `MonoDreams/level-ldtk/**` |
| `ui` | `MonoDreams/ui/**` |
| `cursor` | `MonoDreams/cursor/**` |
| `dialogue` | `MonoDreams/dialogue/**` |
| `debug` | `MonoDreams/debug/**` |
| `level-editor` | `MonoDreams/level-editor/**` |
| `audio` | `MonoDreams/audio/**` |
| `effect` | `MonoDreams/Effect/**` |
| `platform` | `Directory.Build.props`, `global.json`, `MonoDreams.*.Web/**`, `MonoDreams.*.Desktop/**`, `MonoDreams.Web.Hosting/**`, `docs/web-targeting.md` |
| `examples` | `MonoDreams.Examples*/**` |
| `cli` | `MonoDreams.Cli*/**` |

## Sensitive domains

Subset of the domains above where a mistake is expensive or hard to debug. MonoDreams moves no
money and stores no user data, so the axis here is **core-engine correctness that breaks silently
and is expensive to trace**. A change touching ANY of these triggers the **heavy path** in
`deep-plan` and `deep-review` (full lens fan-out + adversarial refute + the PR-create gate).

`foundation, platform`

- `foundation` — `TransformComponent`, the entity hierarchy, `Logger`, input/replay, and
  `ScreenController` are depended on by every other module. A broken invariant here surfaces as a
  confusing bug three modules away.
- `platform` — how desktop (MonoGame DesktopGL) and web (KNI/BlazorGL) are selected and built
  (the `$(MonoDreamsPlatform)` property, the `.Core` + per-head split, content-built-per-platform).
  A regression here breaks one backend silently while the other keeps working.

## Flows

`deep-review` / `deep-plan` always run a **universal** lens set, stack-agnostic by design:
adjacent-code, derived-quantity, negative-space, contract×code, test-coverage.

On top of those, the review spawns **one dedicated lens per *flow* this repo declares** — and in
this repo a "flow" is **a module**. Each module gets its own flow doc whose `covers:` glob is that
module's source tree, so a change touching N modules spawns N specialized module lenses on top of
the universal set: the number of lens/planning agents scales with the change's module footprint.
Each module's flow doc reads like a dedicated core-tenet for that module (the path data/state
takes through it, its entities and their lifecycle, its invariants, its failure modes) and is
seeded from the module's existing colocated `docs/premises.md` + `docs/overview.md`.

- **Flows dir:** `docs/flows/` — one `<module>.md` per module (e.g. `foundation.md`,
  `rendering.md`, `physics.md`, `collision.md`, `level-loading.md`, …).

Mark the `foundation.md` and `platform.md` flow docs with `sensitive: true` in their frontmatter
(consistent with Sensitive domains above). Author the flow docs with the `bootstrap` skill (or by
hand) using the format in [bootstrap/flow.template.md](../../.claude/skills/bootstrap/flow.template.md);
each doc's frontmatter `covers:` globs decide which flows a given change touches — only those
modules' lenses run. No flow docs yet → only the universal lenses run until `bootstrap` authors them.

## Conventions

- **Commit/PR language:** en
- **Conventional commits:** yes (`type(scope): description` — e.g. `feat(web):`, `fix(level-ldtk):`, `refactor(web):`)
- **Branch naming:** `type/kebab-slug` or `ro/kebab-slug` (e.g. `feat/kni`, `ro/convex-collider-sat`)
- **PR body:** keep PRs focused — one module or one premise per PR is ideal. Update tests,
  premises, and `postInstallNotes` in the same PR that changes behavior. See `CONTRIBUTING.md`
  › "Filing issues and PRs".
- **Test conventions:** `dotnet test MonoDreams.Tests/`; integration tests use headless replay +
  log assertions via `GameTestRunner` (`AssertLogContains`, `AssertLogContainsInOrder`); headless
  demo self-verification via `GameTestRunner.RunDemosAsync` + `AssertScreenshotNonBlank` /
  `AssertHeapFlat`. See `CONTRIBUTING.md` › "Tests" and `.claude/CLAUDE.md` › "Testing".
- **Commit trailer:** `Co-Authored-By: Claude <noreply@anthropic.com>`
