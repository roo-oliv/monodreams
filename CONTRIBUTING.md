# Contributing to MonoDreams

Thanks for considering a contribution! MonoDreams is alpha and there's plenty of room to shape it. This doc covers what you need to build the engine, run the tests, and add a new module.

If you only want to *use* the engine for your own game, you don't need this file — see the project [README](./README.md) and the [`monodreams` CLI](./MonoDreams.Cli/).

## Prerequisites

- **.NET 8.0 SDK or newer** (the project uses `<RollForward>Major</RollForward>` so .NET 9/10 work too).
- **Python 3** for the registry-validation helpers (used by the schema check).
- A C# editor — Rider, VS, VS Code with C# Dev Kit, or `dotnet` from the command line.

### macOS (Intel & Apple Silicon)

```bash
# .NET 8 SDK via Homebrew (recommended for Apple Silicon)
brew install dotnet@8 freeimage

# Then add to ~/.zshrc or ~/.bash_profile:
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"

# Apple Silicon only: one-time MGCB FreeImage fixup after first restore
cp /opt/homebrew/lib/libfreeimage.dylib \
   ~/.nuget/packages/dotnet-mgcb/3.8.4/tools/net8.0/any/libFreeImage.dylib
```

Manual `~/.dotnet` install? Use `DOTNET_ROOT="$HOME/.dotnet"` instead.

### Windows

Install the .NET 8 SDK from <https://dotnet.microsoft.com/download/dotnet/8.0>.

### Linux

Follow Microsoft's [Linux install guide](https://learn.microsoft.com/dotnet/core/install/linux). MonoGame's MGCB content pipeline needs `libfontconfig1` and `libfreeimage3`.

## Building

```bash
# Clone and restore
git clone https://github.com/roo-oliv/monodreams.git
cd monodreams
dotnet tool restore
dotnet restore

# Build everything (desktop). The web head is excluded from the default
# solution build — see "Targeting the web (KNI/BlazorGL)" below.
dotnet build

# Run the desktop example game (LDtk platformer)
dotnet run --project MonoDreams.Examples.Desktop/MonoDreams.Examples.Desktop.csproj

# Run the CLI from source
dotnet run --project MonoDreams.Cli -- list
```

> **Build order matters.** Always build `MonoDreams/MonoDreams.csproj` before
> the Examples/Demos heads. The MGCB content step references `MonoDreams.dll`
> by absolute path (not as an MSBuild dependency), so the core dll must exist
> first — otherwise the content build fails with `Failed to create importer
> 'YarnSpinnerImporter'`. A clean `dotnet build MonoDreams.sln` orders this
> correctly.

### Individual projects

```bash
dotnet build MonoDreams/MonoDreams.csproj            # core engine (all modules)
dotnet build MonoDreams.Examples.Desktop/MonoDreams.Examples.Desktop.csproj  # desktop head
dotnet build MonoDreams.Tests/MonoDreams.Tests.csproj
dotnet build MonoDreams.Cli/MonoDreams.Cli.csproj
```

### Targeting the web (KNI/BlazorGL)

MonoDreams builds for the **web browser** as well as desktop, via KNI's
BlazorGL backend. The backend is chosen by the consuming **head project**
(`$(MonoDreamsPlatform)`), never by engine source — `MonoDreams.Examples` is a
shared `MonoDreams.Examples.Core` library plus `.Desktop` and `.Web` heads.

```bash
# Build the web (WASM) head — requires the wasm-tools workload. -p is GLOBAL
# so it flows to the shared Core at restore time.
dotnet build MonoDreams.Examples.Web/MonoDreams.Examples.Web.csproj -p:MonoDreamsPlatform=web

# Scaffold a new game for one or more platforms
dotnet run --project MonoDreams.Cli -- init MyGame --platform desktop|web|multi
```

See [`docs/web-targeting.md`](./docs/web-targeting.md) for the full picture:
the project model, per-platform dependency parity, the same-`.mgcb`/two-backend
content build (and the macOS/Linux MGCB native-lib shim it needs), and the
known open Reach 32-bit-index render limit.

## Tests

```bash
dotnet test MonoDreams.Tests/
```

The integration tests use the **headless replay** harness in `MonoDreams.Tests/IntegrationTests/`. Each test writes an `InputReplayPlan`, spawns the example game in headless mode with a temporary debug directory, waits for it to exit, and asserts on the resulting log. See `GameTestRunner.cs` for the runner and the existing tests (LDtk, infinite-runner) for patterns.

**Process-wide state.** The whole assembly runs in one process, so the engine's statics — the sockets (`Logger.LineSink`, `GatedSystem.TimingSink`, `MasterRenderSystem.RenderedTargetSink`), the switches (`SystemProfiler`, the debug-overlay flags, `FinalDrawSystem`'s colours) and the singletons (`PlatformServices.Current`, the `Logger` session) — are shared by every test. `MonoDreams.Tests/ProcessWideState.cs` lists them and resets them after **every** test (plus one static the engine does not own: DefaultEcs's query-filter memo cache, whose keys can collide and hand one test's predicate to another test's query — see the foundation premise "A process-wide socket is restored by whoever installs it"), and `MonoDreams.Tests/TestOrdering.cs` pins the class order so an order-dependent failure is reproducible instead of seasonal. Two rules follow: **add any new engine-level static to `ProcessWideState`** in the PR that introduces it, and keep restoring state in your own `try`/`finally` — the guard is a net, not a licence. Hunting an order-dependent failure: `MONODREAMS_TEST_SEED=<n>` shuffles the class order reproducibly, `MONODREAMS_TEST_LAST=<substring>` forces matching classes to run last, `MONODREAMS_TEST_REPORT_LEAKS=1` prints what each test still had installed, and `MONODREAMS_TEST_NO_RESET=1` turns the net off.

## Repo layout

```
MonoDreams/                  ← the engine (14 modules + project files)
  module.schema.json          ← JSON Schema for every module.json
  presets.json               ← named module combinations
  MODULES.md                  ← module authoring guide
  MonoDreams.csproj          ← the engine library (compiles every module together)
  <module>/                   ← e.g. foundation/, rendering/, cursor/
    module.json               ← module manifest
    ...source files...       ← every file inside is part of the module
MonoDreams.Examples.Core/    ← reference games, shared lib (LDtk, infinite-runner)
MonoDreams.Examples.Desktop/ ← DesktopGL head (Program.cs -> game.Run())
MonoDreams.Examples.Web/     ← BlazorGL (KNI) WASM head
MonoDreams.Cli/              ← the `monodreams` global tool (init / add / list)
MonoDreams.Tests/            ← integration + unit tests
MonoDreams.Cli.Tests/        ← CLI unit tests (manifest + scaffolder)
docs/                        ← engine tenets, per-domain premises, web-targeting guide
.claude/                     ← Claude Code skills (e.g. /deep-review)
```

The Examples app is laid out as a shared `.Core` library + per-platform heads
(see [`docs/web-targeting.md`](./docs/web-targeting.md)); the `monodreams init`
CLI generates the same shape for new games.

## Adding or modifying modules

The repo is the registry. To add a new module:

1. Create `MonoDreams/<name>/` and drop your source files there. The module dir defines its own contents — anything inside (except `module.json`) is shipped.
2. Write `MonoDreams/<name>/module.json`. Required fields are `name` and `description`; declare every cross-module dependency in `dependencies`. See [`MonoDreams/MODULES.md`](./MonoDreams/MODULES.md) for the full schema and [`MonoDreams/cursor/module.json`](./MonoDreams/cursor/module.json) for a small worked example.
3. List any new NuGet packages your source `using`s under `nugetDependencies` so the CLI injects them when users install your module.
4. Add `postInstallNotes` with the *wiring* — system pipeline order, asset hooks, anything not derivable from reading the files. Both humans and AI agents read these.
5. If the module touches an existing engine domain that has a `docs/<domain>/premises.md`, read the premises first (see [CLAUDE.md's workflow section](./.claude/CLAUDE.md#workflow)) and propose new premise text if your change introduces an invariant the docs don't yet name.

Validate the manifest:

```bash
# With ajv-cli
npx ajv-cli validate -s MonoDreams/module.schema.json -d 'MonoDreams/*/module.json'

# Or with the bundled Python helper (uses uv + jsonschema)
uv run --with jsonschema python3 -c "
import json, glob, jsonschema
schema = json.load(open('MonoDreams/module.schema.json'))
for p in sorted(glob.glob('MonoDreams/*/module.json')):
    jsonschema.validate(json.load(open(p)), schema)
print('all manifests valid')
"
```

Check the manifest is *honest* — that the module compiles from the
dependencies it declares and nothing else. This is what a user gets from
`monodreams add <module>` on a machine that has none of the other modules; it
can never fail in this repo, where every checkout has all 14 on disk:

```bash
# Every module: scaffold a temp project, `add` the module, `dotnet build`.
# Opt-in (the env var) because each case is a real restore + build.
MONODREAMS_MANIFEST_HONESTY=1 dotnet test MonoDreams.Cli.Tests/ --filter FullyQualifiedName~ManifestHonesty

# Just the module you touched
MONODREAMS_MANIFEST_HONESTY=1 MONODREAMS_HONESTY_MODULE=collision \
  dotnet test MonoDreams.Cli.Tests/ --filter FullyQualifiedName~ManifestHonesty
```

CI runs the same check as one job per module on every PR touching `MonoDreams/`
or `MonoDreams.Cli/`. See [`MonoDreams/MODULES.md`](./MonoDreams/MODULES.md) ›
"Manifest honesty" for the compile floor and the known-gap list.

End-to-end test the change:

```bash
# Regenerate a sandbox project via the CLI and build it. init scaffolds a
# shared Sandbox.Core lib + per-platform head(s) + Sandbox.sln; pick the
# platform with --platform desktop|web|multi (default desktop).
rm -rf .sandbox
dotnet run --project MonoDreams.Cli -- init Sandbox --dir .sandbox/Sandbox --platform desktop
dotnet run --project MonoDreams.Cli -- add --preset infinite-runner --dir .sandbox/Sandbox
dotnet build .sandbox/Sandbox/Sandbox.sln
# For a web/multi project, build the web head explicitly:
#   dotnet build .sandbox/Sandbox/Sandbox.Web/Sandbox.Web.csproj -p:MonoDreamsPlatform=web
```

## Conventions

- **Components** end in `Component` (e.g. `TransformComponent`, `VelocityComponent`). They are pure data — no logic, no methods beyond simple constructors/helpers.
- **Systems** end in `System` (e.g. `HierarchySystem`, `GravitySystem`). All behavior lives in systems.
- **Messages** flow through the ECS world via publish-subscribe (e.g. `CollisionMessage`, `LoadLevelRequest`).
- **Namespaces are file-path independent.** A file at `MonoDreams/cursor/Component/CursorControllerComponent.cs` still declares `namespace MonoDreams.Component.Cursor`. This lets us reorganize files into module directories without breaking downstream code.
- **Core has no implicit `using`s.** When moving files in, add explicit `using System;`, `using System.Collections.Generic;`, `using System.Linq;` where needed.
- **CLI options: one name per concept.** The same idea gets the same option name in every `monodreams` command (`--dir` is *the* "which project" option for both `init` and `add`; `--project` survives only as a hidden, deprecated alias — see `MonoDreams.Cli/Commands/DirOption.cs`). New commands get strict parsing for free: `StrictOptions` rejects any unrecognized `--option` by name — with a did-you-mean hint — before binding, so an option token can never be swallowed as a positional argument.

## Engine invariants

Before any non-trivial change, read [`docs/CORE_TENETS.md`](./docs/CORE_TENETS.md). For each module you touch, read the matching `MonoDreams/<module>/docs/premises.md` (foundation, rendering, rendering-text, camera, physics, collision, level-loading, level-ldtk, ui, cursor, dialogue, debug). Skipping these is the most common way changes silently break an engine contract. If your change affects platform targeting, the load-bearing invariants are the *backend/OS-agnostic engine* and *platform-selected-by-head* premises in `foundation` and the *content-built-per-platform* premise in `level-loading` (see [`docs/web-targeting.md`](./docs/web-targeting.md)).

## Coding style

Follow the existing code in the module you're editing — formatting, naming, comment density. The repo doesn't ship an `.editorconfig`-enforced style; the codebase's own consistency is the style guide.

## Skills

The repo's `.claude/skills/` directory contains a portable, config-driven engineering pipeline (vendored from [`roo-oliv/skills`](https://github.com/roo-oliv/skills)). Each skill reads `docs/agents/skills-config.md` for everything repo-specific — stack, verify command, docs layout, domains, sensitive domains (`foundation`, `platform`), the per-module flow lenses, and commit/PR conventions — so nothing stack-specific is hardcoded.

- `/refine` — turn a raw request (text, plan file, or a GitHub/Jira/Slack link) into an approved plan with a verifiable Contract block.
- `/deep-plan` — fill and adversarially refute a plan's contract against the live codebase before code exists; the heavy path engages for changes touching a sensitive domain.
- `/implement` — drive an approved plan to an open PR (wave-based, fresh agent per wave + a persistent ledger), then chains `/review-fix-loop`.
- `/review-fix-loop` — review → fix loop over an open PR until exhaustion; posts a consolidated review.
- `/deep-review` — multi-agent review of a PR/branch/commit/local diff through the universal lens set plus one dedicated lens per module the change touches. Invoke `/deep-review <PR# or branch>` (or no argument for current changes vs `main`); append `cheaper` for tiered model routing.
- `/verify`, `/verify-plan` — run the configured verify command with a fix loop; reconcile an implementation against its plan.
- `/setup`, `/bootstrap` — run-once-per-repo: write `docs/agents/skills-config.md`, and scaffold/revise the docs the skills consume (`CORE_TENETS.md`, per-module `premises.md`, per-module flow docs under `docs/flows/`).

## Filing issues and PRs

- **Bugs** — include the reproduction, the expected vs actual behavior, and which modules are installed in your `monodreams.json`.
- **PRs** — make them focused; one module or one premise per PR is ideal. Update tests, premises, and `postInstallNotes` in the same PR that changes behavior.

## License

By contributing you agree your contributions are licensed under the [MIT License](./LICENSE).
