MonoDreams
==========

<p align="center">
  <br>
   <img src="/Icon/monodreams-logo.png" width="420" alt="MonoDreams — waves & waning moon ASCII logo" title="MonoDreams" />
  <br>
</p>
<p align="center">
A code-first ECS game engine powered by MonoGame
</p>

![NuGet Version](https://img.shields.io/nuget/vpre/MonoDreams.Cli?link=https%3A%2F%2Fwww.nuget.org%2Fpackages%2FMonoDreams.Cli%2F)
![MIT License](https://img.shields.io/crates/l/mit?link=https%3A%2F%2Fgithub.com%2Froo-oliv%2Fmonodreams%2Fblob%2Fmain%2FLICENSE)

## Why MonoDreams

- **You own the engine source.** Every system, every component lives inside your project. Distributed like shadcn, the source is yours to read and change.
- **Composable.** The engine ships as 14 small source modules (foundation, rendering, physics, collision, level-loading, dialogue, audio, …). Use only what you need.
- **ECS-pure.** Built on [DefaultEcs](https://github.com/Doraku/DefaultEcs). Components hold data, systems hold logic. Build performant games easily.
- **Easy learning curve.** By using an ECS design, you can focus on only a few components and systems at a time without having to wrap your head around tons of concepts at once.
- **AI-agent friendly.** Let AI do the code heavy lifting, so you focus on the vision, the art, and all the creative parts. Source lives where agents can read it. Each module ships with a `module.json` manifest, and the repo's `docs/` directory captures engine invariants per domain, meaning your AI agent will know what to do by itself.

## Quickstart

Install the CLI as a .NET global tool:

```bash
dotnet tool install -g MonoDreams.Cli
```

Scaffold a new project (this also installs the `foundation` module):

```bash
monodreams init MyGame
cd MyGame
```

Add the modules you need:

```bash
# A complete preset — procedural shape-driven runner
monodreams add --preset infinite-runner

# Or pick specific modules
monodreams add rendering camera physics collision
```

Build and run:

```bash
dotnet run
```

`monodreams list` shows every module and preset; `--verbose` adds deps and NuGet refs.

Both `init` and `add` spell "which project" the same way — `--dir <path>` (`--project` still
works as a deprecated alias). Unrecognized options are rejected by name rather than mistaken
for a module or a path:

```console
$ monodreams add rendering --dryrun
error: unknown option '--dryrun' for `monodreams add`. Did you mean '--dry-run'?
       Run `monodreams add --help` for the options it accepts.
```

## The 14 modules

```
foundation              required base — installed by `monodreams init`
├── rendering           unified draw stack: sprites + procedural meshes,
│   │                   culling, Y-sort, render targets, Camera class
│   ├── rendering-text  BitmapFont text + revealable typewriter
│   ├── camera          follow-target system (Camera class lives in rendering)
│   ├── cursor          textured cursor with hover types
│   ├── debug           collider/sprite overlays, screenshot capture
│   ├── level-ldtk      load LDtk-exported levels
│   └── ui              flexbox layout, builders, button primitives
│       └── dialogue    YarnSpinner integration
├── physics             velocity + gravity, usable without collision
│   └── collision       AABB + SAT detection, message-based responses
├── level-loading       LoadLevelRequest, EntitySpawnRequest plumbing
├── level-editor        in-game Edit run mode over the real pipeline (scaffold)
└── audio               one-shot SFX, loops, interruptible sources (desktop + web)
```

## Project layout after `init` + `add`

```
MyGame/
  MyGame.csproj
  Program.cs              ← your game entry (scaffolded by `init`)
  monodreams.json         ← records installed modules
  MonoDreams/
    foundation/           ← copied from the engine
      Screen/, State/, Component/, System/, Input/, Util/, ...
    rendering/
    physics/
    ...
```

Everything under `MonoDreams/` is yours. The CLI never reaches back into your code — `monodreams add <new-module>` only adds new files; modifications are always explicit.

## Naming conventions

Browsing a module's source tells you what it contains at a glance:

- **Components** end in `Component` (e.g. `TransformComponent`, `VelocityComponent`, `DialogueStateComponent`).
- **Systems** end in `System` (e.g. `HierarchySystem`, `GravitySystem`, `MasterRenderSystem`).
- **Messages** are publish-subscribe events flowing through the ECS world (e.g. `CollisionMessage`, `LoadLevelRequest`).

## Two reference games

The repo's `MonoDreams.Examples/` directory contains two games, each a clean subset of the module graph:

- **LDtk platformer** — full stack with dialogue, UI, cursor (`monodreams add --preset ldtk-platformer`)
- **Infinite runner** — procedural shapes, no level files, no UI, just physics + collision (`--preset infinite-runner`)

They're the proof that the module boundaries are correct: each example is exactly the union of its preset's modules.

## Docs

- [`MonoDreams/MODULES.md`](./MonoDreams/MODULES.md) — module manifest schema and authoring guide
- [`docs/CORE_TENETS.md`](./docs/CORE_TENETS.md) — engine-wide invariants
- [`docs/<domain>/premises.md`](./docs/) — per-domain technical invariants (rendering, hierarchy-transform, collision, physics, level-loading)
- [`CONTRIBUTING.md`](./CONTRIBUTING.md) — building the engine from source, adding new modules, running the test suite

## Status

MonoDreams is alpha. Module boundaries and APIs may shift between minor versions — but because you own the source, the changes are diffs against your own code, not surprise breaking changes in a binary you can't see.

## License

MIT. See [LICENSE](./LICENSE).

## Special Thanks

This project is intended to support and enable the gamedev community, and to give back. Thanks to:

 - [@MonoGame](https://github.com/MonoGame) (MonoGame Team) for their awesome work on [MonoGame](https://github.com/MonoGame/MonoGame)
 - [@craftworkgames](https://github.com/craftworkgames) (Craftwork Games) for their awesome work on [Monogame.Extended](https://github.com/craftworkgames/MonoGame.Extended)
 - [@prime31](https://github.com/prime31) (Prime31) for their awesome work on [Nez](https://github.com/prime31/Nez)
 - [@Doraku](https://github.com/Doraku) (Paillat Laszlo) for his awesome work on [DefaultECS](https://github.com/Doraku/DefaultEcs)
 - [@OneLoneCoder](https://github.com/OneLoneCoder) (Javidx9) for his [One Lone Coder Youtube Channel](https://www.youtube.com/channel/UC-yuWVUplUJZvieEligKBkA)
 - [@kyleschaub](https://github.com/kyleschaub) (Challacade) for his [Challacade Youtube Channel](https://www.youtube.com/@Challacade)
 - [@spavkov](https://github.com/spavkov) (Slobodan Pavkov) for his [My Public Interface blog](https://blog.roboblob.com/)
 - [@MaddyThorson](https://github.com/MaddyThorson) (Madeline Stephanie Thorson) for her [articles, codes, and tools](https://maddymakesgames.com/index.html#articles)
 - [@NoelFB](https://github.com/NoelFB) (Noel Berry) for his codes and [his blog](https://noelberry.ca/)
 - [@tkarras](https://github.com/tkarras) (Tero Karras) for his [NVIDIA Developer blog Posts](https://developer.nvidia.com/blog/author/tkarras/)
 - [@davidluzgouveia](https://github.com/davidluzgouveia) (David Gouveia) for his [contributions to GameDev StackExchange](https://gamedev.stackexchange.com/users/11686/david-gouveia)
 - [@BoardToBits](https://github.com/BoardToBits) for their [Board To Bits Games Youtube Channel](https://www.youtube.com/@BoardToBitsGames/featured)
 - [@deepnight](https://github.com/deepnight) (Sébastien Bénard) for his awesome work on [LDtk (Level Designer Toolkit)](https://ldtk.io/)
 - Mark Brown for his [Game Maker's Toolkit Youtube Channel](https://www.youtube.com/@GMTK/featured)
 - The Game Developers Conference for their [GDC Youtube Channel](https://www.youtube.com/@Gdconf)
 - My wife and my family for their support and patience ❤️
