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

# Build everything
dotnet build

# Run the example games
dotnet run --project MonoDreams.Examples/MonoDreams.Examples.csproj

# Run the CLI from source
dotnet run --project MonoDreams.Cli -- list
```

### Individual projects

```bash
dotnet build MonoDreams/MonoDreams.csproj            # core engine (all modules)
dotnet build MonoDreams.Examples/MonoDreams.Examples.csproj
dotnet build MonoDreams.Tests/MonoDreams.Tests.csproj
dotnet build MonoDreams.Cli/MonoDreams.Cli.csproj
```

## Tests

```bash
dotnet test MonoDreams.Tests/
```

The integration tests use the **headless replay** harness in `MonoDreams.Tests/IntegrationTests/`. Each test writes an `InputReplayPlan`, spawns the example game in headless mode with a temporary debug directory, waits for it to exit, and asserts on the resulting log. See `GameTestRunner.cs` for the runner and the existing tests (LDtk, Blender, infinite-runner) for patterns.

## Repo layout

```
MonoDreams/                  ← the engine (13 modules + project files)
  module.schema.json          ← JSON Schema for every module.json
  presets.json               ← named module combinations
  MODULES.md                  ← module authoring guide
  MonoDreams.csproj          ← the engine library (compiles every module together)
  <module>/                   ← e.g. foundation/, rendering/, cursor/
    module.json               ← module manifest
    ...source files...       ← every file inside is part of the module
MonoDreams.Examples/         ← reference games (LDtk, Blender, infinite-runner)
MonoDreams.Cli/              ← the `monodreams` global tool (init / add / list)
MonoDreams.Tests/            ← integration + unit tests
docs/                        ← engine tenets and per-domain premises
.claude/                     ← Claude Code skills (e.g. /deep-review)
```

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

End-to-end test the change:

```bash
# Regenerate a sandbox project via the CLI and build it
rm -rf .sandbox
dotnet run --project MonoDreams.Cli -- init Sandbox --dir .sandbox/Sandbox
dotnet run --project MonoDreams.Cli -- add --preset infinite-runner --project .sandbox/Sandbox
dotnet build .sandbox/Sandbox/Sandbox.csproj
```

## Conventions

- **Components** end in `Component` (e.g. `TransformComponent`, `VelocityComponent`). They are pure data — no logic, no methods beyond simple constructors/helpers.
- **Systems** end in `System` (e.g. `HierarchySystem`, `GravitySystem`). All behavior lives in systems.
- **Messages** flow through the ECS world via publish-subscribe (e.g. `CollisionMessage`, `LoadLevelRequest`).
- **Namespaces are file-path independent.** A file at `MonoDreams/cursor/Component/CursorControllerComponent.cs` still declares `namespace MonoDreams.Component.Cursor`. This lets us reorganize files into module directories without breaking downstream code.
- **Core has no implicit `using`s.** When moving files in, add explicit `using System;`, `using System.Collections.Generic;`, `using System.Linq;` where needed.

## Engine invariants

Before any non-trivial change, read [`docs/CORE_TENETS.md`](./docs/CORE_TENETS.md). For each domain you touch, read the matching `docs/<domain>/premises.md` (rendering, hierarchy-transform, collision, physics, level-loading). Skipping these is the most common way changes silently break an engine contract.

## Coding style

Follow the existing code in the module you're editing — formatting, naming, comment density. The repo doesn't ship an `.editorconfig`-enforced style; the codebase's own consistency is the style guide.

## Skills

The repo's `.claude/skills/` directory contains Claude Code skills that help during contribution:

- `/deep-review` — multi-agent review of a PR or branch through six lenses calibrated for MonoDreams (adjacent-code, system-ordering, framework-fit, cross-domain deps, premises/test-coverage, ECS-purity). Invoke with `/deep-review <PR# or branch>`.

## Filing issues and PRs

- **Bugs** — include the reproduction, the expected vs actual behavior, and which modules are installed in your `monodreams.json`.
- **PRs** — make them focused; one module or one premise per PR is ideal. Update tests, premises, and `postInstallNotes` in the same PR that changes behavior.

## License

By contributing you agree your contributions are licensed under the [MIT License](./LICENSE).
