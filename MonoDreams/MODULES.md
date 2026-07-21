# MonoDreams Modules

The engine ships as a set of **source modules** — self-contained,
copyable slices of engine code you own outright. Each module is a
directory under `MonoDreams/` with its own `module.json` manifest
sitting next to the code it describes. The `monodreams` CLI reads these manifests, resolves the
dependency graph, and copies the source into a user's project.

The model is **shadcn for C# game code**: nothing is hidden behind a
NuGet binary. The user owns every line; AI agents can read and edit
the code in place.

## Layout

```
MonoDreams/
  module.schema.json       ← JSON Schema validating every manifest
  presets.json            ← curated module combinations
  MODULES.md               ← this file
  MonoDreams.csproj       ← engine project (globs all module source)
  README.md, Icon.*       ← engine-only project assets
  Effect/                 ← engine-only shaders (not part of any module)
  <module>/
    module.json            ← the manifest
    ...source files...    ← all files inside ARE the module
```

The 13 modules:

```
foundation              (required base — installed by `monodreams init`)
├── rendering            (includes mesh primitives — IMeshGenerator, MeshData)
│   ├── rendering-text
│   ├── camera
│   ├── cursor
│   ├── debug              (+ collision)
│   ├── level-ldtk         (+ level-loading)
│   └── ui
│       └── dialogue       (+ rendering-text)
├── physics
├── collision              (+ physics, soft)
├── level-loading
│   └── level-ldtk
└── level-editor           (+ rendering, ui, cursor, level-loading)
```

`level-editor` is the in-game level editor — an `Edit` run mode layered over the
real game pipeline (it ships only the `foundation` run-state model + docs today;
its own editor systems land in later waves).

## module.json

The schema lives in [`module.schema.json`](./module.schema.json). Each
manifest declares only the metadata; the file list is **implicit** —
every file inside the module's directory (except `module.json` itself)
ships as part of the module.

| Field | Required | Purpose |
|---|---|---|
| `name` | yes | Kebab-case identifier, matches the directory name. |
| `description` | yes | One-line summary, shown by `monodreams list`. |
| `platforms` | no | Target platforms (`desktop`, `web`). Omit for a platform-agnostic module — the default is all platforms. The CLI skips a module for a project whose target platform it does not list. |
| `dependencies` | no | Other modules required transitively. |
| `nugetDependencies` | no | `<PackageReference>` entries to inject into the user's csproj. Each entry may carry its own `platforms` tag so a backend-specific package (e.g. `MonoGame.Framework.DesktopGL`↔`nkast.Xna.Framework`, `MonoGame.Extended`↔`KNI.Extended`) is injected only on the platform it applies to; untagged entries apply to all platforms. |
| `csprojProperties` | no | Properties (e.g. `EnableDynamicLoading`) appended to the csproj. |
| `files` | no | **Override** the implicit-file-list. Useful only for modules that ship files from outside their directory; most modules omit this. |
| `mgcbEntries` | no | Lines appended to the user's Content pipeline `.mgcb`. Each entry is either a bare string (all platforms) or an object `{ value, platforms }` for a content-pipeline line that differs per backend. |
| `postInstallNotes` | no | Markdown printed after install — for both humans and AI agents. |
| `agentsMd` | no | Path to an AGENTS.md snippet appended to the user's `AGENTS.md`. |
| `premisesRef` | no | Pointer into `docs/` so users can find the invariants this module obeys. |
| `demo` | no | Optional working demonstration. Files under `<module>/demo/` only ship with `--with-demo`. See "Module demos" below. |

### File copy convention

The CLI walks the module directory and copies every file at the same
relative path into the user's project. So `MonoDreams/cursor/Cursor.cs`
in this repo lands at `MonoDreams/cursor/Cursor.cs` in the user's
project. Namespaces stay aligned because C# namespaces are file-path
independent — the file at `MonoDreams/cursor/Component/CursorController.cs`
still declares `namespace MonoDreams.Component.Cursor`.

### Worked example

[`cursor/module.json`](./cursor/module.json) — the smallest
non-foundation module, depending on only `foundation` and `rendering`.
The module dir contains 7 source files; the manifest only carries the
metadata. Drift is impossible by construction: adding a file to the
directory makes it part of the module automatically.

## Module demos

A module can ship a runnable demonstration under `<module>/demo/`.
[`camera/demo/CameraDemoScreen.cs`](./camera/demo/CameraDemoScreen.cs)
is the reference. Conventions:

- Demo source lives in `<module>/demo/`. It is **excluded** from the
  core engine library (`MonoDreams.csproj` removes `**/demo/**` from
  its compile glob) and from `monodreams add <module>` by default.
- The host project [`MonoDreams.Demos`](../MonoDreams.Demos/) compiles
  every module's demo directly (`<Compile Include="..\MonoDreams\**\demo\**\*.cs">`)
  and exposes a launcher screen. Run with
  `dotnet run --project MonoDreams.Demos`.
- The demo's entry class implements `IGameScreen`. Reuse
  `MonoDreams.Demos.UI.DemoUI` for menu chrome and
  `DemoButtonComponent` / `DemoButtonInteractionSystem` for clickable
  buttons that dispatch by id.
- Declare the demo in `module.json` via the `demo` field
  (`entry`, `description`, `dependencies`). The extra `dependencies`
  list captures modules the demo needs over and above what the module
  itself requires (e.g. the `camera` demo declares `ui`, `cursor`,
  `rendering-text` for its menu chrome).

## Authoring a module

1. Create `MonoDreams/<name>/` and drop your source files in. Keep
   the module self-contained — anything that cuts across two modules
   either belongs to a parent module or needs splitting.
2. Write `module.json` next to the code. Declare every dependency. The
   CLI fails loudly on a missing reference rather than guessing.
3. List any NuGet packages the source `using`s. The CLI injects them
   into the user's csproj at install time.
4. Add `postInstallNotes` with the *wiring*: which systems go where
   in the pipeline, what assets need to load, anything not derivable
   from reading the files.
5. Validate against `module.schema.json`.

## Validation

Any JSON Schema validator works. From the repo root with
[`ajv-cli`](https://github.com/ajv-validator/ajv-cli):

```bash
ajv validate -s MonoDreams/module.schema.json -d 'MonoDreams/*/module.json'
```

The CLI also runs validation before any install.
