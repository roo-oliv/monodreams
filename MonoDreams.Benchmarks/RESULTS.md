# DefaultEcs vs Arch — baseline measurements

Wave-0 baseline for [issue #119](https://github.com/roo-oliv/monodreams/issues/119) (migrating the
ECS backend from DefaultEcs to Arch behind a MonoDreams-owned facade). Every number below comes from
one complete `-c Release` run of this project on 2026-08-19; nothing is estimated, and nothing is
copied from another machine.

**No engine code is measured here.** The benchmarks talk to the two ECS libraries directly, over
component shapes that mirror the engine's (see `Components.cs`), so the numbers are about the
backends, not about MonoDreams' systems.

## Environment

| | |
|---|---|
| Machine | Apple M4 Pro, 12 physical / 12 logical cores, **arm64** |
| OS | macOS Sequoia 15.7.1 (Darwin 24.6.0) |
| Runtime | .NET SDK 8.0.416 — .NET 8.0.22, Arm64 RyuJIT armv8.0-a |
| Harness | BenchmarkDotNet 0.15.8 |
| Backends | **DefaultEcs 0.18.0-beta01** (the version the engine pins today) vs **Arch 2.1.0** (latest stable) |
| Total run time | 20 min 1 s, 48 benchmark cases |

Reproduce with:

```bash
dotnet run --project MonoDreams.Benchmarks -c Release -- --filter '*'
```

Reports land in `MonoDreams.Benchmarks/bin/Release/net8.0/artifacts/results/` (git-ignored). A single
family runs with e.g. `--filter '*Iteration*'`.

### Job settings (and where they are deliberately reduced)

| Family | Job | Why |
|---|---|---|
| Iteration, one-byte/managed churn | `self-restoring` — 3 warmup + 7 iterations, invocation count auto-tuned | The operation leaves the world as it found it, so BenchmarkDotNet may batch many operations per iteration. That reports **warm steady-state** cost, which is the right model for work a game does every frame. |
| Entity creation | `per-invocation` — 3 warmup + 10 iterations, **invocation count pinned to 1** (unroll 1) | Creation is not self-restoring: each operation ends holding a world of up to 1M entities, torn down in `[IterationCleanup]`. Auto-tuning would keep several such worlds alive at once (hundreds of MB for the managed case) and drop a GC pause inside the measurement. |
| Zero-sized tag churn | `heavy-churn` — 1 warmup + **3 iterations**, invocation count pinned to 1 (unroll 1) | One cell of this family (DefaultEcs, 1M) takes **over two minutes per operation**; auto-tuning it would run for hours. The reduced budget is affordable because the differences this family reports are orders of magnitude, not percentages. |

The `heavy-churn` cells are therefore measured **cold** (one operation per iteration, no batching),
so they compare fairly against each other but not against the warm rows of the other churn family.

## 1. Iteration — one pass over N entities, read one component, write another

The shape of every per-frame system (`VelocitySystem`, `SpritePrepSystem`, `YSortSystem`). Three
idioms: DefaultEcs's pre-built `EntitySet` + `entity.Get<T>()` (what `AEntitySetSystem` does today),
Arch's per-entity delegate query (what the facade's `EntitySystem<T>` will run by default), and
Arch's chunk loop over contiguous spans (the wave-3 conversion target).

| Entities | Payload | DefaultEcs (EntitySet + Get) | Arch (per-entity query) | Arch (chunk loop) |
|---:|---|---:|---:|---:|
| 10 000 | struct | 20.92 µs | **8.84 µs** (2.4× faster) | **6.81 µs** (3.1×) |
| 100 000 | struct | 201.35 µs | **87.46 µs** (2.3×) | **66.90 µs** (3.0×) |
| 1 000 000 | struct | 2 062.31 µs | **934.28 µs** (2.2×) | **750.18 µs** (2.7×) |
| 10 000 | managed `DrawComponent` | 28.07 µs | **20.53 µs** (1.4×) | **10.52 µs** (2.7×) |
| 100 000 | managed `DrawComponent` | 291.15 µs | **232.76 µs** (1.25×) | **111.78 µs** (2.6×) |
| 1 000 000 | managed `DrawComponent` | 2 864.21 µs | 2 850.27 µs (1.005×) | **2 220.37 µs** (1.3×) |

All six cases allocate nothing per operation, in both backends.

**Reading:** Arch wins iteration at every size. On struct components even its *per-entity* query —
the idiom that requires no system rewrite — is 2.2–2.4× faster than today's path, so the wave-2 swap
should improve iteration before wave 3 converts anything. On the managed component the per-entity
query's advantage collapses to nothing at 1M (2 850 µs vs 2 864 µs): with a reference-typed component
the pointer chase dominates and the storage layout stops mattering. Only the chunk loop keeps a real
margin there (1.3×), which is the H8 "managed path" question answered with a number: **converting
managed-component systems to chunk iteration is where their remaining upside is.**

## 2. Entity creation — build a world from empty and populate it

The level-load shape (`LDtkTileParserSystem` / `SceneReaderSystem` minting a scene in one burst).
DefaultEcs adds components one at a time to an already-created entity; Arch mints the entity straight
into its final archetype. Each idiom is the natural one for its backend.

| Entities | Payload | DefaultEcs | Arch | Allocated (DefaultEcs → Arch) |
|---:|---|---:|---:|---|
| 10 000 | 2 structs | **689.4 µs** | 1 366.9 µs | 2 108 KB → **566 KB** |
| 100 000 | 2 structs | **10 780.2 µs** | 14 181.6 µs | 17 466 KB → **5 421 KB** |
| 1 000 000 | 2 structs | 62 307.3 µs | **33 393.3 µs** | 145 946 KB → **77 489 KB** |
| 10 000 | struct + managed | **1 000.9 µs** | 1 665.4 µs | 3 280 KB → **1 738 KB** |
| 100 000 | struct + managed | 22 108.0 µs | **21 342.0 µs** | 29 193 KB → **17 139 KB** |
| 1 000 000 | struct + managed | 290 444.8 µs | **216 134.3 µs** | 263 153 KB → **194 824 KB** |

**Reading:** the crossover sits between 100k and 1M entities. Below it DefaultEcs creates faster
(Arch pays archetype/chunk setup that a small world never amortises); at 1M Arch is 1.9× faster on
structs and 1.3× on the managed shape. Arch allocates **2–3× less** at every size — the number that
matters for a mid-load GC spike, and for the flat-heap assertions the demos already make. Real
MonoDreams levels are far below the crossover, so expect level loading to get *slightly* slower and
markedly less allocation-heavy.

## 3. Structural churn — add a component to N entities, then remove it from all N

The `CullingSystem` shape: `VisibleComponent` goes on and off entities as they enter and leave the
camera view, **every frame**. Under DefaultEcs a structural change is a bitmask flip in a sparse set;
under Arch it moves the entity and everything it carries into another archetype's chunk. This is
hazard H2 of the plan, measured.

### 3a. One-byte tag and managed component (warm steady state)

| Entities | Payload | DefaultEcs | Arch | Winner |
|---:|---|---:|---:|---|
| 10 000 | one-byte tag | **107.2 µs** | 513.3 µs | DefaultEcs 4.8× |
| 100 000 | one-byte tag | **1 154.6 µs** | 5 184.2 µs | DefaultEcs 4.5× |
| 1 000 000 | one-byte tag | **10 793.9 µs** | 61 305.5 µs | DefaultEcs 5.7× |
| 10 000 | managed `DrawComponent` | **354.0 µs** | 570.1 µs | DefaultEcs 1.6× |
| 100 000 | managed `DrawComponent` | **3 956.5 µs** | 5 720.5 µs | DefaultEcs 1.4× |
| 1 000 000 | managed `DrawComponent` | **37 637.5 µs** | 65 929.6 µs | DefaultEcs 1.8× |

Neither backend allocates on this path.

**Reading:** with a normally-sized component, DefaultEcs's sparse set beats Arch's archetype move by
1.4–5.7×, exactly as the architecture predicts. This is the one axis where the migration *costs*
performance, and it is the axis MonoDreams uses every frame — so a system that adds and removes a
component per entity per frame should be treated as a wave-2/3 regression risk and measured, not
assumed. (Per structural change the absolute cost is still small: ~61 ns under Arch at 1M entities.)

### 3b. Zero-sized tag — `VisibleComponent`'s exact shape (cold, reduced iteration budget)

| Entities | DefaultEcs | Arch | Ratio |
|---:|---:|---:|---:|
| 10 000 | 42.34 ms | **5.99 ms** | 7× |
| 100 000 | 1 374.16 ms | **59.31 ms** | 23× |
| 1 000 000 | **137 164.68 ms** (2 min 17 s) | **63.33 ms** | **2 166×** |

**This is the most consequential measurement of wave 0, and it is a finding about the engine as it
stands today, not about the migration.** DefaultEcs 0.18.0-beta01 puts zero-sized components on a
special path whose `Remove<T>()` degrades with the number of entities *currently carrying* the
component; the add half stays fast, and the identical sweep with a one-byte tag (§3a) is three orders
of magnitude cheaper. A side probe against DefaultEcs alone (not part of the committed suite) put the
per-removal cost at ~1.1 µs with 1 000 tagged entities, ~3.3 µs at 5 000, ~12.8 µs at 100 000 —
growing with the tagged population and independent of world size — so a full sweep is quadratic.

`MonoDreams.Component.Draw.VisibleComponent` is an empty struct, and `CullingSystem` removes it every
frame from every entity leaving the view. **The engine is on this path right now.** Two consequences
worth carrying into the next waves:

- It is an argument *for* the migration that is independent of iteration speed, and it should be
  stated as such in the PR that swaps the backend.
- It is also fixable without Arch (give the tag a byte), which means wave 2 must not credit the
  backend swap with a win that a one-line component change would also have delivered. Compare
  against §3a, not against §3b, when judging whether Arch made culling faster.

## 4. What this means for the plan

1. **Iteration gets faster for free** (2.2–2.4× on struct components) as soon as the facade runs on
   Arch, before any system is converted. Wave 3's chunk conversion adds ~25–40% on top for structs
   and is the *only* real win for managed components.
2. **Structural churn gets slower** for normally-sized components (1.4–5.7×). Wave 2's acceptance
   pass should watch the culling/prep path specifically, and wave 3 must not convert structural
   mutators to chunk iteration without buffering (already required by contract item 69).
3. **Level loading gets slightly slower below ~100k entities and allocates 2–3× less.** No action
   needed; worth a line in the wave-2 PR body when the demo heap assertions move.
4. **The zero-sized-component pathology** is a live engine problem, quantified. Whatever happens to
   the migration, it should be recorded as a premise or an issue on its own.
5. Nothing measured here invalidates the plan: no shape the engine relies on failed to run under
   Arch 2.1.0, and every family completed on this machine in 20 minutes.

## Appendix — component shapes under test

Defined in `Components.cs`, mirroring the engine without referencing it (MonoGame types are stood in
for, so the benchmark project stays free of the content pipeline):

| Benchmark type | Mirrors | Shape |
|---|---|---|
| `BenchPosition`, `BenchVelocity` | `TransformComponent` / `VelocityComponent` payload | struct, 2 floats each |
| `BenchVisible` | `VisibleComponent` | struct, **no fields** (zero-sized) |
| `BenchTagByte` | — (control for §3b) | struct, 1 byte |
| `BenchDrawComponent` | `DrawComponent` | **class**, 15 fields: `Vector2`s, floats, packed colour, nullable rect, two object references, a string |
