export const meta = {
  name: 'review-fix-loop',
  description: 'Review→conciliate→fix loop over an open PR until exhaustion (0 High/Blocker in a breadth round + dry enumeration), with a round cap + a final Medium/Low round; posts each consolidated review on the PR',
  whenToUse: 'Invoked by the /review-fix-loop skill (.claude/skills/review-fix-loop/SKILL.md) with an open PR and its branch checked out.',
  phases: [
    { title: 'Classify', detail: 'standard (mirror of Anthropic\'s code-review action) vs deep (deep-review lenses)' },
    { title: 'Review', detail: 'round 1/gate: breadth fan-out (universal + per-flow lenses); rounds 2+: bounded validator of fixes + fix-diff reviewer' },
    { title: 'Enumerate', detail: 'exhausts each hot surface (facets with evidence) in the same round' },
    { title: 'Conciliate', detail: 'fuses with comments/reviews already posted, dedups, posts on the PR' },
    { title: 'Fix', detail: 'addresses Blocker/High + aged Mediums, commit+push, updates the description, replies to divergences' },
  ],
}

// args arrives as a JSON-encoded string in some runtimes — defensive shim.
const ARGS = (() => {
  try { return typeof args === 'string' ? JSON.parse(args) : (args ?? {}) } catch (e) { return {} }
})()

const REQUIRED = ['prNumber', 'repoRoot', 'branch']
const missingArgs = REQUIRED.filter((k) => !ARGS[k])
if (missingArgs.length) {
  return { status: 'blocked', stage: 'args', reason: `required args missing: ${missingArgs.join(', ')}` }
}

const PR = ARGS.prNumber
const MAX_ROUNDS = Math.min(Math.max(ARGS.maxRounds || 5, 1), 10)
const MAX_EXTENSIONS = Math.min(Math.max(ARGS.maxExtensions ?? 2, 0), 5)
const MAX_ENUM_SURFACES = Math.min(Math.max(ARGS.maxEnumSurfaces ?? 6, 1), 12)
const ROLE_DIR = `${ARGS.repoRoot}/.claude/skills/review-fix-loop/agents`
const DEEP_DIR = `${ARGS.repoRoot}/.claude/skills/deep-review/agents`
const CONFIG = 'docs/agents/skills-config.md'

// Deep mode fans out the genericized deep-review lens set installed alongside this skill:
// the UNIVERSAL lenses (always, by flat role-file name) + one FLOW lens per flow doc the
// change touches, via deep-review's generic flow-lens.md mechanism. The repo declares its
// flows as docs (config › Flows; the orchestrator passes the touched ones); a repo with no
// financial (or any) flows simply passes none. If a role file is missing on the branch, the
// lens prompt short-circuits to {"findings": []}.
const UNIVERSAL_LENSES = [
  { key: 'adjacent', file: 'adjacent-code.md' },
  { key: 'quantity', file: 'derived-quantity.md' },
  { key: 'negspace', file: 'negative-space.md' },
  { key: 'contract', file: 'contract-reconciler.md' },
  { key: 'tests', file: 'test-coverage.md' },
]
// Flow lenses come from the orchestrator (the touched flow docs): [{ name, doc }] where `doc`
// is the flow doc's full text. Each runs the generic flow-lens.md role file with that doc.
const FLOW_LENSES = (Array.isArray(ARGS.flows) ? ARGS.flows : [])
  .filter((l) => l && (l.name || l.doc))
  .slice(0, 8)
  .map((l, i) => ({ key: `flow-${i}`, file: 'flow-lens.md', name: String(l.name || `flow-${i}`).slice(0, 60), doc: String(l.doc || '').slice(0, 6000) }))
const DEEP_AGENTS = [...UNIVERSAL_LENSES, ...FLOW_LENSES]

// ---------------------------------------------------------------------------
// Schemas
// ---------------------------------------------------------------------------

const SEVERITIES = ['Blocker', 'High', 'Medium', 'Low']

const FINDING = {
  type: 'object',
  required: ['id', 'severity', 'title', 'description'],
  properties: {
    id: { type: 'string', maxLength: 60, description: 'stable slug derived from file+topic, e.g. payout-sweep-cap' },
    severity: { type: 'string', enum: SEVERITIES },
    title: { type: 'string', maxLength: 120 },
    file: { type: 'string', maxLength: 160 },
    line: { type: 'string', maxLength: 20 },
    surface: { type: 'string', maxLength: 80, description: 'file+mechanism slug (e.g. SettlementService.paidSoFar-query) — groups Blocker/High for facet enumeration; same root = same key' },
    description: { type: 'string', maxLength: 500, description: 'concrete scenario where something observable goes wrong' },
    suggestedFix: { type: 'string', maxLength: 300 },
    source: { type: 'string', maxLength: 80, description: 'this round\'s reviewer | @human | <bot> | earlier round | validator | fix-diff | enumerate:<surface>' },
  },
}

const FINDINGS = {
  type: 'object',
  required: ['findings'],
  properties: { findings: { type: 'array', items: FINDING, maxItems: 25 } },
}

const CONSOLIDATED = {
  type: 'object',
  required: ['findings', 'commentUrl'],
  properties: {
    findings: { type: 'array', items: FINDING, maxItems: 60 },
    commentUrl: { type: 'string', maxLength: 200 },
    droppedAsContested: { type: 'array', items: { type: 'string', maxLength: 60 }, maxItems: 20 },
    droppedForSpace: { type: 'array', items: { type: 'string', maxLength: 60 }, maxItems: 30, description: 'ids of Low omitted from the comment for space — NEVER Blocker/High/Medium' },
  },
}

const ENUM_RESULT = {
  type: 'object',
  required: ['surface', 'dry'],
  properties: {
    surface: { type: 'string', maxLength: 80 },
    dry: { type: 'boolean', description: 'true iff findings is empty' },
    facets: {
      type: 'array',
      maxItems: 25,
      items: {
        type: 'object',
        required: ['facet', 'verdict'],
        properties: {
          facet: { type: 'string', maxLength: 120 },
          verdict: { type: 'string', enum: ['ok', 'finding', 'unchecked'] },
          evidence: { type: 'string', maxLength: 200, description: 'file:line for ok; reason for unchecked' },
        },
      },
    },
    findings: { type: 'array', items: FINDING, maxItems: 15 },
  },
}

const CLASSIFY_RESULT = {
  type: 'object',
  required: ['mode', 'reason'],
  properties: {
    mode: { type: 'string', enum: ['standard', 'deep'] },
    reason: { type: 'string', maxLength: 300 },
  },
}

const FIX_RESULT = {
  type: 'object',
  required: ['status'],
  properties: {
    status: { type: 'string', enum: ['done', 'blocked'] },
    fixed: { type: 'array', maxItems: 30, items: { type: 'object', required: ['id'], properties: { id: { type: 'string', maxLength: 60 }, note: { type: 'string', maxLength: 200 } } } },
    diverged: { type: 'array', maxItems: 20, items: { type: 'object', required: ['id', 'reason'], properties: { id: { type: 'string', maxLength: 60 }, reason: { type: 'string', maxLength: 300 } } } },
    commits: { type: 'array', items: { type: 'string', maxLength: 40 }, maxItems: 15 },
    prBodyUpdated: { type: 'boolean' },
    blockedReason: { type: 'string', maxLength: 500 },
  },
}

const VALIDATION_RESULT = {
  type: 'object',
  required: ['resolved', 'stillOpen'],
  properties: {
    resolved: { type: 'array', maxItems: 20, items: { type: 'object', required: ['id'], properties: { id: { type: 'string', maxLength: 60 }, evidence: { type: 'string', maxLength: 240, description: 'file:line of post-fix code that resolves the scenario' } } } },
    stillOpen: { type: 'array', maxItems: 20, items: { type: 'object', required: ['id', 'why'], properties: { id: { type: 'string', maxLength: 60 }, why: { type: 'string', maxLength: 300 } } } },
  },
}

// ---------------------------------------------------------------------------
// Prompt helpers
// ---------------------------------------------------------------------------

function ctx() {
  return [
    `Target PR: #${PR} in the repo at ${ARGS.repoRoot} (origin). PR branch: ${ARGS.branch} — already checked out in the working tree.`,
    `Work ONLY inside the repo; use rg, never grep -r; do not read /tmp, ~, or sibling worktrees.`,
    `Before anything: git branch --show-current must return ${ARGS.branch} and git pull --ff-only must be at the PR head. Diverged → return blocked.`,
    `Read per-repo config from ${CONFIG} for build/test commands, doc paths, sensitive domains, and conventions — never assume a stack. If a section is absent, fall back to its stated default and say so.`,
    `Your final text is parsed as structured data — follow the schema, no extra prose.`,
  ].join('\n')
}

function classifyPrompt() {
  const sd = ARGS.sensitiveDomains || []
  return `${ctx()}

You decide the review depth for this PR. Collect: gh pr view ${PR} --json title,body,additions,deletions,files; touched domains (git diff --name-only origin/main...HEAD, mapped to domains via ${CONFIG} › Domains).

Choose "deep" if ANY of:
- There is a deep-plan contract committed on this branch (the plan-contract glob from ${CONFIG} › Docs layout, e.g. .claude/deep-plan/*.md, in the diff)${ARGS.deepPlanContractOnBranch ? ' — ALREADY CONFIRMED by the orchestrator: there is a contract on the branch' : ''};
- The diff touches one of the repo's Sensitive domains (${CONFIG} › Sensitive domains) AND changes a status/lifecycle, a state machine, a settlement/payout-equivalent flow, a load-bearing computation, or a migration with a backfill. If no sensitive domains are configured, this arm never fires — default to standard unless a deep-plan contract is on the branch;
- The change is large/delicate enough that a single reviewer would likely miss emergent interactions (rule of thumb: >~25 production files, or new concurrency/async ordering).

Otherwise "standard". Orchestrator hints: sensitiveDomains=${JSON.stringify(sd)}, diffFiles=${ARGS.diffFiles ?? 'unknown'}.`
}

function contractNote() {
  const confirmed = ARGS.deepPlanContractOnBranch ? ' (the orchestrator CONFIRMED there is a contract on this branch)' : ''
  return `Plan-contract as input${confirmed}: if the branch has a committed contract (the plan-contract glob from ${CONFIG} › Docs layout, e.g. .claude/deep-plan/*.md — check with git diff --name-only origin/main...HEAD), READ IT before analyzing the diff. Fixers of earlier rounds update the semantics they minted into it (dimension rows, premises, struck items) — the contract is the CURRENT spec, not the original plan's. A code↔contract divergence is a finding; a derived value present in the code with no corresponding dimension row/premise in the contract is also a finding (High).`
}

function standardReviewPrompt(round) {
  return `${ctx()}

Read ${ROLE_DIR}/standard-review.md and operate as that reviewer (round ${round}). You review the CURRENT head of the PR — earlier rounds may already have fixed things; report what is in the code now, not the history.

${contractNote()}`
}

function deepReviewPrompt(a, round, kind) {
  const isFlow = a.file === 'flow-lens.md'
  const roleLine = isFlow
    ? `Read ${DEEP_DIR}/flow-lens.md and operate as the "${a.name}" flow lens. Review the diff against what this flow doc says must hold:\n\n=== FLOW DOC: ${a.name} ===\n${a.doc || '(no doc text passed; apply the flow name to this diff)'}\n=== END FLOW DOC ===\n(round ${round} of review-fix-loop${kind === 'gate' ? ' — exhaustion GATE: earlier rounds zeroed High/Blocker in targeted passes; you are the clean breadth look that confirms or refutes exhaustion' : ''})`
    : `Read ${DEEP_DIR}/${a.file} and operate as that specialist (round ${round} of review-fix-loop${kind === 'gate' ? ' — exhaustion GATE: earlier rounds zeroed High/Blocker in targeted passes; you are the clean breadth look that confirms or refutes exhaustion' : ''})`
  return `${ctx()}

${roleLine}. If the role file does not exist on this branch, return immediately {"findings": []}.

Context you collect yourself (deep-review Phase 1): diff via gh pr diff ${PR}; metadata via gh pr view ${PR} --json title,body,files; the core-tenets doc (${CONFIG} › Docs layout › Core tenets; default docs/CORE_TENETS.md); the schema and premises docs of the affected domains (${CONFIG} › Docs layout, substituting {domain}/{module}; default docs/schema/{domain}.md and docs/{domain}/premises.md); the repo's conventions doc. Do NOT read the PR's existing comments/reviews — clean look. The diff is the source of truth for what is being proposed; read the full files for adjacent context. Verify before reporting (grep/read before claiming "X doesn't handle Y").

${contractNote()}

Convert your findings to the findings schema (severities Blocker/High/Medium/Low per your role file; a defect in a Sensitive domain per ${CONFIG} › Sensitive domains is always Blocker — if none configured, grade by ordinary functional impact). For each Blocker/High fill "surface" (file+mechanism slug).`
}

function consolidatePrompt(slices, round) {
  return `${ctx()}

Read ${DEEP_DIR}/consolidate.md and apply the consolidation logic (dedup, verification of high-severity — demotion of Blocker/High only with evidence read in the code, classification) to the findings of the ${slices.length} specialists below (round ${round}). Do NOT post anything to GitHub — return only the consolidated list in the schema. For each Blocker/High fill "surface" (file+mechanism slug; same root = same key) — it is the key for the enumeration stage.

Specialist findings:
${JSON.stringify(slices, null, 2)}`
}

function enumeratePrompt(surface, seeds, round, opts) {
  return `${ctx()}

Read ${DEEP_DIR}/enumerate.md and operate as that facet enumerator (round ${round} of review-fix-loop). If the role file does not exist on this branch, return immediately {"surface": ${JSON.stringify(surface)}, "dry": true, "facets": [], "findings": []}.

Hot surface: ${surface}

Seed findings (Blocker/High confirmed on this surface):
${JSON.stringify(seeds, null, 2)}

${opts.postFix
    ? `The seed findings were just addressed by the fixer (commits: ${JSON.stringify(opts.commits || [])}). Do NOT re-report the seed defect: audit the CURRENT code of the surface — the sibling facets the fix did not cover and regressions the fix itself introduced on it.`
    : `The seed findings are NOT yet fixed. Do NOT re-report them — look for the sibling facets of the same surface.`}

${contractNote()}

For each new finding fill "surface" with the same key above. "dry": true only with the whole facet table "ok" with evidence — never mark ok without file:line.`
}

function validationPrompt(claimedFixed, fixResult, postCap) {
  return `${ctx()}

You are the review-fix-loop bounded VALIDATOR${postCap ? ' (post-cap: the loop hit the round cap right after this fix — there was no validation review)' : ''}. The previous round's fixer claimed to have fixed the findings below. Your scope is CLOSED: for EACH finding below, adversarially judge whether the fix present at the current head resolves the finding's concrete scenario — read the post-fix code (file:line), run a targeted test if there's an obvious one. Do NOT look for new problems, do NOT re-review the PR, do NOT edit anything.

Findings fixed without validation (with the fixer's note in fixNote):
${JSON.stringify(claimedFixed, null, 2)}

Fixer commits: ${JSON.stringify(fixResult?.commits || [])}

Every finding below must appear in exactly one bucket: resolved (with file:line evidence) or stillOpen (with a concrete why).`
}

function fixDiffPrompt(commits, round) {
  return `${ctx()}

You are the FIX-DIFF reviewer (round ${round} of review-fix-loop). The previous round's fixer pushed the commits: ${JSON.stringify(commits)}.

Review ONLY the code touched by those commits (git show <sha>, git diff <first>^..<last>) — you look for what the fixes themselves introduced: a regression, a predicate/temporal window/formula with new semantics, a derived value minted without a dimension row in the contract, a guard copied whose precondition doesn't hold for the new caller, a test weakened to pass. Do NOT re-review the whole PR — only the delta of the fixes. Verify before reporting.

${contractNote()}

Standard severities (a defect in a Sensitive domain per ${CONFIG} › Sensitive domains = Blocker; if none configured, grade by functional impact). For each Blocker/High fill "surface" (file+mechanism slug).`
}

function conciliatePrompt(freshFindings, state, round, mode, kind, mediumAges) {
  return `${ctx()}

Read ${ROLE_DIR}/conciliate.md and operate as that agent (round ${round}, mode ${mode}, round type: ${kind}).

Fresh findings from this round (the source field distinguishes: specialists/consolidator, post-fix validator, fix-diff, enumerate:<surface>):
${JSON.stringify(freshFindings, null, 2)}

Loop state (earlier rounds):
- fixed: ${JSON.stringify(state.fixed)}
- diverged (contested by the fixer, argument posted on the PR): ${JSON.stringify(state.diverged)}
- Medium ages (consecutive rounds the id appeared — ≥2 indicates a stale queue; re-price or mark the age): ${JSON.stringify(mediumAges)}`
}

function fixerPrompt(findings, round, opts) {
  const aged = (opts.agedMediums || []).map((m) => m.id)
  return `${ctx()}

Read ${ROLE_DIR}/fixer.md and operate as that agent (round ${round}).

${opts.finalRound
    ? `FINAL ROUND: the review zeroed High/Blocker. Address the pending Medium/Low below. After you NO ONE reviews again — run the FULL Verify command (${CONFIG} › Verify › Full) before the final push.`
    : `Address the Blocker/High below${aged.length ? ` and the AGED Mediums (≥2 rounds in the queue — assigned, not optional: fix or formal divergence): ${JSON.stringify(aged)}` : ''}. Opportunistic Mediums (same files): ${JSON.stringify(opts.mediumsInSameFiles || [])}${opts.mustFullVerify ? `\n\nLAST POSSIBLE ROUND (loop cap): after you no review validates and no fixer corrects — run the FULL Verify command (${CONFIG} › Verify › Full) before the push, exactly like a final round.` : ''}`}

Assigned findings:
${JSON.stringify(findings, null, 2)}`
}

function cappedCommentPrompt(buckets, roundsSummary, gatePending) {
  return `${ctx()}

The review-fix-loop hit the round cap. Post a comment on the PR (gh pr comment ${PR} --body-file <tmpfile>) in the commit/PR language from ${CONFIG} › Conventions, marker <!-- review-fix-loop: capped -->, with the buckets below in SEPARATE sections — the distinction between them is what makes the comment true (the last review and the last fix are different instants; don't mix them):

1. "Open" — High/Blocker with no fix, or whose fix the validator judged insufficient (validatorNote field):
${JSON.stringify(buckets.open, null, 2)}

2. "Fixed in the last round — bounded validation OK, no full review" (evidence field):
${JSON.stringify(buckets.fixedValidated, null, 2)}

3. "Fixed in the last round — WITHOUT validation" (fixer's claim only):
${JSON.stringify(buckets.fixedUnreviewed, null, 2)}

4. "Contested by the fixer, no re-assessment" (argument already posted on the PR):
${JSON.stringify(buckets.contested, null, 2)}

${gatePending ? 'NOTE: the last round zeroed High/Blocker in a TARGETED pass (depth), but the breadth fresh-eyes gate did NOT get to run — the loop capped before it. Say explicitly that exhaustion was NOT confirmed and recommend running /deep-review or a new loop as the gate.' : ''}

Omit empty sections. Include the per-round severity trajectory (with each round's type — breadth/depth/gate — and whether the enumeration came dry) and that the loop stopped awaiting a human decision. Telegraphic and honest — no glossing.

Trajectory:
${JSON.stringify(roundsSummary, null, 2)}

Return only an empty { "findings": [] } in the schema (the comment is the effect).`
}

// ---------------------------------------------------------------------------
// Hot-surface enumeration
// ---------------------------------------------------------------------------

function hotSurfaces(findings) {
  const hb = findings.filter((f) => f.severity === 'Blocker' || f.severity === 'High')
  const bySurface = new Map()
  for (const f of hb) {
    const key = String(f.surface || f.file || f.id || 'unknown').slice(0, 80)
    if (!bySurface.has(key)) bySurface.set(key, [])
    bySurface.get(key).push(f)
  }
  const entries = [...bySurface.entries()]
  entries.sort((a, b) => {
    const aB = a[1].some((f) => f.severity === 'Blocker') ? 0 : 1
    const bB = b[1].some((f) => f.severity === 'Blocker') ? 0 : 1
    return aB - bB
  })
  return { targets: entries.slice(0, MAX_ENUM_SURFACES), truncated: entries.length > MAX_ENUM_SURFACES }
}

async function enumerateSurfaces(seedFindings, round, opts) {
  const { targets, truncated } = hotSurfaces(seedFindings)
  if (!targets.length) return { findings: [], dry: true, surfaces: [], unchecked: 0, truncated: false }
  if (truncated) log(`enumeration r${round}: ${targets.length} surfaces (cap ${MAX_ENUM_SURFACES}) — excess NOT enumerated this round`)
  const results = await parallel(targets.map(([surface, seeds]) => () =>
    agent(enumeratePrompt(surface, seeds, round, opts), { schema: ENUM_RESULT, label: `enum:${surface.slice(0, 40)} r${round}`, phase: 'Enumerate' })
  ))
  const valid = results.filter(Boolean)
  const findings = valid.flatMap((r) => (r.findings || []).map((f) => ({ ...f, surface: f.surface || r.surface, source: f.source || `enumerate:${r.surface}` })))
  const unchecked = valid.reduce((n, r) => n + (r.facets || []).filter((x) => x.verdict === 'unchecked').length, 0)
  // dry = every enumerator answered, no new finding, no facet without evidence, no surface beyond the cap
  const dry = valid.length === targets.length && findings.length === 0 && unchecked === 0 && !truncated
  log(`enumeration r${round}: ${targets.length} surfaces → ${findings.length} new finding(s), ${unchecked} unchecked facet(s)${dry ? ' — DRY' : ''}`)
  return { findings, dry, surfaces: targets.map(([s]) => s), unchecked, truncated }
}

// ---------------------------------------------------------------------------
// Phases
// ---------------------------------------------------------------------------

phase('Classify')
let mode = ARGS.modeHint === 'deep' || ARGS.modeHint === 'standard' ? ARGS.modeHint : null
if (mode) {
  log(`mode forced by the user: ${mode}`)
} else {
  const c = await agent(classifyPrompt(), { schema: CLASSIFY_RESULT, label: 'classify' })
  mode = c?.mode === 'deep' ? 'deep' : 'standard'
  log(`mode: ${mode} — ${c?.reason || 'classifier died; fallback standard'}`)
}

const state = { fixed: [], diverged: [] }
const roundsSummary = []
const allCommits = []
const seenIds = new Set()
const mediumAge = new Map()
let effectiveMax = MAX_ROUNDS
let extensionsUsed = 0
let gateExtensionUsed = 0
let pendingGate = false
let lastHighBlocker = []
let lastFixResult = null
let finalStatus = 'capped'
let blockedReason = null
// Lenses (deep) that crashed in a breadth/gate round — under throttling, agents can exceed the
// StructuredOutput retry cap and fall to null in the parallel fan-out. A breadth round with missing
// lenses does NOT confirm exhaustion; the verdict becomes clean-partial and the orchestrator runs a
// confirmation pass.
const lensFailures = []

for (let round = 1; round <= effectiveMax; round++) {
  let freshFindings = []
  let enumInfo = { findings: [], dry: true, surfaces: [], unchecked: 0, truncated: false }
  let roundKind
  let roundDegradedLenses = [] // keys of the lenses that crashed THIS round (empty outside breadth/gate)

  if (mode === 'standard') {
    // --- Standard: 1 breadth reviewer per round (simple PRs; no enumeration) ---
    roundKind = 'standard'
    const r = await agent(standardReviewPrompt(round), { schema: FINDINGS, label: `review r${round}`, phase: 'Review' })
    freshFindings = r?.findings ?? []
  } else if (round === 1 || pendingGate) {
    // --- Breadth (round 1 or fresh-eyes gate): fan-out of the lens set + consolidation + enumeration ---
    roundKind = pendingGate ? 'gate' : 'breadth'
    pendingGate = false
    const slices = await parallel(DEEP_AGENTS.map((a) => () =>
      agent(deepReviewPrompt(a, round, roundKind), { schema: FINDINGS, label: `deep:${a.key} r${round}`, phase: 'Review' })
    ))
    // Crashed lenses fall to null here. Record them to downgrade clean→clean-partial: a "clean" verdict
    // over a partial lens set is FALSE-clean (a loop can go clean with a crashed lens; a confirmation pass
    // catches a High that the missing lens would have found).
    roundDegradedLenses = DEEP_AGENTS.filter((_, i) => !slices[i]).map((a) => a.key)
    if (roundDegradedLenses.length) {
      lensFailures.push({ round, kind: roundKind, lenses: roundDegradedLenses })
      log(`round ${round} (${roundKind}): ${roundDegradedLenses.length}/${DEEP_AGENTS.length} lens(es) crashed — ${roundDegradedLenses.join(', ')}; breadth coverage DEGRADED (exhaustion not confirmable by these lenses)`)
    }
    const valid = slices.filter(Boolean)
    const cons = await agent(consolidatePrompt(valid, round), { schema: FINDINGS, label: `consolidate r${round}`, phase: 'Review' })
    const consolidated = cons?.findings ?? valid.flatMap((s) => s.findings || [])
    enumInfo = await enumerateSurfaces(consolidated, round, { postFix: false })
    freshFindings = consolidated.concat(enumInfo.findings)
  } else {
    // --- Depth (rounds 2+): bounded validator + fix-diff + re-enumeration of the just-fixed surfaces.
    // A fix regression is rare and local (2/28 in the audited run, both caught the round they were born) —
    // a full re-sweep every round pays for coverage that already exists; breadth returns only at the gate. ---
    roundKind = 'depth'

    // 1. Validate the previous round's fix claims (scope closed to the ids)
    let stillOpen = []
    const fixedById = new Map((lastFixResult?.fixed || []).map((f) => [f.id, f.note || '']))
    const claims = lastHighBlocker.filter((f) => fixedById.has(f.id)).map((f) => ({ ...f, fixNote: fixedById.get(f.id) }))
    if (claims.length) {
      const val = await agent(validationPrompt(claims, lastFixResult, false), { schema: VALIDATION_RESULT, label: `validate r${round}`, phase: 'Review' })
      if (val) {
        const stillById = new Map((val.stillOpen || []).map((s) => [s.id, s.why]))
        stillOpen = claims.filter((c) => stillById.has(c.id)).map((c) => ({ ...c, description: `${String(c.description).slice(0, 280)} [validator: ${stillById.get(c.id)}]`.slice(0, 500), source: 'post-fix validator' }))
      } else {
        // validator died — re-enter everything to be safe; the conciliator verifies B/H in the code before posting
        stillOpen = claims.map((c) => ({ ...c, source: 'validator died — re-entered to be safe' }))
      }
    }

    // 2. Fix-diff review: only the delta of the fixer's commits (catches fix-introduced defects next round)
    let fdFindings = []
    const commits = lastFixResult?.commits || []
    if (commits.length) {
      const fd = await agent(fixDiffPrompt(commits, round), { schema: FINDINGS, label: `fixdiff r${round}`, phase: 'Review' })
      fdFindings = (fd?.findings ?? []).map((f) => ({ ...f, source: f.source || 'fix-diff' }))
    }

    // 3. Re-enumerate the surfaces of the previous round's B/H (the fixer just touched them)
    enumInfo = await enumerateSurfaces(lastHighBlocker, round, { postFix: true, commits })

    freshFindings = stillOpen.concat(fdFindings, enumInfo.findings)
  }

  // --- Conciliate (the only stage that reads the PR history; posts the consolidated review) ---
  const con = await agent(
    conciliatePrompt(freshFindings, state, round, mode, roundKind, Object.fromEntries(mediumAge)),
    { schema: CONSOLIDATED, label: `conciliate r${round}`, phase: 'Conciliate' }
  )
  const findings = con?.findings ?? freshFindings
  const highBlocker = findings.filter((f) => f.severity === 'Blocker' || f.severity === 'High')
  const mediumLow = findings.filter((f) => f.severity === 'Medium' || f.severity === 'Low')

  // --- Medium aging: ≥2 rounds in the queue → no longer optional for the fixer ---
  findings.filter((f) => f.severity === 'Medium').forEach((f) => mediumAge.set(f.id, (mediumAge.get(f.id) || 0) + 1))
  const agedMediums = findings.filter((f) => f.severity === 'Medium' && (mediumAge.get(f.id) || 0) >= 2)

  roundsSummary.push({
    round,
    kind: roundKind,
    blocker: findings.filter((f) => f.severity === 'Blocker').length,
    high: findings.filter((f) => f.severity === 'High').length,
    medium: findings.filter((f) => f.severity === 'Medium').length,
    low: findings.filter((f) => f.severity === 'Low').length,
    enumDry: enumInfo.dry,
    enumSurfaces: enumInfo.surfaces.length,
    agedMediums: agedMediums.length,
    degraded: roundDegradedLenses.length,
    commentUrl: con?.commentUrl,
  })
  log(`round ${round} (${roundKind}): ${highBlocker.length} High/Blocker · ${mediumLow.length} Medium/Low · enum ${enumInfo.dry ? 'dry' : 'NOT dry'} — ${con?.commentUrl || 'comment not confirmed'}`)

  // --- Dynamic cap: a Blocker with a never-seen id at the cap round = a fix-introduced regression; stopping now is worst ---
  const newBlockers = findings.filter((f) => f.severity === 'Blocker' && !seenIds.has(f.id))
  findings.forEach((f) => seenIds.add(f.id))
  if (round === effectiveMax && highBlocker.length > 0 && newBlockers.length > 0 && extensionsUsed < MAX_EXTENSIONS) {
    effectiveMax++
    extensionsUsed++
    log(`round ${round} (cap) found never-seen Blocker(s): ${newBlockers.map((b) => b.id).join(', ')} — cap extended to ${effectiveMax} (extension ${extensionsUsed}/${MAX_EXTENSIONS}); re-disputing an already-seen id does not extend`)
  }

  lastHighBlocker = highBlocker

  // --- Termination by exhaustion: 0 High/Blocker in a BREADTH round with a dry enumeration.
  // A depth round with 0 H/B only schedules the fresh-eyes gate — quiet is not exhausted. ---
  if (highBlocker.length === 0) {
    const exhausted = mode === 'standard' || (roundKind !== 'depth' && enumInfo.dry)
    if (exhausted) {
      if (mediumLow.length) {
        const fin = await agent(fixerPrompt(mediumLow, round, { finalRound: true }), { schema: FIX_RESULT, label: 'final-fix (medium/low)', phase: 'Fix' })
        if (fin?.status === 'done') {
          state.fixed.push(...(fin.fixed || []))
          state.diverged.push(...(fin.diverged || []))
          allCommits.push(...(fin.commits || []))
        } else {
          log(`final Medium/Low round stalled: ${fin?.blockedReason || 'no result'} — pending items documented on the PR`)
        }
      }
      // If the confirming round ran with crashed lenses, exhaustion only holds for the ones that ran —
      // clean-partial signals to the orchestrator that a confirmation pass over the lenses in lensFailures is missing.
      finalStatus = roundDegradedLenses.length ? 'clean-partial' : 'clean'
      break
    }
    pendingGate = true
    if (round === effectiveMax && gateExtensionUsed < 1) {
      effectiveMax++
      gateExtensionUsed++
      log(`round ${round} (${roundKind}) zeroed High/Blocker but exhaustion not confirmed — cap extended by +1 for the fresh-eyes gate`)
    } else if (round === effectiveMax) {
      log(`round ${round} zeroed High/Blocker but the fresh-eyes gate doesn't fit in the cap — loop ends capped with 0 open and exhaustion NOT confirmed`)
    } else {
      log(`round ${round} (${roundKind}) zeroed High/Blocker — next round is the fresh-eyes breadth gate`)
    }
    continue
  }

  // --- Fix Blocker/High + aged Mediums (on the last possible round: full verify) ---
  const assigned = highBlocker.concat(agedMediums.filter((m) => !highBlocker.some((h) => h.id === m.id)))
  const fix = await agent(
    fixerPrompt(assigned, round, {
      finalRound: false,
      mustFullVerify: round === effectiveMax,
      agedMediums,
      mediumsInSameFiles: mediumLow.filter((m) => !agedMediums.some((a) => a.id === m.id) && highBlocker.some((h) => h.file && h.file === m.file)),
    }),
    { schema: FIX_RESULT, label: `fix r${round}`, phase: 'Fix' }
  )
  if (!fix || fix.status === 'blocked') {
    finalStatus = 'blocked'
    blockedReason = fix?.blockedReason || 'fixer died with no result'
    break
  }
  lastFixResult = fix
  state.fixed.push(...(fix.fixed || []))
  state.diverged.push(...(fix.diverged || []))
  allCommits.push(...(fix.commits || []))
  // Fixed Mediums leave the aging queue
  ;(fix.fixed || []).forEach((f) => mediumAge.delete(f.id))
  log(`round ${round} fix: ${(fix.fixed || []).length} fixed, ${(fix.diverged || []).length} divergence(s), ${(fix.commits || []).length} commit(s)`)
}

// --- Post-cap reconciliation: the last review (pre-fix) and the last fix are different instants.
// A raw openHighBlocker from the cap round would report as open what the fixer just addressed.
let openHighBlocker = finalStatus === 'clean' || finalStatus === 'clean-partial' ? [] : lastHighBlocker
let fixedValidated = []
let fixedUnreviewed = []
let contestedUnreviewed = []
if (finalStatus === 'capped') {
  const lastFixedById = new Map((lastFixResult?.fixed || []).map((f) => [f.id, f.note || '']))
  const lastDivergedIds = new Set((lastFixResult?.diverged || []).map((d) => d.id))
  contestedUnreviewed = lastHighBlocker.filter((f) => lastDivergedIds.has(f.id))
  const claimedFixed = lastHighBlocker
    .filter((f) => lastFixedById.has(f.id))
    .map((f) => ({ ...f, fixNote: lastFixedById.get(f.id) }))
  openHighBlocker = lastHighBlocker.filter((f) => !lastFixedById.has(f.id) && !lastDivergedIds.has(f.id))

  if (claimedFixed.length) {
    // "fixed" is the fixer's claim — bounded validation (1 agent, scope closed to the ids), not a new review.
    const val = await agent(validationPrompt(claimedFixed, lastFixResult, true), { schema: VALIDATION_RESULT, label: 'fix-validation post-cap', phase: 'Fix' })
    if (val) {
      const resolvedById = new Map((val.resolved || []).map((r) => [r.id, r.evidence || '']))
      const stillOpenById = new Map((val.stillOpen || []).map((s) => [s.id, s.why]))
      fixedValidated = claimedFixed.filter((f) => resolvedById.has(f.id)).map((f) => ({ ...f, evidence: resolvedById.get(f.id) }))
      openHighBlocker = openHighBlocker.concat(
        claimedFixed.filter((f) => stillOpenById.has(f.id)).map((f) => ({ ...f, validatorNote: stillOpenById.get(f.id) }))
      )
      fixedUnreviewed = claimedFixed.filter((f) => !resolvedById.has(f.id) && !stillOpenById.has(f.id))
    } else {
      fixedUnreviewed = claimedFixed
    }
  }

  await agent(cappedCommentPrompt({ open: openHighBlocker, fixedValidated, fixedUnreviewed, contested: contestedUnreviewed }, roundsSummary, pendingGate), { schema: FINDINGS, label: 'capped-comment', phase: 'Conciliate' })
}

return {
  status: finalStatus,
  mode,
  rounds: roundsSummary,
  extensionsUsed,
  gateNeverRan: finalStatus === 'capped' && pendingGate,
  lensFailures, // [] if no lens crashed; non-empty ⇒ some breadth/gate round ran degraded (partial exhaustion)
  commits: allCommits,
  fixed: state.fixed,
  diverged: state.diverged,
  openHighBlocker,
  fixedValidated,
  fixedUnreviewed,
  contestedUnreviewed,
  blockedReason,
}
