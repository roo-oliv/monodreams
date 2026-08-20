# Wave 0 — Arch target proofs + DefaultEcs measurement harness (THROWAWAY)

Spike deliverables for [issue #119](https://github.com/roo-oliv/monodreams/issues/119) wave 0.
This whole directory is throwaway (wave 4 deletes it, like `scratchpad/blazor-spike/`); it exists to
turn the migration plan's riskiest assumptions into measured facts before wave 1 writes a line of
facade code.

Nothing here references MonoDreams. Arch and DefaultEcs are referenced directly and deliberately —
this tree rides the `scratchpad/` allowlist entry of the wave-1 `EcsBoundaryLintTests` (plan D15).

| Leg | Contract item | Result |
|---|---|---|
| Arch in a **KNI/BlazorGL WASM** head — builds, publishes (trimmed), **runs in Chrome** | 2 (C2) | **PASS** — 43/43 checks |
| Arch under **NativeAOT** (`PublishAot=true` + `Arch.AOT.SourceGenerator`) — publishes and **runs** | 2 (C2) | **PASS** — 43/43 checks, but only with a namespace shim (see finding 1) |
| Arch **process-wide statics** across `World.Destroy` (H9 / C12) | H9 row, C12 | measured over the **enumerated** set — five statics on `World` plus **both** component-type registries: one resets (and only for the shape it was asked about), one accumulates, one is untouched under a sentinel, two never shrink (§1, finding 6) |
| DefaultEcs **subscribe-replay + `NotifyChanged`** measurements | 42, 66 | measured — see the table below |
| **Facade-fired events over Arch** (H7/D1) — Added/Changed(old,new)/Removed, singleton notifications, predicate membership, M10, the LDtk mass-parser shape, teardown (inert, sweeping, creating, re-entrant in both directions, throwing) | 3 (C3), 41 | **PASS** — 148/148 checks, exit 0 |

Every count above is printed by the artifact itself (`== N checks, M failed ==`), so it can be
recounted from a run rather than taken from this table.

---

## 0. Contract deltas — amend the plan before wave 1

Wave 0's fourth deliverable is *findings*, and the plan grants them authority: "if the spike
invalidates part of this plan, **the plan is amended before wave 1**". Nine contract surfaces
changed status below. **A1 blocks wave 1** — it invalidates a pinned semantic *and* the D10
resolution that rests on it. A2–A9 widen or correct scope the plan does not currently name.

Wave 0 deliberately does **not** edit the plan or the committed deep-plan contract
(`.claude/deep-plan/issue-119-7b2cbe2a.md`); it reports, and the amendment is made upstream before
wave 1 writes the corresponding contract test. Anchors below are for whoever makes it.

| # | Contract surface | Plan pins | Measured | Amendment needed |
|---|---|---|---|---|
| **A1** | item **50** + **D10**; deep-plan `:107`, rows `:898`, `:1461`, `:1500-1501`, `:1818`, `:1898`, `:2092-2103`, contradiction 2 at `:39` | `world.Dispose` is event-silent, **"matching DefaultEcs"** | DefaultEcs fires a **10-event cascade** in a measured ORDER (§2) and the facade reproduces it over Arch (§3, S7) | The parity clause is **false**. Deep-plan contradiction 2 (`:39`) offered two branches and D10 took the one justified by parity; that justification is gone. Either (i) keep event-silence as a **deliberate behaviour change** — then C7 byte-identity must be shown not to observe it, and `AudioSystem.OnAudioSourceRemoved` stops running at teardown as it does today — or (ii) fire the full cascade (real parity) and re-resolve contradiction 2 by testing pipeline-before-world ordering at **all six** `world.Dispose` sites. Not a wording fix: item 50's test asserts the opposite of the measurement. The restatement must also pick a side on the **double-fire** the same measurement found (§2, M5): the engine's own unload sweep runs a second time at teardown TODAY. |
| **A2** | **D1**'s documented fallback ("vendoring Arch with `EVENTS` (option b) stays the documented fallback") | option (b) is a fallback in reach | Arch 2.1.0 ships `Subscribe*` publicly; the raise sites are behind the `EVENTS` build flag, so subscriptions compile and fire **zero** times (§3, finding 1) | Restate the fallback's cost as **building Arch from source**, or drop option (b). A silently-inert subscription is also a wave-2 trap: the guard must keep native Arch events unused, not merely unsubscribed. |
| **A3** | §6 precondition diff row *Iteration order* → "**unspecified**; systems needing order sort explicitly"; deep-plan H4 rows `:664-669`; items 22/48/58/70/74 | order is arbitrary, so any deterministic tie-break suffices | Arch enumerates in **descending** index order — measured on **both** iteration forms (the chunk enumerator and `Query(desc, ForEach)`) and **end to end across 4 000 entities spanning several chunks**, not only inside one chunk (§3, finding 2) | "Unspecified" understates it: the order is deterministically **reversed** vs DefaultEcs, so every first-match-and-break pick (48/58) silently flips its answer and every membership sweep written to disk (`SceneWriter`, 70/74) inverts end to end. The row should read *facade-imposed* order, and it is **three** orders, not one: entity-id enumeration *within a channel*, publication-order membership, and — carrying the entity-component `Removed` pools and the world components alike — **this world's subscription order over one channel list** (§3, finding 8, and delta **A9**, which is what settles the last one). Left alone, the pools read straight off Arch's archetype signature and the world components fall out of `Dictionary` enumeration. Together they are what makes item 22 testable. **Scope:** the teardown orders hold while no handler mutates during the cascade; the engine's own unload sweep breaks them (§3, S8), so item 22's teardown clause has to say "with inert handlers" or name the sweep's shape. |
| **A4** | **D6** ("`Arch.AOT.SourceGenerator` yes (console/iOS AOT)"); packaging items 26/31/37 | registration is an AOT optimisation on the heads that need it | Registration is **mandatory** — without it the binary dies on the *first* `world.Create`, for plain structs too — and a source generator only sees its own compilation (§1, finding 2) | Name the obligation **per assembly that declares components**: `MonoDreams.dll`, every game assembly, every CLI-scaffolded project. That is wave-2 facade scope *and* wave-4 scaffolder/manifest-honesty scope; neither currently carries it. Narrow D6's scope too: WASM does **not** need it (§1, finding 3). |
| **A5** | item **2**'s AOT leg | `Arch.AOT.SourceGenerator` works as shipped against the pinned Arch | 1.0.1 (the latest published version) emits Arch 1.x namespaces and fails `CS0103` against Arch 2.1.0; a 5-line forwarding shim restores it (§1, finding 1) | Item 2 is satisfied, but only *with* `shared/ArchCoreUtilsShim.cs`. Wave 2/3 must elect an owner — ship the shim beside the facade, vendor a fixed generator, or have the facade register component types itself (it already needs a type registry for `ReadAllComponents`). |
| **A6** | item **42**'s conditional; deep-plan matrix rows `:1487-1488` and `:1976-1980` ("entity-level replays, singleton does not"); the foundation premise at `:2792` ("entity-level `SubscribeComponentAdded` replays existing state exactly as DefaultEcs") | entity-level subscriptions **replay** already-present components; only the singleton leg does not | **NO replay on any entity-level verb.** Two live carriers at subscribe time produce zero calls, and `SubscribeEntityComponentChanged` / `…Removed` do not replay either (§2, M2) | The parenthetical is backwards. Rows `:1487-1488` and `:1976-1980` and premise `:2792` should read *no replay, all three entity verbs* — the same answer the singleton leg already has. This is not cosmetic: a wave-1 facade built from the current wording ships spurious `Added` replay on every entity subscription, which is precisely the double-parse item 66's resolution was written to prevent. |
| **A7** | **C12** ("`ProcessWideState.Reset` returns Arch `World.Worlds`/component statics to baseline"); the H9 dimension-violation row at deep-plan `:2732` (and `:2714`), which assigns the proof to wave 0 | one reset hook returns *both* Arch registries to baseline | **Five statics on `World`, enumerated rather than assumed** (§1, finding 6), plus `ComponentRegistry` — and they answer differently. `World.Destroy` DOES reset the world half — `World.WorldSize` returns to baseline, the `World.Worlds` slot is nulled and the world id is reused. The **component-type registry is process-lifetime**: it survives every `Destroy` unchanged, which is what keeps the next world usable — and Arch 2.1.0's only clearing entry point, `ComponentRegistry.Remove<T>()`, **throws `ArgumentNullException`** while leaving the entry half-removed, after which a lazily-registered target dies in `ArrayRegistry.GetArray` exactly like the AOT negative control. **And a third static never returns to baseline at all:** `World.Destroy` does not free an id, it **enqueues** it on the process-wide `World.RecycledWorldIds`, and `World.Create` dequeues — measured, two worlds freed high-id-first hand the **higher** id to the next `World.Create` (`2,1`, not `1,2`). `World.SharedJobScheduler` is a fourth — and "untouched by `Destroy`" is now **measured with a sentinel** written into the field rather than inferred from a `null` nothing ever set; `World.InitialCapacity` a fifth (mutable config constant). The registry half is also **two** stores, not one: `Arch.Core.ArrayRegistry` — the one the AOT negative control dies inside and the generator primes — is enumerated too, and its whole static surface is `Add()` + `GetArray(ComponentType, Int32)`: no size, no clear, no remove | Restate C12 as **"reset `World.Worlds` only"**, name the reset hook's SHAPE — it must walk `World.Worlds` by `Length` skipping nulls, never `for (i < World.WorldSize)`, because `WorldSize` is a live COUNT and destroying a non-highest id leaves a hole below a live world (measured: the naive walk reads one null and leaks one live world) — and name **both** component-type registries as deliberately process-lifetime and never to be cleared — there is no working API to clear it, and clearing it reproduces the negative control instead of resetting anything. `ProcessWideState.Reset`'s Arch hook is `World.Destroy` per live world — but "nothing more" must now be said with the **recycled-id queue named as unresettable**: world ids after a `Reset` depend on the order earlier worlds were destroyed in, i.e. on the xUnit shuffle C12's own `MONODREAMS_TEST_SEED=8` clause exists to survive. So C12 must also forbid asserting on a world id (or on anything derived from one), and register `World.SharedJobScheduler` the moment wave 2 sets it for a parallel runner. |
| **A9** | items **40** and **50**'s ordering clauses; the A3 row above; deep-plan `:2092-2103` | the teardown cascade has a shape — `EntityDisposed` for every entity, then `ComponentRemoved` per component, then world components — so a facade reproduces it by running those three phases | **There are no phases.** M4d put the three candidate rules on fixtures that can tell them apart, and DefaultEcs 0.18.0-beta01 answers the same way three times: subscribing a world component BEFORE the entity ones makes it fire **first** (so "world components last" was M4's own subscription order, never a rule); subscribing `EntityDisposed` **last** makes it fire **last** (so it is a channel, not a phase); and `entity.Dispose` walks the same list (so item 40's "EntityDisposed then ComponentRemoved" is that order too). One type subscribed on **both** legs takes **two** slots, each keeping its leg's order | Restate items 40 and 50 as **one order: this world's subscription order over one channel list**, with entity id ordering the carriers *inside* a channel. It is not a wording fix — a facade built on the phase reading diverges from the incumbent the moment a screen subscribes a singleton before its components (`LDtkLevelDataComponent` vs the tile components is exactly that shape), and the divergence is silent. The facade proof was rebuilt on the measured rule and S10 asserts it; S7's phase-looking log is preserved *because* S7 subscribes in the phase order. |
| **A8** | the C12 `ProcessWideState` rows at deep-plan `:657` and `:1220` ("verified `ProcessWideState.cs:41-99` **tracks no ECS statics today** — additions land same PR"); the C5 using-sweep base at `:2722` (449 lines / 320 files on main @`7b2cbe2a`) | `ProcessWideState` holds engine statics only; the **Arch** `World.Worlds` hook will be the first ECS entry it ever gains | **This PR already gave it one, and it is a *DefaultEcs* entry.** Commit `26769cf0` (the `MONODREAMS_TEST_SEED=8` flake fix that unblocked wave 0's own verification) adds a reflection hook into `DefaultEcs.Internal.EntityQueryFilterFactory._filters` — `ProcessWideState.cs:63-71` (the `FieldInfo` + reachability flag), `:136` (the `Reset` call) and `:170-174` (`ResetEcsQueryFilterCache`) — plus a `using DefaultEcs;` in a git-tracked `.cs` outside `scratchpad/` | Rows `:657`/`:1220` are stale as written: one ECS static **is** tracked. State that it is **deleted, not ported**, at wave-1 close (it memoises a DefaultEcs predicate cache that ceases to exist with DefaultEcs), and grow the C5 sweep base at `:2722` by the 1 file / 1 line this PR added. Wave 1 must also say what carries the issue-#114 flake protection afterwards: the collision is upstream-specific (a string key built from `ComponentEnum.ToString()`), and the wave-0 enumeration of Arch's `World` statics found **five, none of them a query or predicate cache** — but that enumeration covered `World` only, so wave 1 owes either "no replacement needed", backed by the same enumeration run over the facade's and Arch's query types, or a named replacement hook. Not assumed either way. |

### Deliberate strengthenings beyond the contract (kept)

None of these is scope-creep; each one is why a finding above exists at all.

| Beyond the contract | Where | Why it stays |
|---|---|---|
| A **1-byte tag** churn family alongside the zero-sized one, and the churn split into two BDN classes / three jobs | `MonoDreams.Benchmarks/Components.cs`, `StructuralChurnBenchmarks.cs` | The contract named struct + managed + zero-sized. Without a 1-byte tag the churn column reports a **DefaultEcs pathology** (quadratic zero-sized `Remove`) as though it were the sparse-set-vs-archetype difference — and that pathology is fixable without Arch. The byte-tag rows are the like-for-like line wave 2 must be judged against. |
| The WASM leg **runs** in headless Chrome, not just compiles | `wasm-head/run-in-chrome.mjs` | Item 2 says "compiles"; the Blazor Release publish **trims**, and the AOT negative control proved that clean-publish-then-runtime-crash is a real failure mode for this exact question. |
| AOT **negative control** `-p:UseArchAotGenerator=false` on both heads | `aot-console/AotConsole.csproj`, `wasm-head/WasmHead.Web.csproj` | It is what turns "it published" into "it is mandatory". Finding A4 does not exist without it. |
| Adjacent DefaultEcs facts measured in the same run (`world.Set`/`Remove` semantics, dispose ordering, the `ReferenceEquals` discriminator) | `defaultecs-measurements/Program.cs` | Items 42/66 asked three questions, but the wave-1 C4 pins around them (items 5, 8, 39, 40, 50) need the surrounding shape measured **at the same version, in the same run**. Report-only by design — no asserts; the pins become tests in wave 1. |
| The **H9 / C12 registry probe** in the shared exercise (`process-wide statics across World.Destroy`) | `shared/ArchExercise.cs` | Not a strengthening so much as a debt paid: the plan's H9 dimension-violation row assigns this proof to wave 0 in so many words, and it produced delta **A7** — C12 as written promises a reset that Arch cannot perform. |
| **S8** — the LDtk unload sweep running *during* `world.Dispose` | `facade-events-proof/Scenarios.cs` | S6 covers the sweep during play and S7 covers teardown with inert handlers; the engine does both at once at every screen change. It is where the M5 double-fire became visible at all. |
| **M5 / M6** in the measurement harness (re-entrant teardown handler, post-teardown `EntitySet` read) | `defaultecs-measurements/Program.cs` | S8 and S7's post-teardown assertions are only meaningful next to what the incumbent does; both answers turned out to be surprises (double-fire; `NullReferenceException`). |
| **S9** — a handler that calls `world.Dispose` *during* `world.Dispose`, and **M7**, the same shape on DefaultEcs | `facade-events-proof/Scenarios.cs`, `defaultecs-measurements/Program.cs` | S8 re-enters *entity* disposal; six engine sites call `world.Dispose` (screen teardown, `Game.UnloadContent`, the editor's world swap), so re-entering *world* disposal is a live shape. Without a guard set before the cascade the facade replayed every event (measured 3× on two carriers before a probe cap stopped it). M7 measured DefaultEcs guarding it, so the fix is parity, not invention. |
| **M4b / M4c** — what *determines* the two teardown orders | `defaultecs-measurements/Program.cs` | M4 measured *that* the cascade is pool-grouped and world-components-last; it could not say what orders the pools, because subscription order, `Set` order and process-wide first touch coincide in its fixture. The engine builds **one world per screen**, so per-world and process-wide ids agree on the first screen and diverge on the second. Measured: **per-world subscription order**, on both legs — which is what the facade now mints, for entity *and* world components. |
| **M5b / M5c** and **S8 leg C** — entities created, and components `Set`, *during* the cascade | both harnesses | Item 41 is Dispose + Create + Publish; S6 covers Create during play and S8 leg A covers Dispose at teardown, leaving Create at teardown unmeasured. M5b: teardown-time creation is event-silent on DefaultEcs (Added fires, nothing else). M5c: a component `Set` mid-cascade on a not-yet-reported carrier **is** reported — which is why the facade re-reads membership at dispatch time instead of snapshotting it. |
| The **enumerated** `World` static probe (five fields) + the recycled-id order check | `shared/ArchExercise.cs` | A7 says "`World.Destroy` per live world, **nothing more**". That is only checkable against an enumerated set: the enumeration found `RecycledWorldIds`, which no API drains, and `SharedJobScheduler`, which `Destroy` never touches. |
| **M4d** — the fixture that separates "three teardown phases" from "one channel list" | `defaultecs-measurements/Program.cs` | M4/M4b/M4c all subscribe in the phase order, so their logs agree with both rules at once. M4d subscribes a world component first, `EntityDisposed` last, and one type on BOTH legs — and produced delta **A9**, the rule the facade's whole teardown was rebuilt on. |
| **M8 / M9** and **S9 leg B / S11** — the crossed re-entrancy and a THROWING teardown handler | both harnesses | S9 leg A settled world-inside-world; the crossed direction (`world.Dispose` from inside a normal `entity.Dispose`) leaves the outer disposal holding a destroyed Arch world, and a throwing handler drops the rest of the cascade. Both are one handler away in engine code, and both were unmeasured. M8/M9 give the incumbent's answers: the same event sequences, but a world DefaultEcs leaves alive and unrecoverable. |
| The **sparse-`World.Worlds`** probe (destroying a NON-highest world id) | `shared/ArchExercise.cs` | Every other probe frees the highest live id, where `World.WorldSize` still bounds the array — so the hook A7 prescribes looks fine. One out-of-order destroy is what shows it reading a null and leaking a live world. |
| The **`ArrayRegistry`** enumeration and the **`SharedJobScheduler` sentinel** | `shared/ArchExercise.cs` | C12 says component-type registr**ies**; the spike answered for one of them while blaming the other for the negative control's crash. And "`Destroy` never touches the scheduler" was a `null` read, not a measurement — wave 2 sets that field. |
| **S0's second and third order probes** — `Query(desc, ForEach)` and a 4 000-entity cross-chunk sweep | `facade-events-proof/Scenarios.cs` | Finding 2's claim is what items 48/58/70/74 rest on, and the facade's `EntitySystem` will run the `ForEach` form, not the chunk enumerator. A single-chunk probe of one of the two forms cannot carry it. |
| `BenchmarkDotNet.Artifacts/` in `.gitignore` | `.gitignore` | BDN writes artifacts relative to the cwd, so running the benchmarks from the repo root drops them outside `bin/`. |

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
| NativeAOT, `osx-arm64` | `IsDynamicCodeSupported=False`, 2.1 MB Mach-O arm64 binary; `ArrayRegistry`'s member metadata stripped (0 members reflected) | `43 checks, 0 failed`, exit 0 |
| KNI/BlazorGL WASM, Chrome 151 headless | `OSArchitecture=Wasm`, `IsDynamicCodeSupported=True` (interpreter), `IsDynamicCodeCompiled=False`, KNI `Xna.Framework 4.2.9001` loaded in the same bundle | `43 checks, 0 failed`, `ARCH-WASM PASS` |
| WASM negative control (`UseArchAotGenerator=false`) | `ComponentRegistry.Size before any World = 0` — lazy registration, `Has<Doomed>()` reads **false** before first use where both generator legs read true, and the post-clear `world.Create<Doomed>` dies with `ArgumentNullException ('elementType')` where the generator legs still succeed | `43 checks, 0 failed`, `ARCH-WASM PASS` |
| AOT negative control (`UseArchAotGenerator=false`) | same publish, no priming | `0 checks, 1 failed` — dies inside the FIRST `world.Create` before any check runs |

The exercise prints its own `== N checks, M failed ==` line, so these numbers are recountable from a
run rather than transcribed by hand.

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
   the Blazor Release trimmer, passes all 43 checks in Chrome: the WASM runtime keeps dynamic code
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
6. **Arch's process-wide statics reset differently, and only one of them resets at all**
   (contract delta **A7**; this is the H9 obligation the plan assigns to wave 0). Both legs measure
   the same thing, in the section `process-wide statics across World.Destroy`. The set is
   **enumerated, not assumed** — the probe reflects over every static field on `Arch.Core.World`
   and prints it, because "`World.Destroy` per live world, *nothing more*" is a claim about a set:
   ```
   World.<Worlds>k__BackingField            Arch.Core.World[]
   World.worldSizeUnsafe                    1
   World.<RecycledWorldIds>k__BackingField  Collections.Pooled.PooledQueue`1[System.Int32]
   World.<SharedJobScheduler>k__BackingField null
   World.InitialCapacity                    128
   ```
   (identical on both legs; the reflection survives both the Blazor trimmer and NativeAOT, and is
   reported rather than checked precisely because that is not guaranteed to keep being true.)
   - **`World.Worlds` / `World.WorldSize` DO reset.** `World.Destroy` nulls the world's slot,
     `World.WorldSize` returns to its pre-`Create` value, and the next `World.Create` **reuses the
     freed id**. A world built after a `Destroy` still constructs a managed archetype, so nothing
     is left half-torn-down. `ProcessWideState.Reset`'s Arch hook is exactly this, per live world.
   - **...but the hook has to WALK that array the right way.** `World.WorldSize` is a **count of
     live worlds**, not a high-water mark, and `World.Worlds` goes **sparse** the moment a
     non-highest id is destroyed — which is the engine's own shape, one world per screen created
     and destroyed out of order. Measured with three live worlds and the middle one destroyed: a
     `for (i < World.WorldSize) World.Destroy(World.Worlds[i])` hook reads **one null slot** (an
     NRE) and **leaks one live world** past its bound. Walking `World.Worlds` by `Length` and
     skipping nulls sees exactly `World.WorldSize` live worlds, and that is the hook C12 should
     name.
   - **`World.RecycledWorldIds` never returns to baseline.** `World.Destroy` does not *free* an id,
     it **enqueues** it; `World.Create` dequeues. Measured: two worlds freed higher-id-first hand
     the ids back as `2,1` — freed order, not lowest-free-first — so after any reset the id a world
     gets depends on the order earlier worlds were destroyed in. Nothing drains the queue, and Arch
     exposes no API that could. Anything keyed on a world id is therefore shuffle-order dependent,
     which is the failure mode C12's `MONODREAMS_TEST_SEED=8` clause exists to catch.
   - **`World.SharedJobScheduler` is a fourth static** — `null` today, and the one wave 2 sets if it
     ever turns on Arch's multithreaded queries. "Untouched by `Destroy`" is measured rather than
     read off that `null`: the probe writes a **sentinel** into the backing field (an uninitialised
     instance, so no worker threads start), creates and destroys a world over it, and finds the
     sentinel still there, then restores `null`. A target that cannot install one says so and the
     check degrades to "never observed being cleared" — both legs installed it.
     `World.InitialCapacity` is a fifth (a mutable configuration constant, 128).
   - **`ComponentRegistry` does NOT, and must never be made to.** It is process-lifetime: `Size`
     and `Has<T>()` are unchanged across `World.Destroy`, which is *why* the world created
     afterwards works. Under the AOT generator it is primed once by a `[ModuleInitializer]`, which
     by construction cannot re-run.
   - **And there are TWO component-type registries, not one.** `Arch.Core.ArrayRegistry` — the
     store the negative control dies inside (`ArrayRegistry.GetArray`) and the one the AOT
     generator's shim primes — is enumerated the same way. Its backing field is a
     `JaggedArray<Func<int, Array>>` with no `Count`, and its entire static surface is `Add()` and
     `GetArray(ComponentType, Int32)`: no size, no clear, no remove. So it is process-lifetime by
     construction, and the readable proof of that is behavioural — it still hands out `Payload[]`
     after every `World.Destroy` above, and after the `ComponentRegistry` clear below.
     One target-specific caveat, reported per leg: under **NativeAOT** the member metadata of this
     type is stripped, so that leg reflects the type but enumerates **0** fields and **0** methods
     (JIT and WASM both see 2 methods). `World`'s own fields survive there because `typeof(World)`
     is statically referenced. The surface claim is therefore carried by the JIT/WASM legs, and
     the artifact prints the member count so a reader can see which.
   - **Clearing it is not a reset, it is the negative control.** `ComponentRegistry.Remove<T>()` —
     Arch 2.1.0's only clearing entry point — **throws `ArgumentNullException ('key')`** from
     inside its own dictionary removal, *after* having already invalidated the type's entry. The
     type then reads `Has<T>() == true` with an unchanged `Size`, and on a lazily-registered target
     the very next `world.Create` of it dies with `ArgumentNullException ('elementType')` inside
     `ArrayRegistry.GetArray` — the same stack as the AOT negative control in finding 2. (With the
     generator on, the parallel `ArrayRegistry` priming survives and the create still succeeds,
     which is why the checked claim is "Remove throws", the part both targets agree on.)

   So C12 cannot promise both halves, and "nothing more" is not free either. Restated: reset
   `World.Worlds`; leave the component-type registry alone, forever; and name the recycled-id queue
   as unresettable, so no test and no engine invariant may depend on a world id.
7. **The `.Web` project-name suffix is load-bearing.** `Directory.Build.props` relocates `obj`/`bin`
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
"entity-level replay matches DefaultEcs" resolves to **no replay** for all three entity verbs — which
**contradicts** the plan's own parenthetical that entity-level subscriptions replay while the
singleton does not, and is why that is carried as contract delta **A6** above rather than only as
prose here. D14 is confirmed with an exact exception type and message to pin.

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
| a live `EntitySet` after one `entity.Dispose()` | drops the member **synchronously** (item 67's baseline) |
| a live `EntitySet` after `world.Dispose()` | keeps a **stale `Count`**, and enumerating it throws `NullReferenceException` (M6) |

### ⚠ Contract item 50 is contradicted by measurement (§0, **A1**)

Item 50 pins `world.Dispose` as **event-silent** "matching DefaultEcs". It does not match: in
DefaultEcs 0.18.0-beta01 `world.Dispose()` fires **10 events** on a world holding 4 struct-component
carriers (one of which *also* carries a managed component) and 1 world component —

```
EntityDisposed        × 4   markers #1, #2, #4, #3      <- ASCENDING ENTITY ID, not creation order
ComponentRemoved      × 4   Marker #1, #2, #4, #3       <- one POOL, every carrier, IsAlive == true
ComponentRemoved      × 1   Managed, on carrier #2      <- the next pool, only then
WorldComponentRemoved × 1
```

Both orders are measured on a fixture built to falsify the obvious guesses, because a naive one
cannot tell the candidates apart:

- **Entity order is the entity ID, not creation order.** M4 disposes a scratch entity *after* the
  third carrier, so the fourth carrier (created **last**) recycles the **lower** id. The measured
  sequence follows the id — `#1, #2, #4, #3` — which the earlier fixture, where the two orders
  coincided by construction, could not have told apart.
- **Component order is pool-grouped, not per-entity.** The discriminator is one carrier holding
  **two** subscribed component types, interleaved between single-component carriers: its second
  type is reported **last of all**, after every other carrier's first type. A per-entity cascade
  would have reported it in the middle.

An entity disposed *before* teardown is not reported twice.

> ⚠ **Read the three-block shape above as M4's SUBSCRIPTION order, not as a phase structure.** M4
> subscribes `EntityDisposed`, then the two component types, then the world component — and M4d
> (below) shows that is exactly what decides the log. Reordering the subscriptions reorders the
> cascade, `EntityDisposed` included. That is contract delta **A9**.

**Both orders are scoped to INERT handlers.** M4's handlers only log. The moment a handler mutates —
which the engine's own unload sweep does at every screen change — the disposals it triggers re-enter
the per-entity path, so pool-grouping collapses for whatever the sweep takes and `ComponentRemoved`
can fire *after* `WorldComponentRemoved` (measured on the facade in §3, S8 leg A; M5 measures the
DefaultEcs side of the same shape). Item 50's restatement must carry that scope, not just the order.

**What *determines* the pool order (M4b), and the order between world components (M4c).** M4's own
fixture cannot say: subscription order, component-`Set` order and process-wide first touch coincide
in it. That ambiguity is not academic — the engine builds **one `World` per screen**
(`LevelSelectionScreen.cs:128`, `SplashScreen.cs:59`), so a per-world rule and a process-wide rule
agree on the first screen and can diverge on the second. Two worlds in one process, the second
subscribing and `Set`ting in the **reversed** order, settle it; two further legs split subscription
order from `Set` order and put a `Set` *before* any subscription:

| Leg | Measured on DefaultEcs 0.18.0-beta01 |
|---|---|
| pools, two worlds with reversed orders (M4b) | **PER-WORLD** — world B followed its own order, not world A's |
| pools, subscribe Gamma→Delta but `Set` Delta→Gamma (M4b) | subscription order wins over `Set` order |
| pools, `Set` Zeta *before* subscribing to anything (M4b) | **subscription order strictly** — a pre-subscription `Set` does not claim the earlier slot |
| between world components, the same three legs (M4c) | identical answers: **per-world, subscription order strictly** |

So the ordering key is "the order this world's subscriptions were made", for entity components and
world components alike. The facade takes each channel's teardown slot in exactly that place
(`MiniFacade.cs`, `Channel<T>`/`WorldChannel<T>`/`SubscribeEntityDisposed`), and M4c is the reason
the world-component leg of its cascade is ordered at all: a raw `Dictionary` walk would have been
BCL insertion order — stable in practice, chosen by nothing.

### ⚠ M4d — the order is ONE channel list, and the "phases" were the fixture (§0, **A9**)

M4, M4b and M4c all subscribe in the same order they then observe (`EntityDisposed` → entity
components → world components), so every one of their logs is consistent with two different rules:
*"the cascade runs three phases"* and *"the cascade walks this world's subscriptions"*. M4d builds
the fixtures that separate them, and the second rule wins on every leg:

| Leg | Measured on DefaultEcs 0.18.0-beta01 |
|---|---|
| subscribe world `Shared`, world `F`, entity `Shared`, entity `B` | `World(Shared), World(F), Entity(Shared), Entity(B)` — the **world leg fires FIRST**. "World components last" was never a rule |
| subscribe world `Shared2`, entity `C`, entity `Shared2` (the same type on BOTH legs) | `World(Shared2), Entity(C), Entity(Shared2)` — each leg keeps its **own** subscription order; the type takes **two** slots, and its world-leg slot does not move its entity-leg one |
| subscribe entity `D`, world `G`, **`EntityDisposed` last** | `Removed(D 1), Removed(D 2), World(G), EntityDisposed, EntityDisposed` — `EntityDisposed` is a **channel**, not a phase |
| `entity.Dispose` with `Removed` subscribed **before** `EntityDisposed` | `Removed(D 7), EntityDisposed` — the per-entity verb walks the **same list**, so item 40's "EntityDisposed then ComponentRemoved" is that order too |

This is why the facade keeps ONE ordered channel list (`MiniFacade._channels`) instead of three
phases and a type-id registry, and why S10 exists: the old shared registry re-minted a type
subscribed on both legs, which reordered the world leg and collided the next type's id. The engine
reaches this shape with no effort at all — `LDtkLevelDataComponent` is subscribed by a system that
also subscribes tile components, and the order between them is whatever the screen's constructor
happens to do.

This matters beyond bookkeeping: `AudioSystem.OnAudioSourceRemoved` (`AudioSystem.cs:133-137`) **does**
run on `world.Dispose` today, so world teardown already cuts live audio instances — the authority
D10 assigns exclusively to `AudioSystem.Dispose`. Wave 1 must either restate item 50 as "world.Dispose
fires the full reactive cascade" (preserving today's behaviour, which is what C7 byte-identity will
demand) or accept a deliberate behaviour change and say so. **The plan should be amended before
wave 1 writes the item-50 contract test.**

### ⚠ M5 — a handler that disposes entities during `world.Dispose` fires the cascade TWICE

The engine has exactly this shape: `LDtkTileParserSystem.cs:42` subscribes the world-component
Removed leg, and `CleanupTileEntities` (`:145-155`) mass-calls `entity.Dispose()`. At teardown that
handler runs against entities the cascade is already reporting, so M5 measured what happens:

| Leg | Measured on DefaultEcs 0.18.0-beta01 |
|---|---|
| **world-component Removed** (the engine's) | the sweep still sees **3 of 3 entities alive**, and disposing them fires **`EntityDisposed` ×2 and `ComponentRemoved` ×2 per entity** — the teardown walk reports them, the sweep reports them again |
| **`EntityDisposed`** | **does not terminate.** The entity reads `IsAlive == true` inside its own `EntityDisposed` handler, disposing it republishes, and nothing in DefaultEcs guards re-entry. What the harness *records* is bounded re-entry: `sweeps stopped by the depth cap` = 3, the same entity re-entered at every level with the state that would end the recursion unchanged. Uncapped it therefore cannot terminate — an **inference** from the measurement (a stack overflow would take the run with it), not a recorded overflow |

Item 50's restatement therefore has to say which of those is the contract: the double-fire is
today's behaviour, so "one event per entity" would be a **second** deliberate change hiding inside
the first. The facade proof reproduces the double-fire and asserts it (§3, S8), and bounds the
recursion the incumbent does not.

### M5b / M5c / M7 / M8 / M9 — the teardown shapes M5 leaves out

| # | Question | Measured on DefaultEcs 0.18.0-beta01 |
|---|---|---|
| **M5b** (item **41**) | a handler that **creates** entities during `world.Dispose` — item 41 is Dispose + Create + Publish, and the LDtk parser does all three | **Teardown-time creation is event-silent.** 4 pre-existing carriers give 4 `EntityDisposed` + 4 `ComponentRemoved`; the 3 newborns fire their `Added` and **nothing else**, then die with the world (`IsAlive == false` for all 3 once `Dispose` returned). No throw — creating during teardown is legal, just unreported |
| **M5c** (item **50**) | a component **`Set` on a still-live carrier** during the cascade — does teardown report the component set as of cascade *entry* or as of *dispatch*? | **As of DISPATCH.** A `ManagedPayload` added by a `Removed` handler to a carrier the walk had not reached yet **is** reported. A facade that snapshots each carrier's components before dispatching would silently drop it — which is why `MiniFacade` re-reads membership per pool instead |
| **M7** (item **50**) | a handler that calls **`world.Dispose` during `world.Dispose`** — six engine sites call it (screen teardown, `Game.UnloadContent`, the editor's world swap) | **Guarded.** 2 carriers → 2 `EntityDisposed` + 2 `ComponentRemoved`, depth cap never hit. The nested call is a no-op, so this is one place the incumbent is *stricter* than the naive implementation — the facade had to add the same guard to reach parity (§3, S9 leg A) |
| **M8** (items **50**/**67**) | the **crossed** re-entrancy: `world.Dispose` from inside a NORMAL `entity.Dispose`, so the world dies underneath a disposal that is still running | The outer `entity.Dispose` **returns normally**, and the log is `EntityDisposed(#1), EntityDisposed(#1), EntityDisposed(#2), Removed(1), Removed(2)` — the outer entity is reported twice, exactly as the facade's S9 leg B. Afterwards the world is a minefield: `Entity.IsAlive`, `world.CreateEntity()` and `world.Set<T>` **all throw `NullReferenceException`**. The facade answers the same refusals with a typed `InvalidOperationException` |
| **M9** (item **50**) | a teardown handler that **throws** — three carriers, the handler fails on the second | **Fail-fast, and the world is left half-torn-down.** The exception escapes `world.Dispose`; 2 `EntityDisposed`, **0** `ComponentRemoved`, **0** `WorldComponentRemoved`. But all **3 carriers stay ALIVE**, the held `EntitySet` still reports 3, and a second `world.Dispose` fires **nothing** — so the world can never be torn down again. The facade reproduces the partial cascade and completes the teardown in its `finally` (§3, S11) |

---

## 3. H7/D1 proof — facade-fired events over Arch

`facade-events-proof/` **runs** decision D1 rather than asserting it. It contains a cut-down facade
(`MiniFacade.cs`) that owns `Set` / `Remove` / `NotifyChanged` / `Dispose` / singleton `Set` and raises
the engine's reactive events itself over Arch 2.1.0, plus twelve scenarios (`Scenarios.cs`) that
drive it through the shapes the migration has to keep working. Component types are named after the
engine components they stand for, so a check reads as a claim about a real site.

```bash
cd scratchpad/arch-spike/facade-events-proof && dotnet run -c Release   # exit code == failing checks
```

### Result: 148 checks, 0 failures

| Scenario | Stands for | What it establishes |
|---|---|---|
| S0 | D1's premise | Arch raises nothing; Arch's own iteration order measured on **both** forms (chunk enumerator and `Query(desc, ForEach)`) and **across chunks** (4 000 entities, several chunks, descending end to end) |
| S1 | M2/M6, D14, items 39/40/67 | Set absent→Added, present→Changed(**old**,new); `NotifyChanged`→Changed with `ReferenceEquals(old,new)`; `Set(new instance)`→different refs; `NotifyChanged` on absent throws; Remove-absent silent; `Dispose`→EntityDisposed then ComponentRemoved per component with `IsAlive == true`; double-Dispose silent |
| S2 | items 17/56/75/76 | id recycled by Arch; the stale handle stays dead, `!=` the new occupant, keyed dictionaries do not collide |
| S3 | M5, CORE_TENETS §9, items 8/39/43/66 | singleton Added / **Changed-not-Added** on re-Set / Removed / silent absent-Remove / Added again after Remove; no replay on subscribe; no carrier entity in any enumeration |
| S4 | M1, items 9/10/11/22/54, C11 | predicate membership backfilled at construction, then moved **only** by publication — an in-place flip keeps the body falling and keeps the retargeted sprite in the old pass; `Count` throws; 10k transient queries leak nothing |
| S5 | **M10** | `Set<ColliderTag>` from inside the `BoxCollider` Added handler: tag present when the outer `Set` returns, the collider set sees it the same frame, 500-entity batch clean, and the **discarded** subscription handle still dispatches after a forced GC |
| S6 | **item 41** | the LDtk parser shape end to end: sweep + 500 `Publish` inside a singleton **Added** dispatch, mass sweep inside **Removed**, three parses with no leak and no stale membership — nesting reaches 3 levels (singleton → message → component Added) |
| S7 | item 50, items 22/67 | `world.Dispose` with **inert** handlers fires the cascade DefaultEcs was measured to fire, **in the same order** — the whole 11-event sequence is asserted, over M4's discriminating fixture (a recycled id, one carrier holding two subscribed component types) plus a **second** world singleton `Set` in the reverse of its subscription order, so the world-component leg is an asserted order too (M4c's DefaultEcs answer). Its subscriptions are made in the phase order, so its log looks like phases — S10 is the fixture that shows what actually decides it. Also: a query held across teardown reads empty, and a disposed world holds no subscriptions and no query registrations |
| S8 | **item 41 at teardown**, item 50 | teardown with **mutating** handlers, three legs, each asserting the FULL sequence over an identity-carrying fixture: **(A)** the LDtk unload sweep from the world-component Removed leg double-fires exactly as DefaultEcs was measured to (M5) — and in doing so **breaks both of S7's teardown orders**: the re-reported entities route through `DisposeEntity`, so pool-grouping collapses to per-entity and `ComponentRemoved` lands *after* `WorldComponentRemoved`; **(B)** the same sweep from the `EntityDisposed` leg, where the facade's per-entity guard terminates (unwinding in reverse carrier order, which only a sequence assert can see) and DefaultEcs does not; **(C)** item 41's *Create* half at teardown — newborns fire `Added` and are then freed silently, matching M5b |
| S9 | item 50, D10's six `world.Dispose` sites | re-entrant **world** disposal, both directions. **(A)** `world.Dispose` from inside `world.Dispose`: the nested call is a no-op, one event per entity, the probe's depth cap never reached — without the guard the facade replayed the whole cascade (measured 3× on two carriers); parity with M7. **(B)** the **crossed** shape, `world.Dispose` from inside a normal `entity.Dispose`: the outer disposal returns normally instead of resuming against a destroyed Arch world, the 5-event sequence matches M8's DefaultEcs log exactly, and the dead world now refuses `CreateEntity` and singleton `Set` with a typed throw where DefaultEcs raises `NullReferenceException` |
| S10 | items 22/40/50, delta **A9** | the teardown order is **one channel list in this world's subscription order**: subscriptions deliberately interleaved (world `LevelData`, entity `Transform`, entity `LevelData`, world `CurrentLevel`, `EntityDisposed` last) and the full sequence asserted. A world component fires **first**, `EntityDisposed` fires **last**, and the type subscribed on **both** legs keeps a slot on each — the regression for the shared type-id registry that used to re-mint and collide. Mirrors M4d |
| S11 | item 50 | a teardown handler that **THROWS**: the exception escapes `world.Dispose`, the rest of the cascade is **dropped** (no `ComponentRemoved`, no `WorldComponentRemoved`, the third carrier never reported) — and the world is torn down anyway by the `finally`, so a second `Dispose` replays nothing. M9 measured the same partial cascade on DefaultEcs, which however leaves every carrier ALIVE and unrecoverable |

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
2. **Arch enumerates in DESCENDING index order**, on **both** iteration forms and **across chunks**.
   Three probes, because the claim carries items 22/48/58/70/74 and each probe alone leaves a way
   out: the chunk enumerator over 6 entities created `0..5` reports `5,4,3,2,1,0`; `world.Query(in
   desc, ForEach)` — the form the facade's `EntitySystem` will actually run, a different code path —
   reports the same; and a 4 000-entity sweep spanning several chunks is one descending run **end to
   end**, identical on both forms. So the reversal is not a within-chunk artefact that averages out
   over a whole-world sweep. Iteration order under Arch is therefore not merely *unspecified*, it is
   **reversed** relative to DefaultEcs' insertion-ish order: every first-match-and-break pick (items
   48, 58) flips, and every membership sweep written to disk (`SceneWriter`, items 70, 74) inverts.
   The facade must impose its own order; this proof sorts its unfiltered enumeration by entity id and
   keeps query membership in publication order, which is what makes item 22's tie-break claim
   testable at all. See finding 8 for the two teardown orders, the ones that are easy to miss.
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
7. **Contract item 50 is contradicted from both directions** (§0, **A1**). The measurement harness shows
   DefaultEcs firing a 10-event cascade on `world.Dispose`; this proof shows the facade reproducing it over
   Arch, in the same order. "Event-silent" would be a deliberate behaviour change, not parity — and C7
   byte-identity would fail.
8. **The teardown cascade is ONE ordered channel list, and without the facade it would inherit two
   different backends' accidents.** `EntityDisposed` order is entity id *within a channel* (finding 2
   covers that) and query membership is publication order — but (a) *which component of a
   multi-component entity reports first* would come from `Archetype.Signature.Components`, i.e. from
   Arch's chunk layout, and (b) *which world singleton reports first* would come from `Dictionary`
   enumeration — BCL insertion order, stable in practice and chosen by nothing. `MiniFacade` gives
   every channel — the `EntityDisposed` channel, each entity-component type, each world-component
   type — a **slot taken at SUBSCRIPTION** (`_channels`), deliberately not on an earlier
   `Set`/`Remove`/`NotifyChanged` (M4b's world D measured DefaultEcs doing exactly that), and both
   disposal verbs walk that one list. It is per-world by design, because M4b measured DefaultEcs to
   be per-world and the engine builds one world per screen. A type subscribed on **both** legs takes
   **two** slots — M4d's measured answer, and what the earlier single type-id registry got wrong: it
   re-minted the type on the second leg, reordering the world leg and colliding the next type's id
   (S10 is the regression). Without any of this, S7's asserted sequence would be a claim about
   Arch's archetype layout and the BCL's hashing rather than about the facade.
9. **A component channel is pool-grouped, not per-entity — while every handler is inert. A handler
   that mutates breaks the teardown order, and the engine ships that handler.** Inside one channel the
   measured DefaultEcs cascade walks that component *pool* across every carrier before the next
   channel runs (§2), which the obvious per-entity implementation gets wrong invisibly — every fixture
   where each entity carries one subscribed type logs identically either way. The proof therefore uses
   M4's own fixture (S7). **S8 then measures the scope of that claim**, with the engine's real unload
   sweep firing *inside* teardown: the swept entities re-enter `DisposeEntity`, which walks the same
   channel list for ONE entity, so pool-grouping survives only for the walk-driven half and
   `ComponentRemoved` can fire *after* `WorldComponentRemoved`. Wave 1 must therefore scope item 50's
   ordering clause to teardowns with inert handlers — the engine runs the other shape at every screen
   change. Four further consequences wave 1 inherits: the cascade must liveness-check each entity (a
   handler may already have disposed it, and reading a destroyed archetype is undefined); it must
   re-read each carrier's component set **at dispatch time**, never from a pre-cascade snapshot,
   because M5c measured DefaultEcs reporting a component a handler `Set`s mid-cascade; the whole
   dispatch must sit in a `try`/`finally`, or a throwing handler leaves a world that never destroyed
   its Arch world and replays the entire cascade on the next `Dispose` (M9 measured DefaultEcs leaving
   exactly that world behind — finding 13); and the `try` alone does **not** bound re-entry — see
   finding 11.
10. **Post-teardown reads are the facade's to define, because DefaultEcs' answer is a crash.** M6
    measured a live `EntitySet` after `world.Dispose`: stale `Count`, and `NullReferenceException` on
    enumeration. The facade drops membership and clears its subscription tables at teardown, so a
    held query reads empty and `SubscriberCount` is 0 (asserted in S7). Nothing in the engine reads a
    set after teardown — it would crash today — so defining the answer costs no behaviour and gives
    the screen-teardown obligation something executable to point at.
11. **`world.Dispose` needs a re-entrancy flag set BEFORE the cascade, and it cannot be the
    `_disposed` flag.** A handler that calls `world.Dispose` from inside the cascade passes a guard
    that only flips in the `finally`, re-snapshots a still-live world and replays every event
    (measured: 2 carriers, 6 `EntityDisposed`, bounded only by the probe's depth cap). It must be a
    separate `_tearingDown` flag, because entities are required to read `IsAlive == true` inside
    their own teardown handlers (S8 leg A) and `IsAlive` keys on `_disposed`. This is **parity**: M7
    measured DefaultEcs guarding the same shape. Six engine sites call `world.Dispose`, so it is a
    live shape, not a thought experiment (S9 leg A). **The crossed direction needs a second, different
    guard** (S9 leg B): when the nested `world.Dispose` comes from inside a normal `entity.Dispose`,
    the teardown *completes* — Arch world destroyed — and the outer `DisposeEntity` then resumes into
    `_arch.Destroy` on a world that no longer exists. That guard keys on `_disposed`, never on
    `_tearingDown`, because disposing entities from inside a *running* teardown is the engine's own
    unload sweep and S8 leg A requires it to keep double-firing. M8 measured the same event sequence
    on DefaultEcs; what the facade adds is the typed refusal afterwards, where DefaultEcs answers
    `CreateEntity`, `world.Set<T>` and even `Entity.IsAlive` with a `NullReferenceException`.
12. **Entities created *during* teardown are cascade-silent, on both backends.** A handler that
    spawns while the cascade is walking gets its `Added` and nothing more — the newborn is freed with
    the world without an `EntityDisposed` or a `ComponentRemoved` (S8 leg C; DefaultEcs measured
    identically in M5b). Item 41 is Dispose + Create + Publish, so its Create half now has a measured
    answer at teardown and not only during play.
13. **A throwing teardown handler silently drops the rest of the cascade — and wave 1 has to choose
    whether that stays.** Measured on both backends with three carriers and a handler failing on the
    second (S11 / M9): the exception escapes `world.Dispose`, the third carrier is never reported, not
    one `ComponentRemoved` fires and `WorldComponentRemoved` never runs. In engine terms
    `AudioSystem.OnAudioSourceRemoved` never cuts the remaining loops and the LDtk sweep never runs,
    at a screen change that happened anyway. The two backends then **diverge**: DefaultEcs leaves all
    three carriers ALIVE with a stale set and a silent second `Dispose` — an unrecoverable world —
    while the facade's `finally` completes the teardown. Item 50's restatement has to say which policy
    is the contract (fail-fast as measured, or aggregate every channel's exceptions and throw once at
    the end) and whether completing the teardown is a guarantee; it is not parity.

### What this proof deliberately does NOT cover

- **It is not the wave-1 facade.** No `ISystem`/`SequentialSystem`/`ParallelSystem`, no `[Subscribe]`
  attribute scan, no `IParallelRunner`, no world-generation stamp (D11), no `ReadAllComponents`.
- **It is not tuned.** Every publication re-evaluates the affected entity against every query
  interested in that component type, walking a copied query list; the real facade indexes queries by
  component type. The proof optimises for being obviously correct, not fast.
- **JIT only.** The WASM and NativeAOT legs are the sibling heads' job (§1), and nothing here is
  target-specific. Note that under NativeAOT this facade would still need the component registration of
  finding 1 in §1.
- **Concurrency.** Every scenario is single-threaded. The parallel runner, and whether a handler may
  fire off the update thread, is wave-2 scope (the plan already caps runner degree at 1).

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
shared/ArchSpikeComponents.cs   component shapes (struct, second struct, zero-sized tag, managed class,
                                plus `Doomed` — sacrificial, used only by the registry-reset probe)
shared/ArchCoreUtilsShim.cs     the Arch 1.x -> 2.x namespace shim the AOT generator needs
shared/ArchExercise.cs          the 43 checks both target proofs run, verbatim (incl. the H9/C12 probe,
                                the enumeration of Arch's World statics AND of ArrayRegistry, the
                                sparse-World.Worlds walk and the SharedJobScheduler sentinel)
aot-console/                    NativeAOT head  (exit code == proof)
wasm-head/                      KNI/BlazorGL Blazor WASM head (document.title == proof)
wasm-head/run-in-chrome.mjs     puppeteer-core driver against the SYSTEM Chrome
defaultecs-measurements/        the DefaultEcs interrogation harness (items 42/66, plus the teardown
                                set M4/M4b/M4c/M4d/M5/M5b/M5c/M6/M7/M8/M9)
facade-events-proof/            the H7/D1 proof: facade-fired events over Arch (items 3/41)
facade-events-proof/MiniFacade.cs   the cut-down facade under test
facade-events-proof/Scenarios.cs    the twelve scenarios, 148 checks (exit code == failing checks)
```
