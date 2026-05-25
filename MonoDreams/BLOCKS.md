# MonoDreams Blocks

The engine ships as a set of **blocks** — self-contained, copyable
slices of source code. Each block is a directory under `MonoDreams/`
with its own `block.json` manifest sitting next to the code it
describes. The `monodreams` CLI reads these manifests, resolves the
dependency graph, and copies the source into a user's project.

The model is **shadcn for C# game code**: nothing is hidden behind a
NuGet binary. The user owns every line; AI agents can read and edit
the code in place.

## Layout

```
MonoDreams/
  block.schema.json       ← JSON Schema validating every manifest
  presets.json            ← curated block combinations
  BLOCKS.md               ← this file
  MonoDreams.csproj       ← engine project (globs all block source)
  README.md, Icon.*       ← engine-only project assets
  Effect/                 ← engine-only shaders (not part of any block)
  <block>/
    block.json            ← the manifest
    ...source files...    ← all files inside ARE the block
```

The 13 blocks:

```
foundation              (required base — installed by `monodreams init`)
├── rendering            (includes mesh primitives — IMeshGenerator, MeshData)
│   ├── rendering-text
│   ├── camera
│   ├── cursor
│   ├── debug              (+ collision)
│   ├── level-ldtk         (+ level-loading)
│   ├── level-blender      (+ level-loading)
│   └── ui
│       └── dialogue       (+ rendering-text)
├── physics
├── collision              (+ physics, soft)
└── level-loading
    ├── level-ldtk
    └── level-blender
```

## block.json

The schema lives in [`block.schema.json`](./block.schema.json). Each
manifest declares only the metadata; the file list is **implicit** —
every file inside the block's directory (except `block.json` itself)
ships as part of the block.

| Field | Required | Purpose |
|---|---|---|
| `name` | yes | Kebab-case identifier, matches the directory name. |
| `description` | yes | One-line summary, shown by `monodreams list`. |
| `dependencies` | no | Other blocks required transitively. |
| `nugetDependencies` | no | `<PackageReference>` entries to inject into the user's csproj. |
| `csprojProperties` | no | Properties (e.g. `EnableDynamicLoading`) appended to the csproj. |
| `files` | no | **Override** the implicit-file-list. Useful only for blocks that ship files from outside their directory; most blocks omit this. |
| `mgcbEntries` | no | Lines appended to the user's Content pipeline `.mgcb`. |
| `postInstallNotes` | no | Markdown printed after install — for both humans and AI agents. |
| `agentsMd` | no | Path to an AGENTS.md snippet appended to the user's `AGENTS.md`. |
| `premisesRef` | no | Pointer into `docs/` so users can find the invariants this block obeys. |

### File copy convention

The CLI walks the block directory and copies every file at the same
relative path into the user's project. So `MonoDreams/cursor/Cursor.cs`
in this repo lands at `MonoDreams/cursor/Cursor.cs` in the user's
project. Namespaces stay aligned because C# namespaces are file-path
independent — the file at `MonoDreams/cursor/Component/CursorController.cs`
still declares `namespace MonoDreams.Component.Cursor`.

### Worked example

[`cursor/block.json`](./cursor/block.json) — the smallest
non-foundation block, depending on only `foundation` and `rendering`.
The block dir contains 7 source files; the manifest only carries the
metadata. Drift is impossible by construction: adding a file to the
directory makes it part of the block automatically.

## Authoring a block

1. Create `MonoDreams/<name>/` and drop your source files in. Keep
   the block self-contained — anything that cuts across two blocks
   either belongs to a parent block or needs splitting.
2. Write `block.json` next to the code. Declare every dependency. The
   CLI fails loudly on a missing reference rather than guessing.
3. List any NuGet packages the source `using`s. The CLI injects them
   into the user's csproj at install time.
4. Add `postInstallNotes` with the *wiring*: which systems go where
   in the pipeline, what assets need to load, anything not derivable
   from reading the files.
5. Validate against `block.schema.json`.

## Validation

Any JSON Schema validator works. From the repo root with
[`ajv-cli`](https://github.com/ajv-validator/ajv-cli):

```bash
ajv validate -s MonoDreams/block.schema.json -d 'MonoDreams/*/block.json'
```

The CLI also runs validation before any install.
