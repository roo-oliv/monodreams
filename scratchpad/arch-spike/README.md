# Wave 0 — Arch target proofs + DefaultEcs measurement harness (THROWAWAY)

Spike deliverables for [issue #119](https://github.com/roo-oliv/monodreams/issues/119) wave 0.
This whole directory is throwaway (wave 4 deletes it, like `scratchpad/blazor-spike/`); it exists to
turn the migration plan's riskiest assumptions into measured facts before wave 1 writes a line of
facade code.

Nothing here references MonoDreams. Arch and DefaultEcs are referenced directly and deliberately —
this tree rides the `scratchpad/` allowlist entry of the wave-1 `EcsBoundaryLintTests` (plan D15).

| Leg | Contract item | Result |
|---|---|---|
| Arch in a **KNI/BlazorGL WASM** head — builds, publishes (trimmed), **runs in Chrome** | 2 (C2) | **PASS** |
| Arch under **NativeAOT** (`PublishAot=true` + `Arch.AOT.SourceGenerator`) — publishes and **runs** | 2 (C2) | **PASS**, but only with a namespace shim (see finding 1) |
| DefaultEcs **subscribe-replay + `NotifyChanged`** measurements | 42, 66 | measured — see the table below |
| **Facade-fired events over Arch** (H7/D1) — Added/Changed(old,new)/Removed, singleton notifications, predicate membership, M10, the LDtk mass-parser shape | 3 (C3), 41 | **PASS** — 93/93 checks, exit 0 |

---

## 1. Target proofs

Both heads compile the **same** exercise (`shared/ArchExercise.cs`), so the two targets make the same
claim about the same operations and a divergence surfaces as a failing check rather than as a
difference between two hand-written programs. The exercise covers creation (struct-only archetypes
and one carrying a managed class component), queries (per-entity `ForEach` **and** chunk/span), and
structural change (Add / Remove / Destroy + dead-handle liveness). Every step self-verifies; the
head turns the failure count into an exit code (`AotConsole`) or into `document.title`
(`ARCH-WASM PASS` / `ARCH-WASM FAIL`, read by `run-in-chrome.mjs`).

### Reproduce

```bash
# --- WASM leg (needs the wasm-tools workload; installed on this host) ---------------------------
dotnet build   scratchpad/arch-spike/wasm-head/WasmHead.Web.csproj -c Release -p:MonoDreamsPlatform=web
dotnet publish scratchpad/arch-spike/wasm-head/WasmHead.Web.csproj -c Release   # Blazor trims on publish

# serve the published bundle with application/wasm MIME, then drive it in the system Chrome:
#   (any static server works; python's http.server needs the .wasm MIME added)
PUPPETEER_SKIP_DOWNLOAD=1 npm i puppeteer-core@23      # node_modules must resolve from the script's dir
SPIKE_URL=http://127.0.0.1:5291/index.html node scratchpad/arch-spike/wasm-head/run-in-chrome.mjs
# -> prints the report, exits 0 only on ARCH-WASM PASS

# --- AOT leg ------------------------------------------------------------------------------------
dotnet publish scratchpad/arch-spike/aot-console/AotConsole.csproj -c Release -r osx-arm64
./scratchpad/arch-spike/aot-console/bin/Release/net8.0/osx-arm64/publish/AotConsole   # exit 0 == pass

# negative control (BOTH heads support it): publish the same program without the generator
dotnet publish scratchpad/arch-spike/aot-console/AotConsole.csproj -c Release -r osx-arm64 -p:UseArchAotGenerator=false
```

### Measured results

| Target | Runtime facts | Outcome |
|---|---|---|
| NativeAOT, `osx-arm64` | `IsDynamicCodeSupported=False`, 2.1 MB Mach-O arm64 binary | 21/21 checks pass, exit 0 |
| KNI/BlazorGL WASM, Chrome 151 headless | `OSArchitecture=Wasm`, `IsDynamicCodeSupported=True` (interpreter), `IsDynamicCodeCompiled=False`, KNI `Xna.Framework 4.2.9001` loaded in the same bundle | 21/21 checks pass, `ARCH-WASM PASS` |

Published WASM bundle: `_framework/` is 16 MB (uncompressed, `.br`/`.gz` siblings included);
`Arch.wasm` is 853 KB of it.

### Findings that matter for waves 2–4

1. **`Arch.AOT.SourceGenerator` 1.0.1 does not compile against Arch 2.1.0 out of the box.** The
   generator emits `using Arch.Core.Utils; ArrayRegistry.Add<T>();` — Arch **1.x**'s namespace
   layout. In Arch 2.1.0 both `ArrayRegistry` and `ComponentRegistry` live in `Arch.Core`, so the
   generated file fails with `CS0103: the name 'ArrayRegistry' does not exist in the current
   context`, and no project-level configuration fixes it. The method signature is unchanged, so a
   5-line forwarding type under the old namespace restores it: `shared/ArchCoreUtilsShim.cs`.
   1.0.1 is the **latest published version** (only 1.0.0 and 1.0.1 exist, Feb 2024), so this is not
   an upgrade away. Wave 2/3 must ship the shim next to the facade, vendor a fixed generator, or
   have the facade own component registration itself — the facade already needs a component-type
   registry for `ReadAllComponents`, so a facade-owned registration path is the likely answer.
2. **Under NativeAOT the registration is MANDATORY, not an optimisation.** With
   `-p:UseArchAotGenerator=false` the publish succeeds identically and the binary then dies on the
   *first* `world.Create`:
   `System.NotSupportedException: 'Position[]' is missing native code or metadata` thrown from
   `Array.CreateInstance` inside `Arch.Core.ArrayRegistry.GetArray`. It is **not** limited to
   managed components — a plain struct component fails, because Arch allocates all component
   storage through `Array.CreateInstance`. Consequence for the plan: on any AOT target, *every*
   component type must be registered before use, and source generators only see their own
   compilation — so the generator (or the facade's registration call) has to run in **each assembly
   that declares components**: `MonoDreams.dll`, every game assembly, and every CLI-scaffolded
   project. That is a wave-2/wave-4 packaging obligation the plan does not currently name.
3. **Under WASM the registration is NOT required.** The same negative control, published through
   the Blazor Release trimmer, passes all 21 checks in Chrome: the WASM runtime keeps dynamic code
   support (interpreter) and the trimmer leaves the component array types reachable. So the AOT
   generator is an *AOT-target* obligation (console/iOS AOT), not a web one — D6's "yes" is correct
   but its scope is narrower than "everywhere".
4. **`Arch.AOT.SourceGenerator` emits references to annotated types with no accessibility check.**
   A component declared as a *private nested* type makes the generated registry fail to compile
   (`CS0122`). Every MonoDreams component is a public top-level type, so this constrains the spike
   (hence `shared/ArchSpikeComponents.cs`), not the engine — but it will bite any game that hides a
   component inside a class.
5. **Arch publishes one AOT analysis warning**, `IL3053: Assembly 'Arch' produced AOT analysis
   warnings`. It does not block the publish and did not produce a runtime failure in this exercise,
   but it means Arch is not annotated as fully AOT-safe: any wave-2 AOT claim has to be backed by a
   run, not by a clean build.
6. **The `.Web` project-name suffix is load-bearing.** `Directory.Build.props` relocates `obj`/`bin`
   for every project built with `-p:MonoDreamsPlatform=web` *except* those whose name ends in
   `.Web`, because Blazor's static-web-assets/`_framework` layout breaks when it moves. The head is
   therefore `WasmHead.Web.csproj`, which is what lets it be built with the contract's
   `-p:MonoDreamsPlatform=web` form and with a plain build, identically.

### What these proofs deliberately do NOT cover

- **No canvas, no game loop, no rendering.** `scratchpad/blazor-spike/` already proved the MonoDreams
  render pipeline runs on BlazorGL. The open question here was narrower: does the ECS backend
  survive the WASM runtime and the publish trimmer while sitting next to the KNI backend. The head
  references KNI types (and reports their assembly identity) so the trimmer cannot drop the backend
  and let the proof pass vacuously — but it never constructs a `Game`.
- **No facade, no events.** Arch's native events stay off per D1; the facade-fired
  Added/Changed/Removed design (contract item 3 / H7) is proved separately in §3.
- **One host only** (macOS/Apple Silicon, .NET SDK 8.0.416). The AOT leg is `osx-arm64`.

---

## 2. DefaultEcs measurement harness

`defaultecs-measurements/` interrogates the **current** backend at the exact version the engine ships
(`DefaultEcs 0.18.0-beta01`) for facts the wave-1 contract tests must be pinned against. It asserts
nothing and cannot "fail" — it reports.

```bash
dotnet run --project scratchpad/arch-spike/defaultecs-measurements/Measurements.csproj -c Release
```

### Headline measurements

| # | Question | Measured answer |
|---|---|---|
| M1 (item **66**) | `SubscribeWorldComponentAdded<T>` over an **already-Set** world component — replay? | **NO replay.** Zero handler calls at subscribe time; the control (subscribe-then-Set) fires `Added` once. |
| M2 (item **42**) | `SubscribeEntityComponentAdded<T>` over **already-present** entity components — replay? | **NO replay.** Two live carriers at subscribe time, zero calls; the control (add-after-subscribe) fires once. `SubscribeEntityComponentChanged` and `…Removed` also do not replay. |
| M3 (item **6** / D14) | `entity.NotifyChanged<T>()` when T is **absent** | **Throws `System.InvalidOperationException`**, message `Entity does not have a component of type <FullTypeName>`. No handler runs before the throw. |

**Consequences.** Contract item 66's conditional resolves: the C4 **singleton no-replay pin stands**
as written, and the LDtk parsers' manual `Has`+`Get` replay (`LDtkTileParserSystem.cs:41-48`) is
load-bearing, not redundant — a facade that *added* replay would double-parse the level. Item 42's
"entity-level replay matches DefaultEcs" resolves to **no replay** for all three entity verbs. D14 is
confirmed with an exact exception type and message to pin.

### Adjacent facts (measured in the same run, to read the headline pins against)

| Operation | Fires |
|---|---|
| `world.Set<T>` when absent / present | `Added` / **`Changed(old→new)`** — the CORE_TENETS §9 Restart shape holds |
| `world.Remove<T>` when present / absent | `Removed` / **nothing** (silent no-op) |
| `world.Set<T>` after a `Remove` | `Added` again — Added-keyed LDtk parsers do re-trigger |
| `entity.Set<T>` when absent / present | `Added` / `Changed(old→new)` — add-or-update (H1) |
| `entity.Remove<T>` when present / absent | `Removed` / **nothing** |
| `entity.NotifyChanged<T>()` on a present component | `Changed` with `ReferenceEquals(old,new) == true`; `Set(new instance)` gives `false` — the discriminator `AudioSystem.cs:141` relies on |
| `entity.Dispose()` | `EntityDisposed`, then `ComponentRemoved` per present component (entity reads `IsAlive == true` inside the handler) |

### ⚠ Contract item 50 is contradicted by measurement

Item 50 pins `world.Dispose` as **event-silent** "matching DefaultEcs". It does not match: in
DefaultEcs 0.18.0-beta01 `world.Dispose()` fires **9 events** on a world holding 3 tagged entities,
1 managed-component carrier and 1 world component —

```
EntityDisposed × 4 (creation order)
ComponentRemoved × 3 (entity struct component, IsAlive == true in the handler)
ComponentRemoved × 1 (managed component)
WorldComponentRemoved × 1
```

— i.e. `EntityDisposed` for every live entity first, then `ComponentRemoved` grouped by component
pool, then world components. An entity disposed *before* teardown is not reported twice.

This matters beyond bookkeeping: `AudioSystem.OnAudioSourceRemoved` (`AudioSystem.cs:133-137`) **does**
run on `world.Dispose` today, so world teardown already cuts live audio instances — the authority
D10 assigns exclusively to `AudioSystem.Dispose`. Wave 1 must either restate item 50 as "world.Dispose
fires the full reactive cascade" (preserving today's behaviour, which is what C7 byte-identity will
demand) or accept a deliberate behaviour change and say so. **The plan should be amended before
wave 1 writes the item-50 contract test.**

---

## 3. H7/D1 proof — facade-fired events over Arch

`facade-events-proof/` **runs** decision D1 rather than asserting it. It contains a cut-down facade
(`MiniFacade.cs`) that owns `Set` / `Remove` / `NotifyChanged` / `Dispose` / singleton `Set` and raises
the engine's reactive events itself over Arch 2.1.0, plus eight scenarios (`Scenarios.cs`) that drive
it through the shapes the migration has to keep working. Component types are named after the engine
components they stand for, so a check reads as a claim about a real site.

```bash
cd scratchpad/arch-spike/facade-events-proof && dotnet run -c Release   # exit code == failing checks
```

### Result: 93 checks, 0 failures

| Scenario | Stands for | What it establishes |
|---|---|---|
| S0 | D1's premise | Arch raises nothing; raw Arch iteration order measured |
| S1 | M2/M6, D14, items 39/40/67 | Set absent→Added, present→Changed(**old**,new); `NotifyChanged`→Changed with `ReferenceEquals(old,new)`; `Set(new instance)`→different refs; `NotifyChanged` on absent throws; Remove-absent silent; `Dispose`→EntityDisposed then ComponentRemoved per component with `IsAlive == true`; double-Dispose silent |
| S2 | items 17/56/75/76 | id recycled by Arch; the stale handle stays dead, `!=` the new occupant, keyed dictionaries do not collide |
| S3 | M5, CORE_TENETS §9, items 8/39/43/66 | singleton Added / **Changed-not-Added** on re-Set / Removed / silent absent-Remove / Added again after Remove; no replay on subscribe; no carrier entity in any enumeration |
| S4 | M1, items 9/10/11/22/54, C11 | predicate membership backfilled at construction, then moved **only** by publication — an in-place flip keeps the body falling and keeps the retargeted sprite in the old pass; `Count` throws; 10k transient queries leak nothing |
| S5 | **M10** | `Set<ColliderTag>` from inside the `BoxCollider` Added handler: tag present when the outer `Set` returns, the collider set sees it the same frame, 500-entity batch clean, and the **discarded** subscription handle still dispatches after a forced GC |
| S6 | **item 41** | the LDtk parser shape end to end: sweep + 500 `Publish` inside a singleton **Added** dispatch, mass sweep inside **Removed**, three parses with no leak and no stale membership — nesting reaches 3 levels (singleton → message → component Added) |
| S7 | item 50 | `world.Dispose` fires the same 9-event cascade DefaultEcs was measured to fire |

### Why it holds on an archetype backend (the three rules wave 2 inherits)

1. **The structural operation completes in Arch before the event dispatches.** A handler never sees a
   half-applied archetype move, so its own `Set`/`Dispose` is just another complete operation —
   nesting, not re-entering. This is what makes M10 safe without deferral or a command buffer.
2. **Query membership is applied before the event dispatches.** The handler, and everything later in
   the same frame, sees the query it just changed (D9 — no "frame-stable" cache).
3. **The facade never holds a `ref` or span across a dispatch.** Measured in S5: a `ref` taken before
   a structural change points at the old chunk afterwards and the write through it is lost. Values are
   copied out before dispatch (`old`) and re-read after. Wave 3's chunked conversions inherit this.

### Findings that matter for waves 1–3

1. **Arch 2.1.0 ships the event API but not the events.** `SubscribeEntityCreated`,
   `SubscribeComponentAdded/Set/Removed` and `SubscribeEntityDestroyed` are all present and compile —
   and fire **zero** times, because the raise sites are behind Arch's `EVENTS` build flag. A
   subscription silently never fires. H7's fallback option (b), "vendor Arch with `EVENTS`", therefore
   means building Arch from source, not flipping a package switch. D1 avoids that entirely.
2. **Arch enumerates a chunk in DESCENDING index order** — both the chunk enumerator and the
   `Query(desc, ForEach)` form (measured: entities created `0..5` enumerate `5,4,3,2,1,0`). Iteration
   order under Arch is therefore not merely *unspecified*, it is **reversed** relative to DefaultEcs'
   insertion-ish order. Every first-match-and-break pick (items 48, 58) flips, and so does every
   membership sweep written to disk (`SceneWriter`, items 70, 74). The facade must impose its own
   order; this proof sorts its unfiltered enumeration by entity id and keeps query membership in
   publication order, which is what makes item 22's tie-break claim testable at all.
3. **World singletons need no carrier entity.** Storing them off-world (a typed box per component
   type, exactly as DefaultEcs' world components are not entities) makes item 43's carrier-invisibility
   requirement true by construction — nothing can leak into an unfiltered `GetEntities()`, and no sweep
   can dispose the carrier. Wave 1 should not port them as a hidden entity.
4. **Discarding the subscription handle must stay legal** (M10's open question:
   `TransformCollisionDetectionSystem.cs:74-75` drops both `IDisposable`s). The facade owns handler
   lifetime with strong references; the proof forces a full GC and the dropped subscription still
   dispatches. A weak-reference or handle-owned design would silently kill auto-tagging.
5. **The facade must own the entity version stamp.** Arch recycles ids eagerly (S2 recycles on the
   first re-create), so version-stamped equality lives in the facade's `Entity`, not in Arch's. With it,
   items 17/56/75/76 fall out: dead handle stays dead, never equals the recycled occupant, and an
   `Entity`-keyed dictionary holds both.
6. **Runtime dispatch needs no reflection.** The `Dispose` cascade only knows `System.Type`, but the
   typed handler list is created where `T` is statically known (`Subscribe<T>`/`Set<T>`), so a
   type-erased base class dispatches it — no `MakeGenericMethod` on the hot path, which is what D12's
   AOT-safety requires.
7. **Contract item 50 is contradicted from both directions.** The measurement harness shows DefaultEcs
   firing a 9-event cascade on `world.Dispose`; this proof shows the facade reproducing it over Arch.
   "Event-silent" would be a deliberate behaviour change, not parity — and C7 byte-identity would fail.

### What this proof deliberately does NOT cover

- **It is not the wave-1 facade.** No `ISystem`/`SequentialSystem`/`ParallelSystem`, no `[Subscribe]`
  attribute scan, no `IParallelRunner`, no world-generation stamp (D11), no `ReadAllComponents`.
- **It is not tuned.** Every publication re-evaluates the affected entity against every query
  interested in that component type, walking a copied query list; the real facade indexes queries by
  component type. The proof optimises for being obviously correct, not fast.
- **JIT only.** The WASM and NativeAOT legs are the sibling heads' job (§1), and nothing here is
  target-specific. Note that under NativeAOT this facade would still need the component registration of
  finding 1 in §1.

---

## Environment (capture)

| Concern | Version |
|---|---|
| dotnet SDK | 8.0.416 (net8.0) |
| dotnet workload | `wasm-tools` manifest 8.0.28 |
| ECS under test | `Arch` **2.1.0** (same pin as `MonoDreams.Benchmarks`) |
| AOT registration | `Arch.AOT.SourceGenerator` **1.0.1** (latest; needs the namespace shim) |
| ECS being measured | `DefaultEcs` **0.18.0-beta01** (the engine's current pin) |
| KNI split framework + BlazorGL platform | `nkast.Xna.Framework*` / `nkast.Kni.Platform.Blazor.GL` **4.2.9001** |
| Blazor WASM hosting | `Microsoft.AspNetCore.Components.WebAssembly` **8.0.11** |
| Host | macOS 15.7.1 (Darwin 24.6.0), Apple Silicon (arm64) |
| Browser (WASM leg) | Google Chrome 151.0.7922.140, headless, driven by `puppeteer-core@23` |

## Layout

```
shared/ArchSpikeComponents.cs   component shapes (struct, second struct, zero-sized tag, managed class)
shared/ArchCoreUtilsShim.cs     the Arch 1.x -> 2.x namespace shim the AOT generator needs
shared/ArchExercise.cs          the checks both target proofs run, verbatim
aot-console/                    NativeAOT head  (exit code == proof)
wasm-head/                      KNI/BlazorGL Blazor WASM head (document.title == proof)
wasm-head/run-in-chrome.mjs     puppeteer-core driver against the SYSTEM Chrome
defaultecs-measurements/        the DefaultEcs interrogation harness (items 42/66)
facade-events-proof/            the H7/D1 proof: facade-fired events over Arch (items 3/41)
facade-events-proof/MiniFacade.cs   the cut-down facade under test
facade-events-proof/Scenarios.cs    the eight scenarios (exit code == failing checks)
```
