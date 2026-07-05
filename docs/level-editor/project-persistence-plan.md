# Project persistence & versioning — saving MonoDreams levels/projects

> **Status: PROPOSAL for review, 2026-07-04.** How a MonoDreams game **saves and
> versions its levels/project** — the gap behind "there is still no save
> mechanism." What exists today (`SceneWriter` + `LoadSceneRequest` +
> `SceneReaderSystem`) is a working scene *serializer*; this plan turns it into a
> project *persistence system*: versioned files in the source tree, loadable as
> the game's real levels, portable to every MonoGame target, and diffable in git.
>
> Decisions already made by the gamedev (banked): **native-first** — the native
> `.mdscene` becomes the game's real level format (`LoadLevelRequest` boots it;
> LDtk/Blender become import-only); **a minimal project manifest** makes "a
> MonoDreams project" a first-class versionable unit; **file-IO-leaning** for the
> write path, with the explicit concern *"what do I lose straying from MGCB,
> especially for console (Switch/PS/Xbox) portability?"* — resolved in §1.

---

## 0. Open decisions for you (ranked)

Everything else proceeds on recommendations. These still shape the build:

1. **Project-root resolution for the editor write (§4).** The editor runs from the
   build-output dir; to write into the *source* tree it must locate the project
   root. Recommended: **an env var `MONODREAMS_PROJECT_ROOT`** set in the same dev
   run-config that already carries `--editor` (one line you edit anyway), with a
   **walk-up-to-find-the-manifest fallback**; if neither resolves (e.g. a shipped
   build), **Save is disabled with a loud message**, never a crash. Accept env-var
   primary + walk-up fallback + fail-safe-disable?
2. **How new `.mdscene` files get bundled for the game to read (§3).** Adding a
   level shouldn't require hand-editing `Content.mgcb`. Recommended: an
   **MSBuild/MGCB glob** that emits a `/copy:` (or copy-to-content) for every
   `Content/Levels/**/*.mdscene` at build — zero-touch, console-correct. Fallback:
   the editor appends the `/copy:` line when it first saves a new scene (the
   editor already edits dev files). Accept the glob approach (validated during
   implementation, fallback if the toolchain fights it)?
3. **Manifest name/format (§5).** Recommended: `game.mdproj` (JSON, same canonical
   serializer as scenes), at the content project root. Fields v1: `formatVersion`,
   `startScene`, `levelsDir` (default `Levels`), `assetRoots`. Name/fields OK, or
   do you want a different name / more in it now (engine version pin, build
   settings)?
4. **LDtk/Blender's fate under native-first (§6).** Recommended: keep them as
   **import-only** (a one-way "import this LDtk/Blender level → a `.mdscene` you
   then own") and stop booting them directly — resolving the CORE_TENETS
   parser-asymmetry backlog. Alternative: leave both live loaders in place
   indefinitely (three formats). Import-only, or leave as-is for now?
5. **Scene-to-scene references / multi-level (§8, likely DEFER).** The island is
   one scene; the game has many, with exits between them (a door → another level).
   Recommended: **defer** cross-scene references to a dedicated pass — v1 persists
   and boots single scenes by id; an "Exit" trigger already carries a string
   identity your game code can map to a `LoadLevelRequest`. Defer, or is
   multi-level linking needed for the first playable?

---

## 1. The portability question, resolved (why we DON'T lose console support)

Your concern — *straying from MGCB could hurt portability to Switch/PS/Xbox* — is
the right instinct, and it resolves cleanly once reading and writing are separated.

**Reads (the shipped game, every platform):** `TitleContainer.OpenStream` is
MonoGame's portable abstraction for reading **bundled, read-only title content**,
and it is the console-safe path (consoles sandbox arbitrary `System.IO.File`, but
`TitleContainer` is exactly what works across DesktopGL, KNI/web, **and consoles**).
Level files are bundled by MGCB's **`/copy:` action** (raw data, not compiled to
`.xnb`). **This is already how `blender_level.json` ships and loads** —
`Content.mgcb:1578` `/copy:./blender_level.json`, read via
`TitleContainer.OpenStream` in `BlenderLevelParserSystem.cs:98`. So the shipped
read path stays 100% inside MGCB + TitleContainer. **Zero portability loss.**

**Writes (the editor):** `File.WriteAllText` into the source tree is a
**desktop-only, dev-only** capability, guarded behind the editor run flag +
`OperatingSystem.IsMacOS/IsWindows/IsLinux`. You never author a level on a console,
so that code path never runs (nor needs to compile into) a console build. The
portability risk only ever applied to *reads*, and reads never leave TitleContainer.

**What we genuinely forgo by using `/copy:` (raw) instead of a full MGCB
*processor* for scene files:** build-time processing of the scene file itself —
which is irrelevant, because a scene is *data* (JSON parsed at load), not an asset
needing compression/mipmaps. The **assets a scene references** (textures, fonts)
still go through the real MGCB processor pipeline for shipping (via the
`file:`→content-key graduation, §7). So: scene files = copied data (portable),
referenced assets = real content (processed). Nothing regresses.

**One-line rule for the docs:** *the shipped game reads levels through
`TitleContainer` over MGCB-`/copy:`-bundled files (console-portable); only the
desktop editor writes, via file IO into the source tree.*

---

## 2. What exists today vs. what's missing

| Concern | Today | This plan |
|---|---|---|
| Where Save writes | `BaseDirectory` (`bin/…` build output) — ephemeral, gitignored, clobbered on rebuild | Source `Content/Levels/<id>.mdscene`, versioned in git |
| Can the saved scene be the game's level? | No — game boots LDtk/Blender via `LoadLevelRequest`; native scenes load only via the editor's Load button | Yes — native-first: `LoadLevelRequest(id)` → `<id>.mdscene` via TitleContainer + native reader |
| Diffable/mergeable in git | No — `WriteIndented=true` only; ordering/floats can churn | Canonical, stable-ordered, invariant-float; tested byte-stable |
| A "project" unit | No | `game.mdproj` manifest (start scene, levels dir, asset roots) |
| Console-portable read | (Blender already is; native isn't wired to the game boot) | Native read = TitleContainer over `/copy:` — same proven path |
| Load→edit→Save round-trip | Fixed (Slice 3.5 re-tag) but writing to the wrong place | Fixed **and** writing to the versioned source tree |

---

## 3. Scene files: format, location, bundling

- **Format:** the existing `.mdscene` JSON (`SceneData`: `version`, `camera`,
  `layers[]`, `sources[]`, `entities[]`). Extension `.mdscene`. One file per level.
- **Location (source, versioned):** `MonoDreams.Examples.Core/Content/Levels/<id>.mdscene`
  (the game's content project, beside the other content). Committed to git — this
  is the versioned unit.
- **Bundling for the game to read:** an MGCB `/copy:` per level (like
  `blender_level.json`), ideally emitted by an **MSBuild/MGCB glob** over
  `Content/Levels/**/*.mdscene` (open decision 2) so adding a level is zero-touch.
  On build, MGCB copies each `.mdscene` into the title content root on every
  platform; the game reads it via `TitleContainer.OpenStream(Path.Combine(
  Content.RootDirectory, "Levels", id + ".mdscene"))` — the same shape as the
  Blender read.
- **Determinism (§ shared with the serializer):** replace the ad-hoc
  `JsonSerializerOptions { WriteIndented = true }` with a **canonical writer**:
  stable property order, `CultureInfo.InvariantCulture` round-trippable floats
  (`"R"`/shortest-round-trip), stable entity order (by a persisted stable id — see
  §9), sorted component maps. Invariant: **serialize(world) is byte-identical
  across runs, and load-then-save equals the source file byte-for-byte** (the
  fixed-point the Slice-3.5 re-tag started; this makes it byte-level and *tested*).
  This is what makes git diffs meaningful and merges tractable (JSON scene merges
  are still hand-work at worst — like Unity YAML — but stable serialization is the
  precondition for them being possible at all).

---

## 4. The editor write path (desktop-dev-only) → source tree

The editor runs from build output; the versioned files live in source. Bridge:

- **Project-root resolution** (open decision 1): `MONODREAMS_PROJECT_ROOT` env var
  (set in the dev run config) points at the project/content root; fallback walks up
  from `BaseDirectory` to find the `game.mdproj` manifest. Resolved once at editor
  init into an `EditorProjectContext { ProjectRoot, LevelsDir, Manifest }`.
- **Write:** Save serializes and writes `File.WriteAllText(ProjectRoot/LevelsDir/<id>.mdscene)`
  — into **source**, so git sees it immediately. Guarded: desktop OS + editor flag
  + resolved root; otherwise Save is **disabled with a loud, visible reason** (the
  save-guard button state we already have gains a "no project root" disabled cause
  alongside the "playing" one). No silent no-op, no crash.
- **Immediate reload in-editor:** the editor reads the scene it just wrote directly
  from the source path (desktop file IO) for instant Load — no build round-trip.
  The MGCB `/copy:` matters for *shipped/other-platform* runs and fresh game
  launches, not for the author's own reload.
- **`ExportScene` is repurposed/retired:** desktop Save no longer targets
  `BaseDirectory`; it targets the resolved source path. `IPlatformServices` keeps a
  narrow role (web download stub stays; desktop scene write moves to the
  project-context path). Document the migration.

*Anchor:* this is precisely Unity/Godot's "the editor operates on the project
source directory" — adapted to an in-game editor by resolving the root at dev time
and hard-disabling the write anywhere it can't (shipped builds, consoles).

---

## 5. The project manifest (`game.mdproj`)

A small versioned JSON at the content-project root, same canonical serializer:

```json
{ "formatVersion": 1,
  "startScene": "island",
  "levelsDir": "Levels",
  "assetRoots": ["Island", "Atlas", "Objects"] }
```

- **Read by the game:** at boot, resolve `startScene` → `LoadLevelRequest(startScene)`
  (replacing the hardcoded `RequestedLevelComponent`/`ScreenName.Game` dance for the
  editor/native path). Read by `TitleContainer` (bundled like a scene).
- **Read by the editor:** anchors the project root (§4), lists levels (the
  `levelsDir` scan populates a "levels" list for a future open/switch-level UI),
  declares asset roots (the palette's catalog scan roots, unifying §7).
- **Versionable unit:** committing `game.mdproj` + `Content/Levels/*.mdscene` +
  the owned assets they reference **is** versioning the project.
- **Scope guard:** v1 is the four fields above. Engine-version pinning, build
  settings, per-scene metadata are deferred (open decision 3 can add fields now if
  you want them).

---

## 6. Native-first level loading (the game boots `.mdscene`)

- `LoadLevelRequest(id)` resolves **native-first**: if `Content/Levels/<id>.mdscene`
  exists (TitleContainer probe), the **native reader** loads it (the existing
  `SceneReaderSystem` path, generalized off the editor-only `LoadSceneRequest` so it
  also serves the game boot). This unifies the today-split load paths and closes the
  CORE_TENETS "LDtk component-driven vs Blender message-driven asymmetry" backlog.
- **LDtk/Blender → import-only** (open decision 4): a desktop-editor action "Import
  LDtk/Blender level → `.mdscene`" runs the existing parser once and serializes the
  result to a native scene you then own and edit. The live LDtk/Blender *boot*
  loaders are removed from the game path (their parser code becomes import
  machinery). Migration: the Examples LDtk/Blender levels get imported to
  `.mdscene` once and committed, or kept as import fixtures.
- **Back-compat:** during migration both can coexist (native-first probe, fall back
  to LDtk/Blender by the current rules) so nothing breaks mid-refactor; the
  fallback is removed when the Examples levels are migrated.

---

## 7. Assets & the versioning boundary

A versioned scene that references unversioned assets breaks on a fresh checkout —
so the asset boundary is part of persistence:

- **Your own art (island, characters):** committed under `Content/` as real MGCB
  content (processed, shipped, portable) — referenced by content keys.
- **Third-party placeholder packs (itch):** stay gitignored behind
  `Content/Island/MANIFEST.md` (licenses forbid redistribution). A scene
  referencing a missing `file:` asset already fails loud (magenta placeholder) —
  good enough for the placeholder phase.
- **`file:` → content-key graduation:** when art finalizes, an asset moves from the
  gitignored drop folder into committed MGCB content and its `AssetKey` flips
  `file:Island/tree.png` → `content:Objects/tree`. A scene is "ship-ready / fully
  portable" when it has **zero `file:` keys** — a checkable invariant (a lint the
  editor or a test can assert per scene). Document as a premise.
- The manifest's `assetRoots` unifies the palette catalog roots with the versioning
  story (one declared list).

---

## 8. Web & the round-trip end to end

- **Web:** authoring/versioning is **desktop-only** (you commit on desktop). Web
  **loads** committed, bundled `.mdscene` via TitleContainer (works on KNI) — read
  only. The web `ExportScene` stub stays a no-op-with-warning (no console/web
  authoring); a browser-download convenience remains a deferred nicety.
- **Full loop:** editor **Save** → `File.WriteAllText` to source
  `Content/Levels/island.mdscene` (git sees it) → editor reloads from source
  directly; a fresh game run (any platform) → MGCB `/copy:` bundled the file →
  `LoadLevelRequest("island")` → TitleContainer read → native reader builds the
  world → **Play**. Load→edit→Save is the tested byte-stable fixed point.

---

## 9. Stable entity identity (prerequisite for clean diffs)

Deterministic files need **stable per-entity ids** in the file (today entity order
/ `EditorId` is per-session — a re-save could reorder `entities[]` and churn the
diff). Add a persisted stable scene-local id per serialized root (monotonic,
assigned at first save, preserved across load/save), and order `entities[]` by it.
This also underpins any future cross-scene references (§0 decision 5) and prefab
identity. Small, but load-bearing for "version projects" — it's what makes a
one-entity move a one-line diff instead of a reshuffle.

---

## 10. Slice plan (dependency-ordered; tests per slice)

1. **Canonical serializer + stable ids + byte-stable fixed-point test.** Replace
   `JsonSerializerOptions`; add stable scene-local ids + deterministic ordering.
   *Tests:* serialize twice = identical bytes; load→save = source bytes; a moved
   entity = a minimal diff. (No new feature surface — hardens what exists.)
2. **`game.mdproj` manifest + `EditorProjectContext` + project-root resolution.**
   Manifest model + canonical read/write; env-var + walk-up resolution; the
   save-guard gains the "no project root" disabled cause. *Tests:* manifest
   round-trip; root resolution (env, walk-up, fail→disable); pure, no window.
3. **Editor writes to the source tree.** Repoint Save from `BaseDirectory` to
   `ProjectRoot/LevelsDir/<id>.mdscene`; scene id/name in the toolbar (name the
   scene, not a fixed `editor_scene.json`); in-editor reload reads source directly.
   *Tests:* Save writes the resolved source path; disabled when unresolved;
   load-from-source reload.
4. **Native-first `LoadLevelRequest` + bundling glob.** Generalize the native
   reader onto `LoadLevelRequest`; TitleContainer resolution `Levels/<id>.mdscene`;
   the MGCB/MSBuild `/copy:` glob for `Content/Levels/**`. Manifest `startScene`
   drives boot. *Tests:* `LoadLevelRequest(id)` loads a bundled native scene
   (in-process over the real reader); boot resolves `startScene`; a `GameTestRunner`
   headless boot of a committed native scene renders non-blank.
5. **LDtk/Blender import-only + Examples migration.** Import action → `.mdscene`;
   migrate the Examples levels; native-first fallback removed. *Tests:* import
   produces an equivalent native scene; migrated Examples levels boot native.
6. **Ship-readiness lint + docs.** The "zero `file:` keys = portable" check; premises
   (canonical/byte-stable, TitleContainer-read/desktop-write, native-first,
   manifest, stable-id); update CORE_TENETS §9, flow doc, `scene-format.md`, the
   island plan's "Save" references, and record the parser-asymmetry backlog as
   resolved.

---

## 11. Deferred (with triggers)

| Deferred | Revisit when |
|---|---|
| Cross-scene references / multi-level graph | The first door between two levels (§0.5) |
| Prefab semantics (edit-propagates) | Repeated identical-building edits (needs the stable ids from §9) |
| In-editor "open/switch level" browser UI | More than a couple of levels exist |
| Browser-download of scenes on web | Anyone wants to author on web (unlikely) |
| Full MGCB *processor* for `.mdscene` (vs `/copy:`) | A scene needs build-time transformation (not foreseen — data doesn't) |
| Merge tooling for scene conflicts | Team authoring / frequent scene merge pain |

## See also
- [`island-authoring-plan.md`](island-authoring-plan.md) — the tools that produce
  the scenes this plan persists; its "Save" is what this plan makes real.
- [`scene-format.md`](../../MonoDreams/level-editor/docs/scene-format.md) — the
  `.mdscene` schema the canonical serializer stabilizes.
- [`docs/web-targeting.md`](../web-targeting.md) — the Reach/TitleContainer
  constraints behind the portability resolution.
