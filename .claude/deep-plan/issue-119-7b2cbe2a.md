<!-- deep-plan contract for issue #119 (DefaultEcs→Arch migration). Run wf_ce468bd4-a20, 2026-08-19, gate PASS, base main@7b2cbe2a. The 2 contradictions and 7 decision themes in ## Síntese were RESOLVED as plan decisions D9–D15 in ~/.claude/plans/issue-119-ecs-arch-migration.md §3b — where this file and the plan disagree, the plan's resolutions win. Committed by the standalone deterministic-clock PR (the pre-wave PR of D16), not by wave 0/1 — it has to exist before the first wave branch reads it. Amended retroactively by that same PR: see "### Retroactive amendment — items 24 / 45 / 49 / 68" at the end of ## Contract, and the struck row in ## Money dimension table. -->
## deep-plan contract — Migrate the ECS backend from DefaultEcs to Arch, behind a MonoDreams-owned facad

### Verdict

- **⚠️ Contradictions flagged: 2** — two commitments give conflicting directives for the same mechanism/seam. RESOLVE THESE FIRST (see the `⚠️ Contradiction` theme(s) in ## Síntese); a gate PASS does not detect inconsistency between commitments.
- **Unresolved interactions:** 15 substantive GAP cell(s) across a 39×63 matrix. **This is the real remaining work** — each is an interaction the plan has not closed.
- **GAP clustering:** those 15 cell(s) reduce to ~13 distinct seam(s), grouped into 7 decision theme(s) in ## Síntese — the headline counts cells, not independent unknowns; most cells are one un-built surface repeated across rows.
- **Refute:** **did NOT converge** — hit the 3-round cap with 24 fresh refutation(s) still landing in the final round. Fresh refutations per round: [25, 25, 24] — shape **FLAT** (final ≈ peak — the interaction surface is far from exhausted; independent re-runs keep finding new interactions, not the same draft re-sampled). Final-round mix: 10 attack earlier rounds' RESOLUTIONS vs 14 new surface — a high resolution share means the loop is in fix-attack equilibrium (each fix mints new attack surface), not still discovering the original surface. Resolution re-refute (targeted pass over the final round's 24 otherwise-unattacked resolution(s)): 16 fresh refutation(s), integrated by a single resolver pass — that patch ships unattacked; weigh its resolutions accordingly. Treat this contract as a SAMPLE of the interaction space, not an exhaustive enumeration.
- **Gate:** PASS — every cell is filled and justified. PASS means *no blank or bare-GAP cell*; it does **NOT** mean zero open interactions — see the GAP count above.
- **Affected domains:** foundation, rendering, rendering-text, camera, physics, collision, level-loading, level-ldtk, ui, cursor, dialogue, debug, level-editor, audio, examples, cli, platform

### ⚠️ Unresolved — resolve or explicitly accept before finalizing

- GAP: Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) × EntityComponentReflection MethodInfo caches — EntityComponentReflection.cs:26-29,68 resolves INSTANCE Entity.Set<T>/Get/Has/Remove by name; plan never names site — wave-2 facade Entity must keep public instance generics or FindGeneric throws; MakeGenericMethod also AOT-hostile vs C2/D6
- GAP: Facade-fired events Added/Changed(old,new)/Removed × EntityComponentReflection MethodInfo caches — events fire only if reflection lands on the facade's Set — contingent on the unresolved reflection-shape gap (Set row, same column); silent event loss on designer edits otherwise
- GAP: Facade message bus typed + [Subscribe] × [Subscribe] hierarchy-walk registration — M3 promises attribute scan but not DefaultEcs's type-hierarchy walk + virtual-override dedup (TransformPhysicalCollisionResolutionSystem.cs:13-14,32) — naive scanner double-subscribes or misses the base-annotated virtual On
- GAP: IsAlive/Entity.Null handle semantics × screen-teardown world.Dispose — Arch recycles World slots in static World.Worlds (H9); a stale Entity from a disposed screen's world can read alive once the slot is reused across 10-screen churn — C13 names entity-id recycling only, not world-id reuse after Dispose
- GAP: Guard ratchet EcsBoundaryLintTests × packaging + manifests — MonoDreams.Cli/Installer/ProjectScaffolder.cs (verified) carries DefaultEcs literals until wave-4 swap — wave-1 guard 'no .cs outside facade' flags it; cli ratchet/allowlist entries not named in plan
- GAP: Guard ratchet EcsBoundaryLintTests × CLI tests asserting literal DefaultEcs — Cli.Tests assert literal 'DefaultEcs' (ManifestPlatformTests.cs:35-36, ScaffolderPlatformTests.cs:279-283,403) — trips wave-1 guard three waves before the wave-4 swap; KnownGaps entries unplanned
- GAP: Mutator: NotifyChanged on absent component × NotifyChanged publication fleet — M2/D4/C4 never pin the absent-component contract (DefaultEcs throws); a silently no-op facade would hide race bugs at ~40 sites — needs an EcsFacadeContractTests entry in C4
- GAP: Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) × EntityComponentReflection MethodInfo caches — events fire only if reflection lands on the facade's Set — contingent on the unresolved reflection-shape gap (Set row, same column); silent event loss on designer edits otherwise
- GAP: Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe × [Subscribe] hierarchy-walk registration — M3 promises attribute scan but not DefaultEcs's type-hierarchy walk + virtual-override dedup (TransformPhysicalCollisionResolutionSystem.cs:13-14,32) — naive scanner double-subscribes or misses the base-annotated virtual On
- GAP: IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids × screen-teardown world.Dispose — Arch recycles World slots in static World.Worlds (H9); a stale Entity from a disposed screen's world can read alive once the slot is reused across 10-screen churn — C13 names entity-id recycling only, not world-id reuse after Dispose
- GAP: Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade × packaging + manifests — MonoDreams.Cli/Installer/ProjectScaffolder.cs carries DefaultEcs literals until wave-4 swap — wave-1 guard 'no .cs outside facade' flags it; cli ratchet/allowlist entries not named in plan
- GAP: Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade × CLI tests asserting literal DefaultEcs — Cli.Tests assert literal DefaultEcs (ManifestPlatformTests.cs:35-36, ScaffolderPlatformTests.cs:279-283,403) — trips wave-1 guard three waves before the wave-4 swap; KnownGaps entries unplanned
- GAP: Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve × NotifyChanged publication fleet — M2/D4/C4 never pin the absent-component contract (DefaultEcs throws); a silently no-op facade would hide race bugs at ~40 sites — needs an EcsFacadeContractTests entry in C4
- GAP: IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids × world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) — same H9 world-slot reuse hole as the screen-teardown cell: stale Entity from a disposed world can read alive after Arch reuses the world slot — C13 must add a world-version stamp; test over 10-screen churn
- GAP: IsAlive/Entity.Null handle semantics × world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) — H9 world-slot reuse: stale Entity from a disposed world can read alive after Arch reuses the world slot — C13 must add a world-version stamp; 10-screen-churn test

## Síntese

Duas contradições de diretiva sobrevivem ao gate (que só valida preenchimento, não concordância), seguidas de cinco temas que agrupam os GAP cells restantes — três deles são pares "célula diz GAP / contrato já carrega o pin" que o planner precisa reconciliar com dono de milestone, não apenas aceitar.

### ⚠️ Contradiction — captura do snapshot de iteração (frame-stable vs por-enumeração)
A premissa D2 ("EntitySystem<T> iterates a **frame-stable** snapshot") e a célula do draw sort ("wave-2 D3 frame-stable list per C15") vs D3 amended(2)/(3) + a premissa nova ("captured per **Update/enumeration start**, never per-frame cached; publication applies membership synchronously") + o teste C4 same-frame Culling→prep. As duas redações prescrevem semânticas diferentes: um snapshot congelado por frame faz os prep systems `[With(VisibleComponent)]` perderem os adds de CullingSystem.cs:100,107 no mesmo frame (TextPrepSystem.cs:75), violando o invariante do pipeline. Decisão: adotar por-enumeração + membership síncrono como redação única e expurgar "frame-stable" da premissa D2, da base da dimension row e da célula do draw sort (a estabilidade do sort vem do DrawSortBuffer copiado, não da query).

### ⚠️ Contradiction — autoridade de parada do áudio no teardown (AudioSystem.cs:133)
Lado A: C4 dispose-cascade exige entrega de `Removed` — "looping AudioSourceComponent cut" em AudioSystem.cs:133-137. Lado B: C4 world.Dispose event-silent exige que :133-137 "observe nothing" no teardown — compatíveis apenas sob a premissa de ordenação pipeline-antes-de-world, pinada só para LoadLevelExampleGameScreen.cs:728-733 e LevelSelectionScreen.cs:626-634 enquanto a coluna lista seis sites de world.Dispose (ScreenController.cs:84,114; SplashScreen.cs:159; DemoLauncherScreen.cs:356; InfiniteRunnerScreen.cs:612). Decisão: eleger AudioSystem.Dispose (:158-173) como autoridade única e testar a ordenação em TODOS os sites listados, ou fazer o facade world.Dispose disparar a cascata — rompendo o "matching DefaultEcs" pinado; sem isso, qualquer tela que disponha o world primeiro vaza instâncias nativas em loop que nenhum teste C4 observa.

### World-slot reuse pós world.Dispose (H9) — o GAP que o próprio contrato admite aberto
Cobre os 3 GAP cells das linhas IsAlive (colunas screen-teardown e world.Dispose bulk-teardown): C13 pina reciclagem de entity-id, mas o contrato registra "extend to world-slot reuse after screen Dispose (**open GAP**)" — um handle stale de um world descartado pode ler vivo quando o Arch reusa o slot em `World.Worlds`. Decisão: estampar world-generation (além da versão de entidade) no Entity do facade, com teste de churn de 10 telas; gira em ScreenController.cs:84,114 e nos 10 screens nomeados na wave 2.

### EntityComponentReflection — genéricos de instância do facade + AOT (EntityComponentReflection.cs:26-29,68)
GAP nas linhas Set e eventos (coluna MethodInfo caches): `FindGeneric` resolve `Entity.Set<T>/Get/Has/Remove` por nome — se o Entity do facade na wave 2 não mantiver esses genéricos públicos de instância, edições do inspector lançam; se a reflexão contornar o facade, os eventos de publicação somem silenciosamente; e `MakeGenericMethod` colide com C2/D6 (AOT/WASM). Decisão: nomear essa superfície como item de contrato da wave 2 com caminho AOT-safe (delegate cache/registry) + teste de edição de designer que atravessa a publicação do facade.

### [Subscribe] hierarchy-walk sem dono de milestone (TransformPhysicalCollisionResolutionSystem.cs:13-14,32)
GAP nas linhas de bus: M3 promete o scan de atributo mas não o hierarchy walk + dedupe de override virtual, enquanto o contrato e a premissa já carregam o teste (dispatch único, `MethodInfo.GetBaseDefinition`) — teste sem mecanismo implementador nomeado. Decisão: amarrar o scanner ao escopo de M3/wave-1 e reconciliar a célula; um scanner ingênuo ressuscita o bug documentado no próprio arquivo (resolução dupla de cada CollisionMessage, com back-projection destrutiva pós-depenetração).

### NotifyChanged em componente ausente — pin existe no contrato, célula segue GAP
GAP na coluna NotifyChanged fleet (ambas variantes de linha) diz "M2/D4/C4 never pin", mas o contrato já traz "pinned by contract test (DefaultEcs throws today) — never a silent no-op". Decisão: confirmar que a entrada entra no EcsFacadeContractTests da wave 1 (com dono) e virar a célula — um no-op silencioso esconderia race bugs nos ~40 sites de publicação.

### Literais DefaultEcs do CLI vs lint da wave 1 — KnownGaps sem entradas nomeadas
GAPs guard-ratchet × packaging e × CLI tests: MonoDreams.Cli/Installer/ProjectScaffolder.cs e os asserts literais de ManifestPlatformTests.cs:35-36 / ScaffolderPlatformTests.cs:279-283,403 disparam o lint "no .cs outside facade" três waves antes do swap da wave 4; o contrato diz que "ride KnownGaps until wave 4", mas as células apontam que as entradas KnownGaps/allowlist não estão enumeradas no plano. Decisão: nomear as entradas exatas na spec do EcsBoundaryLintTests da wave 1 (espelhando o allowlist por onda já dado ao MonoDreams.Benchmarks), para o ratchet nascer verde e esvaziar-se no fechamento da wave 4 antes do sweep C19.

## Contract

1. C1 wave0: MonoDreams.Benchmarks (BenchmarkDotNet, in monodreams.sln) — creation/structural churn/iteration 10k/100k/1M, struct + managed DrawComponent-shaped, DefaultEcs vs Arch; RESULTS.md committed.
2. C2 wave0: Arch compiles into KNI/BlazorGL WASM (-p:MonoDreamsPlatform=web) and AOT publish (PublishAot=true) smoke with Arch.AOT.SourceGenerator.
3. C3 wave0: runnable proof of facade-fired Added/Changed(old,new)/Removed + singleton notifications + predicate membership over Arch, exercising the M10 mutation-inside-handler shape.
4. C4 [unit] Set contract: absent→add+Added; present→replace+Changed(old,new); never throws. Written wave 1, stays green wave 2 over Arch's update-only Set.
5. C4 [unit] NotifyChanged fires Changed with ReferenceEquals(old,new); OnAudioSourceChanged (AudioSystem.cs:139-144) must not stop the live instance; Set(new instance) must.
6. C4 [unit] NotifyChanged on absent component: pinned by contract test (DefaultEcs throws today) — never a silent no-op.
7. C4 [unit] snapshot iteration: mid-Update Set-new/Remove/Dispose/Create → loop completes; each pre-loop member visited once; member disposed earlier in loop SKIPPED; creations not visited this frame.
8. C4 [unit] singleton store: Set-when-present→Changed not Added; Remove→Removed; Remove-when-absent no-op firing nothing (LDtkLevelLoadSystem.cs:71-82 first load); re-Set after Remove→Added (Added-keyed LDtk parsers re-trigger).
9. C4 [unit] EntityQuery.Count and every deliberately unimplemented facade member throws NotSupportedException, never a silent default.
10. C4 [unit] transient using-scoped EntityQuery unhooks callbacks on Dispose (rendering :772); 10k create/dispose cycles → zero subscriber growth.
11. C4 [unit] query construction backfills predicate membership from CURRENT values of pre-existing entities, then publication-driven only.
12. C11 [unit] GravitySystem.cs:10 — flip Gravity.active in place, no publish → keeps falling; Set/NotifyChanged → membership moves (fails today; foundation :692 Tests: none yet). MasterRenderSystem.cs:90 — Set-retarget moves entity between pass sets; in-place edit does not.
13. C10 [unit] M10: BoxColliderComponent add fires ComponentAdded handler Setting ColliderTagComponent inside the callback (TransformCollisionDetectionSystem.cs:87-95); tag present immediately; _activeSet sees collider same frame; batch-add N clean.
14. C10: 11 reactive sites (collision x2, level-ldtk x3, audio x2, TileGridBake x2, BoundaryBake x2), one named passing facade test each; C5 lint self-heals any missed site.
15. [unit] [Subscribe] scan: subclass overriding a virtual [Subscribe] handler WITHOUT re-annotating gets exactly one dispatch (TransformPhysicalCollisionResolutionSystem.cs:13-14,32; collision :220-244); dedupe via MethodInfo.GetBaseDefinition.
16. [unit] message bus: two subscriber instances of one type both receive one Publish (dialogue :222); a second world's subscribers never fire; both Subscribe<T> and Subscribe(this) forms covered.
17. C13 [unit] handle lifetime: Dispose entity, create until id recycles → stale IsAlive==false; Get/Has as DefaultEcs today; never reads new occupant; Entity.Null semantics preserved; extend to world-slot reuse after screen Dispose (open GAP).
18. [unit] class-component identity: Get<DrawComponent>() twice → same instance; in-place writes visible same frame without publish (foundation :707, SpritePrepSystem notify-free pattern); H8 managed path measured, not converted.
19. C12 [unit] hygiene: guard names any test newing a facade/Arch World without Dispose (90 test files call new World()); ProcessWideState.Reset returns Arch World.Worlds/component statics to baseline; MONODREAMS_TEST_SEED=8 green.
20. C5/C14 [unit] EcsBoundaryLintTests (EditorThemeLintTests model + KnownGaps ratchet): no git-tracked .cs outside MonoDreams/foundation/Ecs/ references DefaultEcs (wave1) nor Arch (wave2); ratchet empty at wave close; Cli scaffolder + Cli.Tests literals ride KnownGaps until wave 4.
21. [unit] ParallelSystem<T> at degree 1 runs children sequentially in registration order; EditorPipelineRegistrar group tree (names, policies, SetEnabled cascade) unchanged (5 sites); runner asserts/documents degree==1.
22. [unit] draw tie-break: two same-target same-LayerDepth sprites keep insertion order across frames under Arch (rendering :791); facade snapshot order deterministic and documented.
23. [integration] Restart flow both waves: load→edit→Restart → markers swept, scene entities disposed+reloaded, editor infra survives, undo cleared; EditorTransportTests + EditorGameModeTests pass unchanged.
24. C7 [e2e] wave-1 identity: gate x5 (dotnet test monodreams.sln Release -m:1) + MONODREAMS_TEST_SEED=8; 6/6 Demos headless --frames 600 non-blank; PNGs byte-identical vs main baseline — add byte-diff helper to GameTestRunner (AssertScreenshotNonBlank insufficient).
25. C15 [e2e] wave-2: gate x5 + seeded; 6/6 demos non-blank + AssertHeapFlat (cap 1.5x); Examples boots Level_0 + Blender_Level; .mdscene load→save byte fixed point; web head builds; pointer-replay PNG diffs explained cause-by-cause, never re-baselined (H4).
26. C20 [integration] CLI: monodreams init + add <every module> compiles with facade usings, no DefaultEcs package in scaffolded csproj; ManifestHonesty all legs, MONODREAMS_MANIFEST_HONESTY=1.
27. C6: DrawPrepSystemBase deleted wave 1 (zero subclasses; useParallel param actually maps to DefaultEcs useBuffer).
28. C8: every new foundation facade premise ships Tests: named — none lands with Tests: none yet.
29. C9: wave-2 diff scoped to MonoDreams/foundation/Ecs/ + dependency manifests only.
30. C16/C17/C18: benchmarks wave2-vs-wave0 report in PR; wave-3 hot systems (Culling, SpritePrep, TextPrep, MeshPrep, YSort, MasterRender feed, Gravity, Velocity, BuildEntries) hold-or-improve; regressions reverted or justified; gate x5 + demos green.
31. C19: zero DefaultEcs tokens in git-tracked files except CHANGELOG / docs/ecs-migration.md / README credits (wave 4); blazor-spike deleted (D7); locks regenerated.
32. C21: #118 DefaultEcs-specific defensive resets retired where moot; seeded harness + hygiene guard kept.
33. C22: every premise naming DefaultEcs rewritten/removed same PR — foundation :7/:393/:505/:692, collision :223, audio :14/:207+ov:42, rendering :772, level-editor :1260/:1509+ov:29/:66, ui :988/:993, dialogue :222, camera :235+ov:30+flows/camera.md:83.
34. C22: docs sweep — CORE_TENETS, foundation overview facade tour, CLAUDE.md, skills-config.md stack line, README (credits historical), CONTRIBUTING, web-targeting.md:189, index.md, new docs/ecs-migration.md; CHANGELOG '### Breaking' + *Migration:* per break.
35. C23: after wave-4 merge close #117 and comment on Doraku/DefaultEcs#197 (D8; confirm at approval).
36. Wiring counts: 1826 Set sites; ~321 files (176 engine, 100 tests, 34 Examples.Core, 7 Demos); 17 [Subscribe] in 14 files; 9 Subscribe(this); 19 typed Subscribe<T>; ~40 NotifyChanged; 4 singleton types; 9 world-Remove lines; 5 ParallelSystem uses; 71 files ISystem<GameState>.
37. Exact packaging values: DefaultEcs 0.18.0-beta01 leaves MonoDreams.csproj:143, Examples.Core:38, Demos:25, blazor-spike:58 (deleted), packages.lock.json:11, module.schema.json:55, Cli.csproj:31-33 comment, foundation/module.json:34.
38. Branches issue-119-wave0..wave4 stacked, one PR per wave, wave N+1 base = wave N branch (D5); Conventional Commits, English.
39. C4 [unit] Remove-when-absent (entity + world-singleton) = silent no-op, fires nothing; EditorTransport.cs:399-400,411-412 legs are ALWAYS absent (editor never Sets CurrentLevel/BackgroundColor); re-Set after absent-Remove fires Added.
40. C4 [unit] dispose cascade: entity.Dispose fires ComponentRemoved per present component, synchronously, value captured BEFORE Arch Destroy; looping AudioSourceComponent cut (AudioSystem.cs:133-137); covers EditorTransport.cs:419-429 + LDtkTileParserSystem.cs:145-156 sweeps.
41. C3/C10 parser shape: wave-0 proof + a named test exercise mass Dispose+Create+Publish inside singleton Added AND Removed dispatch (LDtk parsers), not only the single-entity M10 shape.
42. C4 [unit] replay-on-subscribe pinned from wave-0 measurement: singleton Subscribe fires NOTHING for an already-present value (parsers keep manual Has+Get replay — no double parse); entity-level SubscribeComponentAdded replay matches DefaultEcs.
43. C4 [unit] carrier invisibility: hidden singleton carrier excluded from every query/enumeration/count surface incl. the UNFILTERED world.GetEntities() form (now required facade surface: DebugInspector.cs:78, EditorTransport.cs:419-429); sweeps cannot dispose it.
44. D3 amended: EntityQuery.Count stays NotSupported; wave-1 sweep rewrites the two EntitySet.Count asserts (ColliderActionTests.cs:194,297) to snapshot-enumeration counts — named in wave-1 scope.
45. C7 amended: headless Demos get an injected deterministic fixed-step clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt); byte-identity gated only after a main-vs-main double-run precheck; byte-diff helper in GameTestRunner.
46. C17 amended: wave-3 chunk conversions of GravitySystem/MasterRenderSystem feed must pass C11's negative tests through the CONVERTED execution path — a live per-element predicate read (inverting publication-cached membership) fails the gate.
47. C5/C14/C19 amended: MonoDreams.Benchmarks is a named allowlist entry (DefaultEcs waves 1-3, raw Arch waves 2-3); wave 4 deletes the DefaultEcs legs and the allowlist entry before the C19 sweep.
48. Wave 2: camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) get an explicit deterministic rule (lowest-entity-id or single-instance assert) + test — H4's executable seam.
49. C7 amended(2): deterministic clock merges to MAIN as a standalone pre-wave PR; baselines captured from main+clock AFTER it lands; main-vs-main double-run must be byte-identical before C7 gates; on failure fix clock and recapture — baselining from the wave branch is forbidden.
50. C4 [unit] world.Dispose is event-silent: no per-component/singleton Removed, no cascade — entity.Dispose-only; AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs (:74-75) observe nothing, matching DefaultEcs.
51. C4 [unit] composite Dispose cascade: Sequential/Parallel/GatedSystem Dispose recurses to leaves reverse-order — AudioSystem.cs:158-173 stops instances, CullingSystem.cs:112-120, MasterRenderSystem GPU; screens dispose pipeline BEFORE world (LoadLevelExampleGameScreen.cs:728).
52. C17 amended(2): wave-3 YSort conversion preserves BOTH clamps — final depth INCLUDING bias clamped to [minDepth,maxDepth] (YSortSystem.cs:50-55) and child-draw clamp incl. minimalBias (YSortSystem.cs:84-90); edge-of-band+bias regression test.
53. D3 amended(2): enumeration snapshot captured at EACH Update/enumeration start ('frame-stable' wording dropped); publication applies membership synchronously — C4 same-frame test: CullingSystem's VisibleComponent adds reach prep/YSort the SAME frame.
54. C4 [unit] construction-time seeding: transient/late-built queries (filtered, unfiltered, predicate) over a pre-populated world seed membership by live scan at construction, then go publication-driven — InvalidateAll (TileGridBakeSystem.cs:169-175) and Restart-sweep shapes.
55. C10 amended: bake tests assert self-identical Changed (NotifyChanged) IS DELIVERED and triggers re-bake — delivery is the trigger (TileGridBakeSystem.cs:164 quiet-timer, BoundaryBakeSystem.cs:97); a facade suppressing old==new Changed fails.
56. C13 extended(2): facade Entity Equals/GetHashCode/operator== version-stamped — dead handle finds/removes its own keyed entry (TileGridBakeSystem.cs:186,196), never equals the recycled slot's occupant; == default sentinel preserved (EditorPanelStateComponent.cs:38); contract test.
57. [unit] runner-accepting predicate ctor: GravitySystem.cs:9 passes IParallelRunner into the predicate-set system — facade EntitySystem(world, runner) is required surface; degree>1 throws NotSupportedException; C11 negatives routed through this ctor.
58. Wave 2: repo-wide first-match-and-break census (engine + Examples + Demos) — every site gets an explicit deterministic pick or single-instance assert; RunnerSpawnerSystem.cs:56-61, InfiniteRunnerScreen.cs:331-340, SelectionSystem.cs:404-415, TabSystem.cs:71 + camera sites.
59. C4 surface amended: facade EntitySystem<T> exposes AEntitySetSystem template hooks — virtual PreUpdate/PostUpdate and Dispose override (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem).
60. C4 carrier-invisibility extended: AComponentSystem pool iteration (TransformCommitSystem.cs:15) never observes carrier-held singleton instances; no singleton type is an entity component today — pinned regardless.
61. D3 amended(3): wave 1 rewrites ALL EntitySet.Count asserts — measured census ColliderActionTests.cs:149,154,157,160,194,297,493 + EditorContextMenuTests.cs:650-651 (9 asserts/2 files); repo-wide EntitySet '.Count' grep re-run at wave-1 close before the gate.
62. Census corrected: 449 'using DefaultEcs' lines / 320 git-tracked files excl. scratchpad (452/321 incl.) — the 305 figure is retracted; wave-1 sweep sized to this base; C5 lint remains the completeness proof.
63. C4/C10 LDtk reload corrected: Remove legs run only in else/catch (present after prior success -> Removed -> unload sweep); SUCCESS re-import Sets w/o Remove -> Changed with zero subscribers (inert, pinned); fail-then-reimport -> Added re-parses.
64. C4 [unit] bus dispatch: nested world.Publish inside an in-flight handler runs synchronously re-entrant (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101 -> SceneReaderSystem.cs:125 -> EntitySpawnSystem.cs:70); exceptions propagate unwrapped (PrefabExpansionTests.cs:190).
65. C4 [unit] no-double-registration: a handler both [Subscribe]-annotated and ctor typed-subscribed dispatches ONCE; the facade never auto-scans an instance that did not call Subscribe(this); mixed-marking census (6 files) rides the wave-1 sweep.
66. C3 amended(2): wave 0 also measures the DefaultEcs WORLD-component subscribe-replay leg (subscribe over an already-Set world comp); the C4 singleton no-replay pin is conditional on that measurement; parser no-double-parse test ships regardless.
67. C4 dispose synchrony: when entity.Dispose returns, IsAlive==false and query membership already dropped (no deferred CommandBuffer apply); double-Dispose of a dead handle is a silent no-op firing nothing; HierarchySystem.DisposeOrphans (:43,55-83) same-frame poll test.
68. C7 amended(3): every wave branch bases on post-clock main (rebase if the clock PR lands after the stack is cut); the main-vs-main byte-identity precheck also runs ON the wave branch before C7 gates.
69. C17 amended(3): chunked override FORBIDDEN for structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) unless the conversion buffers-then-mutates; TextPrepSystem keeps its per-frame Set publication; structural+retarget tests through the converted path.
70. SceneWriter determinism pinned: membership sweeps save in explicit id order (SceneWriter.cs:80,:257), never backend iteration order; one-camera refusal (:205); save-twice byte-identical test + .mdscene fixed point (C15).
71. C13 census widened: DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114 (type-level fix covers).
72. D3 amended(4): Count census = 23 asserts/5 files: ColliderAction(7), EditorContextMenu(2), ColliderDebugSystemTests.cs:75-195(6), ProxyVertexTests.cs:78-134(7), CameraEntityEditorTests.cs:210; rewritten wave 1; AsSet-var-aware grep gates close.
73. Count census base widened tests->ALL git-tracked .cs: engine hit SceneCameraEnsure.cs:65 (boot path via SceneReaderSystem.cs:391) rewritten wave 1 to a snapshot-enumeration presence check — Count-NotSupported must never reach .mdscene load; C15 boot e2e guards.
74. SceneWriter mint-leg pin: AssignStableIds (SceneWriter.cs:269-274,295) stamps first-time ids over deterministically-ordered roots (facade snapshot order or explicit key), never raw backend enumeration; first-stamp-order test on an UNstamped scene; save-twice alone insufficient.
75. C13 restated: Entity-keyed census non-exhaustive BY DESIGN — type-level version-stamped Equals/GetHashCode/== is the seam; spot-tests: TileGridBake :117/:118/:125, undo subgraphs (DeleteEntityCommand.cs:31,50), DialogueStateComponent.cs:23, EditorChromeBuilder.cs:73-77.
76. C13 [unit] undo dead-handle pin: DeleteEntityCommand holds DISPOSED handles by design (subgraph cleared :50; redo re-creates fresh ids per :25 doc); a dead handle never reads or aliases a recycled occupant across arbitrary churn; subgraph recycled-id test.

### Retroactive amendment — items 24 / 45 / 49 / 68 (the standalone clock PR, measured)

The clock PR landed items 45/49/68 and the byte-diff helper leg of 24. What it measured changes how the
remaining legs of 24 must be read; the amendments below are the authority, and the corresponding
dimension row is struck and replaced in ## Money dimension table.

- **24 (C7 identity gate), amended:** "6/6 Demos headless … PNGs byte-identical vs main baseline" reads
  as **5/6 under the deterministic-input protocol** — launcher/camera/dialogue/ui/audio. The `--frames
  600` non-blank leg is unaffected and still runs 6/6. The `MONODREAMS_TEST_SEED=8` leg of this item is
  green as of the clock PR (the failure it exposed was an upstream DefaultEcs query-filter cache
  collision, now confined per-test by `ProcessWideState`).
- **49/68 (the precheck), satisfied:** the main-vs-main double-run precheck exists as a named,
  branch-portable test — `dotnet test MonoDreams.Tests/ --filter FullyQualifiedName~DeterministicClockTests`
  (~30–45 s, 10 host spawns). Run it ON the wave branch before any C7 gate, exactly as 68 requires.
- **What "captured the same way" means** (49's baseline rule): the protocol, not merely the clock —
  editor flag + op plan present (⇒ `SkipHardwareRead`), `Play@0`, final-frame-only capture. A baseline
  captured outside it is noise, and comparing against it proves nothing.
- **Physics stays out** until its unseeded `Random` is seeded; that is a user-visible product decision
  (fixed layout on every launch vs a headless-only fork) and was deliberately NOT taken by the clock PR.
- **Never loosened:** the comparer itself has no tolerance knob and skips no frames, by design.

## Interaction matrix

> 39 states × 63 columns = 2457 cells · 384 handled / 2058 N·A / **15 GAP**.

| State | Column | Verdict | Where / justification |
|---|---|---|---|
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | GravitySystem predicate set | handled | D4+M1/C11; GravitySystem.cs:10 — Set republish moves membership |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | MasterRenderSystem.BuildDrawSet | handled | D4+C11; MasterRenderSystem.cs:90 predicate re-eval on Set |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | MasterRenderSystem stable draw sort | N/A | sort consumes copied buffer; Set semantics not exercised |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | AudioSystem Changed handler | handled | C4 add-or-update test + C10; AudioSystem.cs:39-42 documents the dependency |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | TransformCollisionDetection reactive add (M10) | handled | C3 M10 proof; add path Has-guarded (TransformCollisionDetectionSystem.cs:89,94) |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | Bake systems old-value diffing | handled | M6/C10 — Changed(old,new) raised by facade Set |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | LDtk world-singleton subscribers + late-join replay | N/A | world-level Set — singleton-store row covers |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | LDtkLevelLoadSystem world Set/Remove | N/A | world-level — singleton-store row covers |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | EditorTransport restart | N/A | world-level — singleton-store row covers |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | HierarchySystem managed singleton | N/A | world-level — singleton-store row covers |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | [Subscribe] hierarchy-walk registration | N/A | message-bus column |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | [Subscribe] + Subscribe(this) fleet | N/A | message-bus column |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | typed Subscribe<T> kept IDisposable | N/A | message-bus column |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | CullingSystem mid-update structural add/remove | handled | D4+D2 waves 1-2; wave-3 chunk conversion gated by C17 buffer-then-mutate rule (contract added) — CullingSystem.cs:100 Has-guarded Set stays safe on every path |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | prep systems mid-loop Set | handled | D4+D2; TextPrepSystem.cs:75, ButtonMeshPrepSystem.cs:83 |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | GameOverSystem create+dispose mid-iteration | N/A | Create/Dispose site — snapshot/IsAlive rows cover |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | manual-iteration mutators | handled | D4+D3; 2b-listed unsafe manual sites migrate wave 1 |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | NotifyChanged publication fleet | N/A | NotifyChanged verb — its own row |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | EntityComponentReflection MethodInfo caches | GAP | EntityComponentReflection.cs:26-29,68 resolves INSTANCE Entity.Set<T>/Get/Has/Remove by name; plan never names site — wave-2 facade Entity must keep public instance generics or FindGeneric throws; MakeGenericMethod also AOT-hostile vs C2/D6 |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | ReadAllComponents consumers | N/A | read-only site |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | IGameScreen ISystem<GameState> contract | N/A | type surface only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | ScreenController.Runner | N/A | runner surface only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no Set at this site |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | screen-teardown world.Dispose | N/A | dispose path — lifecycle row covers |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | PointerReplaySystem persistent sets | N/A | presence-query reads only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | packaging + manifests | N/A | packaging only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | CLI tests asserting literal DefaultEcs | N/A | packaging tests only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | ProcessWideState registry | N/A | no static involved |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | seeded test-order shuffle | N/A | harness only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | guard-test model | handled | D4 'guard-enforced' + C14 — raw Arch update-only Set unreachable outside facade |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | DrawPrepSystemBase (dead) | N/A | deleted wave 1 (C6) |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | value-predicate premise text | handled | premises.md:699 names Set as publication verb; C22 rewrite + C4 codify |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | YSortSystem/DebugInspector hierarchy reads | N/A | singleton reads only |
| Facade-fired events Added/Changed(old,new)/Removed | GravitySystem predicate set | handled | D1+D3 — membership driven by facade publication events; C11 |
| Facade-fired events Added/Changed(old,new)/Removed | MasterRenderSystem.BuildDrawSet | handled | D1+D3/C11 |
| Facade-fired events Added/Changed(old,new)/Removed | MasterRenderSystem stable draw sort | N/A | no event consumer in sort |
| Facade-fired events Added/Changed(old,new)/Removed | AudioSystem Changed handler | handled | D1 mandates Changed(old,new) (M6); C10; AudioSystem.cs:38-42 verified |
| Facade-fired events Added/Changed(old,new)/Removed | TransformCollisionDetection reactive add (M10) | handled | C3/C10 + replay-on-subscribe parity pinned (C4, new mutator row): entity-level Added replay measured wave 0, preserved |
| Facade-fired events Added/Changed(old,new)/Removed | Bake systems old-value diffing | handled | M6 names BoundaryBakeSystem.cs:97 / TileGridBakeSystem.cs:159; C10 |
| Facade-fired events Added/Changed(old,new)/Removed | LDtk world-singleton subscribers + late-join replay | handled | C3 proof extended to parser shape (mass Dispose+Create+Publish inside singleton Added AND Removed dispatch); C4 no-replay-on-subscribe kills double-parse |
| Facade-fired events Added/Changed(old,new)/Removed | LDtkLevelLoadSystem world Set/Remove | handled | M5; C4 'world Remove fires Removed' test |
| Facade-fired events Added/Changed(old,new)/Removed | EditorTransport restart | handled | M5 + C4 Changed-not-Added; CORE_TENETS §9 preserved |
| Facade-fired events Added/Changed(old,new)/Removed | HierarchySystem managed singleton | handled | M5; in-place mutation fires nothing, as today (HierarchySystem.cs:35) |
| Facade-fired events Added/Changed(old,new)/Removed | [Subscribe] hierarchy-walk registration | N/A | message bus, not component events |
| Facade-fired events Added/Changed(old,new)/Removed | [Subscribe] + Subscribe(this) fleet | N/A | message bus |
| Facade-fired events Added/Changed(old,new)/Removed | typed Subscribe<T> kept IDisposable | N/A | message bus |
| Facade-fired events Added/Changed(old,new)/Removed | CullingSystem mid-update structural add/remove | handled | D1 events feed prep-set membership; C15 visual identity gate |
| Facade-fired events Added/Changed(old,new)/Removed | prep systems mid-loop Set | handled | D1+D2; C4 snapshot-tolerance contract test |
| Facade-fired events Added/Changed(old,new)/Removed | GameOverSystem create+dispose mid-iteration | handled | Dispose⇒per-component Removed pinned (new C4 dispose-cascade entry + mutator row); D2 snapshot; no longer bare D1-D3 cite |
| Facade-fired events Added/Changed(old,new)/Removed | manual-iteration mutators | handled | D1+D3 |
| Facade-fired events Added/Changed(old,new)/Removed | NotifyChanged publication fleet | handled | M2 — NotifyChanged fires Changed old==new; C4 |
| Facade-fired events Added/Changed(old,new)/Removed | EntityComponentReflection MethodInfo caches | GAP | events fire only if reflection lands on the facade's Set — contingent on the unresolved reflection-shape gap (Set row, same column); silent event loss on designer edits otherwise |
| Facade-fired events Added/Changed(old,new)/Removed | ReadAllComponents consumers | N/A | read path fires nothing |
| Facade-fired events Added/Changed(old,new)/Removed | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Facade-fired events Added/Changed(old,new)/Removed | ScreenController.Runner | N/A | runner surface |
| Facade-fired events Added/Changed(old,new)/Removed | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition only |
| Facade-fired events Added/Changed(old,new)/Removed | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no events here |
| Facade-fired events Added/Changed(old,new)/Removed | screen-teardown world.Dispose | handled | wave 2 + C12 — facade-owned subscriptions die with world Dispose |
| Facade-fired events Added/Changed(old,new)/Removed | PointerReplaySystem persistent sets | handled | D3 membership fed by facade events; PointerReplaySystem.cs:137-138 |
| Facade-fired events Added/Changed(old,new)/Removed | packaging + manifests | N/A | packaging only |
| Facade-fired events Added/Changed(old,new)/Removed | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Facade-fired events Added/Changed(old,new)/Removed | ProcessWideState registry | handled | C12 — any static event/type table registered on introduction |
| Facade-fired events Added/Changed(old,new)/Removed | seeded test-order shuffle | N/A | harness; leakage covered by lifecycle row |
| Facade-fired events Added/Changed(old,new)/Removed | guard-test model | N/A | guard column |
| Facade-fired events Added/Changed(old,new)/Removed | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Facade-fired events Added/Changed(old,new)/Removed | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Facade-fired events Added/Changed(old,new)/Removed | value-predicate premise text | handled | C8 new facade premises name the event contract; C22 |
| Facade-fired events Added/Changed(old,new)/Removed | YSortSystem/DebugInspector hierarchy reads | N/A | reads fire no events |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | GravitySystem predicate set | handled | M2+C11 — NotifyChanged re-runs gravity predicate |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | MasterRenderSystem.BuildDrawSet | handled | M2+C11 |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | MasterRenderSystem stable draw sort | N/A | no publication in sort |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | AudioSystem Changed handler | handled | M2 old==new preserved; AudioSystem.cs:141 ReferenceEquals verified; C4 |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | TransformCollisionDetection reactive add (M10) | N/A | site uses Set, not NotifyChanged |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | Bake systems old-value diffing | handled | delivery IS the trigger — self-identical Changed must be DELIVERED (quiet-timer reset TileGridBakeSystem.cs:164, enqueue); C10 asserts NotifyChanged->re-bake, never suppression |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | LDtk world-singleton subscribers + late-join replay | N/A | no world-level NotifyChanged in engine |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | LDtkLevelLoadSystem world Set/Remove | N/A | uses Set/Remove |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | EditorTransport restart | N/A | uses Set/Remove |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | HierarchySystem managed singleton | N/A | mutates in place, never notifies |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | [Subscribe] hierarchy-walk registration | N/A | bus column |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | typed Subscribe<T> kept IDisposable | N/A | bus column |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | CullingSystem mid-update structural add/remove | N/A | uses Set/Remove |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | prep systems mid-loop Set | N/A | uses Set |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | GameOverSystem create+dispose mid-iteration | N/A | no NotifyChanged |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | manual-iteration mutators | N/A | use Set/Remove/Dispose |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | NotifyChanged publication fleet | handled | M2 (~40 sites) + D4; C4 contract test |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | EntityComponentReflection MethodInfo caches | N/A | write-back uses Set |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | ReadAllComponents consumers | N/A | read-only |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | IGameScreen ISystem<GameState> contract | N/A | type surface |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | ScreenController.Runner | N/A | runner surface |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no publication |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | screen-teardown world.Dispose | N/A | dispose path |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | PointerReplaySystem persistent sets | N/A | presence-only sets; NotifyChanged can't change membership |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | packaging + manifests | N/A | packaging |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | ProcessWideState registry | N/A | no static |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | seeded test-order shuffle | N/A | harness |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | guard-test model | N/A | guard column |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | value-predicate premise text | handled | premises.md:699 names NotifyChanged; C22 rewrite; M1/M2 implement verbatim |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | YSortSystem/DebugInspector hierarchy reads | N/A | reads only |
| Publication-driven predicate membership | GravitySystem predicate set | handled | M1 (named highest-risk); C11 gravity behaviour test |
| Publication-driven predicate membership | MasterRenderSystem.BuildDrawSet | handled | M1; C11; managed DrawComponent predicate (MasterRenderSystem.cs:90) |
| Publication-driven predicate membership | MasterRenderSystem stable draw sort | handled | wave-1 delegates to DefaultEcs set; C7 identity gated on deterministic clock (contract amended); wave-2 D3 frame-stable list per C15 |
| Publication-driven predicate membership | AudioSystem Changed handler | N/A | presence-only set (AudioSystem.cs:33) |
| Publication-driven predicate membership | TransformCollisionDetection reactive add (M10) | N/A | presence-only tag set |
| Publication-driven predicate membership | Bake systems old-value diffing | N/A | event handlers, no predicate |
| Publication-driven predicate membership | LDtk world-singleton subscribers + late-join replay | N/A | singleton store, no predicate |
| Publication-driven predicate membership | LDtkLevelLoadSystem world Set/Remove | N/A | singleton store |
| Publication-driven predicate membership | EditorTransport restart | N/A | singleton store |
| Publication-driven predicate membership | HierarchySystem managed singleton | N/A | singleton store |
| Publication-driven predicate membership | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Publication-driven predicate membership | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Publication-driven predicate membership | typed Subscribe<T> kept IDisposable | N/A | bus column |
| Publication-driven predicate membership | CullingSystem mid-update structural add/remove | handled | D3 — VisibleComponent add/remove re-evals Main-pass predicate+presence membership |
| Publication-driven predicate membership | prep systems mid-loop Set | handled | D2/D3 — mid-loop Set republishes; snapshot keeps loop safe |
| Publication-driven predicate membership | GameOverSystem create+dispose mid-iteration | N/A | no predicate set touched |
| Publication-driven predicate membership | manual-iteration mutators | N/A | no value-predicate queries at these sites |
| Publication-driven predicate membership | NotifyChanged publication fleet | handled | M1+M2 publication hook; C11 no-move-without-publish test |
| Publication-driven predicate membership | EntityComponentReflection MethodInfo caches | handled | reflection Set routes through facade publication (M1); shape risk tracked at Set-row gap |
| Publication-driven predicate membership | ReadAllComponents consumers | N/A | read-only |
| Publication-driven predicate membership | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Publication-driven predicate membership | ScreenController.Runner | N/A | runner surface |
| Publication-driven predicate membership | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Publication-driven predicate membership | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no predicate |
| Publication-driven predicate membership | screen-teardown world.Dispose | N/A | membership dies with world |
| Publication-driven predicate membership | PointerReplaySystem persistent sets | N/A | presence-only sets |
| Publication-driven predicate membership | packaging + manifests | N/A | packaging |
| Publication-driven predicate membership | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Publication-driven predicate membership | ProcessWideState registry | N/A | per-world state |
| Publication-driven predicate membership | seeded test-order shuffle | N/A | harness |
| Publication-driven predicate membership | guard-test model | N/A | guard column |
| Publication-driven predicate membership | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Publication-driven predicate membership | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Publication-driven predicate membership | value-predicate premise text | handled | 2c: premises.md:692 rewritten entirely in same PR (C22) |
| Publication-driven predicate membership | YSortSystem/DebugInspector hierarchy reads | N/A | singleton reads |
| Snapshot iteration EntitySystem/EntityQuery | GravitySystem predicate set | handled | D2; gravity mutates values only, no structural change |
| Snapshot iteration EntitySystem/EntityQuery | MasterRenderSystem.BuildDrawSet | handled | D3; draw list copied into DrawSortBuffer per frame |
| Snapshot iteration EntitySystem/EntityQuery | MasterRenderSystem stable draw sort | handled | sort runs on copied buffer (MasterRenderSystem.cs:80-85) |
| Snapshot iteration EntitySystem/EntityQuery | AudioSystem Changed handler | handled | D3; Reconcile loop is non-structural (AudioSystem.cs:49-54) |
| Snapshot iteration EntitySystem/EntityQuery | TransformCollisionDetection reactive add (M10) | handled | C3/C4 — structural add inside publish path proven wave 0, tested wave 2 |
| Snapshot iteration EntitySystem/EntityQuery | Bake systems old-value diffing | N/A | event handlers, not iteration |
| Snapshot iteration EntitySystem/EntityQuery | LDtk world-singleton subscribers + late-join replay | N/A | no set iteration at site |
| Snapshot iteration EntitySystem/EntityQuery | LDtkLevelLoadSystem world Set/Remove | N/A | no set iteration at site |
| Snapshot iteration EntitySystem/EntityQuery | EditorTransport restart | N/A | no set iteration at site |
| Snapshot iteration EntitySystem/EntityQuery | HierarchySystem managed singleton | N/A | singleton write only |
| Snapshot iteration EntitySystem/EntityQuery | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Snapshot iteration EntitySystem/EntityQuery | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Snapshot iteration EntitySystem/EntityQuery | typed Subscribe<T> kept IDisposable | N/A | bus column |
| Snapshot iteration EntitySystem/EntityQuery | CullingSystem mid-update structural add/remove | handled | D3 amended: snapshot per-Update/enumeration start, publication applies membership synchronously — Culling's VisibleComponent adds reach prep/YSort the SAME frame; C4 same-frame test |
| Snapshot iteration EntitySystem/EntityQuery | prep systems mid-loop Set | handled | membership applied synchronously at publication; prep [With(VisibleComponent)] sees Culling's same-frame adds/removes (D3 amended, C4) |
| Snapshot iteration EntitySystem/EntityQuery | GameOverSystem create+dispose mid-iteration | handled | 2b names GameOverSystem.cs:64,106,129; D2; C4 |
| Snapshot iteration EntitySystem/EntityQuery | manual-iteration mutators | handled | 2b names ~6 manual sites (SelectionSystem.cs:404-415 etc.); D3 snapshot enumeration |
| Snapshot iteration EntitySystem/EntityQuery | NotifyChanged publication fleet | handled | D3 — publication-driven membership change mid-loop tolerated by snapshot |
| Snapshot iteration EntitySystem/EntityQuery | EntityComponentReflection MethodInfo caches | N/A | single-entity designer op, not iteration |
| Snapshot iteration EntitySystem/EntityQuery | ReadAllComponents consumers | N/A | reads one entity |
| Snapshot iteration EntitySystem/EntityQuery | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Snapshot iteration EntitySystem/EntityQuery | ScreenController.Runner | N/A | runner surface |
| Snapshot iteration EntitySystem/EntityQuery | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Snapshot iteration EntitySystem/EntityQuery | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | wrapper, no iteration |
| Snapshot iteration EntitySystem/EntityQuery | screen-teardown world.Dispose | N/A | no iteration during teardown |
| Snapshot iteration EntitySystem/EntityQuery | PointerReplaySystem persistent sets | handled | D3; PointerReplaySystem.cs:294,352 per-frame enumeration |
| Snapshot iteration EntitySystem/EntityQuery | packaging + manifests | N/A | packaging |
| Snapshot iteration EntitySystem/EntityQuery | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Snapshot iteration EntitySystem/EntityQuery | ProcessWideState registry | N/A | per-world buffers |
| Snapshot iteration EntitySystem/EntityQuery | seeded test-order shuffle | N/A | harness |
| Snapshot iteration EntitySystem/EntityQuery | guard-test model | N/A | guard column |
| Snapshot iteration EntitySystem/EntityQuery | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Snapshot iteration EntitySystem/EntityQuery | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Snapshot iteration EntitySystem/EntityQuery | value-predicate premise text | handled | D2 'documented as a facade premise' + C8 Tests: named |
| Snapshot iteration EntitySystem/EntityQuery | YSortSystem/DebugInspector hierarchy reads | N/A | singleton reads |
| World-singleton store Set/Get/Has/Remove | GravitySystem predicate set | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | MasterRenderSystem.BuildDrawSet | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | MasterRenderSystem stable draw sort | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | AudioSystem Changed handler | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | TransformCollisionDetection reactive add (M10) | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | Bake systems old-value diffing | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | LDtk world-singleton subscribers + late-join replay | handled | M5 4 types + notifications; late-join Has+Get replay kept (LDtkTileParserSystem.cs:41-46) |
| World-singleton store Set/Get/Has/Remove | LDtkLevelLoadSystem world Set/Remove | handled | M5 names LDtkLevelLoadSystem.cs:71,80,81; C4 Removed-fires test |
| World-singleton store Set/Get/Has/Remove | EditorTransport restart | handled | carrier invisible to all query/sweep surfaces (new premise + C4 test); DisposeSceneEntities (EditorTransport.cs:419-421) cannot dispose it; EditorTransportTests re-run |
| World-singleton store Set/Get/Has/Remove | HierarchySystem managed singleton | handled | EntityHierarchy survives Restart/tab-switch sweep — carrier unseen by unfiltered AsSet (carrier-invisibility premise + C4 test) |
| World-singleton store Set/Get/Has/Remove | [Subscribe] hierarchy-walk registration | N/A | bus column |
| World-singleton store Set/Get/Has/Remove | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| World-singleton store Set/Get/Has/Remove | typed Subscribe<T> kept IDisposable | N/A | bus column |
| World-singleton store Set/Get/Has/Remove | CullingSystem mid-update structural add/remove | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | prep systems mid-loop Set | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | GameOverSystem create+dispose mid-iteration | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | manual-iteration mutators | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | NotifyChanged publication fleet | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | EntityComponentReflection MethodInfo caches | N/A | entity-level reflection |
| World-singleton store Set/Get/Has/Remove | ReadAllComponents consumers | handled | DebugInspector.cs:78 unfiltered enumeration excludes carrier (C4 invisibility test); count/roots/introspection unchanged |
| World-singleton store Set/Get/Has/Remove | IGameScreen ISystem<GameState> contract | N/A | type surface |
| World-singleton store Set/Get/Has/Remove | ScreenController.Runner | N/A | runner surface |
| World-singleton store Set/Get/Has/Remove | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| World-singleton store Set/Get/Has/Remove | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no singleton use |
| World-singleton store Set/Get/Has/Remove | screen-teardown world.Dispose | handled | singleton entity dies with facade world Dispose (wave 2, C12) |
| World-singleton store Set/Get/Has/Remove | PointerReplaySystem persistent sets | N/A | entity sets only |
| World-singleton store Set/Get/Has/Remove | packaging + manifests | N/A | packaging |
| World-singleton store Set/Get/Has/Remove | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| World-singleton store Set/Get/Has/Remove | ProcessWideState registry | N/A | per-world state |
| World-singleton store Set/Get/Has/Remove | seeded test-order shuffle | N/A | harness |
| World-singleton store Set/Get/Has/Remove | guard-test model | N/A | guard column |
| World-singleton store Set/Get/Has/Remove | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| World-singleton store Set/Get/Has/Remove | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| World-singleton store Set/Get/Has/Remove | value-predicate premise text | N/A | premise covers entity predicates |
| World-singleton store Set/Get/Has/Remove | YSortSystem/DebugInspector hierarchy reads | handled | M5 keeps optional Has/Get pattern (YSortSystem.cs:67, DebugInspector.cs:105) |
| Facade message bus typed + [Subscribe] | GravitySystem predicate set | N/A | no bus use |
| Facade message bus typed + [Subscribe] | MasterRenderSystem.BuildDrawSet | N/A | no bus use |
| Facade message bus typed + [Subscribe] | MasterRenderSystem stable draw sort | N/A | no bus use |
| Facade message bus typed + [Subscribe] | AudioSystem Changed handler | handled | M3 typed sub (AudioSystem.cs:35); synchronous publish kept ('identical' precondition diff) |
| Facade message bus typed + [Subscribe] | TransformCollisionDetection reactive add (M10) | N/A | component events, not bus |
| Facade message bus typed + [Subscribe] | Bake systems old-value diffing | N/A | component events |
| Facade message bus typed + [Subscribe] | LDtk world-singleton subscribers + late-join replay | N/A | component events |
| Facade message bus typed + [Subscribe] | LDtkLevelLoadSystem world Set/Remove | handled | flipped from N/A: singleton Set/Remove runs INSIDE its own bus handler — WorldComponentAdded parser cascade nested in bus dispatch; C3/C4 nested-dispatch proof covers |
| Facade message bus typed + [Subscribe] | EditorTransport restart | handled | flipped from N/A: restart IS bus-driven — ReloadFromDisk->Reload->world.Publish(LoadLevelRequest); restart e2e re-run through facade bus (EditorTransportTests both waves) |
| Facade message bus typed + [Subscribe] | HierarchySystem managed singleton | N/A | no bus use |
| Facade message bus typed + [Subscribe] | [Subscribe] hierarchy-walk registration | GAP | M3 promises attribute scan but not DefaultEcs's type-hierarchy walk + virtual-override dedup (TransformPhysicalCollisionResolutionSystem.cs:13-14,32) — naive scanner double-subscribes or misses the base-annotated virtual On |
| Facade message bus typed + [Subscribe] | [Subscribe] + Subscribe(this) fleet | handled | M3 measured + C4 no-double-registration pin (contract added): 6 classes carry [Subscribe] on methods ALSO typed-subscribed and never call Subscribe(this) — level must load once, collisions resolve once |
| Facade message bus typed + [Subscribe] | typed Subscribe<T> kept IDisposable | handled | M3 (19 typed sites); bus returns IDisposable (CameraFollowSystem.cs:61) |
| Facade message bus typed + [Subscribe] | CullingSystem mid-update structural add/remove | N/A | no bus use |
| Facade message bus typed + [Subscribe] | prep systems mid-loop Set | N/A | no bus use |
| Facade message bus typed + [Subscribe] | GameOverSystem create+dispose mid-iteration | N/A | no bus interaction in scope |
| Facade message bus typed + [Subscribe] | manual-iteration mutators | N/A | no bus use |
| Facade message bus typed + [Subscribe] | NotifyChanged publication fleet | N/A | component publication, not bus |
| Facade message bus typed + [Subscribe] | EntityComponentReflection MethodInfo caches | N/A | no bus use |
| Facade message bus typed + [Subscribe] | ReadAllComponents consumers | N/A | no bus use |
| Facade message bus typed + [Subscribe] | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Facade message bus typed + [Subscribe] | ScreenController.Runner | N/A | runner surface |
| Facade message bus typed + [Subscribe] | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Facade message bus typed + [Subscribe] | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no bus use |
| Facade message bus typed + [Subscribe] | screen-teardown world.Dispose | handled | bus is per-world (precondition diff); dies with world Dispose; C12 |
| Facade message bus typed + [Subscribe] | PointerReplaySystem persistent sets | N/A | no bus use |
| Facade message bus typed + [Subscribe] | packaging + manifests | N/A | packaging |
| Facade message bus typed + [Subscribe] | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Facade message bus typed + [Subscribe] | ProcessWideState registry | N/A | per-world bus |
| Facade message bus typed + [Subscribe] | seeded test-order shuffle | N/A | harness |
| Facade message bus typed + [Subscribe] | guard-test model | N/A | guard column |
| Facade message bus typed + [Subscribe] | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Facade message bus typed + [Subscribe] | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Facade message bus typed + [Subscribe] | value-predicate premise text | N/A | premise not about bus |
| Facade message bus typed + [Subscribe] | YSortSystem/DebugInspector hierarchy reads | N/A | no bus use |
| Facade ISystem<T>/IGameScreen contract | GravitySystem predicate set | N/A | blanket wave-1 retype; nothing column-specific |
| Facade ISystem<T>/IGameScreen contract | MasterRenderSystem.BuildDrawSet | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | MasterRenderSystem stable draw sort | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | AudioSystem Changed handler | N/A | blanket wave-1 retype (IsEnabled kept per issue §4) |
| Facade ISystem<T>/IGameScreen contract | TransformCollisionDetection reactive add (M10) | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | Bake systems old-value diffing | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | LDtk world-singleton subscribers + late-join replay | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | LDtkLevelLoadSystem world Set/Remove | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | EditorTransport restart | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | HierarchySystem managed singleton | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Facade ISystem<T>/IGameScreen contract | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Facade ISystem<T>/IGameScreen contract | typed Subscribe<T> kept IDisposable | N/A | bus column |
| Facade ISystem<T>/IGameScreen contract | CullingSystem mid-update structural add/remove | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | prep systems mid-loop Set | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | GameOverSystem create+dispose mid-iteration | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | manual-iteration mutators | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | NotifyChanged publication fleet | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | EntityComponentReflection MethodInfo caches | N/A | not a system |
| Facade ISystem<T>/IGameScreen contract | ReadAllComponents consumers | N/A | not a system contract |
| Facade ISystem<T>/IGameScreen contract | IGameScreen ISystem<GameState> contract | handled | M7 (GameScreen.cs:11-13 verified; 71 files); wave 1 moves IGameScreen to facade types |
| Facade ISystem<T>/IGameScreen contract | ScreenController.Runner | handled | wave-1 sweep + 2b facts; ScreenController.cs:27,35 verified |
| Facade ISystem<T>/IGameScreen contract | EditorPipelineRegistrar ParallelSystem<T> | handled | M4 — facade Sequential/ParallelSystem compose facade ISystem |
| Facade ISystem<T>/IGameScreen contract | GatedSystem IsEnabled + ISuspendableSystem cast | handled | issue §4 ISystem incl. IsEnabled; 2b: ISuspendableSystem uncoupled (GatedSystem.cs:130) |
| Facade ISystem<T>/IGameScreen contract | screen-teardown world.Dispose | handled | screens dispose composite pipeline BEFORE _world.Dispose (LoadLevelExampleGameScreen.cs:728-733, LevelSelectionScreen.cs:626-634); facade composites cascade Dispose to leaves (C4 test); IGameScreen stays IDisposable |
| Facade ISystem<T>/IGameScreen contract | PointerReplaySystem persistent sets | N/A | blanket wave-1 retype |
| Facade ISystem<T>/IGameScreen contract | packaging + manifests | N/A | packaging |
| Facade ISystem<T>/IGameScreen contract | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Facade ISystem<T>/IGameScreen contract | ProcessWideState registry | N/A | no static |
| Facade ISystem<T>/IGameScreen contract | seeded test-order shuffle | N/A | harness |
| Facade ISystem<T>/IGameScreen contract | guard-test model | N/A | guard column |
| Facade ISystem<T>/IGameScreen contract | DefaultEcs.Threading heads + fully-qualified use | N/A | runner row covers |
| Facade ISystem<T>/IGameScreen contract | DrawPrepSystemBase (dead) | handled | C6 deletes the dead ISystem base wave 1 |
| Facade ISystem<T>/IGameScreen contract | value-predicate premise text | N/A | premise not about system typing |
| Facade ISystem<T>/IGameScreen contract | YSortSystem/DebugInspector hierarchy reads | N/A | read pattern only |
| IParallelRunner + sequential ParallelSystem<T> | GravitySystem predicate set | handled | GravitySystem.cs:9 ctor takes IParallelRunner into the predicate-set AEntitySetSystem — facade runner-accepting EntitySystem ctor is required surface, degree==1 asserted; C11 negatives routed through it |
| IParallelRunner + sequential ParallelSystem<T> | MasterRenderSystem.BuildDrawSet | N/A | no runner in draw path |
| IParallelRunner + sequential ParallelSystem<T> | MasterRenderSystem stable draw sort | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | AudioSystem Changed handler | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | TransformCollisionDetection reactive add (M10) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | Bake systems old-value diffing | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | LDtk world-singleton subscribers + late-join replay | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | LDtkLevelLoadSystem world Set/Remove | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | EditorTransport restart | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | HierarchySystem managed singleton | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | [Subscribe] hierarchy-walk registration | N/A | bus column |
| IParallelRunner + sequential ParallelSystem<T> | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| IParallelRunner + sequential ParallelSystem<T> | typed Subscribe<T> kept IDisposable | N/A | bus column |
| IParallelRunner + sequential ParallelSystem<T> | CullingSystem mid-update structural add/remove | handled | facade EntitySystem accepts runner; hosts degree 1 (2b, M4) |
| IParallelRunner + sequential ParallelSystem<T> | prep systems mid-loop Set | handled | 2b: 9 runner-consuming system types/5 files; degree 1 |
| IParallelRunner + sequential ParallelSystem<T> | GameOverSystem create+dispose mid-iteration | handled | same runner surface; degree 1 (M4) |
| IParallelRunner + sequential ParallelSystem<T> | manual-iteration mutators | N/A | manual loops, no runner |
| IParallelRunner + sequential ParallelSystem<T> | NotifyChanged publication fleet | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | EntityComponentReflection MethodInfo caches | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | ReadAllComponents consumers | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | IGameScreen ISystem<GameState> contract | N/A | ISystem row covers |
| IParallelRunner + sequential ParallelSystem<T> | ScreenController.Runner | handled | 2b measured ctor/property; facade IParallelRunner + default runner (wave 1) |
| IParallelRunner + sequential ParallelSystem<T> | EditorPipelineRegistrar ParallelSystem<T> | handled | M4 — sequential impl behaviour-preserving at degree 1; 5 uses |
| IParallelRunner + sequential ParallelSystem<T> | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | wrapper takes no runner |
| IParallelRunner + sequential ParallelSystem<T> | screen-teardown world.Dispose | N/A | runner outlives worlds by design, unchanged |
| IParallelRunner + sequential ParallelSystem<T> | PointerReplaySystem persistent sets | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | packaging + manifests | N/A | packaging row covers |
| IParallelRunner + sequential ParallelSystem<T> | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| IParallelRunner + sequential ParallelSystem<T> | ProcessWideState registry | N/A | runner instance-scoped |
| IParallelRunner + sequential ParallelSystem<T> | seeded test-order shuffle | N/A | harness |
| IParallelRunner + sequential ParallelSystem<T> | guard-test model | N/A | guard column |
| IParallelRunner + sequential ParallelSystem<T> | DefaultEcs.Threading heads + fully-qualified use | handled | wave-1 sweep + C5 catches fully-qualified token (CollisionConsumerAuditTests.cs:225) |
| IParallelRunner + sequential ParallelSystem<T> | DrawPrepSystemBase (dead) | handled | C6 delete removes the misnamed useParallel/useBuffer param |
| IParallelRunner + sequential ParallelSystem<T> | value-predicate premise text | N/A | premise not about runners |
| IParallelRunner + sequential ParallelSystem<T> | YSortSystem/DebugInspector hierarchy reads | N/A | no runner |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | GravitySystem predicate set | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | MasterRenderSystem.BuildDrawSet | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | MasterRenderSystem stable draw sort | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | AudioSystem Changed handler | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | TransformCollisionDetection reactive add (M10) | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | Bake systems old-value diffing | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | LDtk world-singleton subscribers + late-join replay | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | LDtkLevelLoadSystem world Set/Remove | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | EditorTransport restart | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | HierarchySystem managed singleton | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | [Subscribe] hierarchy-walk registration | N/A | bus column |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | typed Subscribe<T> kept IDisposable | N/A | bus column |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | CullingSystem mid-update structural add/remove | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | prep systems mid-loop Set | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | GameOverSystem create+dispose mid-iteration | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | manual-iteration mutators | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | NotifyChanged publication fleet | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | EntityComponentReflection MethodInfo caches | N/A | separate mechanism — tracked at Set-row gap |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | ReadAllComponents consumers | handled | M8: 3 sites + ComponentIntrospector.cs:9,39; wave-1 registry/reflection-backed AOT-safe port |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | IGameScreen ISystem<GameState> contract | N/A | unrelated surface |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | ScreenController.Runner | N/A | unrelated |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | EditorPipelineRegistrar ParallelSystem<T> | N/A | unrelated |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | unrelated |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | screen-teardown world.Dispose | N/A | no lifetime coupling |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | PointerReplaySystem persistent sets | N/A | no ReadAll use |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | packaging + manifests | N/A | packaging |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | ProcessWideState registry | handled | C12 — any static component registry backing the port gets registered |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | seeded test-order shuffle | N/A | harness |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | guard-test model | N/A | guard column |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | value-predicate premise text | N/A | level-editor premise :1509 covered by C22, not this premise |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | YSortSystem/DebugInspector hierarchy reads | N/A | singleton reads, not ReadAll |
| IsAlive/Entity.Null handle semantics | GravitySystem predicate set | N/A | no IsAlive use |
| IsAlive/Entity.Null handle semantics | MasterRenderSystem.BuildDrawSet | N/A | no IsAlive use |
| IsAlive/Entity.Null handle semantics | MasterRenderSystem stable draw sort | N/A | no IsAlive use |
| IsAlive/Entity.Null handle semantics | AudioSystem Changed handler | N/A | handler receives live entity |
| IsAlive/Entity.Null handle semantics | TransformCollisionDetection reactive add (M10) | N/A | handler entity live inside publish |
| IsAlive/Entity.Null handle semantics | Bake systems old-value diffing | N/A | handler entity live |
| IsAlive/Entity.Null handle semantics | LDtk world-singleton subscribers + late-join replay | N/A | world-level |
| IsAlive/Entity.Null handle semantics | LDtkLevelLoadSystem world Set/Remove | N/A | world-level |
| IsAlive/Entity.Null handle semantics | EditorTransport restart | N/A | world-level |
| IsAlive/Entity.Null handle semantics | HierarchySystem managed singleton | N/A | world-level |
| IsAlive/Entity.Null handle semantics | [Subscribe] hierarchy-walk registration | N/A | bus column |
| IsAlive/Entity.Null handle semantics | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| IsAlive/Entity.Null handle semantics | typed Subscribe<T> kept IDisposable | N/A | bus column |
| IsAlive/Entity.Null handle semantics | CullingSystem mid-update structural add/remove | handled | C13; CullingSystem.cs:45,64 cached-handle IsAlive checks verified |
| IsAlive/Entity.Null handle semantics | prep systems mid-loop Set | N/A | no cached handles |
| IsAlive/Entity.Null handle semantics | GameOverSystem create+dispose mid-iteration | handled | C13 + D2 — dispose-mid-iteration then handle checks |
| IsAlive/Entity.Null handle semantics | manual-iteration mutators | handled | C13/D3 — Dispose inside manual loops |
| IsAlive/Entity.Null handle semantics | NotifyChanged publication fleet | N/A | live-entity sites |
| IsAlive/Entity.Null handle semantics | EntityComponentReflection MethodInfo caches | N/A | boxed copy addresses same storage; no liveness check |
| IsAlive/Entity.Null handle semantics | ReadAllComponents consumers | N/A | read of live entity |
| IsAlive/Entity.Null handle semantics | IGameScreen ISystem<GameState> contract | N/A | type surface |
| IsAlive/Entity.Null handle semantics | ScreenController.Runner | N/A | runner surface |
| IsAlive/Entity.Null handle semantics | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| IsAlive/Entity.Null handle semantics | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no entity handles |
| IsAlive/Entity.Null handle semantics | screen-teardown world.Dispose | GAP | Arch recycles World slots in static World.Worlds (H9); a stale Entity from a disposed screen's world can read alive once the slot is reused across 10-screen churn — C13 names entity-id recycling only, not world-id reuse after Dispose |
| IsAlive/Entity.Null handle semantics | PointerReplaySystem persistent sets | N/A | re-queries each frame, no stale handles |
| IsAlive/Entity.Null handle semantics | packaging + manifests | N/A | packaging |
| IsAlive/Entity.Null handle semantics | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| IsAlive/Entity.Null handle semantics | ProcessWideState registry | N/A | lifecycle row covers |
| IsAlive/Entity.Null handle semantics | seeded test-order shuffle | N/A | harness |
| IsAlive/Entity.Null handle semantics | guard-test model | N/A | guard column |
| IsAlive/Entity.Null handle semantics | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| IsAlive/Entity.Null handle semantics | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| IsAlive/Entity.Null handle semantics | value-predicate premise text | N/A | premise not about liveness |
| IsAlive/Entity.Null handle semantics | YSortSystem/DebugInspector hierarchy reads | N/A | world reads |
| EcsWorld.Create/Dispose over Arch static registry | GravitySystem predicate set | N/A | per-world system |
| EcsWorld.Create/Dispose over Arch static registry | MasterRenderSystem.BuildDrawSet | N/A | per-world |
| EcsWorld.Create/Dispose over Arch static registry | MasterRenderSystem stable draw sort | N/A | per-world |
| EcsWorld.Create/Dispose over Arch static registry | AudioSystem Changed handler | N/A | per-world |
| EcsWorld.Create/Dispose over Arch static registry | TransformCollisionDetection reactive add (M10) | N/A | per-world |
| EcsWorld.Create/Dispose over Arch static registry | Bake systems old-value diffing | N/A | per-world |
| EcsWorld.Create/Dispose over Arch static registry | LDtk world-singleton subscribers + late-join replay | N/A | same-world ops |
| EcsWorld.Create/Dispose over Arch static registry | LDtkLevelLoadSystem world Set/Remove | N/A | same-world ops |
| EcsWorld.Create/Dispose over Arch static registry | EditorTransport restart | N/A | restart mutates one world, no re-create |
| EcsWorld.Create/Dispose over Arch static registry | HierarchySystem managed singleton | N/A | per-world |
| EcsWorld.Create/Dispose over Arch static registry | [Subscribe] hierarchy-walk registration | N/A | bus column |
| EcsWorld.Create/Dispose over Arch static registry | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| EcsWorld.Create/Dispose over Arch static registry | typed Subscribe<T> kept IDisposable | N/A | bus column |
| EcsWorld.Create/Dispose over Arch static registry | CullingSystem mid-update structural add/remove | N/A | entity-level |
| EcsWorld.Create/Dispose over Arch static registry | prep systems mid-loop Set | N/A | entity-level |
| EcsWorld.Create/Dispose over Arch static registry | GameOverSystem create+dispose mid-iteration | N/A | entity-level, not world |
| EcsWorld.Create/Dispose over Arch static registry | manual-iteration mutators | N/A | entity-level |
| EcsWorld.Create/Dispose over Arch static registry | NotifyChanged publication fleet | N/A | entity-level |
| EcsWorld.Create/Dispose over Arch static registry | EntityComponentReflection MethodInfo caches | N/A | type-keyed caches survive world churn harmlessly |
| EcsWorld.Create/Dispose over Arch static registry | ReadAllComponents consumers | N/A | per-world reads |
| EcsWorld.Create/Dispose over Arch static registry | IGameScreen ISystem<GameState> contract | N/A | screens create worlds via EcsWorld.Create (H10) — covered wave 1 |
| EcsWorld.Create/Dispose over Arch static registry | ScreenController.Runner | N/A | runner not world-scoped |
| EcsWorld.Create/Dispose over Arch static registry | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| EcsWorld.Create/Dispose over Arch static registry | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no world lifecycle |
| EcsWorld.Create/Dispose over Arch static registry | screen-teardown world.Dispose | handled | wave 2 — facade Dispose unhooks Arch registry + subs; leaking tests fixed; C12; 10 screens named |
| EcsWorld.Create/Dispose over Arch static registry | PointerReplaySystem persistent sets | N/A | per-world sets |
| EcsWorld.Create/Dispose over Arch static registry | packaging + manifests | N/A | packaging |
| EcsWorld.Create/Dispose over Arch static registry | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| EcsWorld.Create/Dispose over Arch static registry | ProcessWideState registry | handled | H9/C12; verified ProcessWideState.cs:41-99 tracks no ECS statics today — additions land same PR |
| EcsWorld.Create/Dispose over Arch static registry | seeded test-order shuffle | handled | verification protocol MONODREAMS_TEST_SEED=8 — the shuffle that exposes static leakage |
| EcsWorld.Create/Dispose over Arch static registry | guard-test model | N/A | statics guarded by hygiene tests, not source lint |
| EcsWorld.Create/Dispose over Arch static registry | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| EcsWorld.Create/Dispose over Arch static registry | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| EcsWorld.Create/Dispose over Arch static registry | value-predicate premise text | N/A | premise not about lifecycle |
| EcsWorld.Create/Dispose over Arch static registry | YSortSystem/DebugInspector hierarchy reads | N/A | per-world reads |
| Iteration order unspecified (H4) | GravitySystem predicate set | N/A | per-entity integration, order-free |
| Iteration order unspecified (H4) | MasterRenderSystem.BuildDrawSet | handled | explicit sort downstream (precondition diff: systems needing order sort explicitly) |
| Iteration order unspecified (H4) | MasterRenderSystem stable draw sort | handled | stable sort; C7 gated on injected deterministic clock + main-vs-main double-run precheck (contract amended); C15 explained-diff, never re-baselined |
| Iteration order unspecified (H4) | AudioSystem Changed handler | N/A | per-source reconcile order-free |
| Iteration order unspecified (H4) | TransformCollisionDetection reactive add (M10) | N/A | tagging order-free |
| Iteration order unspecified (H4) | Bake systems old-value diffing | N/A | event-driven |
| Iteration order unspecified (H4) | LDtk world-singleton subscribers + late-join replay | N/A | spawn order data-driven, not query order |
| Iteration order unspecified (H4) | LDtkLevelLoadSystem world Set/Remove | N/A | explicit statement order |
| Iteration order unspecified (H4) | EditorTransport restart | N/A | explicit statement order |
| Iteration order unspecified (H4) | HierarchySystem managed singleton | N/A | single value |
| Iteration order unspecified (H4) | [Subscribe] hierarchy-walk registration | N/A | registration, not iteration |
| Iteration order unspecified (H4) | [Subscribe] + Subscribe(this) fleet | N/A | bus dispatch order facade-owned, unchanged |
| Iteration order unspecified (H4) | typed Subscribe<T> kept IDisposable | N/A | bus order facade-owned |
| Iteration order unspecified (H4) | CullingSystem mid-update structural add/remove | N/A | add/remove per entity, order-free |
| Iteration order unspecified (H4) | prep systems mid-loop Set | N/A | per-entity prep order-free |
| Iteration order unspecified (H4) | GameOverSystem create+dispose mid-iteration | N/A | outcome order-independent |
| Iteration order unspecified (H4) | manual-iteration mutators | handled | wave-2 first-match census (contract): SelectionSystem.cs:404-415 + TabSystem.cs:71 get explicit deterministic rule or single-instance assert — no longer H4-exposed |
| Iteration order unspecified (H4) | NotifyChanged publication fleet | N/A | publication order-free |
| Iteration order unspecified (H4) | EntityComponentReflection MethodInfo caches | N/A | single entity |
| Iteration order unspecified (H4) | ReadAllComponents consumers | N/A | inspector listing order cosmetic |
| Iteration order unspecified (H4) | IGameScreen ISystem<GameState> contract | N/A | system order is explicit composition |
| Iteration order unspecified (H4) | ScreenController.Runner | N/A | no iteration |
| Iteration order unspecified (H4) | EditorPipelineRegistrar ParallelSystem<T> | N/A | registration order explicit |
| Iteration order unspecified (H4) | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no iteration |
| Iteration order unspecified (H4) | screen-teardown world.Dispose | N/A | no iteration |
| Iteration order unspecified (H4) | PointerReplaySystem persistent sets | N/A | existence gates order-free |
| Iteration order unspecified (H4) | packaging + manifests | N/A | packaging |
| Iteration order unspecified (H4) | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Iteration order unspecified (H4) | ProcessWideState registry | N/A | no iteration |
| Iteration order unspecified (H4) | seeded test-order shuffle | N/A | test order, not entity order |
| Iteration order unspecified (H4) | guard-test model | N/A | guard column |
| Iteration order unspecified (H4) | DefaultEcs.Threading heads + fully-qualified use | N/A | degree-1 runner, no reorder |
| Iteration order unspecified (H4) | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Iteration order unspecified (H4) | value-predicate premise text | N/A | order claims live in camera docs :235/:83, rewritten per 2c/C22 |
| Iteration order unspecified (H4) | YSortSystem/DebugInspector hierarchy reads | handled | YSort sorts explicitly; hierarchy read order-free (H4 precondition diff) |
| Guard ratchet EcsBoundaryLintTests | GravitySystem predicate set | N/A | repo-wide lint, nothing site-specific |
| Guard ratchet EcsBoundaryLintTests | MasterRenderSystem.BuildDrawSet | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | MasterRenderSystem stable draw sort | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | AudioSystem Changed handler | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | TransformCollisionDetection reactive add (M10) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | Bake systems old-value diffing | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | LDtk world-singleton subscribers + late-join replay | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | LDtkLevelLoadSystem world Set/Remove | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | EditorTransport restart | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | HierarchySystem managed singleton | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | [Subscribe] hierarchy-walk registration | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | [Subscribe] + Subscribe(this) fleet | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | typed Subscribe<T> kept IDisposable | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | CullingSystem mid-update structural add/remove | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | prep systems mid-loop Set | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | GameOverSystem create+dispose mid-iteration | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | manual-iteration mutators | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | NotifyChanged publication fleet | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | EntityComponentReflection MethodInfo caches | handled | wave-1 sweep + C5 lint forces literal cleanup (EntityComponentReflection.cs:5,74 'DefaultEcs' strings) |
| Guard ratchet EcsBoundaryLintTests | ReadAllComponents consumers | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | IGameScreen ISystem<GameState> contract | handled | C5 proves sweep complete (empty ratchet); census corrected: 449 'using DefaultEcs' lines / 320 git-tracked files excl. scratchpad (452/321 incl.) — phantom 305 retracted |
| Guard ratchet EcsBoundaryLintTests | ScreenController.Runner | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | EditorPipelineRegistrar ParallelSystem<T> | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | screen-teardown world.Dispose | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | PointerReplaySystem persistent sets | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | packaging + manifests | GAP | MonoDreams.Cli/Installer/ProjectScaffolder.cs (verified) carries DefaultEcs literals until wave-4 swap — wave-1 guard 'no .cs outside facade' flags it; cli ratchet/allowlist entries not named in plan |
| Guard ratchet EcsBoundaryLintTests | CLI tests asserting literal DefaultEcs | GAP | Cli.Tests assert literal 'DefaultEcs' (ManifestPlatformTests.cs:35-36, ScaffolderPlatformTests.cs:279-283,403) — trips wave-1 guard three waves before the wave-4 swap; KnownGaps entries unplanned |
| Guard ratchet EcsBoundaryLintTests | ProcessWideState registry | N/A | hygiene tests, not lint |
| Guard ratchet EcsBoundaryLintTests | seeded test-order shuffle | N/A | harness |
| Guard ratchet EcsBoundaryLintTests | guard-test model | handled | intent wave 1: modeled on EditorThemeLintTests + KnownGaps ratchet — the exact repo pattern (C5/C14) |
| Guard ratchet EcsBoundaryLintTests | DefaultEcs.Threading heads + fully-qualified use | handled | source-text lint catches fully-qualified DefaultEcs.Threading token (C5) |
| Guard ratchet EcsBoundaryLintTests | DrawPrepSystemBase (dead) | N/A | deleted same wave (C6) before guard could flag it |
| Guard ratchet EcsBoundaryLintTests | value-predicate premise text | N/A | docs, not .cs |
| Guard ratchet EcsBoundaryLintTests | YSortSystem/DebugInspector hierarchy reads | N/A | repo-wide lint |
| Packaging: Arch replaces DefaultEcs | GravitySystem predicate set | N/A | code, not packaging |
| Packaging: Arch replaces DefaultEcs | MasterRenderSystem.BuildDrawSet | N/A | code |
| Packaging: Arch replaces DefaultEcs | MasterRenderSystem stable draw sort | N/A | code |
| Packaging: Arch replaces DefaultEcs | AudioSystem Changed handler | N/A | code |
| Packaging: Arch replaces DefaultEcs | TransformCollisionDetection reactive add (M10) | N/A | code |
| Packaging: Arch replaces DefaultEcs | Bake systems old-value diffing | N/A | code |
| Packaging: Arch replaces DefaultEcs | LDtk world-singleton subscribers + late-join replay | N/A | code |
| Packaging: Arch replaces DefaultEcs | LDtkLevelLoadSystem world Set/Remove | N/A | code |
| Packaging: Arch replaces DefaultEcs | EditorTransport restart | N/A | code |
| Packaging: Arch replaces DefaultEcs | HierarchySystem managed singleton | N/A | code |
| Packaging: Arch replaces DefaultEcs | [Subscribe] hierarchy-walk registration | N/A | code |
| Packaging: Arch replaces DefaultEcs | [Subscribe] + Subscribe(this) fleet | N/A | code |
| Packaging: Arch replaces DefaultEcs | typed Subscribe<T> kept IDisposable | N/A | code |
| Packaging: Arch replaces DefaultEcs | CullingSystem mid-update structural add/remove | N/A | code |
| Packaging: Arch replaces DefaultEcs | prep systems mid-loop Set | N/A | code |
| Packaging: Arch replaces DefaultEcs | GameOverSystem create+dispose mid-iteration | N/A | code |
| Packaging: Arch replaces DefaultEcs | manual-iteration mutators | N/A | code |
| Packaging: Arch replaces DefaultEcs | NotifyChanged publication fleet | N/A | code |
| Packaging: Arch replaces DefaultEcs | EntityComponentReflection MethodInfo caches | N/A | code |
| Packaging: Arch replaces DefaultEcs | ReadAllComponents consumers | N/A | code |
| Packaging: Arch replaces DefaultEcs | IGameScreen ISystem<GameState> contract | N/A | code |
| Packaging: Arch replaces DefaultEcs | ScreenController.Runner | N/A | code |
| Packaging: Arch replaces DefaultEcs | EditorPipelineRegistrar ParallelSystem<T> | N/A | code |
| Packaging: Arch replaces DefaultEcs | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | code |
| Packaging: Arch replaces DefaultEcs | screen-teardown world.Dispose | N/A | code |
| Packaging: Arch replaces DefaultEcs | PointerReplaySystem persistent sets | N/A | code |
| Packaging: Arch replaces DefaultEcs | packaging + manifests | handled | M9 enumerates all 7 surfaces incl. module.schema.json:55, foundation/module.json:34 (verified sole manifest hit); wave 4 + C19/C20; locks regenerated |
| Packaging: Arch replaces DefaultEcs | CLI tests asserting literal DefaultEcs | handled | C19 zero-token sweep forces rewrite of ManifestPlatformTests.cs:35-36 etc.; M11 facade packages; C20 all legs |
| Packaging: Arch replaces DefaultEcs | ProcessWideState registry | N/A | no packaging static |
| Packaging: Arch replaces DefaultEcs | seeded test-order shuffle | N/A | harness |
| Packaging: Arch replaces DefaultEcs | guard-test model | N/A | guard row covers |
| Packaging: Arch replaces DefaultEcs | DefaultEcs.Threading heads + fully-qualified use | handled | M9: Examples.Core:38 / Demos:25 csproj refs; head code via wave-1 sweep |
| Packaging: Arch replaces DefaultEcs | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Packaging: Arch replaces DefaultEcs | value-predicate premise text | N/A | docs row (C22) |
| Packaging: Arch replaces DefaultEcs | YSortSystem/DebugInspector hierarchy reads | N/A | code |
| Mutator: Set-on-present fires Changed not Added | GravitySystem predicate set | handled | C4+C11 — re-Set with toggled Gravity.active re-evals membership |
| Mutator: Set-on-present fires Changed not Added | MasterRenderSystem.BuildDrawSet | handled | C11 — retarget via Set moves entity between per-target sets |
| Mutator: Set-on-present fires Changed not Added | MasterRenderSystem stable draw sort | N/A | sort consumes buffer |
| Mutator: Set-on-present fires Changed not Added | AudioSystem Changed handler | handled | the exact documented dependency (AudioSystem.cs:39-42); C4 + C10 named test |
| Mutator: Set-on-present fires Changed not Added | TransformCollisionDetection reactive add (M10) | handled | Has-guard avoids Set-on-present (verified :89,94); C3 |
| Mutator: Set-on-present fires Changed not Added | Bake systems old-value diffing | handled | Changed delivery triggers re-bake; old value consumed for invalidation (BoundaryBakeSystem.cs:97), never a suppression key (C10 amended) |
| Mutator: Set-on-present fires Changed not Added | LDtk world-singleton subscribers + late-join replay | N/A | singleton — its own mutator row |
| Mutator: Set-on-present fires Changed not Added | LDtkLevelLoadSystem world Set/Remove | N/A | singleton row |
| Mutator: Set-on-present fires Changed not Added | EditorTransport restart | N/A | singleton row |
| Mutator: Set-on-present fires Changed not Added | HierarchySystem managed singleton | N/A | singleton row |
| Mutator: Set-on-present fires Changed not Added | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Mutator: Set-on-present fires Changed not Added | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Mutator: Set-on-present fires Changed not Added | typed Subscribe<T> kept IDisposable | N/A | bus column |
| Mutator: Set-on-present fires Changed not Added | CullingSystem mid-update structural add/remove | N/A | Has-guarded — Set only when absent (CullingSystem.cs:98-101) |
| Mutator: Set-on-present fires Changed not Added | prep systems mid-loop Set | handled | per-frame Set-on-present fires Changed as today; C4 + C7 identity gate |
| Mutator: Set-on-present fires Changed not Added | GameOverSystem create+dispose mid-iteration | N/A | create/dispose only |
| Mutator: Set-on-present fires Changed not Added | manual-iteration mutators | handled | C4 semantics apply to migrated sites |
| Mutator: Set-on-present fires Changed not Added | NotifyChanged publication fleet | N/A | different verb |
| Mutator: Set-on-present fires Changed not Added | EntityComponentReflection MethodInfo caches | handled | write-back Set-on-present re-fires Changed (documented in file); C4 — shape risk tracked at Set-row gap |
| Mutator: Set-on-present fires Changed not Added | ReadAllComponents consumers | N/A | read-only |
| Mutator: Set-on-present fires Changed not Added | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Mutator: Set-on-present fires Changed not Added | ScreenController.Runner | N/A | runner surface |
| Mutator: Set-on-present fires Changed not Added | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Mutator: Set-on-present fires Changed not Added | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no component writes |
| Mutator: Set-on-present fires Changed not Added | screen-teardown world.Dispose | N/A | no Set at teardown |
| Mutator: Set-on-present fires Changed not Added | PointerReplaySystem persistent sets | N/A | presence unchanged by re-Set |
| Mutator: Set-on-present fires Changed not Added | packaging + manifests | N/A | packaging |
| Mutator: Set-on-present fires Changed not Added | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Mutator: Set-on-present fires Changed not Added | ProcessWideState registry | N/A | no static |
| Mutator: Set-on-present fires Changed not Added | seeded test-order shuffle | N/A | harness |
| Mutator: Set-on-present fires Changed not Added | guard-test model | N/A | guard column |
| Mutator: Set-on-present fires Changed not Added | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Mutator: Set-on-present fires Changed not Added | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Mutator: Set-on-present fires Changed not Added | value-predicate premise text | handled | premises.md:698-706 publication contract; C22 rewrite |
| Mutator: Set-on-present fires Changed not Added | YSortSystem/DebugInspector hierarchy reads | N/A | reads only |
| Mutator: Remove-then-Set round trip | GravitySystem predicate set | handled | C4 — Removed drops membership, Added re-evals predicate |
| Mutator: Remove-then-Set round trip | MasterRenderSystem.BuildDrawSet | handled | C4/C11 |
| Mutator: Remove-then-Set round trip | MasterRenderSystem stable draw sort | N/A | sort consumes buffer |
| Mutator: Remove-then-Set round trip | AudioSystem Changed handler | handled | AudioSystem subscribes Removed AND Changed (:38,:42); C10 |
| Mutator: Remove-then-Set round trip | TransformCollisionDetection reactive add (M10) | handled | re-Add re-fires Added → re-tag via Has-guard; C3/C10 |
| Mutator: Remove-then-Set round trip | Bake systems old-value diffing | handled | Added+Changed subs at BoundaryBakeSystem.cs:88-97; C10 |
| Mutator: Remove-then-Set round trip | LDtk world-singleton subscribers + late-join replay | N/A | singleton — its own mutator row |
| Mutator: Remove-then-Set round trip | LDtkLevelLoadSystem world Set/Remove | N/A | singleton row |
| Mutator: Remove-then-Set round trip | EditorTransport restart | N/A | singleton row |
| Mutator: Remove-then-Set round trip | HierarchySystem managed singleton | N/A | singleton row |
| Mutator: Remove-then-Set round trip | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Mutator: Remove-then-Set round trip | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Mutator: Remove-then-Set round trip | typed Subscribe<T> kept IDisposable | N/A | bus column |
| Mutator: Remove-then-Set round trip | CullingSystem mid-update structural add/remove | handled | VisibleComponent add/remove churn is the system's job; D1+D3; C15 |
| Mutator: Remove-then-Set round trip | prep systems mid-loop Set | handled | [With(VisibleComponent)] membership follows removes/adds (D3) |
| Mutator: Remove-then-Set round trip | GameOverSystem create+dispose mid-iteration | N/A | create/dispose only |
| Mutator: Remove-then-Set round trip | manual-iteration mutators | handled | C4/D3 — SelectionSystem Remove/Set patterns |
| Mutator: Remove-then-Set round trip | NotifyChanged publication fleet | N/A | different verb |
| Mutator: Remove-then-Set round trip | EntityComponentReflection MethodInfo caches | handled | inspector add/remove flows through facade Remove/Set; C4 |
| Mutator: Remove-then-Set round trip | ReadAllComponents consumers | N/A | read-only |
| Mutator: Remove-then-Set round trip | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Mutator: Remove-then-Set round trip | ScreenController.Runner | N/A | runner surface |
| Mutator: Remove-then-Set round trip | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Mutator: Remove-then-Set round trip | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no component writes |
| Mutator: Remove-then-Set round trip | screen-teardown world.Dispose | N/A | no round trip at teardown |
| Mutator: Remove-then-Set round trip | PointerReplaySystem persistent sets | N/A | cursor components not removed/re-set |
| Mutator: Remove-then-Set round trip | packaging + manifests | N/A | packaging |
| Mutator: Remove-then-Set round trip | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Mutator: Remove-then-Set round trip | ProcessWideState registry | N/A | no static |
| Mutator: Remove-then-Set round trip | seeded test-order shuffle | N/A | harness |
| Mutator: Remove-then-Set round trip | guard-test model | N/A | guard column |
| Mutator: Remove-then-Set round trip | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Mutator: Remove-then-Set round trip | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Mutator: Remove-then-Set round trip | value-predicate premise text | N/A | premise about publication, not removal |
| Mutator: Remove-then-Set round trip | YSortSystem/DebugInspector hierarchy reads | N/A | reads only |
| Mutator: NotifyChanged on absent component | GravitySystem predicate set | N/A | edge unreachable — throws before predicate |
| Mutator: NotifyChanged on absent component | MasterRenderSystem.BuildDrawSet | N/A | edge unreachable |
| Mutator: NotifyChanged on absent component | MasterRenderSystem stable draw sort | N/A | no NotifyChanged |
| Mutator: NotifyChanged on absent component | AudioSystem Changed handler | N/A | subscriber side; no event fires on absent |
| Mutator: NotifyChanged on absent component | TransformCollisionDetection reactive add (M10) | N/A | verb unused at site |
| Mutator: NotifyChanged on absent component | Bake systems old-value diffing | N/A | subscriber side |
| Mutator: NotifyChanged on absent component | LDtk world-singleton subscribers + late-join replay | N/A | verb unused on singletons |
| Mutator: NotifyChanged on absent component | LDtkLevelLoadSystem world Set/Remove | N/A | verb unused |
| Mutator: NotifyChanged on absent component | EditorTransport restart | N/A | verb unused |
| Mutator: NotifyChanged on absent component | HierarchySystem managed singleton | N/A | verb unused |
| Mutator: NotifyChanged on absent component | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Mutator: NotifyChanged on absent component | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Mutator: NotifyChanged on absent component | typed Subscribe<T> kept IDisposable | N/A | bus column |
| Mutator: NotifyChanged on absent component | CullingSystem mid-update structural add/remove | N/A | verb unused |
| Mutator: NotifyChanged on absent component | prep systems mid-loop Set | N/A | verb unused |
| Mutator: NotifyChanged on absent component | GameOverSystem create+dispose mid-iteration | N/A | verb unused |
| Mutator: NotifyChanged on absent component | manual-iteration mutators | N/A | verb unused |
| Mutator: NotifyChanged on absent component | NotifyChanged publication fleet | GAP | M2/D4/C4 never pin the absent-component contract (DefaultEcs throws); a silently no-op facade would hide race bugs at ~40 sites — needs an EcsFacadeContractTests entry in C4 |
| Mutator: NotifyChanged on absent component | EntityComponentReflection MethodInfo caches | N/A | uses Set |
| Mutator: NotifyChanged on absent component | ReadAllComponents consumers | N/A | read-only |
| Mutator: NotifyChanged on absent component | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Mutator: NotifyChanged on absent component | ScreenController.Runner | N/A | runner surface |
| Mutator: NotifyChanged on absent component | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Mutator: NotifyChanged on absent component | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no publication |
| Mutator: NotifyChanged on absent component | screen-teardown world.Dispose | N/A | no publication at teardown |
| Mutator: NotifyChanged on absent component | PointerReplaySystem persistent sets | N/A | cursor components always present when notified |
| Mutator: NotifyChanged on absent component | packaging + manifests | N/A | packaging |
| Mutator: NotifyChanged on absent component | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Mutator: NotifyChanged on absent component | ProcessWideState registry | N/A | no static |
| Mutator: NotifyChanged on absent component | seeded test-order shuffle | N/A | harness |
| Mutator: NotifyChanged on absent component | guard-test model | N/A | guard column |
| Mutator: NotifyChanged on absent component | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Mutator: NotifyChanged on absent component | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Mutator: NotifyChanged on absent component | value-predicate premise text | N/A | edge documented once fleet-column GAP resolves |
| Mutator: NotifyChanged on absent component | YSortSystem/DebugInspector hierarchy reads | N/A | reads only |
| Mutator: singleton Remove-absent / re-Set after Remove | GravitySystem predicate set | N/A | entity-level column |
| Mutator: singleton Remove-absent / re-Set after Remove | MasterRenderSystem.BuildDrawSet | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | MasterRenderSystem stable draw sort | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | AudioSystem Changed handler | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | TransformCollisionDetection reactive add (M10) | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | Bake systems old-value diffing | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | LDtk world-singleton subscribers + late-join replay | handled | reload is NOT Removed->Added-driven: fail-then-reimport fires Added (parsers re-trigger); success re-import fires Changed with no subscribers — inert by design; C10 pins both sequences (contract amended) |
| Mutator: singleton Remove-absent / re-Set after Remove | LDtkLevelLoadSystem world Set/Remove | handled | corrected: Remove runs only in else/catch — after a prior success it removes PRESENT comps (Removed -> unload sweep); success re-import Sets w/o Remove -> Changed, zero world-Changed subs (quirk pinned); C4 both legs (contract amended) |
| Mutator: singleton Remove-absent / re-Set after Remove | EditorTransport restart | handled | BOTH legs pinned: present => Removed fires + Has false (EditorTransportTests.cs:186 Restart_RemovesTheWorldLevelComponents); absent => silent no-op (C4); precondition rows corrected |
| Mutator: singleton Remove-absent / re-Set after Remove | HierarchySystem managed singleton | N/A | EntityHierarchy never removed |
| Mutator: singleton Remove-absent / re-Set after Remove | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Mutator: singleton Remove-absent / re-Set after Remove | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Mutator: singleton Remove-absent / re-Set after Remove | typed Subscribe<T> kept IDisposable | N/A | bus column |
| Mutator: singleton Remove-absent / re-Set after Remove | CullingSystem mid-update structural add/remove | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | prep systems mid-loop Set | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | GameOverSystem create+dispose mid-iteration | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | manual-iteration mutators | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | NotifyChanged publication fleet | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | EntityComponentReflection MethodInfo caches | N/A | entity-level reflection |
| Mutator: singleton Remove-absent / re-Set after Remove | ReadAllComponents consumers | N/A | entity reads |
| Mutator: singleton Remove-absent / re-Set after Remove | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Mutator: singleton Remove-absent / re-Set after Remove | ScreenController.Runner | N/A | runner surface |
| Mutator: singleton Remove-absent / re-Set after Remove | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Mutator: singleton Remove-absent / re-Set after Remove | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no singleton use |
| Mutator: singleton Remove-absent / re-Set after Remove | screen-teardown world.Dispose | handled | world.Dispose pinned event-silent (new mutator row + C4 teardown test); carrier destroyed WITHOUT singleton Removed |
| Mutator: singleton Remove-absent / re-Set after Remove | PointerReplaySystem persistent sets | N/A | entity sets |
| Mutator: singleton Remove-absent / re-Set after Remove | packaging + manifests | N/A | packaging |
| Mutator: singleton Remove-absent / re-Set after Remove | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Mutator: singleton Remove-absent / re-Set after Remove | ProcessWideState registry | N/A | per-world |
| Mutator: singleton Remove-absent / re-Set after Remove | seeded test-order shuffle | N/A | harness |
| Mutator: singleton Remove-absent / re-Set after Remove | guard-test model | N/A | guard column |
| Mutator: singleton Remove-absent / re-Set after Remove | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Mutator: singleton Remove-absent / re-Set after Remove | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Mutator: singleton Remove-absent / re-Set after Remove | value-predicate premise text | N/A | premise not about singletons |
| Mutator: singleton Remove-absent / re-Set after Remove | YSortSystem/DebugInspector hierarchy reads | handled | Has-guard tolerates absent singleton (M5; YSortSystem.cs:67, DebugInspector.cs:105) |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | typed Subscribe<T> with kept IDisposable | N/A | message-bus column |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | GravitySystem predicate set | handled | D1+D3 — membership driven by facade publication events; C11 |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | MasterRenderSystem.BuildDrawSet | handled | D1+D3/C11 |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | MasterRenderSystem stable draw sort | N/A | no event consumer in sort |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | AudioSystem Changed handler | handled | D1 mandates Changed(old,new) (M6); C10; AudioSystem.cs:38-42 |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | TransformCollisionDetection reactive add (M10) | handled | C3 M10 proof + C4 replay-on-subscribe parity (editor recomposition over live world tags colliders exactly once) |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | Bake systems old-value diffing | handled | M6 names BoundaryBakeSystem.cs:97 / TileGridBakeSystem.cs:159; C10 |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | LDtk world-singleton subscribers + late-join replay | handled | C3 parser-shape proof + C10 parser test; C4 no-replay-on-subscribe test — manual Has+Get replay stays single-fire |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | LDtkLevelLoadSystem world Set/Remove | handled | M5; C4 world-Remove-fires-Removed test |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | EditorTransport restart | handled | M5 + C4 Changed-not-Added; CORE_TENETS §9 preserved |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | HierarchySystem managed singleton | handled | M5; in-place mutation fires nothing, as today (HierarchySystem.cs:35) |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | [Subscribe] hierarchy-walk registration | N/A | message bus, not component events |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | [Subscribe] + Subscribe(this) fleet | N/A | message bus |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | typed Subscribe<T> with kept IDisposable | N/A | message bus |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | CullingSystem mid-update structural add/remove | handled | D1 events feed prep-set membership; C15 visual identity gate |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | prep systems mid-loop Set | handled | D1+D2; C4 snapshot-tolerance contract test |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | GameOverSystem create+dispose mid-iteration | handled | Dispose⇒Removed cascade pinned by C4 dispose-cascade entry (values pre-teardown); D2 snapshot |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | manual-iteration mutators | handled | D1+D3 |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | NotifyChanged publication fleet | handled | M2 — NotifyChanged fires Changed old==new; C4 |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | EntityComponentReflection MethodInfo caches | GAP | events fire only if reflection lands on the facade's Set — contingent on the unresolved reflection-shape gap (Set row, same column); silent event loss on designer edits otherwise |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | ReadAllComponents consumers | N/A | read path fires nothing |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | ScreenController.Runner | N/A | runner surface |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition only |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no events here |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | screen-teardown world.Dispose | handled | wave 2 + C12 — facade-owned subscriptions die with world Dispose |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | PointerReplaySystem persistent sets | handled | D3 membership fed by facade events; PointerReplaySystem.cs:137-138 |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | packaging + manifests | N/A | packaging only |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | ProcessWideState registry | handled | C12 — static event/type tables registered on introduction |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | seeded test-order shuffle | N/A | harness; lifecycle row covers leakage |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | guard-test model | N/A | guard column |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | value-predicate premise text | handled | C8 new facade premises name the event contract; C22 |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | YSortSystem/DebugInspector hierarchy reads | N/A | reads fire no events |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | GravitySystem predicate set | handled | M1 (named highest-risk); C11 gravity behaviour test |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | MasterRenderSystem.BuildDrawSet | handled | M1; C11; managed DrawComponent predicate (MasterRenderSystem.cs:90) |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | MasterRenderSystem stable draw sort | handled | C7 identity gated on deterministic headless clock (contract amended); wave-2 D3 frame-stable list, diffs per C15 |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | AudioSystem Changed handler | N/A | presence-only set (AudioSystem.cs:33) |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | TransformCollisionDetection reactive add (M10) | N/A | presence-only tag set |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | Bake systems old-value diffing | N/A | event handlers, no predicate |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | LDtk world-singleton subscribers + late-join replay | N/A | singleton store, no predicate |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | LDtkLevelLoadSystem world Set/Remove | N/A | singleton store |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | EditorTransport restart | N/A | singleton store |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | HierarchySystem managed singleton | N/A | singleton store |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | CullingSystem mid-update structural add/remove | handled | D3 — VisibleComponent add/remove re-evals Main-pass membership |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | prep systems mid-loop Set | handled | D2/D3 — mid-loop Set republishes; snapshot keeps loop safe |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | GameOverSystem create+dispose mid-iteration | N/A | no predicate set touched |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | manual-iteration mutators | N/A | no value-predicate queries at these sites |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | NotifyChanged publication fleet | handled | M1+M2 publication hook; C11 no-move-without-publish test |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | EntityComponentReflection MethodInfo caches | handled | reflection Set routes through facade publication (M1); shape risk tracked at Set-row gap |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | ReadAllComponents consumers | N/A | read-only |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | ScreenController.Runner | N/A | runner surface |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no predicate |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | screen-teardown world.Dispose | N/A | membership dies with world |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | PointerReplaySystem persistent sets | N/A | presence-only sets |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | packaging + manifests | N/A | packaging |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | ProcessWideState registry | N/A | per-world state |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | seeded test-order shuffle | N/A | harness |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | guard-test model | N/A | guard column |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | value-predicate premise text | handled | 2c: premises.md:692 rewritten entirely in same PR (C22) |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | YSortSystem/DebugInspector hierarchy reads | N/A | singleton reads |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | GravitySystem predicate set | handled | D2; gravity mutates values only, no structural change |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | MasterRenderSystem.BuildDrawSet | handled | D3; draw list copied into DrawSortBuffer per frame |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | MasterRenderSystem stable draw sort | handled | sort runs on copied buffer (MasterRenderSystem.cs:80-85) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | AudioSystem Changed handler | handled | D3; Reconcile loop non-structural (AudioSystem.cs:49-54) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | TransformCollisionDetection reactive add (M10) | handled | C3/C4 — structural add inside publish path proven wave 0, tested wave 2 |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | Bake systems old-value diffing | N/A | event handlers, not iteration |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | LDtk world-singleton subscribers + late-join replay | N/A | no set iteration at site |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | LDtkLevelLoadSystem world Set/Remove | N/A | no set iteration at site |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | EditorTransport restart | N/A | no set iteration at site |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | HierarchySystem managed singleton | N/A | singleton write only |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | CullingSystem mid-update structural add/remove | handled | snapshot per-enumeration, never per-frame cached (D3 amended); C4 same-frame Culling->prep visibility test |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | prep systems mid-loop Set | handled | per-enumeration snapshot + synchronous membership (D3 amended); 2b sites TextPrepSystem.cs:75 / ButtonMeshPrepSystem.cs:83 still safe |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | GameOverSystem create+dispose mid-iteration | handled | 2b names GameOverSystem.cs:64,106,129; D2; C4 disposed-member-skipped assertion |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | manual-iteration mutators | handled | 2b names ~6 manual sites (SelectionSystem.cs:404-415 etc.); D3 snapshot enumeration |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | NotifyChanged publication fleet | handled | D3 — publication-driven membership change mid-loop tolerated by snapshot |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | EntityComponentReflection MethodInfo caches | N/A | single-entity designer op, not iteration |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | ReadAllComponents consumers | N/A | reads one entity |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | ScreenController.Runner | N/A | runner surface |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | wrapper, no iteration |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | screen-teardown world.Dispose | N/A | no iteration during teardown |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | PointerReplaySystem persistent sets | handled | D3; PointerReplaySystem.cs:294,352 per-frame enumeration |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | packaging + manifests | N/A | packaging |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | ProcessWideState registry | N/A | per-world buffers |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | seeded test-order shuffle | N/A | harness |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | guard-test model | N/A | guard column |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | value-predicate premise text | handled | D2 documented as facade premise + C8 Tests: named |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | YSortSystem/DebugInspector hierarchy reads | N/A | singleton reads |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | GravitySystem predicate set | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | MasterRenderSystem.BuildDrawSet | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | MasterRenderSystem stable draw sort | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | AudioSystem Changed handler | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | TransformCollisionDetection reactive add (M10) | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | Bake systems old-value diffing | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | LDtk world-singleton subscribers + late-join replay | handled | M5 4 types + notifications; late-join Has+Get replay kept (LDtkTileParserSystem.cs:41-46) |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | LDtkLevelLoadSystem world Set/Remove | handled | M5 names LDtkLevelLoadSystem.cs:71,80,81; C4 Removed-fires test |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | EditorTransport restart | handled | carrier invisible to all query/sweep surfaces (new premise + C4 test); Restart sweep can't kill store backing; EditorTransportTests re-run |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | HierarchySystem managed singleton | handled | EntityHierarchy set once in ctor survives sweeps — carrier invisible (premise + C4 test); H8 measured, not converted |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | [Subscribe] hierarchy-walk registration | N/A | bus column |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | CullingSystem mid-update structural add/remove | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | prep systems mid-loop Set | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | GameOverSystem create+dispose mid-iteration | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | manual-iteration mutators | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | NotifyChanged publication fleet | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | EntityComponentReflection MethodInfo caches | N/A | entity-level reflection |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | ReadAllComponents consumers | handled | DebugInspector.cs:78 enumerates with NO filter; carrier excluded by C4 invisibility test — no phantom row |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | IGameScreen ISystem<GameState> contract | N/A | type surface |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | ScreenController.Runner | N/A | runner surface |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no singleton use |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | screen-teardown world.Dispose | handled | singleton entity dies with facade world Dispose (wave 2, C12) |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | PointerReplaySystem persistent sets | N/A | entity sets only |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | packaging + manifests | N/A | packaging |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | ProcessWideState registry | N/A | per-world state |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | seeded test-order shuffle | N/A | harness |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | guard-test model | N/A | guard column |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | value-predicate premise text | N/A | premise covers entity predicates |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | YSortSystem/DebugInspector hierarchy reads | handled | M5 keeps optional Has/Get pattern (YSortSystem.cs:67, DebugInspector.cs:105) |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | GravitySystem predicate set | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | MasterRenderSystem.BuildDrawSet | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | MasterRenderSystem stable draw sort | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | AudioSystem Changed handler | handled | M3 typed sub (AudioSystem.cs:35); synchronous publish kept per precondition diff |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | TransformCollisionDetection reactive add (M10) | N/A | component events, not bus |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | Bake systems old-value diffing | N/A | component events |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | LDtk world-singleton subscribers + late-join replay | N/A | component events |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | LDtkLevelLoadSystem world Set/Remove | handled | singleton dispatch nested in bus dispatch pinned by C3 parser-shape proof + C4 reentrancy test |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | EditorTransport restart | handled | restart publishes LoadLevelRequest through the facade bus; e2e re-run pins it |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | HierarchySystem managed singleton | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | [Subscribe] hierarchy-walk registration | GAP | M3 promises attribute scan but not DefaultEcs's type-hierarchy walk + virtual-override dedup (TransformPhysicalCollisionResolutionSystem.cs:13-14,32) — naive scanner double-subscribes or misses the base-annotated virtual On |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | [Subscribe] + Subscribe(this) fleet | handled | both forms supported; mixed-marking census + C4 single-dispatch pin close the double-registration hole (contract added) |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | typed Subscribe<T> with kept IDisposable | handled | M3 (19 typed sites); bus returns IDisposable (CameraFollowSystem.cs:61) |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | CullingSystem mid-update structural add/remove | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | prep systems mid-loop Set | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | GameOverSystem create+dispose mid-iteration | N/A | no bus interaction in scope |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | manual-iteration mutators | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | NotifyChanged publication fleet | N/A | component publication, not bus |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | EntityComponentReflection MethodInfo caches | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | ReadAllComponents consumers | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | ScreenController.Runner | N/A | runner surface |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | screen-teardown world.Dispose | handled | bus is per-world (precondition diff); dies with world Dispose; C12 |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | PointerReplaySystem persistent sets | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | packaging + manifests | N/A | packaging |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | ProcessWideState registry | N/A | per-world bus |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | seeded test-order shuffle | N/A | harness |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | guard-test model | N/A | guard column |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | value-predicate premise text | N/A | premise not about bus |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | YSortSystem/DebugInspector hierarchy reads | N/A | no bus use |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | GravitySystem predicate set | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | MasterRenderSystem.BuildDrawSet | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | MasterRenderSystem stable draw sort | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | AudioSystem Changed handler | N/A | blanket wave-1 retype (IsEnabled kept per issue §4) |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | TransformCollisionDetection reactive add (M10) | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | Bake systems old-value diffing | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | LDtk world-singleton subscribers + late-join replay | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | LDtkLevelLoadSystem world Set/Remove | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | EditorTransport restart | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | HierarchySystem managed singleton | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | CullingSystem mid-update structural add/remove | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | prep systems mid-loop Set | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | GameOverSystem create+dispose mid-iteration | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | manual-iteration mutators | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | NotifyChanged publication fleet | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | EntityComponentReflection MethodInfo caches | N/A | not a system |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | ReadAllComponents consumers | N/A | not a system contract |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | IGameScreen ISystem<GameState> contract | handled | M7 (GameScreen.cs:11-13; 71 files); wave 1 moves IGameScreen to facade types |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | ScreenController.Runner | handled | wave-1 sweep + 2b facts; ScreenController.cs:27,35 verified |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | EditorPipelineRegistrar ParallelSystem<T> | handled | M4 — facade Sequential/ParallelSystem compose facade ISystem |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | GatedSystem IsEnabled + ISuspendableSystem cast | handled | issue §4 ISystem incl. IsEnabled; 2b: ISuspendableSystem uncoupled (GatedSystem.cs:130) |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | screen-teardown world.Dispose | handled | pipeline-before-world Dispose order kept; composite cascade reaches leaves (C4 test); wave-1 retype plus the cascade contract |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | PointerReplaySystem persistent sets | N/A | blanket wave-1 retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | packaging + manifests | N/A | packaging |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | ProcessWideState registry | N/A | no static |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | seeded test-order shuffle | N/A | harness |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | guard-test model | N/A | guard column |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | DefaultEcs.Threading heads + fully-qualified use | N/A | runner row covers |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | DrawPrepSystemBase (dead) | handled | C6 deletes the dead ISystem base wave 1 |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | value-predicate premise text | N/A | premise not about system typing |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | YSortSystem/DebugInspector hierarchy reads | N/A | read pattern only |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | GravitySystem predicate set | handled | runner-accepting predicate-set ctor named in surface (contract); degree>1 throws NotSupportedException (dimension row seam) |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | MasterRenderSystem.BuildDrawSet | N/A | no runner in draw path |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | MasterRenderSystem stable draw sort | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | AudioSystem Changed handler | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | TransformCollisionDetection reactive add (M10) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | Bake systems old-value diffing | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | LDtk world-singleton subscribers + late-join replay | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | LDtkLevelLoadSystem world Set/Remove | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | EditorTransport restart | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | HierarchySystem managed singleton | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | CullingSystem mid-update structural add/remove | handled | facade EntitySystem accepts runner; hosts degree 1 (2b, M4) |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | prep systems mid-loop Set | handled | 2b: 9 runner-consuming system types/5 files; degree 1 |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | GameOverSystem create+dispose mid-iteration | handled | same runner surface; degree 1 (M4) |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | manual-iteration mutators | N/A | manual loops, no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | NotifyChanged publication fleet | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | EntityComponentReflection MethodInfo caches | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | ReadAllComponents consumers | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | IGameScreen ISystem<GameState> contract | N/A | ISystem row covers |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | ScreenController.Runner | handled | 2b measured ctor/property; facade IParallelRunner + default runner (wave 1) |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | EditorPipelineRegistrar ParallelSystem<T> | handled | M4 — sequential impl behaviour-preserving at degree 1; 5 uses |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | wrapper takes no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | screen-teardown world.Dispose | N/A | runner outlives worlds by design, unchanged |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | PointerReplaySystem persistent sets | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | packaging + manifests | N/A | packaging row covers |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | ProcessWideState registry | N/A | runner instance-scoped |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | seeded test-order shuffle | N/A | harness |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | guard-test model | N/A | guard column |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | DefaultEcs.Threading heads + fully-qualified use | handled | wave-1 sweep + C5 catches fully-qualified token (CollisionConsumerAuditTests.cs:225) |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | DrawPrepSystemBase (dead) | handled | C6 delete removes the misnamed useParallel/useBuffer param |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | value-predicate premise text | N/A | premise not about runners |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | YSortSystem/DebugInspector hierarchy reads | N/A | no runner |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | GravitySystem predicate set | N/A | no IsAlive use |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | MasterRenderSystem.BuildDrawSet | N/A | no IsAlive use |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | MasterRenderSystem stable draw sort | N/A | no IsAlive use |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | AudioSystem Changed handler | N/A | handler receives live entity |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | TransformCollisionDetection reactive add (M10) | N/A | handler entity live inside publish |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | Bake systems old-value diffing | N/A | handler entity live |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | LDtk world-singleton subscribers + late-join replay | N/A | world-level |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | LDtkLevelLoadSystem world Set/Remove | N/A | world-level |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | EditorTransport restart | N/A | world-level |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | HierarchySystem managed singleton | N/A | world-level |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | [Subscribe] hierarchy-walk registration | N/A | bus column |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | CullingSystem mid-update structural add/remove | handled | C13; CullingSystem.cs:45,64 cached-handle IsAlive checks |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | prep systems mid-loop Set | N/A | no cached handles |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | GameOverSystem create+dispose mid-iteration | handled | C13 + D2 — dispose-mid-iteration then handle checks |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | manual-iteration mutators | handled | C13/D3 — Dispose inside manual loops |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | NotifyChanged publication fleet | N/A | live-entity sites |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | EntityComponentReflection MethodInfo caches | N/A | boxed copy addresses same storage; no liveness check |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | ReadAllComponents consumers | N/A | read of live entity |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | IGameScreen ISystem<GameState> contract | N/A | type surface |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | ScreenController.Runner | N/A | runner surface |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no entity handles |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | screen-teardown world.Dispose | GAP | Arch recycles World slots in static World.Worlds (H9); a stale Entity from a disposed screen's world can read alive once the slot is reused across 10-screen churn — C13 names entity-id recycling only, not world-id reuse after Dispose |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | PointerReplaySystem persistent sets | N/A | re-queries each frame, no stale handles |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | packaging + manifests | N/A | packaging |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | ProcessWideState registry | N/A | lifecycle row covers |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | seeded test-order shuffle | N/A | harness |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | guard-test model | N/A | guard column |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | value-predicate premise text | N/A | premise not about liveness |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | YSortSystem/DebugInspector hierarchy reads | N/A | world reads |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | GravitySystem predicate set | N/A | per-world system |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | MasterRenderSystem.BuildDrawSet | N/A | per-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | MasterRenderSystem stable draw sort | N/A | per-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | AudioSystem Changed handler | N/A | per-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | TransformCollisionDetection reactive add (M10) | N/A | per-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | Bake systems old-value diffing | N/A | per-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | LDtk world-singleton subscribers + late-join replay | N/A | same-world ops |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | LDtkLevelLoadSystem world Set/Remove | N/A | same-world ops |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | EditorTransport restart | N/A | restart mutates one world, no re-create |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | HierarchySystem managed singleton | N/A | per-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | [Subscribe] hierarchy-walk registration | N/A | bus column |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | CullingSystem mid-update structural add/remove | N/A | entity-level |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | prep systems mid-loop Set | N/A | entity-level |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | GameOverSystem create+dispose mid-iteration | N/A | entity-level, not world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | manual-iteration mutators | N/A | entity-level |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | NotifyChanged publication fleet | N/A | entity-level |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | EntityComponentReflection MethodInfo caches | N/A | type-keyed caches survive world churn harmlessly |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | ReadAllComponents consumers | N/A | per-world reads |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | IGameScreen ISystem<GameState> contract | N/A | screens create worlds via EcsWorld.Create (H10) — covered wave 1 |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | ScreenController.Runner | N/A | runner not world-scoped |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no world lifecycle |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | screen-teardown world.Dispose | handled | wave 2 — facade Dispose unhooks Arch registry + subs; leaking tests fixed; C12; 10 screens named |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | PointerReplaySystem persistent sets | N/A | per-world sets |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | packaging + manifests | N/A | packaging |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | ProcessWideState registry | handled | H9/C12; ProcessWideState.cs:41-99 tracks no ECS statics today — additions land same PR |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | seeded test-order shuffle | handled | verification protocol MONODREAMS_TEST_SEED=8 — the shuffle that exposes static leakage |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | guard-test model | N/A | statics guarded by hygiene tests, not source lint |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | value-predicate premise text | N/A | premise not about lifecycle |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | YSortSystem/DebugInspector hierarchy reads | N/A | per-world reads |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | GravitySystem predicate set | N/A | per-entity integration, order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | MasterRenderSystem.BuildDrawSet | handled | explicit sort downstream (precondition diff: systems needing order sort explicitly) |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | MasterRenderSystem stable draw sort | handled | stable sort; C7 byte-identity only valid under deterministic headless clock (contract amended) + double-run precheck; C15 |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | AudioSystem Changed handler | N/A | per-source reconcile order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | TransformCollisionDetection reactive add (M10) | N/A | tagging order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | Bake systems old-value diffing | N/A | event-driven |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | LDtk world-singleton subscribers + late-join replay | N/A | spawn order data-driven, not query order |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | LDtkLevelLoadSystem world Set/Remove | N/A | explicit statement order |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | EditorTransport restart | N/A | explicit statement order |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | HierarchySystem managed singleton | N/A | single value |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | [Subscribe] hierarchy-walk registration | N/A | registration, not iteration |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | [Subscribe] + Subscribe(this) fleet | N/A | bus dispatch order facade-owned, unchanged |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | typed Subscribe<T> with kept IDisposable | N/A | bus order facade-owned |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | CullingSystem mid-update structural add/remove | N/A | add/remove per entity, order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | prep systems mid-loop Set | N/A | per-entity prep order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | GameOverSystem create+dispose mid-iteration | N/A | outcome order-independent |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | manual-iteration mutators | handled | same census commitment (contract); per-site deterministic pick replaces reliance on set order |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | NotifyChanged publication fleet | N/A | publication order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | EntityComponentReflection MethodInfo caches | N/A | single entity |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | ReadAllComponents consumers | N/A | inspector listing order cosmetic |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | IGameScreen ISystem<GameState> contract | N/A | system order is explicit composition |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | ScreenController.Runner | N/A | no iteration |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | EditorPipelineRegistrar ParallelSystem<T> | N/A | registration order explicit |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no iteration |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | screen-teardown world.Dispose | N/A | no iteration |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | PointerReplaySystem persistent sets | N/A | existence gates order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | packaging + manifests | N/A | packaging |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | ProcessWideState registry | N/A | no iteration |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | seeded test-order shuffle | N/A | test order, not entity order |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | guard-test model | N/A | guard column |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | DefaultEcs.Threading heads + fully-qualified use | N/A | degree-1 runner, no reorder |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | value-predicate premise text | N/A | order claims live in camera docs :235/:83, rewritten per 2c/C22 |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | YSortSystem/DebugInspector hierarchy reads | handled | YSort sorts explicitly; hierarchy read order-free (H4 precondition diff) |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | GravitySystem predicate set | N/A | repo-wide lint, nothing site-specific |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | MasterRenderSystem.BuildDrawSet | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | MasterRenderSystem stable draw sort | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | AudioSystem Changed handler | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | TransformCollisionDetection reactive add (M10) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | Bake systems old-value diffing | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | LDtk world-singleton subscribers + late-join replay | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | LDtkLevelLoadSystem world Set/Remove | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | EditorTransport restart | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | HierarchySystem managed singleton | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | [Subscribe] hierarchy-walk registration | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | [Subscribe] + Subscribe(this) fleet | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | typed Subscribe<T> with kept IDisposable | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | CullingSystem mid-update structural add/remove | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | prep systems mid-loop Set | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | GameOverSystem create+dispose mid-iteration | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | manual-iteration mutators | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | NotifyChanged publication fleet | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | EntityComponentReflection MethodInfo caches | handled | wave-1 sweep + C5 lint forces literal cleanup (EntityComponentReflection.cs:5,74) |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | ReadAllComponents consumers | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | IGameScreen ISystem<GameState> contract | handled | sweep sized to measured 449 lines / 320 files (contract corrected); lint = completeness proof |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | ScreenController.Runner | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | EditorPipelineRegistrar ParallelSystem<T> | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | screen-teardown world.Dispose | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | PointerReplaySystem persistent sets | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | packaging + manifests | GAP | MonoDreams.Cli/Installer/ProjectScaffolder.cs carries DefaultEcs literals until wave-4 swap — wave-1 guard 'no .cs outside facade' flags it; cli ratchet/allowlist entries not named in plan |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | CLI tests asserting literal DefaultEcs | GAP | Cli.Tests assert literal DefaultEcs (ManifestPlatformTests.cs:35-36, ScaffolderPlatformTests.cs:279-283,403) — trips wave-1 guard three waves before the wave-4 swap; KnownGaps entries unplanned |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | ProcessWideState registry | N/A | hygiene tests, not lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | seeded test-order shuffle | N/A | harness |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | guard-test model | handled | wave 1: modeled on EditorThemeLintTests + KnownGaps ratchet — the exact repo pattern (C5/C14) |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | DefaultEcs.Threading heads + fully-qualified use | handled | source-text lint catches fully-qualified DefaultEcs.Threading token (C5) |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | DrawPrepSystemBase (dead) | N/A | deleted same wave (C6) before guard could flag it |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | value-predicate premise text | N/A | docs, not .cs |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | YSortSystem/DebugInspector hierarchy reads | N/A | repo-wide lint |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | GravitySystem predicate set | N/A | code, not packaging |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | MasterRenderSystem.BuildDrawSet | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | MasterRenderSystem stable draw sort | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | AudioSystem Changed handler | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | TransformCollisionDetection reactive add (M10) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | Bake systems old-value diffing | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | LDtk world-singleton subscribers + late-join replay | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | LDtkLevelLoadSystem world Set/Remove | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | EditorTransport restart | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | HierarchySystem managed singleton | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | [Subscribe] hierarchy-walk registration | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | [Subscribe] + Subscribe(this) fleet | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | typed Subscribe<T> with kept IDisposable | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | CullingSystem mid-update structural add/remove | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | prep systems mid-loop Set | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | GameOverSystem create+dispose mid-iteration | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | manual-iteration mutators | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | NotifyChanged publication fleet | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | EntityComponentReflection MethodInfo caches | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | ReadAllComponents consumers | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | IGameScreen ISystem<GameState> contract | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | ScreenController.Runner | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | EditorPipelineRegistrar ParallelSystem<T> | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | screen-teardown world.Dispose | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | PointerReplaySystem persistent sets | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | packaging + manifests | handled | M9 enumerates all 7 surfaces incl. module.schema.json:55, foundation/module.json:34; wave 4 + C19/C20; locks regenerated |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | CLI tests asserting literal DefaultEcs | handled | C19 zero-token sweep forces rewrite of ManifestPlatformTests.cs:35-36 etc.; M11 facade packages; C20 all legs |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | ProcessWideState registry | N/A | no packaging static |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | seeded test-order shuffle | N/A | harness |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | guard-test model | N/A | guard row covers |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | DefaultEcs.Threading heads + fully-qualified use | handled | M9: Examples.Core:38 / Demos:25 csproj refs; head code via wave-1 sweep |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | value-predicate premise text | N/A | docs row (C22) |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | YSortSystem/DebugInspector hierarchy reads | N/A | code |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | GravitySystem predicate set | handled | C4+C11 — re-Set with toggled Gravity.active re-evals membership |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | MasterRenderSystem.BuildDrawSet | handled | C11 — retarget via Set moves entity between per-target sets |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | MasterRenderSystem stable draw sort | N/A | sort consumes buffer |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | AudioSystem Changed handler | handled | the exact documented dependency (AudioSystem.cs:39-42); C4 + C10 named test |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | TransformCollisionDetection reactive add (M10) | handled | Has-guard avoids Set-on-present (verified :89,94); C3 |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | Bake systems old-value diffing | handled | delivery-as-trigger pinned (C10 amended); a facade suppressing old==new Changed fails the bake tests |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | LDtk world-singleton subscribers + late-join replay | N/A | singleton — its own mutator row |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | LDtkLevelLoadSystem world Set/Remove | N/A | singleton row |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | EditorTransport restart | N/A | singleton row |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | HierarchySystem managed singleton | N/A | singleton row |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | CullingSystem mid-update structural add/remove | N/A | Has-guarded — Set only when absent (CullingSystem.cs:98-101) |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | prep systems mid-loop Set | handled | per-frame Set-on-present fires Changed as today; C4 + C7 identity gate |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | GameOverSystem create+dispose mid-iteration | N/A | create/dispose only |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | manual-iteration mutators | handled | C4 semantics apply to migrated sites |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | NotifyChanged publication fleet | N/A | different verb |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | EntityComponentReflection MethodInfo caches | handled | write-back Set-on-present re-fires Changed; C4 — shape risk tracked at Set-row gap |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | ReadAllComponents consumers | N/A | read-only |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | ScreenController.Runner | N/A | runner surface |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no component writes |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | screen-teardown world.Dispose | N/A | no Set at teardown |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | PointerReplaySystem persistent sets | N/A | presence unchanged by re-Set |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | packaging + manifests | N/A | packaging |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | ProcessWideState registry | N/A | no static |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | seeded test-order shuffle | N/A | harness |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | guard-test model | N/A | guard column |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | value-predicate premise text | handled | premises.md:698-706 publication contract; C22 rewrite |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | YSortSystem/DebugInspector hierarchy reads | N/A | reads only |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | GravitySystem predicate set | handled | C4 — Removed drops membership, Added re-evals predicate |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | MasterRenderSystem.BuildDrawSet | handled | C4/C11 |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | MasterRenderSystem stable draw sort | N/A | sort consumes buffer |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | AudioSystem Changed handler | handled | AudioSystem subscribes Removed AND Changed (:38,:42); C10 |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | TransformCollisionDetection reactive add (M10) | handled | re-Add re-fires Added → re-tag via Has-guard; C3/C10 |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | Bake systems old-value diffing | handled | Added+Changed subs at BoundaryBakeSystem.cs:88-97; C10 |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | LDtk world-singleton subscribers + late-join replay | N/A | singleton — its own mutator row |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | LDtkLevelLoadSystem world Set/Remove | N/A | singleton row |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | EditorTransport restart | N/A | singleton row |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | HierarchySystem managed singleton | N/A | singleton row |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | CullingSystem mid-update structural add/remove | handled | VisibleComponent add/remove churn is the system's job; D1+D3; C15 |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | prep systems mid-loop Set | handled | [With(VisibleComponent)] membership follows removes/adds (D3) |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | GameOverSystem create+dispose mid-iteration | N/A | create/dispose only |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | manual-iteration mutators | handled | C4/D3 — SelectionSystem Remove/Set patterns |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | NotifyChanged publication fleet | N/A | different verb |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | EntityComponentReflection MethodInfo caches | handled | inspector add/remove flows through facade Remove/Set; C4 |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | ReadAllComponents consumers | N/A | read-only |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | ScreenController.Runner | N/A | runner surface |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no component writes |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | screen-teardown world.Dispose | N/A | no round trip at teardown |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | PointerReplaySystem persistent sets | N/A | cursor components not removed/re-set |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | packaging + manifests | N/A | packaging |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | ProcessWideState registry | N/A | no static |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | seeded test-order shuffle | N/A | harness |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | guard-test model | N/A | guard column |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | value-predicate premise text | N/A | premise about publication, not removal |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | YSortSystem/DebugInspector hierarchy reads | N/A | reads only |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | GravitySystem predicate set | N/A | edge unreachable — throws before predicate |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | MasterRenderSystem.BuildDrawSet | N/A | edge unreachable |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | MasterRenderSystem stable draw sort | N/A | no NotifyChanged |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | AudioSystem Changed handler | N/A | subscriber side; no event fires on absent |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | TransformCollisionDetection reactive add (M10) | N/A | verb unused at site |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | Bake systems old-value diffing | N/A | subscriber side |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | LDtk world-singleton subscribers + late-join replay | N/A | verb unused on singletons |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | LDtkLevelLoadSystem world Set/Remove | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | EditorTransport restart | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | HierarchySystem managed singleton | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | CullingSystem mid-update structural add/remove | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | prep systems mid-loop Set | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | GameOverSystem create+dispose mid-iteration | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | manual-iteration mutators | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | NotifyChanged publication fleet | GAP | M2/D4/C4 never pin the absent-component contract (DefaultEcs throws); a silently no-op facade would hide race bugs at ~40 sites — needs an EcsFacadeContractTests entry in C4 |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | EntityComponentReflection MethodInfo caches | N/A | uses Set |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | ReadAllComponents consumers | N/A | read-only |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | ScreenController.Runner | N/A | runner surface |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no publication |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | screen-teardown world.Dispose | N/A | no publication at teardown |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | PointerReplaySystem persistent sets | N/A | cursor components always present when notified |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | packaging + manifests | N/A | packaging |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | ProcessWideState registry | N/A | no static |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | seeded test-order shuffle | N/A | harness |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | guard-test model | N/A | guard column |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | value-predicate premise text | N/A | edge documented once fleet-column GAP resolves |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | YSortSystem/DebugInspector hierarchy reads | N/A | reads only |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | GravitySystem predicate set | N/A | entity-level column |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | MasterRenderSystem.BuildDrawSet | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | MasterRenderSystem stable draw sort | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | AudioSystem Changed handler | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | TransformCollisionDetection reactive add (M10) | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | Bake systems old-value diffing | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | LDtk world-singleton subscribers + late-join replay | handled | fail-then-reimport Added re-parses; success re-import Changed inert; C10 both-sequence tests (contract amended) |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | LDtkLevelLoadSystem world Set/Remove | handled | Remove legs = error paths only; present-leg fires Removed -> HandleLevelUnloaded; absent-leg silent no-op; success re-import = Changed no-subscriber quirk (contract amended) |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | EditorTransport restart | handled | Remove-when-PRESENT leg reachable (tests Set markers then Restart); C4 pins both legs; no-op-only claim retracted |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | HierarchySystem managed singleton | N/A | EntityHierarchy never removed |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | [Subscribe] hierarchy-walk registration | N/A | bus column |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | [Subscribe] + Subscribe(this) fleet | N/A | bus column |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | typed Subscribe<T> with kept IDisposable | N/A | bus column |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | CullingSystem mid-update structural add/remove | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | prep systems mid-loop Set | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | GameOverSystem create+dispose mid-iteration | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | manual-iteration mutators | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | NotifyChanged publication fleet | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | EntityComponentReflection MethodInfo caches | N/A | entity-level reflection |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | ReadAllComponents consumers | N/A | entity reads |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | ScreenController.Runner | N/A | runner surface |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no singleton use |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | screen-teardown world.Dispose | handled | world.Dispose pinned event-silent (new mutator row + C4 teardown test); no singleton Removed at teardown |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | PointerReplaySystem persistent sets | N/A | entity sets |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | packaging + manifests | N/A | packaging |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | ProcessWideState registry | N/A | per-world |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | seeded test-order shuffle | N/A | harness |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | guard-test model | N/A | guard column |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | value-predicate premise text | N/A | premise not about singletons |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | YSortSystem/DebugInspector hierarchy reads | handled | Has-guard tolerates absent singleton (M5; YSortSystem.cs:67, DebugInspector.cs:105) |
| World-singleton store Set/Get/Has/Remove | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | handled | carrier excluded from unfiltered surface (premise + C4 test); _totalEntityCount unaffected by 4 singleton Sets |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | handled | carrier excluded from unfiltered surface (premise + C4 test) |
| World-singleton store Set/Get/Has/Remove | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | handled | sweep sees real scene entities only; carrier invisible (premise + C4); store survives Restart/RestoreBackup |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | handled | carrier invisible to catch-all AsSet (premise + C4); Survives() never consulted for it — foundation stays editor-type-free |
| Iteration order unspecified (H4) | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | handled | clock lands on main pre-wave; ALL wave branches base on post-clock main (rebase if clock lands after the stack is cut); main-vs-main precheck ALSO runs on the wave branch before C7 gates (contract amended) |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | handled | stack-base ordering fixed: rebase onto post-clock main; wave-branch precheck required before byte-identity gating (contract amended) |
| Snapshot iteration EntitySystem/EntityQuery | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | handled | census corrected: 23 asserts/5 files (+ColliderDebugSystemTests, ProxyVertexTests, CameraEntityEditorTests.cs:210) + engine SceneCameraEnsure.cs:65; all rewritten wave 1; AsSet-var-aware grep gates wave close (contract corrected) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | handled | 9-assert/2-file figure retracted; measured 23/5 + SceneCameraEnsure.cs:65; rewritten wave 1; AsSet-var-aware grep gates wave-1 close (contract corrected) |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | handled | C4 amended: no deferred CommandBuffer — IsAlive false + membership dropped before Dispose returns; double-Dispose no-op (no double Removed to AudioSystem) |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | handled | mass-dispose sweeps fire per-entity Removed cascades pre-destroy (C4); new precondition rows added |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | GameOverSystem create+dispose mid-iteration | handled | dispose mid-loop fires cascade synchronously; D2 snapshot skips the disposed member (C4) |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | LDtk world-singleton subscribers + late-join replay | handled | CleanupTileEntities mass dispose covered by parser-shape C3 proof + C10 test |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | AudioSystem Changed handler | N/A | Changed path — dispose-path column covers this row's site |
| Facade-fired events Added/Changed(old,new)/Removed | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | handled | C4 dispose-cascade: Removed delivered synchronously with pre-teardown value; loop cut, no leak |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | handled | C4 dispose-cascade entry; facade captures values then raises Removed before Arch Destroy |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | LDtk world-singleton subscribers + late-join replay | handled | wave 0 MEASURES both legs incl. world-comp subscribe-replay (contract amended); C4 singleton no-replay pin conditional on measurement; parser no-double-parse test ships regardless |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | TransformCollisionDetection reactive add (M10) | handled | wave 0 measures DefaultEcs entity-level Added replay; facade pins parity (C4); collider tagging on recomposition covered |
| Wave-3 chunk conversions preserve publication-cached predicate membership | GravitySystem predicate set | handled | C17 amended: C11 in-place-no-move negative must pass through the CONVERTED chunk path; live per-element read of Gravity.active fails the gate |
| Wave-3 chunk conversions preserve publication-cached predicate membership | MasterRenderSystem.BuildDrawSet | handled | C17 amended: converted feed keeps last-published Target membership; C11 negatives routed through converted path |
| Iteration order unspecified (H4) | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | handled | wave 2: explicit deterministic pick (lowest-entity-id / single-instance assert) + test (contract added); camera premise upgraded from none-yet |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | handled | first-match-and-break sites get explicit deterministic rule wave 2 (contract added); H4 gains an executable seam here |
| Guard ratchet EcsBoundaryLintTests | MonoDreams.Benchmarks dual-backend project | handled | named allowlist entry in EcsBoundaryLintTests (DefaultEcs w1-3, raw Arch w2-3); entry deleted wave 4 with the DefaultEcs legs (contract added) |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | MonoDreams.Benchmarks dual-backend project | handled | Benchmarks allowlisted per wave in the lint (contract added); ratchet otherwise empty |
| Packaging: Arch replaces DefaultEcs | MonoDreams.Benchmarks dual-backend project | handled | Benchmarks keeps DefaultEcs package until wave 4; wave 4 deletes DefaultEcs legs + ref BEFORE the C19 zero-token sweep (contract added) |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | MonoDreams.Benchmarks dual-backend project | handled | wave-4 fate named: delete DefaultEcs legs + package ref, drop allowlist entry, then C19 sweep (contract added) |
| Snapshot iteration EntitySystem/EntityQuery | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | handled | transient AsSet seeds by construction-time live scan (C4), then per-enumeration snapshot — sweep sees the full scene, no event-history dependence; Restart cannot duplicate |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | handled | construction-time live-scan seeding (C4) + snapshot tolerant of its own Disposes (D2/D3 amended) |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | handled | unfiltered (no With/Without) query added to required facade surface (contract); M8 port reads real entities only — carrier excluded |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | C4 teardown test: world.Dispose fires NO per-component/singleton Removed (matches DefaultEcs today); discarded M10 subs + LDtk singleton subscribers observe nothing |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | screen-teardown world.Dispose | handled | C4 teardown event-silence test; cascade reserved to entity.Dispose; no teardown-time HandleLevelUnloaded/AudioSystem re-entry |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | screen-teardown world.Dispose | handled | cascade is entity.Dispose-ONLY; world.Dispose bypasses it (C4 event-silence test) — reconciles with the dispose-cascade contract |
| Facade-fired events Added/Changed(old,new)/Removed | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | facade tears subscriptions down before storage drain; zero events at teardown (C4 event-silence test) |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | subs die before drain; zero events at teardown (C4 event-silence test) |
| EcsWorld.Create/Dispose over Arch static registry | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | silent drain then Arch registry unhook (wave 2, C12) |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | silent drain then registry unhook (wave 2, C12); 10-screen churn covered |
| World-singleton store Set/Get/Has/Remove | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | store + carrier die silently with the world; no singleton Removed (C4 teardown test) |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | store + carrier die silently with the world; no singleton Removed (C4 teardown test) |
| Facade ISystem<T>/IGameScreen contract | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | handled | facade Sequential/Parallel/GatedSystem Dispose recurses to leaves reverse-order (new C4 test + premise) — sole path to audio stop + GPU free |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | handled | composite Dispose cascade added to facade surface (C4 test + premise); screens dispose pipeline before world |
| IParallelRunner + sequential ParallelSystem<T> | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | handled | M4 behaviour-preservation extended to Dispose: sequential ParallelSystem disposes children like DefaultEcs's (C4 cascade test) |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | handled | sequential ParallelSystem Dispose recurses to children (C4 cascade test); EditorPipelineRegistrar tree unchanged |
| Wave-3 chunk conversions preserve publication-cached predicate membership | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | handled | C17 amended: conversion preserves BOTH clamps — final depth incl. bias (:50-55) AND child clamp incl. minimalBias (:84-90); edge-of-band+bias test |
| Iteration order unspecified (H4) | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic, not order — C17 clamp preservation covers |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic, not order — C17 covers |
| Snapshot iteration EntitySystem/EntityQuery | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | handled | construction-time live-scan seeding (C4 seeding contract) fills the transient AsSet from the live world; enumerated immediately, snapshot per-enumeration |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | handled | live-scan seeding at construction (C4), then publication-driven; mid-game transient query returns the full grid set |
| World-singleton store Set/Get/Has/Remove | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | handled | carrier invisibility extended to component-pool surface (premise amended + C4 leg); no singleton type is an entity component today — pinned anyway |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | handled | pool iteration never observes carrier-held singleton instances (premise amended + C4 test) |
| Facade ISystem<T>/IGameScreen contract | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | handled | AComponentSystem in wave-1 facade surface (issue §4); TransformCommitSystem retypes unchanged |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | handled | AComponentSystem named in wave-1 surface; ref-T Update signature preserved |
| IsAlive/Entity.Null handle semantics | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | handled | C13 extended: version-stamped Equals/GetHashCode/== incl. default sentinel — dead key finds/removes its own entry, never equals recycled slot's occupant (contract + premise) |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | handled | version in equality+hash (C13 extended); post-mortem _quiet removal (TileGridBakeSystem.cs:186,196) and cross-frame maps safe |
| Iteration order unspecified (H4) | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | handled | wave-2 repo-wide first-match-and-break census (contract added): each site gets deterministic rule or single-instance assert |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | handled | census covers Examples+Demos, not only camera sites (contract added) |
| Facade ISystem<T>/IGameScreen contract | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | handled | facade EntitySystem<T> exposes virtual PreUpdate/PostUpdate + Dispose override matching AEntitySetSystem template (contract amended); listed overriders retype unchanged |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | handled | template hooks named in wave-1 surface (contract amended); YSortSystem PostUpdate child pass depends on it |
| IsAlive/Entity.Null handle semantics | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | handled | restated: list is a SAMPLE, never a closure — census non-exhaustive by design; C13 type-level Equals/GetHashCode/== is the sole seam; undo/dialogue/bake columns add the named spot-tests (contract restated) |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | handled | enumerated-list-as-proof dropped; type-level guarantee + spot-tests (TileGridBake :117/:125, undo subgraphs, OptionEntities) are the evidence (contract restated) |
| Facade message bus typed + [Subscribe] | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | handled | C4 bus contract added: nested Publish dispatches synchronously re-entrant; handler exceptions propagate unwrapped (PrefabExpansionTests.cs:190 stays green); boot e2e (C15 Level_0/Blender_Level) exercises the chain |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | handled | synchronous re-entrant dispatch + exception transparency pinned (C4 contract added); a queueing/locking/wrapping bus fails the named tests |
| Snapshot iteration EntitySystem/EntityQuery | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | handled | mass CreateEntity/Set inside nested dispatch = C3 parser-shape proof extended to nested-bus depth; EnsureCameraEntity covered by boot e2e |
| Facade message bus typed + [Subscribe] | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | handled | C4 no-double-registration test (contract added): handler reachable by both forms dispatches once; facade never auto-scans unrequested instances; 6-file census rides wave-1 sweep |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | handled | decorative [Subscribe] beside ctor typed-subscribe = one dispatch (C4 pin); sweep must not normalize to Subscribe(this) |
| Wave-3 chunk conversions preserve publication-cached predicate membership | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | C17 amended: chunked override FORBIDDEN for structural mutators unless buffer-then-mutate; CullingSystem/TextPrepSystem excluded or buffered — D2 snapshot shield never silently dropped |
| Snapshot iteration EntitySystem/EntityQuery | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | D2 snapshot shields waves 1-2; wave 3 gated by C17 buffer-then-mutate/exclusion rule + structural test through converted path (contract added) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | H2 archetype-move-mid-chunk-walk risk closed by C17 gate; skip/double-visit test through converted path |
| Wave-3 chunk conversions preserve publication-cached predicate membership | prep systems mid-loop Set | handled | C17 amended: TextPrepSystem conversion KEEPS the per-frame Set publication (TextPrepSystem.cs:75) feeding BuildDrawSet per-target membership; retarget test through converted path — ref-write-only conversion fails it |
| Wave-3 chunk conversions preserve publication-cached predicate membership | CullingSystem mid-update structural add/remove | handled | C17 amended: Culling conversion buffers-then-mutates or stays on the D2 snapshot path; structural-safety test through converted path |
| Iteration order unspecified (H4) | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | handled | id-sort claim holds only AFTER stamping — mint leg (:269-274) reopened into its own column; this cell covers ordered re-save + one-camera refusal; save-twice valid for re-saves only |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | handled | save-twice byte-identity pins re-save determinism only; first-stamp determinism carried by the new AssignStableIds column + first-stamp test (contract added) |
| Snapshot iteration EntitySystem/EntityQuery | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | handled | transient filtered AsSets seed by construction-time live scan (C4 seeding contract); Entity-keyed maps covered by C13 type-level equality |
| IsAlive/Entity.Null handle semantics | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | handled | C4 dispose-synchrony pin (contract added): IsAlive false before Dispose returns — orphan poll sees deaths same frame; cascade disposal deterministic |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | handled | synchronous IsAlive flip pinned; DisposeOrphans same-frame poll test (contract added) |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | handled | synchronous destroy pinned: IsAlive false + membership dropped on Dispose return; double-Dispose of dead handle silent no-op (contract added) |
| Snapshot iteration EntitySystem/EntityQuery | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | handled | all 14 verified AsSet()-var Counts (debugMeshes/proxies/selected); rewritten to snapshot-enumeration counts wave 1; Count stays NotSupported (contract added) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | handled | wave-1 rewrite list extended to these 14; history.Count list asserts correctly excluded (plain lists, not EntitySets); grep re-run gates wave-1 close (contract added) |
| Snapshot iteration EntitySystem/EntityQuery | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | handled | wave 1 rewrites :65 to a snapshot-enumeration presence check (any-member, no Count); C15 Blender_Level/.mdscene boot e2e exercises the path both waves (contract added) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | handled | engine-side Count hit found by widened base (tests->all git-tracked .cs); Count-NotSupported must never reach .mdscene load — presence check rewritten wave 1 (contract added) |
| Iteration order unspecified (H4) | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | handled | mint pinned: roots deterministically ordered before first-stamp (facade snapshot order or explicit key); first-stamp-order test on an UNstamped scene — save-twice/round-trip blind to this leg (contract added) |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | handled | the only H4-derived value persisted into committed files (doc admits flat-enumeration mint); deterministic mint key + unstamped-scene first-save byte test (contract added) |
| IsAlive/Entity.Null handle semantics | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | handled | holds DISPOSED handles by design (:25 doc; cleared :50; redo re-creates fresh ids); C13 version-stamped equality/IsAlive keeps them dead across recycling; undo/redo recycled-id spot-test (contract added) |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | handled | dead-by-design handles never alias a recycled occupant; C13 type-level fix + undo/redo-across-recycling spot-test (contract added) |
| IsAlive/Entity.Null handle semantics | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | handled | C13 type-level equality/IsAlive covers component-held OptionEntities and pooled chrome/editor lists; OptionEntities post-close liveness spot-test (contract added) |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | handled | census non-exhaustive by design — protection is the type-level version stamp, spot-tests name the shapes (contract added) |
| IsAlive/Entity.Null handle semantics | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | handled | C13 version-stamped hash/equality: dead-grid sweep removes its own keys from _bakeNow/_quiet/_streams, never a recycled occupant's; fields verified :117-129; spot-test (contract added) |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | handled | keyed removals after grid dispose covered by the C13 recycled-id equality test; type-level guarantee, no per-site edits (contract added) |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | dispose sweep, no Set |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count-rewrite col |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | dispose path |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | reads only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | teardown path |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | query build, no Set |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | ref writes, no Set |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | handled | spawn path mass Set on fresh entities = add leg (D4); C4 Set contract + boot e2e |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | read/serialize path |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | dispose path |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | C17 buffer-then-mutate rule keeps Has-guarded Set safe on converted paths |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check col |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count-rewrite col |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint-order col (H4 row covers) |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Facade Set = add-or-update (Arch splits Set/Add; 1826 sites must never see update-only Set) | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | handled | mass dispose fires per-entity Removed pre-destroy (C4); carrier invisible |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count-rewrite col |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads fire nothing |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | reads only |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | handled | leaf Dispose unhooks subscriptions (C4 cascade test) |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | seeding scan fires nothing |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | pool reads |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | handled | spawn Sets raise Added synchronously inside nested dispatch (C3 parser proof) |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | read path fires nothing |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | handled | orphan Dispose cascade fires synchronously (C4) |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | C17: converted mutators keep facade Set/Remove publication — events still fire |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count-rewrite col |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Facade-fired reactive events Added/Changed(old,new)/Removed (Arch EVENTS compiled out; facade raises them itself) | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | verb unused |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | dispose path |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | verb unused |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | teardown path |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | verb unused |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | ref writes, no publish |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | verb unused |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | read path |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | verb unused |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | sites use Set |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| NotifyChanged publication verb (fires Changed with old==new; also re-runs predicate membership) | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | unfiltered, no predicate |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | presence-only set |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | unfiltered |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | no predicate |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | membership dies with world |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | filter-only AsSet |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | pool, no predicate |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | no predicate |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | no predicate |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | filter-only |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | no predicate |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | C17 forbids live per-element predicate reads on converted paths; C11 negatives |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Publication-driven value-predicate query membership (predicate runs only on Set/NotifyChanged, cached) | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | handled | cascade fires outside loop; disposed member skipped (C4) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | handled | unfiltered enumeration snapshots per-enumeration; carrier excluded (D3/C4) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | H4 col |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | no iteration at teardown |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | separate pool surface (issue §4) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | H4 col |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | handled | hooks wrap the same snapshot loop (contract amended) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | handled | mass Create/Set inside nested dispatch = C3 parser proof |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | handled | transient filtered AsSets seed by construction live scan (C4) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | handled | buffered walk + synchronous IsAlive flip (C4) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | handled | mint runs over deterministically-ordered facade snapshot (contract added) |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Snapshot iteration in EntitySystem<T>/EntityQuery (Set-new/Remove/Dispose/Create mid-loop tolerated) | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | typed Subscribe<T> kept IDisposable | N/A | bus col |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | entity query |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | handled | singleton Set/Remove inside nested dispatch pinned (C3/C4 reentrancy + singleton tests) |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | entity sweep |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | entity-level |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | entity query |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| World-singleton component store: Set/Get/Has/Remove + notifications; Set-when-present fires Changed not Added | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | typed Subscribe<T> kept IDisposable | handled | M3: 19 typed sites; bus returns IDisposable (CameraFollowSystem.cs:61) |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | component events |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | per-world bus dies with world; no teardown dispatch (C12/C4) |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | no bus use |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Facade message bus: typed Subscribe<T> + [Subscribe] attribute scan, per-world, AOT-safe | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | not a system contract |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | event path |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | read util |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | blanket retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | pipeline disposed before world; readers detached first (C4 cascade + event-silence) |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | not a system |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | blanket retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | bus col |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | not a system |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | blanket retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | chunked override is opt-in on facade EntitySystem (C17); ISystem contract unchanged |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | blanket retype |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Facade ISystem<T> incl. IsEnabled; IGameScreen contract moves to facade types | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | typed Subscribe<T> kept IDisposable | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | runner outlives worlds |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | handled | runner-accepting EntitySystem keeps template hooks; degree==1 assert (contract) |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | no runner |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Facade IParallelRunner + sequential ParallelSystem<T> (all hosts use degree 1) | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | typed Subscribe<T> kept IDisposable | N/A | bus col |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | handled | IsAlive false on Dispose return — sweep-safe (C4 dispose synchrony) |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | handled | double-Dispose of dead handle silent no-op (C4) |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | live enumeration |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | live entities |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | GAP | same H9 world-slot reuse hole as the screen-teardown cell: stale Entity from a disposed world can read alive after Arch reuses the world slot — C13 must add a world-version stamp; test over 10-screen churn |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | system dispose, no handles |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | fresh live scan |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | live pool |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | live entities |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | fresh entities |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | live sweep |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | C17 col |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| IsAlive/Entity.Null version-checked handle semantics over Arch recycled ids | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | typed Subscribe<T> kept IDisposable | N/A | bus order facade-owned |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | handled | sweep disposes ALL members — outcome order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | counts order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | per-entity |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | listing cosmetic |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | teardown order-free (event-silent) |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | handled | cascade order explicit reverse-registration, not backend order (C4) |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | all invalidated, order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | per-element commit order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | hook order explicit |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | spawn order data-driven |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | poll order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | C17 buffer rule — chunk order never observable through mutators |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | counts order-free |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Iteration order unspecified (H4): sparse-set insertion-ish -> archetype/chunk order | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | dispose path |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | dispose path |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | reads only |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | no Set at teardown |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | no Set |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | ref writes |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | fresh adds = Added leg |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | reads only |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | dispose path |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | TextPrep per-frame Set-on-present kept under conversion (C17) |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | first-stamp = add leg; mint col |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Mutator row — facade Set on already-present component: overwrite fires Changed(old,new), never Added | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | dispose path |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | dispose path |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | reads only |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | no round trip at teardown |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | no round trip |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | ref writes |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | fresh adds only |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | reads only |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | dispose path |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | Culling remove/re-add via buffered path keeps Removed→Added order (C17) |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Mutator row — Remove-then-Set round trip: Removed then Added fire; predicate membership drops and re-evals | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | entity sweep |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | teardown event-silent; no singleton Removed fires (C4) |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | entity query |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | LDtk rows cover |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | entity sweep |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | entity-level |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | entity query |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Mutator row — world-singleton Remove-when-absent / re-Set after Remove: Removed->Added sequence preserved | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | GravitySystem predicate set | N/A | no dispose at site |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | MasterRenderSystem.BuildDrawSet | N/A | no dispose |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | MasterRenderSystem stable draw sort | N/A | no dispose |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | TransformCollisionDetection reactive add (M10) | N/A | add path |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | Bake systems old-value diffing | N/A | IsAlive-polled, no Removed sub |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | LDtkLevelLoadSystem world Set/Remove | N/A | world-level |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | EditorTransport restart | handled | restart sweep = mass entity.Dispose; C4 cascade + EditorTransportTests re-run |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | HierarchySystem managed singleton | N/A | world-level |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | [Subscribe] hierarchy-walk registration | N/A | bus col |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | [Subscribe] + Subscribe(this) fleet | N/A | bus col |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | CullingSystem mid-update structural add/remove | N/A | no dispose in culling |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | prep systems mid-loop Set | N/A | no dispose |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | manual-iteration mutators | handled | mid-loop Dispose fires cascade synchronously; snapshot skips dead member (C4/D2) |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | NotifyChanged publication fleet | N/A | different verb |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | EntityComponentReflection MethodInfo caches | N/A | no dispose |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | ReadAllComponents consumers | N/A | reads only |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | ScreenController.Runner | N/A | runner surface |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no entity dispose |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | PointerReplaySystem persistent sets | N/A | no dispose |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | packaging + manifests | N/A | packaging |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | ProcessWideState registry | N/A | no static |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | seeded test-order shuffle | N/A | harness |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | guard-test model | N/A | guard col |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | value-predicate premise text | N/A | premise not about dispose |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | YSortSystem/DebugInspector hierarchy reads | N/A | reads only |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | reads only |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | cascade is entity.Dispose-only; world.Dispose bypasses it (C4 event-silence) |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | system dispose, not entity |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | no dispose |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | pool reads |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | handled | dead handle removes own keyed entry after cascade (C13 equality) |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | create path |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | no dispose in save |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | Set/Remove sites, no dispose |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | handled | subgraph delete disposes entities; cascade fires then handles stay dead (C4/C13) |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Mutator: entity.Dispose fires ComponentRemoved per present component, synchronously, pre-teardown value readable | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | GravitySystem predicate set | N/A | query seeding row covers |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | MasterRenderSystem.BuildDrawSet | N/A | query seeding row covers |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | MasterRenderSystem stable draw sort | N/A | no subscription |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | AudioSystem Changed handler | N/A | ctor subscribe precedes audio entities |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | Bake systems old-value diffing | handled | editor recomposition subscribes over live world; wave-0 measures replay leg (C4) |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | LDtkLevelLoadSystem world Set/Remove | N/A | bus messages, not comp events |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | EditorTransport restart | N/A | no late subscribe |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | HierarchySystem managed singleton | N/A | manual Has+Get, no replay reliance |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | [Subscribe] hierarchy-walk registration | N/A | bus col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | [Subscribe] + Subscribe(this) fleet | N/A | bus col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | CullingSystem mid-update structural add/remove | N/A | no reactive subscribe |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | prep systems mid-loop Set | N/A | no reactive subscribe |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | GameOverSystem create+dispose mid-iteration | N/A | no reactive subscribe |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | manual-iteration mutators | N/A | no reactive subscribe |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | NotifyChanged publication fleet | N/A | publication, not subscription |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | EntityComponentReflection MethodInfo caches | N/A | no subscription |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | ReadAllComponents consumers | N/A | reads only |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | IGameScreen ISystem<GameState> contract | N/A | type surface |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | ScreenController.Runner | N/A | runner surface |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition; M10 recomposition cell covers |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | no subscription |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | screen-teardown world.Dispose | N/A | teardown path |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | PointerReplaySystem persistent sets | N/A | seeding row covers |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | packaging + manifests | N/A | packaging |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | ProcessWideState registry | N/A | no static |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | seeded test-order shuffle | N/A | harness |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | guard-test model | N/A | guard col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | value-predicate premise text | N/A | seeding row covers |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | YSortSystem/DebugInspector hierarchy reads | N/A | reads only |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | no subscribe |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | removal, not replay |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | reads only |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | teardown path |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | seeding row covers |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | no subscription |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | bus col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | seeding row covers |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | polls, not subscribes |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | no subscription |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Mutator: subscribe-after-state-exists replay semantics (entity-level replays, singleton does not) | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Wave-3 chunk conversions preserve publication-cached predicate membership | MasterRenderSystem stable draw sort | handled | converted feed keeps stable sort on copied buffer; ties test (C17; rendering :791) |
| Wave-3 chunk conversions preserve publication-cached predicate membership | AudioSystem Changed handler | N/A | not a wave-3 target |
| Wave-3 chunk conversions preserve publication-cached predicate membership | TransformCollisionDetection reactive add (M10) | handled | BuildEntries conversion keeps reactive tag handler; C17 test through converted path |
| Wave-3 chunk conversions preserve publication-cached predicate membership | Bake systems old-value diffing | N/A | not a wave-3 target |
| Wave-3 chunk conversions preserve publication-cached predicate membership | LDtk world-singleton subscribers + late-join replay | N/A | not a wave-3 target |
| Wave-3 chunk conversions preserve publication-cached predicate membership | LDtkLevelLoadSystem world Set/Remove | N/A | not a wave-3 target |
| Wave-3 chunk conversions preserve publication-cached predicate membership | EditorTransport restart | N/A | not a wave-3 target |
| Wave-3 chunk conversions preserve publication-cached predicate membership | HierarchySystem managed singleton | N/A | not a wave-3 target |
| Wave-3 chunk conversions preserve publication-cached predicate membership | [Subscribe] hierarchy-walk registration | N/A | bus col |
| Wave-3 chunk conversions preserve publication-cached predicate membership | [Subscribe] + Subscribe(this) fleet | N/A | bus col |
| Wave-3 chunk conversions preserve publication-cached predicate membership | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Wave-3 chunk conversions preserve publication-cached predicate membership | GameOverSystem create+dispose mid-iteration | N/A | Examples, not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | manual-iteration mutators | N/A | not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | NotifyChanged publication fleet | handled | C17: converted paths re-tested with C11 no-move-without-publish negatives |
| Wave-3 chunk conversions preserve publication-cached predicate membership | EntityComponentReflection MethodInfo caches | N/A | not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | ReadAllComponents consumers | N/A | not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | IGameScreen ISystem<GameState> contract | N/A | opt-in override; contract unchanged |
| Wave-3 chunk conversions preserve publication-cached predicate membership | ScreenController.Runner | N/A | runner surface |
| Wave-3 chunk conversions preserve publication-cached predicate membership | EditorPipelineRegistrar ParallelSystem<T> | N/A | editor not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | wrapper |
| Wave-3 chunk conversions preserve publication-cached predicate membership | screen-teardown world.Dispose | N/A | teardown path |
| Wave-3 chunk conversions preserve publication-cached predicate membership | PointerReplaySystem persistent sets | N/A | not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | packaging + manifests | N/A | packaging |
| Wave-3 chunk conversions preserve publication-cached predicate membership | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Wave-3 chunk conversions preserve publication-cached predicate membership | ProcessWideState registry | N/A | no static |
| Wave-3 chunk conversions preserve publication-cached predicate membership | seeded test-order shuffle | N/A | harness |
| Wave-3 chunk conversions preserve publication-cached predicate membership | guard-test model | handled | chunked override lives inside facade EntitySystem; C14 lint unchanged |
| Wave-3 chunk conversions preserve publication-cached predicate membership | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Wave-3 chunk conversions preserve publication-cached predicate membership | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Wave-3 chunk conversions preserve publication-cached predicate membership | value-predicate premise text | handled | premise :692 rewrite names converted-path guarantee (C22/C17) |
| Wave-3 chunk conversions preserve publication-cached predicate membership | YSortSystem/DebugInspector hierarchy reads | N/A | reads only |
| Wave-3 chunk conversions preserve publication-cached predicate membership | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Wave-3 chunk conversions preserve publication-cached predicate membership | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | editor, not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Wave-3 chunk conversions preserve publication-cached predicate membership | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Wave-3 chunk conversions preserve publication-cached predicate membership | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | camera not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | MonoDreams.Benchmarks dual-backend project | handled | C16/C17 bench gate: hold-or-improve, regressions reverted or justified |
| Wave-3 chunk conversions preserve publication-cached predicate membership | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | teardown path |
| Wave-3 chunk conversions preserve publication-cached predicate membership | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Wave-3 chunk conversions preserve publication-cached predicate membership | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | bake not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Wave-3 chunk conversions preserve publication-cached predicate membership | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | Examples not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | handled | chunked override = opt-in hook on EntitySystem; PreUpdate/PostUpdate preserved (C17) |
| Wave-3 chunk conversions preserve publication-cached predicate membership | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Wave-3 chunk conversions preserve publication-cached predicate membership | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | load path not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Wave-3 chunk conversions preserve publication-cached predicate membership | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | save path not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | boot path not converted |
| Wave-3 chunk conversions preserve publication-cached predicate membership | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Wave-3 chunk conversions preserve publication-cached predicate membership | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Wave-3 chunk conversions preserve publication-cached predicate membership | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Wave-3 chunk conversions preserve publication-cached predicate membership | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Wave-3 chunk conversions preserve publication-cached predicate membership | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | GravitySystem predicate set | N/A | no world.Dispose at site |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | MasterRenderSystem.BuildDrawSet | N/A | no world.Dispose |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | MasterRenderSystem stable draw sort | N/A | no world.Dispose |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | AudioSystem Changed handler | handled | AudioSystem.cs:133-137 observes nothing at teardown (C4 event-silence) |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | TransformCollisionDetection reactive add (M10) | handled | discarded subs (:74-75) never fire at teardown (C4) |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | Bake systems old-value diffing | N/A | no teardown reader |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | LDtk world-singleton subscribers + late-join replay | handled | LDtkTileParserSystem.cs:42 singleton sub silent at teardown (C4) |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | LDtkLevelLoadSystem world Set/Remove | N/A | explicit Remove path, not teardown |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | EditorTransport restart | N/A | restart != world.Dispose |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | HierarchySystem managed singleton | N/A | dies silently with world |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | [Subscribe] hierarchy-walk registration | N/A | bus col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | [Subscribe] + Subscribe(this) fleet | N/A | bus col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | CullingSystem mid-update structural add/remove | N/A | no world.Dispose |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | prep systems mid-loop Set | N/A | no world.Dispose |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | GameOverSystem create+dispose mid-iteration | N/A | entity-level dispose |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | manual-iteration mutators | N/A | entity-level |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | NotifyChanged publication fleet | N/A | no world.Dispose |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | EntityComponentReflection MethodInfo caches | N/A | no world.Dispose |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | ReadAllComponents consumers | N/A | reads only |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | IGameScreen ISystem<GameState> contract | handled | screens dispose pipeline BEFORE world — readers detached (C4) |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | ScreenController.Runner | N/A | runner outlives worlds |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | EditorPipelineRegistrar ParallelSystem<T> | N/A | composition |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | GatedSystem IsEnabled + ISuspendableSystem cast | N/A | wrapper |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | PointerReplaySystem persistent sets | N/A | dies with world |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | packaging + manifests | N/A | packaging |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | CLI tests asserting literal DefaultEcs | N/A | packaging tests |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | ProcessWideState registry | N/A | hygiene rows cover |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | seeded test-order shuffle | N/A | harness |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | guard-test model | N/A | guard col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | DefaultEcs.Threading heads + fully-qualified use | N/A | runner sites |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | DrawPrepSystemBase (dead) | N/A | deleted (C6) |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | value-predicate premise text | N/A | premise not about teardown |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | YSortSystem/DebugInspector hierarchy reads | N/A | reads only |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | explicit sweep, not teardown |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | handled | no double Removed: teardown silent; cascade fires only on entity.Dispose (C4) |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | reads only |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | handled | composite Dispose (audio stop, GPU free) runs BEFORE silent world.Dispose (C4) |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | no world.Dispose |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | pool reads |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | load path, not teardown |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | save path |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | entity-level dispose |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | no world.Dispose |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | boot path |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Mutator: world.Dispose is event-silent bulk teardown (no per-component Removed, no singleton Removed; cascade is entity.Dispose-only) | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | dispose sweep, no ReadAll |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | no ReadAll |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | no ReadAll |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | no ReadAll at teardown |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | no ReadAll |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | pool surface, not ReadAll |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | no ReadAll |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | no ReadAll |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | not an M8 consumer (3 sites + introspector only) |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | no ReadAll |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | no ReadAll |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | Count col |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| ReadAllComponents/IComponentReader port (registry/reflection-backed, AOT-safe) | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | typed Subscribe<T> kept IDisposable | N/A | bus col |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | same-world ops |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | entity-level |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | per-world reads |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | per-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | MonoDreams.Benchmarks dual-backend project | N/A | bench runs own process; C1 covers |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | system dispose, not world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | same-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | per-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | per-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | same-world ops |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | same-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | entity-level |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | entity-level |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | same-world |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| EcsWorld.Create()/Dispose lifecycle over Arch's static World.Worlds registry (ProcessWideState-tracked) | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | typed Subscribe<T> kept IDisposable | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | repo-wide lint |
| Guard ratchet: EcsBoundaryLintTests — no DefaultEcs (wave1+), no raw Arch (wave2+) outside facade | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | repo-wide lint |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | typed Subscribe<T> kept IDisposable | N/A | code, not packaging |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | code |
| Packaging: Arch replaces DefaultEcs in csproj/lockfile/module.json/CLI-emitted manifests | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | code |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | typed Subscribe<T> kept IDisposable | N/A | bus col |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | dispose path |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | teardown path |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | ref writes |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | read path |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | verb unused |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | sites use Set |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Mutator row — NotifyChanged on absent component: today throws in DefaultEcs; facade must define/preserve | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Facade-fired events Added/Changed(old,new)/Removed | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Facade-fired events Added/Changed(old,new)/Removed | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | handled | mass dispose fires per-entity Removed pre-destroy (C4); carrier invisible |
| Facade-fired events Added/Changed(old,new)/Removed | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count-rewrite col |
| Facade-fired events Added/Changed(old,new)/Removed | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Facade-fired events Added/Changed(old,new)/Removed | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads fire nothing |
| Facade-fired events Added/Changed(old,new)/Removed | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | reads only |
| Facade-fired events Added/Changed(old,new)/Removed | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Facade-fired events Added/Changed(old,new)/Removed | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | handled | leaf Dispose unhooks subscriptions (C4 cascade test) |
| Facade-fired events Added/Changed(old,new)/Removed | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Facade-fired events Added/Changed(old,new)/Removed | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | seeding scan fires nothing |
| Facade-fired events Added/Changed(old,new)/Removed | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | pool reads |
| Facade-fired events Added/Changed(old,new)/Removed | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Facade-fired events Added/Changed(old,new)/Removed | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Facade-fired events Added/Changed(old,new)/Removed | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Facade-fired events Added/Changed(old,new)/Removed | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Facade-fired events Added/Changed(old,new)/Removed | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | handled | spawn Sets raise Added synchronously inside nested dispatch (C3 parser proof) |
| Facade-fired events Added/Changed(old,new)/Removed | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Facade-fired events Added/Changed(old,new)/Removed | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | read path fires nothing |
| Facade-fired events Added/Changed(old,new)/Removed | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | handled | orphan Dispose cascade fires synchronously (C4) |
| Facade-fired events Added/Changed(old,new)/Removed | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | C17: converted mutators keep facade Set/Remove publication — events still fire |
| Facade-fired events Added/Changed(old,new)/Removed | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Facade-fired events Added/Changed(old,new)/Removed | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count-rewrite col |
| Facade-fired events Added/Changed(old,new)/Removed | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Facade-fired events Added/Changed(old,new)/Removed | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Facade-fired events Added/Changed(old,new)/Removed | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Facade-fired events Added/Changed(old,new)/Removed | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Publication-driven predicate membership | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Publication-driven predicate membership | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | unfiltered, no predicate |
| Publication-driven predicate membership | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Publication-driven predicate membership | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | presence-only set |
| Publication-driven predicate membership | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Publication-driven predicate membership | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | unfiltered |
| Publication-driven predicate membership | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | no predicate |
| Publication-driven predicate membership | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Publication-driven predicate membership | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | membership dies with world |
| Publication-driven predicate membership | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Publication-driven predicate membership | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Publication-driven predicate membership | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | filter-only AsSet |
| Publication-driven predicate membership | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | pool, no predicate |
| Publication-driven predicate membership | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Publication-driven predicate membership | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | no predicate |
| Publication-driven predicate membership | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Publication-driven predicate membership | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Publication-driven predicate membership | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | no predicate |
| Publication-driven predicate membership | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Publication-driven predicate membership | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | filter-only |
| Publication-driven predicate membership | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | no predicate |
| Publication-driven predicate membership | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | C17 forbids live per-element predicate reads on converted paths; C11 negatives |
| Publication-driven predicate membership | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Publication-driven predicate membership | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Publication-driven predicate membership | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Publication-driven predicate membership | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Publication-driven predicate membership | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Publication-driven predicate membership | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Snapshot iteration EntitySystem/EntityQuery | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Snapshot iteration EntitySystem/EntityQuery | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | handled | cascade fires outside loop; disposed member skipped (C4) |
| Snapshot iteration EntitySystem/EntityQuery | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Snapshot iteration EntitySystem/EntityQuery | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | handled | unfiltered enumeration snapshots per-enumeration; carrier excluded (D3/C4) |
| Snapshot iteration EntitySystem/EntityQuery | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | H4 col |
| Snapshot iteration EntitySystem/EntityQuery | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Snapshot iteration EntitySystem/EntityQuery | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | no iteration at teardown |
| Snapshot iteration EntitySystem/EntityQuery | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Snapshot iteration EntitySystem/EntityQuery | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Snapshot iteration EntitySystem/EntityQuery | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | separate pool surface (issue §4) |
| Snapshot iteration EntitySystem/EntityQuery | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Snapshot iteration EntitySystem/EntityQuery | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | H4 col |
| Snapshot iteration EntitySystem/EntityQuery | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | handled | hooks wrap the same snapshot loop (contract amended) |
| Snapshot iteration EntitySystem/EntityQuery | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Snapshot iteration EntitySystem/EntityQuery | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Snapshot iteration EntitySystem/EntityQuery | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | handled | buffered walk + synchronous IsAlive flip (C4) |
| Snapshot iteration EntitySystem/EntityQuery | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | handled | mint runs over deterministically-ordered facade snapshot (contract added) |
| Snapshot iteration EntitySystem/EntityQuery | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Snapshot iteration EntitySystem/EntityQuery | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Snapshot iteration EntitySystem/EntityQuery | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| World-singleton store Set/Get/Has/Remove | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| World-singleton store Set/Get/Has/Remove | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| World-singleton store Set/Get/Has/Remove | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| World-singleton store Set/Get/Has/Remove | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| World-singleton store Set/Get/Has/Remove | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| World-singleton store Set/Get/Has/Remove | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| World-singleton store Set/Get/Has/Remove | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | entity query |
| World-singleton store Set/Get/Has/Remove | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| World-singleton store Set/Get/Has/Remove | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| World-singleton store Set/Get/Has/Remove | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| World-singleton store Set/Get/Has/Remove | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | handled | singleton Set/Remove inside nested dispatch pinned (C3/C4 reentrancy + singleton tests) |
| World-singleton store Set/Get/Has/Remove | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| World-singleton store Set/Get/Has/Remove | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | entity sweep |
| World-singleton store Set/Get/Has/Remove | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | entity-level |
| World-singleton store Set/Get/Has/Remove | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | entity query |
| World-singleton store Set/Get/Has/Remove | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| World-singleton store Set/Get/Has/Remove | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| World-singleton store Set/Get/Has/Remove | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| World-singleton store Set/Get/Has/Remove | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| World-singleton store Set/Get/Has/Remove | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Facade message bus typed + [Subscribe] | typed Subscribe<T> with kept IDisposable | handled | M3: 19 typed sites; bus returns IDisposable (CameraFollowSystem.cs:61) |
| Facade message bus typed + [Subscribe] | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | no bus use |
| Facade message bus typed + [Subscribe] | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Facade message bus typed + [Subscribe] | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | component events |
| Facade message bus typed + [Subscribe] | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Facade message bus typed + [Subscribe] | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | no bus use |
| Facade message bus typed + [Subscribe] | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | no bus use |
| Facade message bus typed + [Subscribe] | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Facade message bus typed + [Subscribe] | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | per-world bus dies with world; no teardown dispatch (C12/C4) |
| Facade message bus typed + [Subscribe] | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Facade message bus typed + [Subscribe] | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Facade message bus typed + [Subscribe] | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | no bus use |
| Facade message bus typed + [Subscribe] | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | no bus use |
| Facade message bus typed + [Subscribe] | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Facade message bus typed + [Subscribe] | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | no bus use |
| Facade message bus typed + [Subscribe] | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Facade message bus typed + [Subscribe] | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Facade message bus typed + [Subscribe] | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | no bus use |
| Facade message bus typed + [Subscribe] | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | no bus use |
| Facade message bus typed + [Subscribe] | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | no bus use |
| Facade message bus typed + [Subscribe] | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | no bus use |
| Facade message bus typed + [Subscribe] | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Facade message bus typed + [Subscribe] | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Facade message bus typed + [Subscribe] | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Facade message bus typed + [Subscribe] | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Facade message bus typed + [Subscribe] | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Facade ISystem<T>/IGameScreen contract | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Facade ISystem<T>/IGameScreen contract | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | not a system contract |
| Facade ISystem<T>/IGameScreen contract | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Facade ISystem<T>/IGameScreen contract | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | event path |
| Facade ISystem<T>/IGameScreen contract | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Facade ISystem<T>/IGameScreen contract | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | read util |
| Facade ISystem<T>/IGameScreen contract | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | blanket retype |
| Facade ISystem<T>/IGameScreen contract | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Facade ISystem<T>/IGameScreen contract | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | pipeline disposed before world; readers detached first (C4 cascade + event-silence) |
| Facade ISystem<T>/IGameScreen contract | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Facade ISystem<T>/IGameScreen contract | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | not a system |
| Facade ISystem<T>/IGameScreen contract | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Facade ISystem<T>/IGameScreen contract | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | blanket retype |
| Facade ISystem<T>/IGameScreen contract | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Facade ISystem<T>/IGameScreen contract | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | bus col |
| Facade ISystem<T>/IGameScreen contract | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Facade ISystem<T>/IGameScreen contract | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | not a system |
| Facade ISystem<T>/IGameScreen contract | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | blanket retype |
| Facade ISystem<T>/IGameScreen contract | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | chunked override is opt-in on facade EntitySystem (C17); ISystem contract unchanged |
| Facade ISystem<T>/IGameScreen contract | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | blanket retype |
| Facade ISystem<T>/IGameScreen contract | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Facade ISystem<T>/IGameScreen contract | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Facade ISystem<T>/IGameScreen contract | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Facade ISystem<T>/IGameScreen contract | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Facade ISystem<T>/IGameScreen contract | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| IParallelRunner + sequential ParallelSystem<T> | typed Subscribe<T> with kept IDisposable | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| IParallelRunner + sequential ParallelSystem<T> | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| IParallelRunner + sequential ParallelSystem<T> | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | runner outlives worlds |
| IParallelRunner + sequential ParallelSystem<T> | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| IParallelRunner + sequential ParallelSystem<T> | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| IParallelRunner + sequential ParallelSystem<T> | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | handled | runner-accepting EntitySystem keeps template hooks; degree==1 assert (contract) |
| IParallelRunner + sequential ParallelSystem<T> | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| IParallelRunner + sequential ParallelSystem<T> | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| IParallelRunner + sequential ParallelSystem<T> | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | no runner |
| IParallelRunner + sequential ParallelSystem<T> | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| IParallelRunner + sequential ParallelSystem<T> | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| IParallelRunner + sequential ParallelSystem<T> | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| IParallelRunner + sequential ParallelSystem<T> | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| IParallelRunner + sequential ParallelSystem<T> | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| IsAlive/Entity.Null handle semantics | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| IsAlive/Entity.Null handle semantics | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | handled | IsAlive false on Dispose return — sweep-safe (C4 dispose synchrony) |
| IsAlive/Entity.Null handle semantics | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| IsAlive/Entity.Null handle semantics | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | handled | double-Dispose of dead handle silent no-op (C4) |
| IsAlive/Entity.Null handle semantics | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| IsAlive/Entity.Null handle semantics | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | live enumeration |
| IsAlive/Entity.Null handle semantics | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | live entities |
| IsAlive/Entity.Null handle semantics | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| IsAlive/Entity.Null handle semantics | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | GAP | H9 world-slot reuse: stale Entity from a disposed world can read alive after Arch reuses the world slot — C13 must add a world-version stamp; 10-screen-churn test |
| IsAlive/Entity.Null handle semantics | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | system dispose, no handles |
| IsAlive/Entity.Null handle semantics | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| IsAlive/Entity.Null handle semantics | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | fresh live scan |
| IsAlive/Entity.Null handle semantics | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | live pool |
| IsAlive/Entity.Null handle semantics | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | live entities |
| IsAlive/Entity.Null handle semantics | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| IsAlive/Entity.Null handle semantics | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | fresh entities |
| IsAlive/Entity.Null handle semantics | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| IsAlive/Entity.Null handle semantics | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | live sweep |
| IsAlive/Entity.Null handle semantics | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | C17 col |
| IsAlive/Entity.Null handle semantics | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| IsAlive/Entity.Null handle semantics | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| IsAlive/Entity.Null handle semantics | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| EcsWorld.Create/Dispose over Arch static registry | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| EcsWorld.Create/Dispose over Arch static registry | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | same-world ops |
| EcsWorld.Create/Dispose over Arch static registry | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| EcsWorld.Create/Dispose over Arch static registry | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | entity-level |
| EcsWorld.Create/Dispose over Arch static registry | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| EcsWorld.Create/Dispose over Arch static registry | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | per-world reads |
| EcsWorld.Create/Dispose over Arch static registry | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | per-world |
| EcsWorld.Create/Dispose over Arch static registry | MonoDreams.Benchmarks dual-backend project | N/A | bench runs own process; C1 covers |
| EcsWorld.Create/Dispose over Arch static registry | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | system dispose, not world |
| EcsWorld.Create/Dispose over Arch static registry | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| EcsWorld.Create/Dispose over Arch static registry | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | same-world |
| EcsWorld.Create/Dispose over Arch static registry | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | per-world |
| EcsWorld.Create/Dispose over Arch static registry | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| EcsWorld.Create/Dispose over Arch static registry | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | per-world |
| EcsWorld.Create/Dispose over Arch static registry | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| EcsWorld.Create/Dispose over Arch static registry | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| EcsWorld.Create/Dispose over Arch static registry | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | same-world ops |
| EcsWorld.Create/Dispose over Arch static registry | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| EcsWorld.Create/Dispose over Arch static registry | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | same-world |
| EcsWorld.Create/Dispose over Arch static registry | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | entity-level |
| EcsWorld.Create/Dispose over Arch static registry | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | entity-level |
| EcsWorld.Create/Dispose over Arch static registry | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | same-world |
| EcsWorld.Create/Dispose over Arch static registry | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| EcsWorld.Create/Dispose over Arch static registry | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| EcsWorld.Create/Dispose over Arch static registry | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| EcsWorld.Create/Dispose over Arch static registry | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| EcsWorld.Create/Dispose over Arch static registry | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Iteration order unspecified (H4) | typed Subscribe<T> with kept IDisposable | N/A | bus order facade-owned |
| Iteration order unspecified (H4) | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | handled | sweep disposes ALL members — outcome order-free |
| Iteration order unspecified (H4) | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | counts order-free |
| Iteration order unspecified (H4) | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | per-entity |
| Iteration order unspecified (H4) | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | listing cosmetic |
| Iteration order unspecified (H4) | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Iteration order unspecified (H4) | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | teardown order-free (event-silent) |
| Iteration order unspecified (H4) | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | handled | cascade order explicit reverse-registration, not backend order (C4) |
| Iteration order unspecified (H4) | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | all invalidated, order-free |
| Iteration order unspecified (H4) | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | per-element commit order-free |
| Iteration order unspecified (H4) | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Iteration order unspecified (H4) | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | hook order explicit |
| Iteration order unspecified (H4) | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Iteration order unspecified (H4) | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | spawn order data-driven |
| Iteration order unspecified (H4) | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Iteration order unspecified (H4) | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | poll order-free |
| Iteration order unspecified (H4) | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | C17 buffer rule — chunk order never observable through mutators |
| Iteration order unspecified (H4) | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence order-free |
| Iteration order unspecified (H4) | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | counts order-free |
| Iteration order unspecified (H4) | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Iteration order unspecified (H4) | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Iteration order unspecified (H4) | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Guard ratchet EcsBoundaryLintTests | typed Subscribe<T> with kept IDisposable | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | repo-wide lint |
| Guard ratchet EcsBoundaryLintTests | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | repo-wide lint |
| Packaging: Arch replaces DefaultEcs | typed Subscribe<T> with kept IDisposable | N/A | code |
| Packaging: Arch replaces DefaultEcs | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | code |
| Packaging: Arch replaces DefaultEcs | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | code |
| Packaging: Arch replaces DefaultEcs | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | code |
| Packaging: Arch replaces DefaultEcs | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | code |
| Packaging: Arch replaces DefaultEcs | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | code |
| Packaging: Arch replaces DefaultEcs | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | code |
| Packaging: Arch replaces DefaultEcs | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | code |
| Packaging: Arch replaces DefaultEcs | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | code |
| Packaging: Arch replaces DefaultEcs | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | code |
| Packaging: Arch replaces DefaultEcs | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | code |
| Packaging: Arch replaces DefaultEcs | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | code |
| Packaging: Arch replaces DefaultEcs | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | code |
| Packaging: Arch replaces DefaultEcs | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | code |
| Packaging: Arch replaces DefaultEcs | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | code |
| Packaging: Arch replaces DefaultEcs | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | code |
| Packaging: Arch replaces DefaultEcs | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | code |
| Packaging: Arch replaces DefaultEcs | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | code |
| Packaging: Arch replaces DefaultEcs | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | code |
| Packaging: Arch replaces DefaultEcs | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | code |
| Packaging: Arch replaces DefaultEcs | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | code |
| Packaging: Arch replaces DefaultEcs | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | code |
| Packaging: Arch replaces DefaultEcs | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | code |
| Packaging: Arch replaces DefaultEcs | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | code |
| Packaging: Arch replaces DefaultEcs | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | code |
| Packaging: Arch replaces DefaultEcs | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | code |
| Packaging: Arch replaces DefaultEcs | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | code |
| Mutator: Set-on-present fires Changed not Added | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Mutator: Set-on-present fires Changed not Added | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | dispose path |
| Mutator: Set-on-present fires Changed not Added | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Mutator: Set-on-present fires Changed not Added | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | dispose path |
| Mutator: Set-on-present fires Changed not Added | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Mutator: Set-on-present fires Changed not Added | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Mutator: Set-on-present fires Changed not Added | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | reads only |
| Mutator: Set-on-present fires Changed not Added | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Mutator: Set-on-present fires Changed not Added | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | no Set at teardown |
| Mutator: Set-on-present fires Changed not Added | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Mutator: Set-on-present fires Changed not Added | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Mutator: Set-on-present fires Changed not Added | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | no Set |
| Mutator: Set-on-present fires Changed not Added | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | ref writes |
| Mutator: Set-on-present fires Changed not Added | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Mutator: Set-on-present fires Changed not Added | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Mutator: Set-on-present fires Changed not Added | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Mutator: Set-on-present fires Changed not Added | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Mutator: Set-on-present fires Changed not Added | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | fresh adds = Added leg |
| Mutator: Set-on-present fires Changed not Added | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Mutator: Set-on-present fires Changed not Added | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | reads only |
| Mutator: Set-on-present fires Changed not Added | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | dispose path |
| Mutator: Set-on-present fires Changed not Added | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | TextPrep per-frame Set-on-present kept under conversion (C17) |
| Mutator: Set-on-present fires Changed not Added | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Mutator: Set-on-present fires Changed not Added | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Mutator: Set-on-present fires Changed not Added | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | first-stamp = add leg; mint col |
| Mutator: Set-on-present fires Changed not Added | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Mutator: Set-on-present fires Changed not Added | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Mutator: Set-on-present fires Changed not Added | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Mutator: Remove-then-Set round trip | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Mutator: Remove-then-Set round trip | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | dispose path |
| Mutator: Remove-then-Set round trip | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Mutator: Remove-then-Set round trip | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | dispose path |
| Mutator: Remove-then-Set round trip | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Mutator: Remove-then-Set round trip | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Mutator: Remove-then-Set round trip | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | reads only |
| Mutator: Remove-then-Set round trip | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Mutator: Remove-then-Set round trip | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | no round trip at teardown |
| Mutator: Remove-then-Set round trip | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Mutator: Remove-then-Set round trip | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Mutator: Remove-then-Set round trip | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | no round trip |
| Mutator: Remove-then-Set round trip | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | ref writes |
| Mutator: Remove-then-Set round trip | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Mutator: Remove-then-Set round trip | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Mutator: Remove-then-Set round trip | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Mutator: Remove-then-Set round trip | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Mutator: Remove-then-Set round trip | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | fresh adds only |
| Mutator: Remove-then-Set round trip | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Mutator: Remove-then-Set round trip | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | reads only |
| Mutator: Remove-then-Set round trip | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | dispose path |
| Mutator: Remove-then-Set round trip | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | handled | Culling remove/re-add via buffered path keeps Removed->Added order (C17) |
| Mutator: Remove-then-Set round trip | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Mutator: Remove-then-Set round trip | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Mutator: Remove-then-Set round trip | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Mutator: Remove-then-Set round trip | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Mutator: Remove-then-Set round trip | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Mutator: Remove-then-Set round trip | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Mutator: NotifyChanged on absent component | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Mutator: NotifyChanged on absent component | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | verb unused |
| Mutator: NotifyChanged on absent component | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Mutator: NotifyChanged on absent component | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | dispose path |
| Mutator: NotifyChanged on absent component | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Mutator: NotifyChanged on absent component | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Mutator: NotifyChanged on absent component | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | verb unused |
| Mutator: NotifyChanged on absent component | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Mutator: NotifyChanged on absent component | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | N/A | teardown path |
| Mutator: NotifyChanged on absent component | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Mutator: NotifyChanged on absent component | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Mutator: NotifyChanged on absent component | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | verb unused |
| Mutator: NotifyChanged on absent component | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | ref writes |
| Mutator: NotifyChanged on absent component | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Mutator: NotifyChanged on absent component | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | reads only |
| Mutator: NotifyChanged on absent component | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Mutator: NotifyChanged on absent component | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Mutator: NotifyChanged on absent component | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | verb unused |
| Mutator: NotifyChanged on absent component | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Mutator: NotifyChanged on absent component | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | read path |
| Mutator: NotifyChanged on absent component | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | verb unused |
| Mutator: NotifyChanged on absent component | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | sites use Set |
| Mutator: NotifyChanged on absent component | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | presence check |
| Mutator: NotifyChanged on absent component | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Mutator: NotifyChanged on absent component | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Mutator: NotifyChanged on absent component | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Mutator: NotifyChanged on absent component | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Mutator: NotifyChanged on absent component | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |
| Mutator: singleton Remove-absent / re-Set after Remove | typed Subscribe<T> with kept IDisposable | N/A | bus col |
| Mutator: singleton Remove-absent / re-Set after Remove | EditorTransport.DisposeSceneEntities unfiltered dispose sweep (EditorTransport.cs:419-429; LDtkTileParserSystem CleanupTileEntities:145-156) | N/A | entity sweep |
| Mutator: singleton Remove-absent / re-Set after Remove | ColliderActionTests EntitySet.Count asserts (ColliderActionTests.cs:194,297) | N/A | Count col |
| Mutator: singleton Remove-absent / re-Set after Remove | AudioSystem OnAudioSourceRemoved dispose-path (AudioSystem.cs:38,133-137) | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | headless demo clock (Game1.cs:119 IsFixedTimeStep=false; GameState.cs:28 wallclock dt) | N/A | clock infra |
| Mutator: singleton Remove-absent / re-Set after Remove | DebugInspector unfiltered world enumeration (DebugInspector.cs:78) | N/A | reads only |
| Mutator: singleton Remove-absent / re-Set after Remove | camera first-match picks (CameraFollowSystem.cs:70-77,84; CameraSyncSystem.cs:70) | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | MonoDreams.Benchmarks dual-backend project | N/A | bench allowlisted |
| Mutator: singleton Remove-absent / re-Set after Remove | world.Dispose bulk-teardown event contract (ScreenController.cs:84,114; SplashScreen.cs:159; LevelSelectionScreen.cs:634; InfiniteRunnerScreen.cs:612; DemoLauncherScreen.cs:356; readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs TransformCollisionDetectionSystem.cs:74-75) | handled | teardown event-silent; no singleton Removed fires (C4) |
| Mutator: singleton Remove-absent / re-Set after Remove | composite Dispose cascade (LoadLevelExampleGameScreen.cs:728-733; LevelSelectionScreen.cs:626-634; AudioSystem.cs:158-173; CullingSystem.cs:112-120; MasterRenderSystem GPU) | N/A | dispose path |
| Mutator: singleton Remove-absent / re-Set after Remove | YSortSystem child-draw depth clamp (YSortSystem.cs:84-90, minimalBias :85) | N/A | arithmetic only |
| Mutator: singleton Remove-absent / re-Set after Remove | TileGridBakeSystem.InvalidateAll transient AsSet (TileGridBakeSystem.cs:169-175) | N/A | entity query |
| Mutator: singleton Remove-absent / re-Set after Remove | AComponentSystem pool iteration (TransformCommitSystem.cs:15) | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | Entity-keyed collections + equality (TileGridBakeSystem.cs:186,196; EntityHierarchy.cs:15-16; ColliderDebugSystem.cs:53; HighlightSystem.cs:72; SceneLayerSystem.cs:39; LDtkTileParserSystem.cs:32; EditorPanelStateComponent.cs:38) | N/A | equality col (C13) |
| Mutator: singleton Remove-absent / re-Set after Remove | Examples first-match picks (RunnerSpawnerSystem.cs:56-61; InfiniteRunnerScreen.cs:331-340) | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | EntitySystem template hooks PreUpdate/PostUpdate/Dispose (YSortSystem.cs:30-36,64; CullingSystem PreUpdate; TextInputSystem; ToolbarSystem; OffScreenCleanupSystem) | N/A | type surface |
| Mutator: singleton Remove-absent / re-Set after Remove | Entity-keyed census extension (DebugInspector.cs:23,87-93; SpriteDebugSystem.cs:34; LayoutDebugSystem.cs:43; EntitySceneTree.cs:56,60,104-113; TriggerOverlaySystem.cs:63,104; BoundaryToolSystem.cs:91,275; BoundaryBakeSystem.cs:65,72; EditorPanelSystem.cs:110,114) | N/A | equality col (C13) |
| Mutator: singleton Remove-absent / re-Set after Remove | native load chain nested Publish (LevelLoadRequestSystem.cs:52 -> NativeLevelLoader.cs:101/143/180 -> SceneReaderSystem.cs:122-126 -> EntitySpawnSystem.cs:70; exception transparency PrefabExpansionTests.cs:190) | N/A | LDtk rows cover |
| Mutator: singleton Remove-absent / re-Set after Remove | mixed-marking [Subscribe]+typed-Subscribe sites (LevelLoadRequestSystem.cs:46,51; SceneReaderSystem.cs:122,125; EntitySpawnSystem.cs:40,70; RunnerCollisionHandlerSystem.cs:19,22; LDtkLevelLoadSystem.cs:43,48; LevelSelectionScreen.cs:152,194) | N/A | bus col |
| Mutator: singleton Remove-absent / re-Set after Remove | SceneWriter save-path membership sweep (SceneWriter.cs:219-266; one-camera refusal :74,:205; id-ordered saves :80,:257) | N/A | entity sweep |
| Mutator: singleton Remove-absent / re-Set after Remove | HierarchySystem.DisposeOrphans IsAlive polling (HierarchySystem.cs:43,55-83) | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | wave-3 chunked-path structural mutators (CullingSystem.cs:100,107; TextPrepSystem.cs:75) | N/A | entity-level |
| Mutator: singleton Remove-absent / re-Set after Remove | SceneCameraEnsure boot Count check (SceneCameraEnsure.cs:65 via SceneReaderSystem.cs:391) | N/A | entity query |
| Mutator: singleton Remove-absent / re-Set after Remove | widened EntitySet.Count test asserts (ColliderDebugSystemTests.cs:75,100,106,164,169,195; ProxyVertexTests.cs:78,90,96,101,121,129,134; CameraEntityEditorTests.cs:210) | N/A | Count col |
| Mutator: singleton Remove-absent / re-Set after Remove | SceneWriter AssignStableIds mint order (SceneWriter.cs:269-274,295; roots from CollectMembership :238 backend enumeration) | N/A | mint col |
| Mutator: singleton Remove-absent / re-Set after Remove | undo subgraph dead-handle caches (DeleteEntityCommand.cs:31,50; CreateEntityCommand.cs:30; CreateInstanceCommand.cs:33; EntitySubgraph.cs:22-45) | N/A | C13 col |
| Mutator: singleton Remove-absent / re-Set after Remove | component-held & pooled Entity lists (DialogueStateComponent.cs:23 OptionEntities; EditorChromeBuilder.cs:73-77; AutotileRuleEditorSystem.cs:108-115) | N/A | C13 col |
| Mutator: singleton Remove-absent / re-Set after Remove | TileGridBakeSystem cross-frame Entity-keyed state (_bakeNow :117, _quiet :118, _streams :125, _deadGrids :129, chunk lists) | N/A | C13 col |

## Money dimension table

| Variable | Unit / base | Cap | require/check seam |
|---|---|---|---|
| EntityQuery membership (base: facade publication events — Set/Add/Remove/NotifyChanged/Dispose — not live field state; enumeration = frame-stable snapshot) | other | subset of alive entities matching filter; hidden singleton carrier excluded from EVERY surface incl. pool iteration | snapshot at EACH Update/enumeration start (never per-frame cached); publication applies synchronously (Culling->prep same frame, C4); construction-time live-scan seeding for transient/late-built queries (C4); Count NotSupported |
| value-predicate membership `b.Gravity.active` / `e.Target == source` (base: LAST-PUBLISHED component value, never current in-place field) | other | subset of structural match | MonoDreams/physics/System/GravitySystem.cs:10 + MonoDreams/rendering/System/Draw/MasterRenderSystem.cs:90; C11 tests; foundation premises.md:692 rewrite |
| Changed payload (old,new) (base: previous stored reference vs incoming; NotifyChanged => same reference for class comps, value copy for structs — H8) | other | none | reader MonoDreams/audio/System/AudioSystem.cs:143 ReferenceEquals; C4 test NotifyChanged fires Changed old==new |
| EntitySystem<T> iteration snapshot (base: membership frozen at Update start; world state read live per element) | other | length <= membership at capture; must skip members disposed earlier in same loop | MISSING — C4 'tolerates Dispose mid-loop' must add assertion: disposed member is SKIPPED, not delivered dead (GameOverSystem shape) |
| LayerDepth (base: normalized [0,1] depth = minDepth + clamp(t)*(range) + YSortDepthBias per DrawLayerMap layer) | other | final value INCLUDING bias clamped to [minDepth,maxDepth] — bias can never escape the layer band | YSortSystem.cs:50-55 outer clamp incl. bias + YSortSystem.cs:84-90 child-draw clamp (minimalBias :85); wave-3 conversion keeps identical arithmetic; C17 edge-of-band+bias test |
| DrawSortBuffer tie-break index (base: ENTITY-SET insertion order today; becomes facade-snapshot order under Arch) | other | none | MISSING — MasterRenderSystem.cs:176-186 + docs/flows/rendering.md:54 pin ties to set order; facade must define deterministic snapshot order + ties regression test |
| world singleton presence (base: <=1 instance per type per world; 4 types; Set-when-present => Changed not Added) | other | <=1 per type per world | facade singleton store + C4 Restart test; Remove legs are 9 lines: LDtkLevelLoadSystem.cs:71-72,80-82 + EditorTransport.cs:399-400,411-412 (intent lists 6) |
| EcsBoundaryLintTests ratchet count (base: git-tracked .cs outside foundation/Ecs referencing DefaultEcs, later Arch) | other | 0 (ratchet empty, C5/C14) | planned MonoDreams.Tests/Architecture/EcsBoundaryLintTests.cs, modeled on MonoDreams.Tests/LevelEditor/EditorThemeLintTests.cs + ManifestHonestyTests.cs:91 KnownGaps |
| ProcessWideState registry coverage (base: set of facade/Arch process-wide statics, incl. Arch World.Worlds + component-type registries) | other | all introduced statics registered (C12) | MonoDreams.Tests/ProcessWideState.cs:93 Reset; Arch-side reset path unproven — wave-0 must prove it |
| heap growth ratio (base: max/min of post-warmup live-heap samples within one demo run, skipSamples=1) | other | <= 1.5x | MonoDreams.Tests/GameTestRunner.cs:110-121 AssertHeapFlat |
| ParallelSystem runner degree (base: parallelism degree; M4 sequential-equivalence claim measured only at degree 1) | other | == 1 (asserted) | facade default runner + runner-accepting EntitySystem ctor assert degree==1, >1 throws NotSupportedException; hosts GravitySystem.cs:9, EditorPipelineRegistrar.cs; C4 test + facade premise |
| reactive-site test coverage (base: 11 sites re-enumerated from code: collision x2, level-ldtk x3, audio x2, TileGridBake x2, BoundaryBake x2) | other | named tests == sites (C10) | C10 test list; C5 lint self-heals any missed site (leftover DefaultEcs ref fails ratchet) |
| ~~wave-1 screenshot identity (base: PNG byte diff vs main baseline, 6 demo screens)~~ | ~~other~~ | ~~0 differing bytes vs a baseline captured from main+clock (C7); wave-2 diffs explained, never re-baselined (C15/H4)~~ | ~~deterministic fixed-step clock merges to MAIN as standalone pre-wave PR BEFORE baseline capture; main-vs-main double-run precheck gates C7 — fail => fix clock + recapture; baselining from the wave branch forbidden~~ **SUPERSEDED — see the row below (measured, clock PR)** |
| wave-1 screenshot identity (base: PNG byte diff vs a main baseline captured under the deterministic-input PROTOCOL, over 5 of the 6 demo screens — launcher/camera/dialogue/ui/audio) | other | 0 differing bytes, no tolerance and no skipped frames (the comparer takes neither); physics EXCLUDED until its RNG is seeded; wave-2 diffs explained, never re-baselined (C15/H4) | clock landed on MAIN as the standalone pre-wave PR; "captured the same way" now MEANS the protocol — `MONODREAMS_EDITOR=1` + an `editor_op_plan.json` present (that presence is what sets `CursorInputSystem.SkipHardwareRead`; a bare headless run reads the hardware mouse and draws the cursor at a per-launch position), a single `Play@0` op (the editor flag boots frozen — a frozen scene would be identical trivially), and `captureEvery: 0` so only the final frame is read back (the frame-0 window backbuffer has been observed partially composited). Executable on any branch: `MonoDreams.Tests/IntegrationTests/DeterministicClockTests.cs` (`Demo_RunTwiceHeadless_ProducesByteIdenticalPngs`), comparer `GameTestRunner.AssertScreenshotsByteIdentical`; scope held by `Precheck_CoversEveryDemoScreen_OrNamesTheExclusionAndWhy`. **A C7 baseline captured any other way is not comparable and must not be used.** |
| benchmark delta (base: same BenchmarkDotNet cases/config/machine; w2-vs-w0 report, w3-vs-w2 gate) | other | wave-3 hold-or-improve, regressions reverted/justified (C17); w2 report-only (C16) | MonoDreams.Benchmarks RESULTS.md comparison in PR — manual gate, document in plan |
| EntitySet.Count call-site census (base: asserts in git-tracked tests calling EntitySet.Count) | other | 0 remaining at wave-1 close (base WIDENED: all git-tracked .cs, engine included) | 23 asserts/5 test files (ColliderAction 7, EditorContextMenu 2, ColliderDebug 6, ProxyVertex 7, CameraEntityEditor 1) + engine SceneCameraEnsure.cs:65; AsSet-var-aware grep gates wave-1 close |
| wave-1 using-sweep size (base: git-tracked .cs with 'using DefaultEcs', excl. scratchpad) | other | 0 at wave-1 close (C5) | EcsBoundaryLintTests; measured 449 lines / 320 files on main @7b2cbe2a |
| chunked-override eligibility (base: wave-3 converted systems performing structural mutation mid-iteration) | other | 0 structural mutators on the raw chunk path | C17 gate: buffer-then-mutate or exclusion; CullingSystem.cs:100,107 + TextPrepSystem.cs:75 named; converted-path skip/double-visit test |
| SceneWriter stable-id mint order (base: first-serialization AssignStableIds order — persisted into committed .mdscene entities[]) | other | 0 backend-order-dependent mints | SceneWriter.cs:269-274,295: mint over deterministically-ordered roots (facade snapshot order or explicit key); first-stamp-order test on an UNstamped scene; save-twice insufficient alone |
| cross-frame Entity-keyed collection coverage (base: any Dictionary/HashSet/List<Entity> alive across frames — census non-exhaustive by design) | other | none — protection is type-level, not enumerable | facade Entity version-stamped Equals/GetHashCode/== (C13); spot-tests: TileGridBakeSystem :117/:118/:125, undo subgraphs, DialogueStateComponent.cs:23, EditorChromeBuilder.cs:73-77 |

**Dimension violations:**
- DrawSortBuffer tie-break index: wrong-base — Facade EntityQuery must define a documented deterministic snapshot order; add same-depth-ties regression test + rendering premise update; C7 byte-identity guards wave 1 only — wave 2 needs the executable order guarantee.
- EntitySystem<T> snapshot stale-handle: no-seam — C4's mid-loop-mutation test must assert a member disposed earlier in the loop is skipped (facade checks IsAlive per element), not merely 'no crash' — GameOverSystem is the live reproducer.
- EntityQuery predicate backfill at construction: no-seam — Intent defines publication-driven updates but not build-time seeding: query construction must evaluate predicates over CURRENT values for pre-existing entities; add to C4/C11 test list.
- ParallelSystem runner degree: uncapped — facade runner + runner-accepting EntitySystem ctor assert degree==1 (>1 throws NotSupportedException); GravitySystem.cs:9 predicate-set runner ctor named in required surface; premise + C4 test
- Arch static registry resettability (H9): no-seam — C12 registers statics but Arch's World.Worlds/component-type registries may lack a reset API; wave-0 spike must prove World.Destroy (or equivalent) clears them, and ProcessWideState.Reset gains the hook in the same PR.
- wave-1 PNG byte-identity check: no-seam — clock lands on MAIN via standalone pre-wave PR; baselines captured from main+clock only; main-vs-main precheck must pass before C7 gates — failure means fix clock and recapture, never baseline from the wave branch. **RESOLVED with a narrowed base (clock PR, measured):** the clock alone does not make pixels reproducible — it makes TIME reproducible. Two further sources were measured on main+clock and neither is a clock defect, so "fix clock and recapture" is not their remedy: (1) a bare headless run samples the hardware mouse and renders the cursor at a per-launch position (measured: 611 differing px on the audio demo, the arrow drawn in two places), fixed by running under the input protocol the row above defines; (2) `MonoDreams/physics/demo/PhysicsDemoScreen.cs:118` builds its scene from an unseeded `Random`, so the physics demo's scene CONTENT differs per process (3,165–7,723 differing px) — no clock or input protocol can absorb that, and it is why the base is 5 screens, not 6. **Open decision for wave 1: seed that RNG (always, or headless-only) and re-widen the base to 6/6 — deliberately left to the user, since it changes what a human sees when they run the physics demo.** Until then C7 is gateable over 5 screens under the protocol, and the exclusion is enforced in code (`DeterministicClockTests.Excluded` + its coverage guard), not carried in prose.
- M5 world-Remove leg list: untagged-base — legs re-based on measured control flow: Remove only in else/catch; present-leg fires Removed driving unload; success reload fires Changed with 0 subscribers — precondition rows amended

## Precondition diff

| Guard | Copy | Old precondition | New reality | Resolution |
|---|---|---|---|---|
| if (!entity.Has<X>()) entity.Set<X>() — add-guard (redundant under add-or-update, load-bearing if Set ever becomes Arch update-only) | MonoDreams/collision/System/TransformCollisionDetectionSystem.cs:89 | DefaultEcs Set is add-or-update; Has-guard only avoids handler re-entry/double-publish, never needed for safety. | Facade keeps Set add-or-update (D4); Arch's update-only Set unreachable outside facade (C14); this Set runs inside a ComponentAdded callback (M10). | Keep guard verbatim; C3 M10 wave-0 proof + C10 named test cover Set-inside-handler; C4 pins add-or-update both waves. |
| if (!entity.Has<X>()) entity.Set<X>() — add-guard (redundant under add-or-update, load-bearing if Set ever becomes Arch update-only) | MonoDreams/collision/System/TransformCollisionDetectionSystem.cs:94 | DefaultEcs Set is add-or-update; Has-guard only avoids re-fire of Added on re-tag. | Same M10 shape as :89 — facade Set stays add-or-update; mutation inside publish path must survive Arch archetype moves. | Keep guard; C3 proof + C10 test; C4 Set contract test pins semantics. |
| if (!entity.Has<X>()) entity.Set<X>() — add-guard (redundant under add-or-update, load-bearing if Set ever becomes Arch update-only) | MonoDreams/rendering/System/Draw/CullingSystem.cs:98-100 | DefaultEcs add-or-update makes the Has-guard pure event hygiene (avoid spurious Changed on already-visible entities). | Facade Set add-or-update (D4); mid-Update structural add is safe only via D2 snapshot iteration. | Keep guard; C4 mid-loop structural test + C15 visual identity gate protect; no site edit. |
| if (!entity.Has<X>()) entity.Set<X>() — add-guard (redundant under add-or-update, load-bearing if Set ever becomes Arch update-only) | MonoDreams/ui/TabSystem.cs:75-76 | Add-or-update Set means guard exists only to avoid re-firing Added on already-tagged tab. | Facade preserves add-or-update; manual-iteration site — mid-loop add rides D3 snapshot enumeration. | Keep guard; wave-1 sweep migrates usings only; C4 contract test is the seam. |
| if (!entity.Has<X>()) entity.Set<X>() — add-guard (redundant under add-or-update, load-bearing if Set ever becomes Arch update-only) | MonoDreams/ui/DialogSystem.cs:49-50 | Same as TabSystem: guard is event hygiene under add-or-update Set. | Facade add-or-update kept (D4); raw Arch Set/Add split never reaches this site (C14 lint). | Keep guard; C4 Set contract test; no behavioral edit. |
| if (entity.Has<X>()) entity.Remove<X>() — remove-guard | MonoDreams/rendering/System/Draw/CullingSystem.cs:105-107 | DefaultEcs Remove-when-absent is a silent no-op; guard skips event churn, not required for safety. | Arch Remove on an absent component is undefined/throwing; facade Remove must Has-check internally, stay a no-op, fire no Removed. | Keep guard; add C4 contract test Remove-when-absent = silent no-op; facade internal Has-check is the executable seam. |
| if (entity.Has<X>()) entity.Remove<X>() — remove-guard | MonoDreams/ui/TabSystem.cs:77 | Remove-when-absent no-op in DefaultEcs; guard avoids spurious Removed subscriptions firing. | Facade Remove mirrors DefaultEcs no-op semantics over Arch's throwing Remove. | Keep guard; covered by the same C4 Remove-when-absent test. |
| if (entity.Has<X>()) entity.Remove<X>() — remove-guard | MonoDreams/ui/DialogSystem.cs:51 | Same no-op semantics; guard is hygiene. | Facade Remove Has-checks internally; site unchanged wave 1. | Keep guard; C4 test seam. |
| entity.IsAlive liveness check before Get/Set/Dispose — must stay version-checked under Arch recycled ids | MonoDreams/foundation/System/HierarchySystem.cs:63 | DefaultEcs IsAlive is version-checked: stale handle to a disposed entity reads dead even after slot reuse. | Arch recycles ids; a raw handle can alias the new occupant (H6). Facade Entity carries a version stamp; IsAlive/Get require match. | Facade version-stamped handle + C13 recycled-id stale-handle test; site unchanged. |
| entity.IsAlive liveness check before Get/Set/Dispose — must stay version-checked under Arch recycled ids | MonoDreams/foundation/System/HierarchySystem.cs:76 | Version-checked IsAlive gates parent walk over possibly-disposed entities. | Facade preserves version semantics over Arch recycled ids (C13). | C13 test; site unchanged. |
| entity.IsAlive liveness check before Get/Set/Dispose — must stay version-checked under Arch recycled ids | MonoDreams/foundation/System/HierarchySystem.cs:89 | Version-checked IsAlive gates child cleanup. | Same facade guarantee (C13); stale never aliases recycled slot. | C13 test; site unchanged. |
| entity.IsAlive liveness check before Get/Set/Dispose — must stay version-checked under Arch recycled ids | MonoDreams/foundation/State/EntityHierarchy.cs:62 | EntityHierarchy stores long-lived handles; DefaultEcs version check keeps stale reads dead. | Managed singleton keeps handles across frames — highest recycled-id exposure; facade version stamp is the protection. | C13 test explicitly covers cached-handle-in-EntityHierarchy shape; site unchanged. |
| entity.IsAlive liveness check before Get/Set/Dispose — must stay version-checked under Arch recycled ids | MonoDreams/foundation/State/EntityHierarchy.cs:65 | Same cached-handle liveness dependence. | Same facade version-stamp guarantee (C13). | C13 test; site unchanged. |
| entity.IsAlive liveness check before Get/Set/Dispose — must stay version-checked under Arch recycled ids | MonoDreams/ui/TabSystem.cs:65 | IsAlive check before per-tab Get; DefaultEcs versioned handles. | Facade preserves observable IsAlive semantics (precondition diff: identical). | C13 test; site unchanged. |
| entity.IsAlive liveness check before Get/Set/Dispose — must stay version-checked under Arch recycled ids | MonoDreams/ui/DialogSystem.cs:45 | IsAlive gate before dialog entity ops. | Facade versioned handle (C13). | C13 test; site unchanged. |
| entity.IsAlive liveness check before Get/Set/Dispose — must stay version-checked under Arch recycled ids | MonoDreams/rendering/System/Draw/CullingSystem.cs:114 | IsAlive check on cached camera/target handles before cull math. | Facade versioned handle; CullingSystem.cs:45,64 cached handles named in C13 coverage. | C13 test; site unchanged. |
| edit-field-then-publish discipline (mutate then NotifyChanged/Set so predicate sets re-evaluate) — ~30 engine copies | MonoDreams/level-editor/System/HudPreviewSystem.cs (5 sites) | Predicate sets re-evaluate only on publication (Set/NotifyChanged); in-place edits invisible until published (foundation :692). | Facade owns publication-driven membership (M1/M2); same discipline; NotifyChanged routes through facade, fires Changed old==new. | Sites keep mutate-then-publish verb; C11 no-move-without-publish + C4 NotifyChanged tests guard the discipline. |
| edit-field-then-publish discipline (mutate then NotifyChanged/Set so predicate sets re-evaluate) — ~30 engine copies | MonoDreams/cursor/System/CursorPositionSystem.cs (2) | Cursor edits publish via NotifyChanged so downstream cursor sets re-evaluate. | Facade NotifyChanged is the sole publication hook (M2); membership cached between publishes. | Verb unchanged; C4 NotifyChanged contract test + pointer-replay e2e (C15) cover. |
| edit-field-then-publish discipline (mutate then NotifyChanged/Set so predicate sets re-evaluate) — ~30 engine copies | MonoDreams/ui/PanelGroupSystem.cs (2) | Panel layout edits publish so layout/visual sets re-run. | Facade publication hook identical (M1/M2). | Verb unchanged; C4 + C11 tests are the seam. |
| edit-field-then-publish discipline (mutate then NotifyChanged/Set so predicate sets re-evaluate) — ~30 engine copies | MonoDreams/level-editor/System/AutotileRuleEditorSystem.cs (4) | Editor rule edits publish for bake systems to diff (M6 old-value delivery). | Facade Changed(old,new) payload preserved; NotifyChanged delivers old==new. | Verb unchanged; C10 bake-site tests + C4 contract test. |
| edit-field-then-publish discipline (mutate then NotifyChanged/Set so predicate sets re-evaluate) — ~30 engine copies | MonoDreams/level-editor/Undo/PaintValueCommands.cs (4) | Undo/redo mutates then publishes so paint/bake sets re-evaluate. | Facade publication semantics identical; undo path exercised by Restart integration flow. | Verb unchanged; EditorTransportTests re-run (C10 integration) + C4. |
| edit-field-then-publish discipline (mutate then NotifyChanged/Set so predicate sets re-evaluate) — ~30 engine copies | MonoDreams/level-editor/System/EditorDialogSystem.cs (4) | Dialog state edits publish for dialog visual sets. | Facade publication hook identical (M2). | Verb unchanged; C4 contract test covers the fleet. |
| world.Remove<marker> before reload/re-Set so the re-publish fires Added not Changed | MonoDreams/level-ldtk/System/Level/LDtkLevelLoadSystem.cs:71-72 | Claimed Remove precedes every re-Set so reload fires Added; Remove-when-absent no-op the only extra leg. | Remove runs only in the else (load-failure) path: after a prior success it removes PRESENT comps -> Removed -> HandleLevelUnloaded mass-dispose; a successful re-import Sets WITHOUT Remove -> Changed, zero world-Changed subscribers. | C4 pins present-leg Removed in the error handler, absent-leg silent no-op, AND the success-re-import Changed-inert quirk (no re-parse); fail-then-reimport Added test (contract amended). |
| world.Remove<marker> before reload/re-Set so the re-publish fires Added not Changed | MonoDreams/level-ldtk/System/Level/LDtkLevelLoadSystem.cs:80-82 | Claimed same Removed->Added contract on level swap driven by these removes. | These 3 removes live in the catch (exception) path — present after a prior success, absent on first-load failure; the normal reload never executes them, so Removed->Added arises only via fail-then-reimport. | C4 both-legs test on the catch path; C10 fail-then-reimport re-parse test; success-reload Changed-inert pinned. |
| world.Remove<marker> before reload/re-Set so the re-publish fires Added not Changed | MonoDreams/level-editor/Composition/EditorTransport.cs:399-400 | Claimed ALWAYS Remove-when-absent — only the silent no-op leg exercised. | EditorTransportTests.cs:186 Restart_RemovesTheWorldLevelComponents Sets both markers then Restarts: Remove-when-PRESENT runs — must clear presence and fire Removed; absent leg stays a silent no-op. | C4 pins BOTH legs (present => Removed + Has false; absent => no-op firing nothing); EditorTransportTests re-run both waves. |
| world.Remove<marker> before reload/re-Set so the re-publish fires Added not Changed | MonoDreams/level-editor/Composition/EditorTransport.cs:411-412 | Claimed always Remove-when-absent; no-op contract called the only load-bearing leg. | Same test path reaches this leg with markers PRESENT — Remove must clear presence and fire Removed; a no-op-only facade fails Restart_RemovesTheWorldLevelComponents. | C4 both-legs contract test; RestoreBackup covered by the same present+absent pair. |
| buffer-then-mutate (collect entities first, structurally mutate after the enumeration) | MonoDreams/foundation/System/HierarchySystem.cs:59-81 | Buffering was defensive under DefaultEcs — maintained sets mostly tolerated mid-enumeration structural mutation. | Arch archetype moves make mid-iteration structural mutation unsafe (H2); facade snapshot iteration (D2/D3) is the real protection; buffered sites doubly safe. | Keep buffering; D2 snapshot premise + C4 mid-loop test cover; no rewrite. |
| buffer-then-mutate (collect entities first, structurally mutate after the enumeration) | MonoDreams.Examples.Core/System/Runner/GameOverSystem.cs:129 | Partially buffered: :129 buffers, but :64,:106 create AND dispose mid-iteration relying on DefaultEcs tolerance. | Unbuffered legs depend entirely on facade snapshot (D2) + skip-disposed-member semantics. | C4 snapshot test asserts disposed member SKIPPED (GameOverSystem shape); no site rewrite wave 1. |
| buffer-then-mutate (collect entities first, structurally mutate after the enumeration) | ~7 level-editor systems per intent 2b | These already buffer-then-mutate — safe under both backends by construction. | Pattern stays correct under Arch; snapshot iteration adds a second layer. | No change; document pattern in new facade premise (C8) as the recommended idiom. |
| entity.Dispose() ⇒ synchronous per-component Removed cascade with pre-teardown value readable | MonoDreams/audio/System/AudioSystem.cs:38,133-137 | Cascade pinned only as 'Removed raised before Arch Destroy' — a deferred CommandBuffer Destroy passes that while IsAlive stays true until apply. | Under deferral, sweeps' !IsAlive guards re-Dispose cascade-dead entities (double Removed to AudioSystem) and orphan polls miss deaths a frame. | C4 pins: IsAlive false + query membership dropped before Dispose returns; double-Dispose of a dead handle is a silent no-op firing nothing (contract added). |
| entity.Dispose() ⇒ synchronous per-component Removed cascade with pre-teardown value readable | MonoDreams/level-editor/Composition/EditorTransport.cs:419-429 | Restart/tab-switch sweep relied on dispose-time Removed cascades reaching every reactive subscriber. | Facade-fired dispose cascade is the only delivery under Arch; the unfiltered sweep must also never see the hidden carrier. | C4 dispose-cascade test + carrier-invisibility premise; EditorTransportTests restart re-run both waves. |
| entity.Dispose() ⇒ synchronous per-component Removed cascade with pre-teardown value readable | MonoDreams/level-ldtk/System/Level/LDtkTileParserSystem.cs:145-156 | CleanupTileEntities mass dispose fires Removed per tile for downstream cleanup. | Mass dispose runs inside singleton dispatch under Arch archetype churn (Removed leg of level swap). | C3 parser-shape proof + C10 test: mass Dispose+Create+Publish inside singleton Added AND Removed dispatch. |
| entity.Dispose() ⇒ synchronous per-component Removed cascade with pre-teardown value readable | MonoDreams/foundation/System/HierarchySystem.cs:43,55-83 (DisposeOrphans) | Per-frame parent.IsAlive polling assumed to see deaths the frame they happen — DefaultEcs flips IsAlive synchronously at Dispose. | A facade deferring Arch Destroy delays the flip: DisposeOrphans misses deaths a frame and cascade order drifts. | C4 dispose-synchrony pin + DisposeOrphans same-frame poll test; new matrix column covers the site. |

## Failing-first tests & premises

| Premise | require/check seam | Failing-first test | Status |
|---|---|---|---|
| Facade Set<T> is add-or-update; Arch's update-only Set / add-only Add are never reachable outside MonoDreams/foundation/Ecs/ | facade Entity.Set impl (Has→update else add) + EcsBoundaryLintTests source scan as the executable check | EcsFacadeContractTests.Set_AddsWhenAbsent_UpdatesWhenPresent | new |
| NotifyChanged fires Changed with old==new — the SAME stored reference, not a copy | facade NotifyChanged raises Changed(entity, stored, stored); contract test asserts ReferenceEquals | AudioSystemTests: NotifyChanged_DoesNotCutTheLiveInstance (pairs with existing OverwritingTheComponentViaSet_CutsTheOldValuesLiveInstance) | new |
| A value-predicate query re-evaluates only when the component is published (foundation premises :692 — currently Tests: none yet) | facade EntityQuery predicate hooked ONLY to facade publication events (Set/NotifyChanged); GravitySystem.cs:10 + MasterRenderSystem.cs:90 ride it | EcsFacadeContractTests.InPlaceMutation_DoesNotMoveMembership_UntilPublished — written failing-first in wave 1 (no test exists on main; foundation :692 Tests: none yet), stays green over Arch in wave 2 | new |
| EntitySystem<T> iterates a frame-stable snapshot; Set/Remove/Dispose/Create mid-Update are safe (D2) | facade EntitySystem copies membership to a buffer before the loop; iteration never touches live archetypes | snapshot mid-loop structural-change test (C4) | new |
| World-singleton Set-when-present fires Changed never Added; world-level Remove fires Removed (CORE_TENETS §9 Restart semantics) | facade singleton store branches on presence before raising | singleton contract test + EditorTransportTests Restart re-run | new |
| [Subscribe] registration walks the type hierarchy and registers a virtual handler exactly once (collision premises :220) | facade bus scan dedupes via MethodInfo.GetBaseDefinition | subclass-override single-dispatch bus test; PenetrationResolutionTests re-verify downstream | new |
| Every facade/Arch process-wide static is in ProcessWideState in the PR that introduces it (foundation premises :480 extended to the new backend) | ProcessWideState.CaptureDefaults/Reset entries + hygiene guard naming an undisposed World | ProcessWideStateHygieneTests extension: leaked-World detection under MONODREAMS_TEST_NO_RESET=1 | existing |
| A stale entity handle never aliases a recycled slot: IsAlive false, Get/Has keep today's DefaultEcs semantics | facade Entity carries a version stamp; IsAlive/Get require version match | recycled-id stale-handle test (C13) | new |
| Class components round-trip by reference: facade Get<T> for managed T returns the stored instance, never a copy | contract test ReferenceEquals check on facade Get; no boxing copy in the managed-component path | Get-twice-same-instance + cross-frame in-place-write visibility test | new |
| Deliberately unimplemented facade surface throws NotSupportedException — no silent default (log-and-continue anti-pattern guard) | EntityQuery.Count et al throw at the member | Count_Throws contract test | new |
| Same-depth draw ties resolve by insertion order regardless of backend enumeration (rendering premises :791) | prep systems stamp an explicit insertion index consumed by the stable sort — no reliance on archetype order | same-depth two-sprite stable-order test under Arch | existing |
| CameraFollowSystem's multi-target pick is documented-nondeterministic (camera premises :235); code assuming DefaultEcs order must sort explicitly | wave 2: explicit deterministic pick (lowest-entity-id / single-instance assert) at CameraFollowSystem.cs:70-84 + CameraSyncSystem.cs:70 | two-target/two-camera deterministic-pick test under Arch | new |
| The hidden singleton-carrier entity is invisible to every facade query/enumeration/count surface — filtered, unfiltered, and mass-dispose sweeps; only the singleton store reaches it | facade EntityQuery/GetEntities AND AComponentSystem pool iteration (TransformCommitSystem.cs:15) skip the carrier | C4 carrier-invisibility incl. pool-iteration leg: pool Update count unchanged after 4 singleton Sets | new |
| entity.Dispose fires ComponentRemoved per present component, synchronously, with the value captured before backend destroy | facade Dispose: capture -> Removed per comp -> Arch Destroy in the same call; IsAlive false + membership dropped on return; double-Dispose no-op | C4 dispose-synchrony + DisposeOrphans same-frame poll + double-Dispose no-op tests | new |
| World-singleton Subscribe never replays an already-present value; entity-level SubscribeComponentAdded replays existing state exactly as DefaultEcs (measured wave 0) | wave 0 measures BOTH legs (entity-level AND world-component subscribe-replay); facade pins whichever DefaultEcs does | C4 no-double-parse + wave-0 world-comp subscribe-replay measurement test | new |
| world.Dispose is event-silent bulk teardown: no per-component or singleton Removed fires — the dispose cascade is entity.Dispose-only | facade World.Dispose tears subscriptions down, then drains storage without raising events | C4 teardown event-silence test (readers AudioSystem.cs:133-137, LDtkTileParserSystem.cs:42, discarded M10 subs) | new |
| Composite system Dispose recurses to leaf systems in reverse registration order — the sole path stopping native audio instances and freeing GPU resources at screen switch | facade Sequential/Parallel/GatedSystem Dispose cascade; screens dispose pipeline BEFORE world (LevelSelectionScreen.cs:626-634) | C4 composite-cascade test (AudioSystem.cs:158-173, CullingSystem.cs:112-120 reached) | new |
| Facade Entity Equals/GetHashCode/operator== are version-stamped: a dead handle finds/removes its own keyed entry and never equals the recycled slot's occupant; == default sentinel preserved | facade Entity struct includes version in equality and hash | C13 equality/hash recycled-id tests: TileGridBakeSystem _quiet/_bakeNow/_streams shapes + undo-subgraph dead handles + DialogueStateComponent.OptionEntities spot-tests | new |
| Query membership applies synchronously at publication; enumeration snapshots are captured per Update/enumeration start (never per-frame cached); construction seeds membership by live scan | facade EntityQuery/EntitySystem snapshot + seeding implementation | C4 same-frame Culling->prep visibility test + construction-seeding test over a pre-populated world | new |
| Facade bus Publish is synchronous and re-entrant: a nested Publish inside an in-flight dispatch runs immediately in the same call stack; handler exceptions propagate unwrapped to the publisher | facade message-bus dispatch loop — no queueing, no reentrancy lock, no exception wrapping | C4 nested-Publish reentrancy test + PrefabExpansionTests.cs:190 stays green + native boot e2e | new |
| A handler reachable by both [Subscribe] and a ctor typed Subscribe registers exactly once; the facade never auto-scans an instance that did not call Subscribe(this) | facade bus: attribute scan runs only on explicit Subscribe(this); typed path independent | C4 no-double-registration test (LevelLoadRequestSystem shape: level loads once per request) | new |
| A successful LDtk re-import Sets over present singletons firing Changed — with zero world-Changed subscribers it is deliberately inert; only fail-then-reimport produces Removed->Added and re-parses | LDtkLevelLoadSystem.cs:64-82 control flow + facade singleton Changed-not-Added | C10 re-import-inert test + fail-then-reimport re-parse test | new |
| First-time stable-id minting in SceneWriter is backend-order-independent — entities[] order in a first-saved .mdscene never encodes ECS enumeration order | SceneWriter.cs:269-274 AssignStableIds runs over deterministically-ordered roots, not raw CollectMembership enumeration | first-stamp-order test: save an UNstamped scene from two differently-populated worlds -> identical bytes | new |