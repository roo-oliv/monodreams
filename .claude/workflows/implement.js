export const meta = {
  name: 'implement',
  description: 'Wave-based implementation of an approved plan: fresh agent per wave + persistent ledger, verify and verify-plan at the end, opens the PR with documented decisions',
  whenToUse: 'Invoked by the /implement skill (.claude/skills/implement/SKILL.md) with an APPROVED plan. Do not invoke without a plan that has a Contract block.',
  phases: [
    { title: 'Setup', detail: 'validates branch, seeds/resumes ledger, splits the plan into waves, commits the deep-plan contract if present' },
    { title: 'Implement', detail: 'one wave at a time: fresh agent implements, incremental verify, commit, push, ledger' },
    { title: 'Verify', detail: 'full verify with fix loop (≤3) + verify-plan reconciliation + 1 fix round' },
    { title: 'PR', detail: 'rebase onto origin/<base>, push, gh pr create with an extensive body in the repo PR language' },
  ],
}

// args may arrive as a JSON-encoded string in some runtimes — defensive shim.
const ARGS = (() => {
  try { return typeof args === 'string' ? JSON.parse(args) : (args ?? {}) } catch (e) { return {} }
})()

const REQUIRED = ['planPath', 'branch', 'repoRoot', 'ledgerPath', 'timestamp']
const missingArgs = REQUIRED.filter((k) => !ARGS[k])
if (missingArgs.length) {
  return { status: 'blocked', stage: 'args', reason: `required args missing: ${missingArgs.join(', ')}` }
}

const BASE = ARGS.baseBranch || 'main'
const MAX_WAVES = Math.min(ARGS.maxWaves || 8, 12)
const ROLE_DIR = `${ARGS.repoRoot}/.claude/skills/implement/agents`
const CONFIG = 'docs/agents/skills-config.md'

// ---------------------------------------------------------------------------
// Schemas
// ---------------------------------------------------------------------------

const DECISION = {
  type: 'object',
  required: ['point', 'chosen', 'why'],
  properties: {
    point: { type: 'string', maxLength: 240 },
    options: { type: 'array', items: { type: 'string', maxLength: 160 }, maxItems: 4 },
    chosen: { type: 'string', maxLength: 240 },
    why: { type: 'string', maxLength: 240 },
  },
}

const SETUP_RESULT = {
  type: 'object',
  required: ['branchOk', 'resumed', 'waves'],
  properties: {
    branchOk: { type: 'boolean' },
    resumed: { type: 'boolean', description: 'true if a pre-existing ledger with completed waves was found' },
    waves: {
      type: 'array',
      maxItems: 12,
      description: 'ONLY pending waves (on a resume, exclude the ones already completed in the ledger)',
      items: {
        type: 'object',
        required: ['id', 'title', 'goal'],
        properties: {
          id: { type: 'integer' },
          title: { type: 'string', maxLength: 80 },
          goal: { type: 'string', maxLength: 400 },
          contractItems: { type: 'array', items: { type: 'string', maxLength: 200 }, maxItems: 15 },
          files: { type: 'array', items: { type: 'string', maxLength: 200 }, maxItems: 20 },
        },
      },
    },
    planSummary: { type: 'string', maxLength: 600 },
    prTitle: { type: 'string', maxLength: 100, description: 'PR title in the repo commit language (Conventional Commits if config says so)' },
    contractCommitted: { type: 'boolean', description: 'true if a deep-plan contract was committed under .claude/deep-plan/' },
    blockedReason: { type: 'string', maxLength: 400 },
  },
}

const WAVE_RESULT = {
  type: 'object',
  required: ['status', 'untestedItems'],
  properties: {
    status: { type: 'string', enum: ['done', 'blocked'] },
    commitShas: { type: 'array', items: { type: 'string', maxLength: 40 }, maxItems: 10 },
    decisions: { type: 'array', items: DECISION, maxItems: 10 },
    testEvidence: {
      type: 'array',
      maxItems: 15,
      description: 'named test(s) per wave contract item, or na justification',
      items: { type: 'object', required: ['item'], properties: { item: { type: 'string', maxLength: 200 }, tests: { type: 'array', items: { type: 'string', maxLength: 160 }, maxItems: 5 }, na: { type: 'string', maxLength: 200 } } },
    },
    untestedItems: { type: 'array', items: { type: 'string', maxLength: 240 }, maxItems: 15, description: 'items without a test and without justification — gate: must be empty to close the wave' },
    handoff: { type: 'string', maxLength: 300 },
    blockedReason: { type: 'string', maxLength: 500 },
  },
}

const VERIFY_RESULT = {
  type: 'object',
  required: ['green'],
  properties: {
    green: { type: 'boolean' },
    commits: { type: 'array', items: { type: 'string', maxLength: 40 }, maxItems: 10 },
    failingSummary: { type: 'string', maxLength: 1500, description: 'tests/steps still failing, telegraphic' },
  },
}

const RECON_RESULT = {
  type: 'object',
  required: ['missing', 'diverged', 'unplanned', 'untestedPremises'],
  properties: {
    missing: { type: 'array', maxItems: 20, items: { type: 'object', required: ['item'], properties: { item: { type: 'string', maxLength: 240 }, note: { type: 'string', maxLength: 240 } } } },
    diverged: { type: 'array', maxItems: 20, items: { type: 'object', required: ['item'], properties: { item: { type: 'string', maxLength: 240 }, planned: { type: 'string', maxLength: 200 }, actual: { type: 'string', maxLength: 200 }, file: { type: 'string', maxLength: 160 } } } },
    unplanned: { type: 'array', maxItems: 20, items: { type: 'object', required: ['file'], properties: { file: { type: 'string', maxLength: 160 }, note: { type: 'string', maxLength: 240 } } } },
    untestedPremises: { type: 'array', maxItems: 15, items: { type: 'object', required: ['premise', 'file'], properties: { premise: { type: 'string', maxLength: 200 }, file: { type: 'string', maxLength: 160 } } } },
  },
}

const PR_RESULT = {
  type: 'object',
  required: ['status'],
  properties: {
    status: { type: 'string', enum: ['pr-opened', 'blocked', 'blocked-gate'] },
    prNumber: { type: 'integer' },
    prUrl: { type: 'string', maxLength: 200 },
    rebased: { type: 'boolean', description: 'true if the rebase brought new commits from the base' },
    blockedReason: { type: 'string', maxLength: 600 },
  },
}

// ---------------------------------------------------------------------------
// Prompt helpers
// ---------------------------------------------------------------------------

function ctx() {
  return [
    `Repo: ${ARGS.repoRoot} (work ONLY inside it; use rg, never grep -r; do not read /tmp, ~, or sibling worktrees).`,
    `Read ${CONFIG} for this repo's stack-specific inputs (Verify command, Sensitive domains, Docs layout, Conventions). If a section is absent, fall back to the stated default and say so.`,
    `Working branch: ${ARGS.branch} (base: origin/${BASE}). Mandatory guard: git branch --show-current must return exactly this; if it diverges, return blocked immediately.`,
    `Push authorized ONLY via "git push origin ${ARGS.branch}". Never force (except --force-with-lease post-rebase, PR author only), never ${BASE}.`,
    `Approved plan: ${ARGS.planPath}. Ledger: ${ARGS.ledgerPath} (gitignored — lives in .claude/.implement/, outside versioning; NEVER commit it).`,
    `Your final text is parsed as structured data by the orchestrator — follow the schema, no extra prose.`,
  ].join('\n')
}

function setupPrompt() {
  return `${ctx()}

You are the SETUP agent of the /implement workflow.

1. Confirm the branch guard and that the working tree is clean (git status --porcelain).
2. Read the plan (${ARGS.planPath}) in full — prose + "## Contract" (+ interaction matrix / precondition diff if present).
3. Ledger: if ${ARGS.ledgerPath} already exists, read it and PRESERVE all existing content — especially the "## Directives (from the user)" section, which is the orchestrator/user injection channel and takes precedence over agents' autonomous decisions. It is a RESUME (resumed=true) only if there are waves marked completed with commits; a ledger seeded with only a header/directives is NOT a resume. If it doesn't exist, create it (create the directory if needed) with:
   - plan: ${ARGS.planPath} | branch: ${ARGS.branch} | started: ${ARGS.timestamp}
   - sections "## Directives (from the user)" (empty if none), "## Waves" (checklist), "## Decisions", "## Handoffs".
4. ${ARGS.deepPlanContractPath ? `Deep-plan contract: ${ARGS.deepPlanContractPath}. If not yet committed on the branch under .claude/deep-plan/, copy it there (name <branch-with-/-as-->-<shortSha>.md), git add + commit (a chore commit adding the deep-plan plan-contract, per the repo's commit conventions in ${CONFIG} › Conventions) + push. It is the artifact the PR gate hook reads on a sensitive-domain branch.` : 'No deep-plan contract to commit.'}
5. Split the plan into AT MOST ${MAX_WAVES} PENDING waves, in dependency order: by the plan's phase structure if it has one, otherwise by logical commit groups (schema/migration + entity → service/logic → wiring/listeners → tests — adapt to the plan and to the repo's commit conventions). Each wave: id, title, goal (what will be true at the end), contractItems (numbers/text from the Contract it covers — EVERY Contract item must belong to exactly one wave), files (initial guess). Waves small enough for a fresh agent to finish without blowing context (~≤8 new/changed files per wave as a rule of thumb).
6. Record the wave split in the ledger.

If anything blocks (wrong branch, plan with no Contract, dirty tree), return branchOk=false/blockedReason. Also suggest the prTitle (per the repo's commit conventions in ${CONFIG} › Conventions)${ARGS.prTitleHint ? ` — user hint: "${ARGS.prTitleHint}"` : ''}.`
}

function wavePrompt(wave, retryReason) {
  return `${ctx()}

Read ${ROLE_DIR}/wave-implementer.md and operate as that agent.

Your wave:
${JSON.stringify(wave, null, 2)}
${retryReason ? `\nRETRY: the previous attempt of this wave failed with: "${retryReason}". Read the ledger and git log to see what it left half-done before continuing — there may be uncommitted or partially committed work.` : ''}`
}

function verifyPrompt(attempt, previousFailing) {
  return `${ctx()}

You are the VERIFY agent (attempt ${attempt} of 3) of the /implement workflow. All waves have committed; your job is to leave the whole build green.

Run the repo's FULL Verify command (${CONFIG} › Verify): format → lint → tests targeted at the branch diff (git diff --name-only origin/${BASE}...HEAD) → full build/test, plus the always-run gates listed there. Tee long output to a file and read failures from there if the output overflows. If config is absent, ask the user for the format/lint/build/test command.
${previousFailing ? `Pending failures from the previous attempt: ${previousFailing}` : ''}

Fix the failures you find (surgical changes; follow the repo's CLAUDE.md and conventions), commit (a fix commit per the repo's commit conventions — "post-implementation verify fixes" or more specific) + push per group of fixes. If the repo's test conventions / repo memory document known flaky tests, confirm with an isolated re-run before treating a failure as a regression.

green=true ONLY with the full Verify command passing end to end. If you can't, green=false + a precise failingSummary.`
}

function reconPrompt(isRecheck) {
  return `${ctx()}

You are the VERIFY-PLAN agent${isRecheck ? ' (RE-CHECK post-fix)' : ''} — plan↔diff reconciliation with fresh eyes. You implemented nothing; do not assume intent.

Read ${ARGS.repoRoot}/.claude/skills/verify-plan/SKILL.md and apply its Phase 1 (coverage / fidelity / scope-creep), with:
- Plan: ${ARGS.planPath} (use the "## Contract"; include the interaction matrix and precondition diff if present).
- Diff: git fetch origin ${BASE} && git diff origin/${BASE}...HEAD (everything is already committed).

Bounded: this is NOT a bug hunt. Item by item against the contract.

4th MECHANICAL bucket — untestedPremises: run git diff origin/${BASE}...HEAD over the branch's premises files (use the premises path pattern from ${CONFIG} › Docs layout; default 'docs/**/premises.md' if absent) and list every premise added/changed on this branch whose **Tests:** field is "none yet" or absent. For a premise introduced in the PR this is a merge gate (per the repo's premises rule), not a suggestion.

Return the four structured buckets; empty if clean.`
}

function reconFixPrompt(recon) {
  return `${ctx()}

Read ${ROLE_DIR}/wave-implementer.md and operate as that agent, with one difference: your "wave" is the verify-plan findings below. Resolve each Missing (implement the contracted item) and each Diverged (align the code to the contract — if the deviation is deliberate and superior, keep the code and record it as a decision with the why). Unplanned: assess; remove if it's value-less scope-creep, record as a decision if it stays. UntestedPremises: write the test that protects each premise (per the repo's test conventions in ${CONFIG} › Conventions — a test that BREAKS if the premise is violated, not a happy path) and fill its **Tests:** field. Incremental verify + commit + push + ledger as usual.

Findings:
${JSON.stringify(recon, null, 2)}`
}

function prPrompt(setup, decisions, residual, mustFullBuild, testEvidence) {
  return `${ctx()}

Read ${ROLE_DIR}/pr-author.md and operate as that agent.

- Suggested title: ${setup.prTitle || ARGS.prTitleHint || '(derive from the plan)'}
- Plan summary: ${setup.planSummary || '(read the plan)'}
- Plan origin (link in the Context section if it's an issue/Jira/Slack): see the "Origin" header of the plan file.
- Basis for the test-plan section: the tests named per contract item below (mark [x] those the verify already ran; items with an na justification become a note, not a checkbox):
${JSON.stringify(testEvidence, null, 2)}
- Autonomous decisions (mandatory in the body, one by one):
${JSON.stringify(decisions, null, 2)}
- verify-plan residuals for the "What this PR does NOT cover" section (empty = omit the section):
${JSON.stringify(residual, null, 2)}
- ${mustFullBuild ? `There were post-verify fixes: run the repo's full Verify command (${CONFIG} › Verify) before opening the PR (in addition to the rebase rule in the role file).` : 'Build already verified; run the full Verify command only if the rebase brought changes from the base (role-file rule).'}`
}

// ---------------------------------------------------------------------------
// Phases
// ---------------------------------------------------------------------------

phase('Setup')
const setup = await agent(setupPrompt(), { schema: SETUP_RESULT, label: 'setup' })
if (!setup) return { status: 'blocked', stage: 'setup', reason: 'setup agent died without a result' }
if (!setup.branchOk) return { status: 'blocked', stage: 'setup', reason: setup.blockedReason || 'invalid branch/preflight' }

const waves = (setup.waves || []).slice(0, MAX_WAVES)
log(`${setup.resumed ? 'RESUME — ' : ''}${waves.length} pending wave(s): ${waves.map((w) => `${w.id}:${w.title}`).join(' · ')}`)
if (!waves.length && !setup.resumed) return { status: 'blocked', stage: 'setup', reason: 'setup produced no waves' }

phase('Implement')
const allDecisions = []
const allTestEvidence = []
const waveReports = []
let blocked = null
// closing gate: done with a non-empty untestedItems does NOT close the wave (untested commitments are the cheapest review findings to prevent at the source)
const closeFailure = (r) => {
  if (!r) return 'no result'
  if (r.status !== 'done') return r.blockedReason || 'blocked without reason'
  if ((r.untestedItems || []).length) return `test gate: items without a test or justification — ${r.untestedItems.join(' | ')}. Write the tests (or justify na in testEvidence) to close the wave.`
  return null
}
for (const wave of waves) {
  let res = await agent(wavePrompt(wave), { schema: WAVE_RESULT, label: `wave-${wave.id}: ${wave.title}`, phase: 'Implement' })
  let failure = closeFailure(res)
  if (failure) {
    log(`wave ${wave.id} did not close (${failure.slice(0, 160)}) — single retry with a fresh agent`)
    res = await agent(wavePrompt(wave, failure), { schema: WAVE_RESULT, label: `wave-${wave.id}-retry`, phase: 'Implement' })
    failure = closeFailure(res)
  }
  if (failure) {
    blocked = { wave: wave.id, title: wave.title, reason: failure }
    break
  }
  allDecisions.push(...(res.decisions || []))
  allTestEvidence.push(...(res.testEvidence || []))
  waveReports.push({ wave: wave.id, title: wave.title, commits: res.commitShas || [], handoff: res.handoff || 'none' })
  log(`wave ${wave.id} done: ${(res.commitShas || []).length} commit(s), ${(res.decisions || []).length} decision(s), ${(res.testEvidence || []).length} item(s) with a named test`)
}
if (blocked) {
  return { status: 'blocked', stage: 'implement', blocked, wavesDone: waveReports, decisions: allDecisions, note: 'work of the completed waves is committed and pushed on the branch' }
}

phase('Verify')
let green = false
let failingSummary = ''
for (let attempt = 1; attempt <= 3 && !green; attempt++) {
  const v = await agent(verifyPrompt(attempt, failingSummary), { schema: VERIFY_RESULT, label: `verify-${attempt}`, phase: 'Verify' })
  green = !!v?.green
  failingSummary = v?.failingSummary || ''
  if (!green) log(`verify attempt ${attempt}: still red — ${failingSummary.slice(0, 200)}`)
}
if (!green) {
  return { status: 'verify-failed', stage: 'verify', failingSummary, wavesDone: waveReports, decisions: allDecisions, note: 'branch pushed with a red build — do NOT open a PR' }
}

let recon = await agent(reconPrompt(false), { schema: RECON_RESULT, label: 'verify-plan', phase: 'Verify' })
let reconFixed = false
if (recon && ((recon.missing || []).length || (recon.diverged || []).length || (recon.untestedPremises || []).length)) {
  log(`verify-plan: ${(recon.missing || []).length} missing, ${(recon.diverged || []).length} diverged, ${(recon.untestedPremises || []).length} premise(s) without a test — 1 fix round`)
  const fix = await agent(reconFixPrompt(recon), { schema: WAVE_RESULT, label: 'recon-fix', phase: 'Verify' })
  if (fix?.decisions) allDecisions.push(...fix.decisions)
  reconFixed = true
  recon = await agent(reconPrompt(true), { schema: RECON_RESULT, label: 'verify-plan-recheck', phase: 'Verify' })
}
const residual = recon || { missing: [], diverged: [], unplanned: [] }

phase('PR')
const pr = await agent(prPrompt(setup, allDecisions, residual, reconFixed, allTestEvidence), { schema: PR_RESULT, label: 'pr-author', phase: 'PR' })
return {
  status: pr?.status || 'blocked',
  stage: 'pr',
  prNumber: pr?.prNumber,
  prUrl: pr?.prUrl,
  rebased: pr?.rebased || false,
  blockedReason: pr?.blockedReason,
  resumed: setup.resumed,
  wavesDone: waveReports,
  decisions: allDecisions,
  testEvidence: allTestEvidence,
  verifyPlanResiduals: residual,
}
