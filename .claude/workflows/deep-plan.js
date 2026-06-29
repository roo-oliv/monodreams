export const meta = {
  name: 'deep-plan',
  description: 'Fill and adversarially refute a plan-contract (matrix, dimension table, precondition diff) from design intent + the live codebase, gating on completeness before synthesis.',
  whenToUse: 'Heavy pass of the /deep-plan skill: a sensitive-domain change that adds/modifies a status/lifecycle or replaces a flow. Invoked by .claude/skills/deep-plan/SKILL.md.',
  phases: [
    { title: 'Enumerate', detail: 'grep caller-enumerated matrix columns + state-mutation seams + guard copies' },
    { title: 'Analyze', detail: 'dimension table, matrix cells, premise/test obligations' },
    { title: 'Refute', detail: 'anchoring-free refuters loop-until-dry; resolver reopens cells/rows' },
    { title: 'Gate', detail: 'programmatic completeness: no empty/unjustified cell, every load-bearing premise executable, every guard copy diffed' },
    { title: 'Synthesize', detail: 'deterministic verdict header + lossless artifacts (rendered in JS); LLM writes only the narrative synthesis' },
  ],
}

// =====================================================================
// Structured-output schemas (one per specialist) — gates read these, so
// they are programmatic, not prose.
// =====================================================================

const BASE_ENUM = ['face', 'residual', 'principal-only', 'with-interest', 'net-of-reserved', 'discount-net', 'other']

const DIMENSION_TABLE = {
  type: 'object',
  required: ['rows', 'dimensionalChecks', 'violations'],
  properties: {
    rows: {
      type: 'array',
      items: {
        type: 'object',
        required: ['variable', 'unitBase', 'cap', 'seam'],
        properties: {
          variable: { type: 'string' },
          unitBase: { type: 'string', enum: BASE_ENUM },
          cap: { type: 'string', description: 'upper bound or "none"' },
          seam: { type: 'string', description: 'require/check location, or "MISSING — add at <seam>"' },
        },
      },
    },
    dimensionalChecks: {
      type: 'array',
      items: {
        type: 'object',
        required: ['expression', 'consistent'],
        properties: {
          expression: { type: 'string' },
          consistent: { type: 'boolean' },
          note: { type: 'string' },
        },
      },
    },
    violations: {
      type: 'array',
      items: {
        type: 'object',
        required: ['variable', 'issue'],
        properties: {
          variable: { type: 'string' },
          issue: { type: 'string', enum: ['untagged-base', 'uncapped', 'wrong-base', 'no-seam'] },
          resolution: { type: 'string', maxLength: 240 },
        },
      },
    },
  },
}

const MATRIX_COLUMNS = {
  type: 'object',
  required: ['states', 'columns'],
  properties: {
    states: { type: 'array', items: { type: 'string' }, description: 'each new status/state the intent introduces' },
    columns: {
      type: 'array',
      items: {
        type: 'object',
        required: ['name', 'site', 'reads'],
        properties: {
          name: { type: 'string' },
          site: { type: 'string', description: 'file:line of the reader/transition' },
          reads: { type: 'string', maxLength: 200, description: 'what state it reads' },
          loadBearing: { type: 'string' },
        },
      },
    },
    missedByMemory: { type: 'array', items: { type: 'string' } },
  },
}

const SETTLEMENT_SEAMS = {
  type: 'object',
  required: ['seams', 'requiredChecks', 'copiedGuards'],
  properties: {
    seams: {
      type: 'array',
      items: {
        type: 'object',
        required: ['seam', 'distributionBase', 'filtersNewRecord', 'status'],
        properties: {
          seam: { type: 'string' },
          distributionBase: { type: 'string' },
          filtersNewRecord: { type: 'boolean' },
          capSeam: { type: 'string' },
          status: { type: 'string', enum: ['ok', 'GAP'] },
        },
      },
    },
    requiredChecks: {
      type: 'array',
      items: {
        type: 'object',
        required: ['invariant', 'seam', 'exists'],
        properties: {
          invariant: { type: 'string' },
          seam: { type: 'string' },
          exists: { type: 'boolean' },
        },
      },
    },
    copiedGuards: {
      type: 'array',
      items: {
        type: 'object',
        required: ['predicate', 'copies'],
        properties: {
          predicate: { type: 'string' },
          copies: { type: 'array', items: { type: 'string' }, description: 'file:line of each copy' },
        },
      },
    },
  },
}

const MATRIX_CELLS = {
  type: 'object',
  required: ['cells'],
  properties: {
    cells: {
      type: 'array',
      items: {
        type: 'object',
        required: ['state', 'column', 'verdict'],
        properties: {
          state: { type: 'string' },
          column: { type: 'string' },
          verdict: { type: 'string', enum: ['handled', 'N/A', 'GAP'] },
          where: { type: 'string', maxLength: 240, description: 'required for handled' },
          justification: { type: 'string', maxLength: 240, description: 'required for N/A and GAP' },
        },
      },
    },
  },
}

const PREMISE_OBLIGATIONS = {
  type: 'object',
  required: ['tests', 'premises', 'drift'],
  properties: {
    tests: {
      type: 'array',
      items: {
        type: 'object',
        required: ['scenario', 'level', 'catches'],
        properties: {
          scenario: { type: 'string', maxLength: 280 },
          level: { type: 'string', enum: ['e2e', 'integration', 'unit'] },
          catches: { type: 'string' },
        },
      },
    },
    premises: {
      type: 'array',
      items: {
        type: 'object',
        required: ['premise', 'seam', 'failingTest', 'status'],
        properties: {
          premise: { type: 'string', maxLength: 240 },
          seam: { type: 'string', description: 'require/check location' },
          failingTest: { type: 'string' },
          status: { type: 'string', enum: ['new', 'existing', 'none-yet'] },
        },
      },
    },
    drift: {
      type: 'array',
      items: {
        type: 'object',
        required: ['doc', 'what'],
        properties: { doc: { type: 'string' }, what: { type: 'string' } },
      },
    },
  },
}

const REFUTATION = {
  type: 'object',
  required: ['refutations', 'survived', 'missingColumns'],
  properties: {
    refutations: {
      type: 'array',
      items: {
        type: 'object',
        required: ['surface', 'target', 'scenario', 'forces', 'attacks'],
        properties: {
          surface: { type: 'string', enum: ['unhandled-scenario', 'dimension-violation', 'precondition-false', 'missing-column'] },
          target: { type: 'string', maxLength: 240, description: 'the cell / dimension row / precondition row attacked' },
          scenario: { type: 'string', maxLength: 280 },
          evidence: { type: 'string', maxLength: 200, description: 'file:line' },
          forces: { type: 'string', maxLength: 200, description: 'which cell/row must be reopened' },
          attacks: { type: 'string', enum: ['resolution', 'new-surface'], description: "'resolution' = breaks a fill/fix the draft already contains; 'new-surface' = names territory the draft lacks" },
        },
      },
    },
    survived: {
      type: 'array',
      items: {
        type: 'object',
        properties: { target: { type: 'string' }, checked: { type: 'string' } },
      },
    },
    missingColumns: {
      type: 'array',
      items: {
        type: 'object',
        properties: { site: { type: 'string' }, reads: { type: 'string' } },
      },
    },
  },
}

const PRECONDITION_ROW = {
  type: 'object',
  required: ['guard', 'copy', 'oldPrecondition', 'newReality', 'resolution'],
  properties: {
    guard: { type: 'string', description: 'the predicate, matching a copiedGuards predicate' },
    copy: { type: 'string', description: 'file:line of this copy' },
    oldPrecondition: { type: 'string', maxLength: 240 },
    newReality: { type: 'string', maxLength: 240 },
    resolution: { type: 'string', maxLength: 240 },
  },
}

// The resolver / consolidator return the full draft so gates run over one object.
const DRAFT = {
  type: 'object',
  required: ['contract', 'matrix', 'dimension', 'precondition', 'premises'],
  properties: {
    contract: { type: 'array', items: { type: 'string', maxLength: 280 } },
    matrix: {
      type: 'object',
      required: ['columns', 'states', 'cells'],
      properties: {
        columns: { type: 'array', items: { type: 'string' } },
        states: { type: 'array', items: { type: 'string' } },
        cells: MATRIX_CELLS.properties.cells,
      },
    },
    dimension: {
      type: 'object',
      required: ['rows', 'violations'],
      properties: {
        rows: DIMENSION_TABLE.properties.rows,
        violations: DIMENSION_TABLE.properties.violations,
      },
    },
    precondition: { type: 'array', items: PRECONDITION_ROW },
    premises: PREMISE_OBLIGATIONS.properties.premises,
  },
}

// A PATCH the resolver returns instead of the full draft for the ITERATIVE steps
// (refute-resolve, gate-justify). Re-emitting the whole — and growing — draft
// every round is what crashed the run on the 64k output-token ceiling: a
// round-5 refuter found its strongest refutation (a creation-time amortization
// gap) and the resolver then returned nothing, silently dropping it. The resolver
// now emits only what it adds/changes; applyPatch() merges by key. The merge is
// additive/override-only — it can never delete a filled cell/row, so a thin or
// malformed patch degrades to "gate still fails", never to silent corruption.
const DRAFT_PATCH = {
  type: 'object',
  properties: {
    columnsAdd: { type: 'array', items: { type: 'string' } },
    statesAdd: { type: 'array', items: { type: 'string' } },
    cellsUpsert: { type: 'array', items: MATRIX_CELLS.properties.cells.items },
    dimensionRowsUpsert: { type: 'array', items: DIMENSION_TABLE.properties.rows.items },
    violationsResolved: {
      type: 'array',
      items: {
        type: 'object',
        required: ['variable', 'resolution'],
        properties: {
          variable: { type: 'string' },
          issue: { type: 'string' },
          resolution: { type: 'string', maxLength: 240 },
        },
      },
    },
    premisesUpsert: { type: 'array', items: PREMISE_OBLIGATIONS.properties.premises.items },
    preconditionUpsert: { type: 'array', items: PRECONDITION_ROW },
    contractAdd: { type: 'array', items: { type: 'string', maxLength: 280 } },
  },
}

// =====================================================================
// Helpers
// =====================================================================

// This runtime delivers the Workflow `args` as a JSON-ENCODED STRING, not an
// object (probed: `Workflow({args:{intent}})` arrives as the literal
// string `'{"intent":...}'`). Reading `args.intent` off a string yields
// `undefined`, which silently zeroes the DESIGN INTENT and runs the whole heavy
// pass on an empty brief — an 870k-token run was wasted exactly this way before
// this shim. Normalize defensively so it works whether args is delivered as a
// string or an object. (The Workflow tool's own doc warns a stringified value
// reaches the script verbatim; this is the matching guard on the read side.)
const ARGS = (typeof args === 'string' && args.trim())
  ? (() => { try { return JSON.parse(args) } catch { return {} } })()
  : (args || {})

const intent = ARGS.intent || ''
const domains = ARGS.domains || []
const economy = !!ARGS.economy
const noThrow = !!ARGS.noThrow
const seedDraft = ARGS.seedDraft
// The repo root the skill is running in. Every agent search MUST stay inside it
// (and inside .gitignore) — never /tmp, never sibling worktrees. See ctx().
const repoRoot = ARGS.repoRoot || '(the current repo root / cwd)'
// 4 lensed refuters / 3 rounds. History: a 4x5 run reached the full r1+r2 cluster
// union in one run — but at ~1.8x cost, STILL no convergence (flat 16/12/15/15/15),
// and a gate of 374->0. The post-mortem found the real bug was label drift, not
// breadth: every extra refuter wrote cells under its own (state,column) label variant
// ("S1" vs "S1 PERFORMANCE_BONUS_RETAINED"), so the exact-string gate saw 374
// phantom-empty grid pairs and the `refKey` dedup never fired (a variant label = a new
// key = "fresh" forever -> the loop CAN'T converge). More refuters amplified the drift
// (gate violations track refuter count: 6->1, 6->84, 20->374). The fix is `normCode`
// keying (below) — once labels collapse by code the gate is honest at ANY breadth. With
// that landed AND canonicalizeMatrixStates collapsing the state-axis drift, the amplifier
// is fixed twice over and a coherent matrix is confirmed — so breadth is now re-raised
// DELIBERATELY, as that post-mortem invited. DRY_ROUNDS_TO_STOP=2 early-exits converged plans.
//
// Breadth = 4 (was 2). The FLAT 11/11/8 trajectory showed coverage-per-round — the number
// of distinct attack angles launched — was the binding constraint, NOT round depth (which
// doesn't converge). Refuters run in PARALLEL (well under the concurrency cap), so breadth
// is ~free on wall-clock: it trades tokens for coverage. Each refuter takes a DISTINCT lens
// (REFUTER_LENSES) so N refuters give N near-independent surfaces, not N collisions on the
// same targets. The ceiling is the resolver patch size (all fresh funnel into ONE patch ->
// StructuredOutput bloat), not thinking — so this is a band, not "more is better".
// ARGS.refutersPerRound overrides per-run for a big change.
const REFUTERS_PER_ROUND = Math.max(1, ARGS.refutersPerRound || 4)
const REFUTER_LENSES = [
  'QUANTITY / dimensional: attack the derived values — a value combined at the wrong base (e.g. face vs residual vs principal-only vs with-interest for money, or a window-bounded sum vs an instantaneous snapshot for a count), an uncapped settle/mint/derivation, a balance or Σ=0 invariant checked against a SELF-REFERENTIAL sum, a double-count across legs.',
  'ASYNC / ordering / lifecycle: attack timing — a post-commit write lost to a same-transaction join, a crash window between ack and commit, a non-idempotent re-fire / double-action, a two-clock lag, a predicate that must read live status-as-of-event but reads it once.',
  'PREMISE / invariant: attack the stated invariants — a path that violates an immutability / sum / identity premise or a CORE TENET, a load-bearing invariant with no require/check at its seam, a premise the new code introduces but never protects.',
  'COMPLETENESS / wrong-cell: attack the matrix itself — a cell marked handled/N·A that is actually a GAP, a column or state the matrix never enumerated, a copied guard whose precondition is now false for the new caller set.',
]
const MAX_REFUTE_ROUNDS = 3
const DRY_ROUNDS_TO_STOP = 2
// Targeted refuters for the resolution re-refute pass (after the loop ends at the round
// cap, the FINAL round's resolver patch ships unattacked — every earlier patch was
// re-attacked by the next round). Half breadth: the target set is a handful of named
// resolutions, not the whole draft. The blind spot was priced post-code: resolutions
// minted late in refute became a Blocker + Highs that a downstream review found at ~one
// facet per round.
const RESOLUTION_REREFUTERS = Math.max(1, ARGS.reRefuters || 2)
// Gate-justify passes. A refuter that adds a column/state grows the cartesian, leaving
// new (state×column) pairs empty; ONE justify pass left them empty on a real run,
// the gate stayed FAIL, and the run threw away ~3M tokens. Each pass is fed the
// deterministic missing-cell list (missingCells) so it fills the whole grown row/column,
// and a residual after the loop is recorded (NOT thrown) — deep-plan never blocks.
const GATE_JUSTIFY_ROUNDS = 2

// Economy-mode model routing (mirrors the SKILL.md table). Full mode omits the
// model so each agent inherits the session model.
function mdl(role) {
  if (!economy) return undefined
  const eco = { a1: 'opus', a2: 'sonnet', a3: 'opus', a4: 'sonnet', a5: 'sonnet', a6: 'opus', resolver: 'opus', consolidate: 'opus' }
  return eco[role]
}

// Shared Phase-1 preamble. Agents have file tools and must read the docs
// themselves — the intent is the analogue of deep-review's diff. Doc paths are
// NOT hardcoded: agents read the repo's `docs/agents/skills-config.md` (the file
// `setup` wrote) for where core-tenets / premises / schema / planning docs live,
// and fall back to the canonical defaults when a section is absent.
function ctx(roleFile) {
  return [
    `Read \`.claude/skills/deep-plan/agents/${roleFile}\` and operate as that specialist.`,
    `There is NO diff — the codebase on disk is the CURRENT (pre-change) state. Use file tools to discover the real callers, guards, readers, and seams the intent will collide with.`,
    ``,
    `SEARCH HYGIENE (mandatory — violations have hung runs for >1 hour):`,
    `- Use \`rg\` (ripgrep), NEVER \`grep -r\`/\`grep -rln\`. \`rg\` honors .gitignore, so it skips build output, the VCS dir, and worktrees — a bare \`grep -r\` traverses build artifacts and every SIBLING worktree and can hang for tens of minutes.`,
    `- Stay INSIDE this repo root: \`${repoRoot}\`. Never read or search \`/tmp\`, \`..\`, \`~\`, or any absolute path outside it. Pass scoped paths to \`rg\` (e.g. \`rg PATTERN <source roots>\`).`,
    `- One simple command per Bash call. No fragile compound one-liners (\`rg … | head; echo; find …\`) — an unbalanced quote makes the shell hang waiting on stdin.`,
    `- If you query a DB/MCP, bound it (LIMIT, narrow filters). You are analyzing CODE; don't run heavy unbounded prod queries.`,
    ``,
    `CONTAMINATION GUARD: the ONLY source of truth for what is being proposed is the DESIGN INTENT below. IGNORE any plan-contract, intent file, \`deep-plan-*.md\`, or cached JSON you encounter on disk — they belong to other runs/projects and will anchor you to the wrong feature. Do not let an on-disk artifact override the intent below.`,
    ``,
    `Phase-1 context to read (inside this repo only). Read \`docs/agents/skills-config.md\` › Docs layout for the exact paths and the premises {domain}/{module} pattern, then read: the recurring-failure-modes doc (config › Docs layout › Planning — MANDATORY when present: every matching entry is a question you must answer), the core-tenets doc (config › Docs layout › Core tenets; default \`docs/CORE_TENETS.md\`), the plan-contract spec (config › Docs layout › Planning; default \`docs/planning/plan-contract.md\`), and for each affected domain its schema doc + premises (default \`docs/schema/{domain}.md\` + \`docs/{domain}/premises.md\`). If a config section is absent, use these defaults and note it.`,
    ``,
    `Affected domains: ${domains.join(', ') || '(infer from the intent)'}.`,
    ``,
    `OUTPUT MUST BE TERSE — large structured outputs crash serialization. Every field: file:line + one clause, <= 240 chars, no paragraphs, no restating the intent.`,
    ``,
    `=== DESIGN INTENT (what is being proposed — the single source of truth) ===`,
    intent,
    `=== END DESIGN INTENT ===`,
    ``,
    `Return the structured output your role file describes. Do NOT post anywhere — return data to the orchestrator.`,
  ].join('\n')
}

// Compact, reasoning-free serialization of the draft — this is ALL the refuter
// sees (plus the intent + its own codebase access). It must not include agent
// notes or the analysis that produced the draft.
function draftSummary(d) {
  const cells = (d.matrix.cells || []).map(c => `  - [${c.state}] x [${c.column}] => ${c.verdict}${c.where ? ' @ ' + c.where : ''}${c.justification ? ' (' + c.justification + ')' : ''}`).join('\n')
  const dim = (d.dimension.rows || []).map(r => `  - ${r.variable}: base=${r.unitBase}, cap=${r.cap}, seam=${r.seam}`).join('\n')
  const pre = (d.precondition || []).map(p => `  - guard \`${p.guard}\` @ ${p.copy}: old=${p.oldPrecondition} | new=${p.newReality} | resolution=${p.resolution}`).join('\n')
  return [
    `INTERACTION MATRIX (states: ${(d.matrix.states || []).join(', ')}; columns: ${(d.matrix.columns || []).join(', ')}):`,
    cells || '  (no cells)',
    ``,
    `DIMENSION TABLE:`,
    dim || '  (no rows)',
    ``,
    `PRECONDITION DIFF:`,
    pre || '  (no rows)',
  ].join('\n')
}

function refKey(r) {
  return `${r.surface}::${(r.target || '').trim()}::${(r.forces || '').trim()}`
}

// Normalize a matrix axis label to its leading code (S1, C12, …) so the SAME logical
// cell written under two conventions keys identically — "S1" and
// "S1 PERFORMANCE_BONUS_RETAINED", "C1" and "C1 RETENTION mint hook". A 4-refuter ×
// 5-round run wrote cells under 68 column-labels that collapse to the 34 real codes and 22
// state-labels → 15 codes; because the gate keyed by exact string, all 374 grid pairs
// read EMPTY (label drift, not missing analysis), a 59k bulk-justify re-created 374
// duplicate cells, and the real analysis sat in 105 orphaned cells. Falls back to the
// trimmed/lowercased full label when there is no S#/C# code, so an uncoded label still
// keys consistently with itself.
function normCode(s) {
  const m = String(s || '').match(/^\s*([SC]\d+)\b/i)
  return m ? m[1].toUpperCase() : String(s || '').trim().toLowerCase()
}

// Loose canonical form of a matrix STATE label, for variant detection BEYOND normCode.
// normCode collapses only EXACT-string variants once a leading code is present; an
// uncoded state falls back to its full lowercased label, so two conventions for the SAME
// logical state stay distinct. This strips the parenthetical gloss and ALL whitespace and
// lowercases, turning "StoreStopLoss.status = STOPLOSS (active, routes…)" and the terse
// "status=STOPLOSS" into "storestoploss.status=stoploss" and "status=stoploss" — where the
// terse form is a dotted-suffix of the verbose one.
// The gloss strip must handle NESTED parens: a single-pass `\([^)]*\)` stops at the first
// `)`, so "SRE RETENTION_HOLD credit (…heldToKeep=max(0,capΣ−sreBalance))" left a trailing
// `)` residue that defeated both the equality and the endsWith check — a real run got
// 2 phantom dup states × 57 columns = 114 empty grid cells, the entire justify-pass
// workload. Strip innermost-first to a fixpoint, then drop any unbalanced leftover parens.
function looseStateForm(s) {
  let out = String(s || '').toLowerCase()
  let prev
  do { prev = out; out = out.replace(/\([^()]*\)/g, '') } while (out !== prev)
  return out.replace(/[()]/g, '').replace(/\s+/g, '')
}

// True when two state labels denote the SAME logical state: equal loose forms, or one is
// the other with a leading qualifier dropped ("Entity.field=value" vs "field=value").
// The suffix match is boundary-anchored on '.' so the bare "status=x" matches the
// qualified "storestoploss.status=x", while two DISTINCT qualified states
// ("cohortpayout.status=x" vs "transfer.status=x") never match each other (neither is a
// suffix of the other). Applied to the state axis ONLY — columns are seam-anchored
// (file:line) and do not exhibit this verbose/terse drift (verified: zero loose collapses
// among the 32 columns of one real run).
function sameLogicalState(a, b) {
  const x = looseStateForm(a), y = looseStateForm(b)
  if (!x || !y) return false
  if (x === y) return true
  const [short, long] = x.length <= y.length ? [x, y] : [y, x]
  if (short.length < 4 || !long.endsWith(short)) return false
  return long[long.length - short.length - 1] === '.'
}

// Load-bearing, verbatim-ish anchors in a string: file:line, ALL_CAPS enum, multi-hump
// CamelCase identifier. Shared by the consolidation-fidelity and contract-block checks.
// File-extension set is broad (covers any stack's source/config files), so this stays
// stack-agnostic; an uncoded prose item with no anchor is skipped (paraphrase is legit).
function anchorTokens(s) {
  return (s || '').match(/[\w/.]+\.(?:kt|kts|sql|ya?ml|java|ts|tsx|js|jsx|cs|py|go|rs|rb|php|swift|c|cc|cpp|h|hpp)(?::\d+)?|:\d+\b|\b[A-Z]{2,}(?:_[A-Z0-9]+)+\b|\b[A-Z][a-z]+(?:[A-Z][a-z]+){2,}\b/g) || []
}

// Matrix discipline: a column becomes a matrix axis only if it cites a CONCRETE
// seam — a file:line or a source-file token. A run enumerated 24 columns into a
// 284-cell matrix whose 504k-token fill STILL left 73 empty cells the loop never
// reached, which the gate then bulk-justified in one thin pass. A column with no
// concrete seam is speculation that multiplies cells without adding coverage; drop
// it. If it is real, a refuter re-adds it via `missingColumns` WITH a site — so this
// prunes noise without losing a genuine interaction. Never silent: dropped names log.
function columnsWithSeam(columns) {
  const hasSeam = (s) => /:\d+|\.(kt|kts|sql|ya?ml|java|ts|tsx|js|jsx|cs|py|go|rs|rb|php|swift|c|cc|cpp|h|hpp)\b/i.test(s || '')
  const kept = [], dropped = []
  for (const c of (columns || [])) (hasSeam(c.site) ? kept : dropped).push(c)
  return { kept, dropped }
}

// Assemble the raw draft from the analyze-phase structured outputs. Precondition
// rows are seeded one-per-copy from the guard enumeration; the resolver fills them.
function assemble(cols, seams, dim, cells) {
  const precondition = []
  for (const g of (seams.copiedGuards || [])) {
    for (const copy of (g.copies || [])) {
      precondition.push({ guard: g.predicate, copy, oldPrecondition: '', newReality: '', resolution: '' })
    }
  }
  return {
    contract: [],
    matrix: {
      columns: (cols.columns || []).map(c => c.name),
      states: cols.states || [],
      cells: cells.cells || [],
    },
    dimension: { rows: dim.rows || [], violations: dim.violations || [] },
    precondition,
    premises: [],
  }
}

// Merge a resolver PATCH (DRAFT_PATCH) into the draft. Additive/override-by-key
// only: upserts cells (by state+column), dimension rows (by variable), premises
// (by text), precondition rows (by guard+copy); appends columns/states/contract
// items uniquely; stamps resolutions onto matching dimension violations. It never
// deletes, so a malformed patch can only leave the gate unsatisfied — which the
// gate then catches — rather than silently corrupting a filled draft.
function applyPatch(draft, patch) {
  if (!patch) return draft
  const d = JSON.parse(JSON.stringify(draft))
  const uniqPush = (arr, items) => { for (const x of (items || [])) if (!arr.includes(x)) arr.push(x) }
  // Axes dedupe by CODE (not exact label) so a patch adding "S1" next to an existing
  // "S1 PERFORMANCE…" doesn't create a normCode-duplicate axis entry.
  const uniqPushBy = (arr, items, keyFn) => { const seen = new Set(arr.map(keyFn)); for (const x of (items || [])) { const k = keyFn(x); if (!seen.has(k)) { seen.add(k); arr.push(x) } } }
  uniqPushBy(d.matrix.columns, patch.columnsAdd, normCode)
  uniqPushBy(d.matrix.states, patch.statesAdd, normCode)
  uniqPush(d.contract, patch.contractAdd)

  // Key cells by normalized (state,column) CODE so a variant-labeled upsert updates the
  // existing cell instead of creating a duplicate (the label-drift fix).
  const ckey = (s, c) => JSON.stringify([normCode(s), normCode(c)])
  const cellIdx = {}
  d.matrix.cells.forEach((c, i) => { cellIdx[ckey(c.state, c.column)] = i })
  for (const c of (patch.cellsUpsert || [])) {
    const k = ckey(c.state, c.column)
    if (k in cellIdx) d.matrix.cells[cellIdx[k]] = c
    else { cellIdx[k] = d.matrix.cells.length; d.matrix.cells.push(c) }
  }
  // Referential integrity: refuters add cells faster than they add columnsAdd/statesAdd,
  // so a cell can reference an axis code absent from columns[]/states[] (a run: cells used
  // 15 state-codes / 34 column-codes but the axes declared only 11 / 34, leaving 4 real
  // states uncovered). Reconcile by CODE so the axes always span every analyzed cell and
  // the gate validates the real surface, not a stale grid.
  const haveState = new Set(d.matrix.states.map(normCode))
  const haveCol = new Set(d.matrix.columns.map(normCode))
  for (const c of d.matrix.cells) {
    const sc = normCode(c.state); if (!haveState.has(sc)) { haveState.add(sc); d.matrix.states.push(c.state) }
    const cc = normCode(c.column); if (!haveCol.has(cc)) { haveCol.add(cc); d.matrix.columns.push(c.column) }
  }

  const rowIdx = {}
  d.dimension.rows.forEach((r, i) => { rowIdx[r.variable] = i })
  for (const r of (patch.dimensionRowsUpsert || [])) {
    if (r.variable in rowIdx) d.dimension.rows[rowIdx[r.variable]] = r
    else { rowIdx[r.variable] = d.dimension.rows.length; d.dimension.rows.push(r) }
  }
  for (const vr of (patch.violationsResolved || [])) {
    const hit = d.dimension.violations.find(x => x.variable === vr.variable && (!vr.issue || x.issue === vr.issue))
    if (hit) hit.resolution = vr.resolution
  }

  const premIdx = {}
  d.premises.forEach((p, i) => { premIdx[p.premise] = i })
  for (const p of (patch.premisesUpsert || [])) {
    if (p.premise in premIdx) d.premises[premIdx[p.premise]] = p
    else { premIdx[p.premise] = d.premises.length; d.premises.push(p) }
  }

  const pkey = (g, c) => JSON.stringify([g, c])
  const preIdx = {}
  d.precondition.forEach((p, i) => { preIdx[pkey(p.guard, p.copy)] = i })
  for (const p of (patch.preconditionUpsert || [])) {
    const k = pkey(p.guard, p.copy)
    if (k in preIdx) d.precondition[preIdx[k]] = p
    else { preIdx[k] = d.precondition.length; d.precondition.push(p) }
  }
  canonicalizeMatrixStates(d)
  return d
}

// Pick the surviving cell when two variant-state cells collapse onto the same
// (canonical state, column). GAP outranks handled/N·A so a merge can never HIDE an
// unresolved interaction; among equal verdicts the richer (longer) text wins.
function mergeCell(a, b) {
  const rank = { GAP: 3, 'N/A': 1, handled: 1 }
  const ra = rank[a.verdict] || 0, rb = rank[b.verdict] || 0
  if (rb !== ra) return rb > ra ? b : a
  const len = (c) => ((c.justification || '') + (c.where || '')).length
  return len(b) > len(a) ? b : a
}

// Collapse verbose/terse variants of the same logical state to ONE canonical label, remap
// every cell's state, and merge cells that then collide on (state, column). WHY: a
// downstream fill/refute/justify agent paraphrases an enumerated state into a terser
// convention ("status=STOPLOSS" vs the verbose "StoreStopLoss.status = STOPLOSS (…)");
// normCode's full-label fallback can't collapse them, so the cell-reconcile loop minted a
// phantom duplicate state, the cartesian DOUBLED (a run: 18 states / 576 cells where
// 9 / 288 were real), and the gate-justify pass then dutifully FILLED all ~288 phantom
// cells — wasted fill work AND a 2x-inflated GAP count that read twice as alarming as the
// truth (every verbose/terse pair carried an identical GAP count). Canonical label = the
// longest in each group (most context). Idempotent; a no-op when <2 states.
function canonicalizeMatrixStates(d) {
  const states = (d.matrix && d.matrix.states) || []
  if (states.length < 2) return
  // Group over a SPECIFICITY-SORTED copy (entity-qualified — loose form contains '.' —
  // first, longer first) so every qualified group exists before any terse variant
  // attaches; a terse state then sees the full picture and ambiguity is detectable.
  // Output axis order still follows the ORIGINAL states array (newStates below).
  const bySpecificity = [...states].sort((a, b) => {
    const la = looseStateForm(a), lb = looseStateForm(b)
    return (lb.includes('.') ? 1 : 0) - (la.includes('.') ? 1 : 0) || lb.length - la.length
  })
  const groups = []
  for (const s of bySpecificity) {
    // Membership = mutual compatibility with EVERY member of EXACTLY ONE group.
    // sameLogicalState is deliberately non-transitive — a terse "status=PAID" matches
    // BOTH "CohortPayout.status=PAID" and "Transfer.status=PAID", which never match each
    // other — so the old `some`-membership computed the transitive closure: the terse
    // state BRIDGED two distinct qualified states into one group, silently erasing a
    // matrix row. `every` blocks the bridge; the ≥2-full-matches guard keeps a genuinely
    // ambiguous terse state as its own row (honest phantom cells) instead of binding its
    // cells to an arbitrary entity.
    const full = groups.filter((grp) => grp.members.every((m) => sameLogicalState(m, s)))
    let g = full.length === 1 ? full[0] : null
    if (!g) { g = { canon: s, members: [] }; groups.push(g) }
    g.members.push(s)
    if (String(s).length > String(g.canon).length) g.canon = s
  }
  if (groups.length === states.length) return // every state distinct — nothing to collapse
  const canonical = {}
  for (const g of groups) for (const m of g.members) canonical[m] = g.canon
  const seen = new Set(); const newStates = []
  for (const s of states) { const c = canonical[s] || s; if (!seen.has(c)) { seen.add(c); newStates.push(c) } }
  d.matrix.states = newStates
  const byKey = new Map()
  for (const c of (d.matrix.cells || [])) {
    const cc = { ...c, state: canonical[c.state] || c.state }
    const k = JSON.stringify([normCode(cc.state), normCode(cc.column)])
    byKey.set(k, byKey.has(k) ? mergeCell(byKey.get(k), cc) : cc)
  }
  d.matrix.cells = [...byKey.values()]
}

// =====================================================================
// Programmatic GATE — pure function over the draft + the guard-copy enumeration.
// Returns { pass, violations }. This is the deterministic completeness check the
// plan's Contract item 6 specifies.
// =====================================================================
function gateCheck(d, copiedGuards) {
  const v = []
  const states = d.matrix.states || []
  const columns = d.matrix.columns || []
  const cells = d.matrix.cells || []

  // 1. No empty cell: every (state x column) pair must have a cell, and each
  //    cell must be justified.
  // Key by normalized (state,column) CODE: state/column names contain spaces and are
  // written under drifting label variants ("S1" vs "S1 PERFORMANCE_BONUS_RETAINED"), so
  // keying by exact string made a run see 374 phantom-empty grid pairs while the matching
  // cells existed under a different label. normCode collapses the variants; JSON.stringify
  // keeps the composite key collision-free. Iterate the grid by DISTINCT code so a
  // drifted axis list doesn't double-count a pair.
  const ckey = (s, c) => JSON.stringify([normCode(s), normCode(c)])
  const distinct = (arr) => { const seen = new Set(), out = []; for (const x of arr) { const k = normCode(x); if (!seen.has(k)) { seen.add(k); out.push(x) } } return out }
  const cellAt = {}
  for (const c of cells) cellAt[ckey(c.state, c.column)] = c
  for (const s of distinct(states)) {
    for (const col of distinct(columns)) {
      const c = cellAt[ckey(s, col)]
      if (!c) {
        v.push({ kind: 'empty-cell', detail: `matrix cell [${s}] x [${col}] is missing` })
        continue
      }
      if (c.verdict === 'GAP' && !(c.justification && c.justification.trim())) {
        v.push({ kind: 'unjustified-gap', detail: `cell [${s}] x [${col}] is GAP without justification` })
      }
      if (c.verdict === 'handled' && !(c.where && c.where.trim())) {
        v.push({ kind: 'handled-no-where', detail: `cell [${s}] x [${col}] is handled but cites no location` })
      }
      if (c.verdict === 'N/A' && !((c.justification && c.justification.trim()) || (c.where && c.where.trim()))) {
        v.push({ kind: 'na-no-reason', detail: `cell [${s}] x [${col}] is N/A without a why` })
      }
    }
  }

  // 2. Every load-bearing premise must be executable: a require/check seam AND a
  //    failing-first test. A documentation-only or none-yet premise fails.
  for (const p of (d.premises || [])) {
    if (p.status === 'none-yet' || !(p.seam && p.seam.trim()) || !(p.failingTest && p.failingTest.trim())) {
      v.push({ kind: 'premise-not-executable', detail: `premise "${p.premise}" lacks ${!(p.seam && p.seam.trim()) ? 'a require/check seam' : 'a failing-first test'}` })
    }
  }

  // 2b. A load-bearing invariant exists (a dimension row with a real cap) but NO
  //     executable premise protects it -> the documentation-only-invariant
  //     failure the gate exists to catch. Vacuous over an empty premise list
  //     otherwise.
  const cappedRows = (d.dimension.rows || []).filter(
    (r) => r.cap && r.cap.trim() && r.cap.trim().toLowerCase() !== 'none',
  )
  if (cappedRows.length > 0 && (d.premises || []).length === 0) {
    v.push({ kind: 'no-executable-premise', detail: `${cappedRows.length} capped invariant(s) but zero executable premises (require/check + failing-first test)` })
  }

  // 3. Every unresolved dimension violation fails (a wrong/untagged base
  //    or uncapped variable that was never resolved).
  for (const dv of (d.dimension.violations || [])) {
    if (!(dv.resolution && dv.resolution.trim())) {
      v.push({ kind: 'dimension-violation', detail: `${dv.variable}: ${dv.issue} unresolved` })
    }
  }

  // 4. Every touched guard must have one precondition row PER COPY, each filled.
  for (const g of (copiedGuards || [])) {
    const rows = (d.precondition || []).filter(p => p.guard === g.predicate)
    if (rows.length < (g.copies || []).length) {
      v.push({ kind: 'missing-precondition-copy', detail: `guard \`${g.predicate}\` has ${(g.copies || []).length} copies but only ${rows.length} precondition rows` })
    }
    for (const r of rows) {
      if (!(r.resolution && r.resolution.trim()) || !(r.newReality && r.newReality.trim()) || !(r.oldPrecondition && r.oldPrecondition.trim())) {
        v.push({ kind: 'unfilled-precondition', detail: `precondition row for \`${g.predicate}\` @ ${r.copy} is unfilled` })
      }
    }
  }

  return { pass: v.length === 0, violations: v }
}

// Deterministic cartesian-completeness: the (state × column) pairs (keyed by normCode)
// that have NO cell. Pure function — the same product gateCheck walks, exposed so the
// fill (Lever 1) and the gate-justify drive the resolver to fill ONLY the gaps rather
// than re-emit all N cells. A refuter that adds a column/state grows the product; this
// surfaces the new empty pairs so the justify pass fills the whole grown row/column, not
// just the one cell the refuter named (the matrix-growth gate-fail).
function missingCells(draft) {
  const states = (draft.matrix && draft.matrix.states) || []
  const columns = (draft.matrix && draft.matrix.columns) || []
  const cells = (draft.matrix && draft.matrix.cells) || []
  const distinct = (arr) => { const seen = new Set(), out = []; for (const x of arr) { const k = normCode(x); if (!seen.has(k)) { seen.add(k); out.push(x) } } return out }
  const have = new Set(cells.map((c) => JSON.stringify([normCode(c.state), normCode(c.column)])))
  const missing = []
  for (const s of distinct(states)) for (const col of distinct(columns)) {
    if (!have.has(JSON.stringify([normCode(s), normCode(col)]))) missing.push({ state: s, column: col })
  }
  return missing
}

// Consolidation fidelity: the gate proves the DRAFT (JSON) is complete; this checks the
// rendered body did not DROP any of it. When an LLM retyped the artifacts this caught real
// lossiness — a run dropped a premise, buried a scoped commitment, left dangling refs,
// because a 284-cell draft can't fit faithfully in a ~32k-char body. The artifacts are
// now rendered deterministically (renderArtifacts), so this is a REGRESSION GUARD on the
// engine's own render — a hit means renderArtifacts itself dropped something. It catches the
// DROP class: every premise / contract item / GAP cell whose distinctive tokens (file:line,
// ALL_CAPS enum, multi-hump CamelCase identifier) appear NOWHERE in the body. Items with no
// distinctive token are skipped (prose rephrasing is legitimate); the check only fires on
// load-bearing, verbatim-ish anchors — so a hit is a real omission, not a paraphrase.
function consolidationFidelity(draft, body) {
  const lc = (body || '').toLowerCase()
  const covered = (fields) => {
    const toks = [...new Set(fields.flatMap(anchorTokens))]
    if (!toks.length) return true
    return toks.some((t) => lc.includes(t.toLowerCase()))
  }
  const missing = []
  for (const p of (draft.premises || [])) {
    if (!covered([p.premise, p.seam, p.failingTest])) missing.push({ kind: 'premise', item: p.premise })
  }
  for (const c of (draft.contract || [])) {
    if (!covered([c])) missing.push({ kind: 'contract', item: c })
  }
  for (const cell of (draft.matrix.cells || []).filter((c) => c.verdict === 'GAP')) {
    if (!covered([cell.justification, cell.column, cell.state])) missing.push({ kind: 'gap-cell', item: `[${cell.state}] x [${cell.column}]` })
  }
  return missing
}

// Structural Contract-block coverage. consolidationFidelity catches DROPPED items
// (anchor absent from the WHOLE body) but not BURIED ones — a contract commitment the
// consolidator mentions only in prose, OUTSIDE the numbered "## Contract" block, reads
// as present to a token scan yet is invisible to `/verify-plan`, which reconciles against
// the Contract block. A run buried item 30 (and another buried a scoped commitment)
// exactly this way. This isolates the Contract block from the body and flags any contract
// item whose anchor is in the body but NOT inside that block, plus a missing block.
// The anchor heading must BEGIN with "contract" (the section heading "### Contract"), not
// merely contain it: `.*contract` matched the document TITLE "## deep-plan contract — …"
// first, isolating the title+intro as the "block" so all real Contract items read as
// buried → a wasted repair pass + a polluted body. The title begins with "deep-plan", so a
// leading-word match excludes it while still catching "### Contract".
function contractBlockCoverage(draft, body) {
  const items = draft.contract || []
  const lines = (body || '').split('\n')
  const start = lines.findIndex((l) => /^#{1,6}\s+contract\b/i.test(l))
  let block = ''
  if (start >= 0) {
    const rest = lines.slice(start + 1)
    let rel = rest.findIndex((l) => /^#{1,6}\s/.test(l))
    const end = rel === -1 ? lines.length : start + 1 + rel
    block = lines.slice(start, end).join('\n')
  }
  const blockLc = block.toLowerCase()
  const bodyLc = (body || '').toLowerCase()
  const buried = []
  for (const it of items) {
    const toks = [...new Set(anchorTokens(it))]
    if (!toks.length) continue
    const inBody = toks.some((t) => bodyLc.includes(t.toLowerCase()))
    const inBlock = toks.some((t) => blockLc.includes(t.toLowerCase()))
    if (inBody && !inBlock) buried.push(it)
  }
  return { missingBlock: start < 0 && items.length > 0, buried }
}

// The consolidator body must START at its title heading. A repair-pass agent
// prepended task narration — "Now I understand the consolidator's job … I'll re-emit …" —
// which leaked into result.body line 1. The role file says "Start directly with the
// heading — no preface", but a model can ignore it, so strip any lines before the first
// markdown heading. No-op when the body already begins with a heading (h === 0) or has
// none (h < 0) — a heading-less body is returned verbatim.
function stripPreamble(body) {
  const lines = (body || '').split('\n')
  const h = lines.findIndex((l) => /^#{1,6}\s/.test(l))
  return h > 0 ? lines.slice(h).join('\n') : (body || '')
}

// Structural count reconciliation. consolidationFidelity (token presence) and
// contractBlockCoverage (block membership) are blind to COUNT: a run rendered 34 premise
// rows from a 22-premise draft and neither check noticed the 22≠34 drift (the draft array
// was the lossy side — the gate validated only 22). This counts the rendered rows of each
// structured collection and reports the draft-vs-rendered pair, flagging a `shortfall`
// ONLY when the render has FEWER than the draft (a real drop in the deliverable; a richer
// render is fine and just surfaced in the counts). Counting is table/list-structural and
// tolerant of multiple tables per section (it subtracts one header + one separator per table).
function structuralCounts(draft, body) {
  const lines = (body || '').split('\n')
  const seg = (re) => {
    const s = lines.findIndex((l) => /^#{1,6}\s/.test(l) && re.test(l))
    if (s < 0) return null
    const rest = lines.slice(s + 1)
    const rel = rest.findIndex((l) => /^#{1,6}\s/.test(l))
    return rel === -1 ? rest : rest.slice(0, rel)
  }
  const tableRows = (re) => {
    const s = seg(re)
    if (!s) return 0
    const pipe = s.filter((l) => /^\s*\|/.test(l))
    const seps = pipe.filter((l) => /^\s*\|[\s:|-]+\|?\s*$/.test(l))
    return Math.max(0, pipe.length - 2 * seps.length)
  }
  const contractBlock = seg(/^#{1,6}\s+contract\b/i)
  const contractItems = contractBlock ? contractBlock.filter((l) => /^\s*\d+\.\s/.test(l)).length : 0
  const dimRows = (draft.dimension && draft.dimension.rows) || []
  const counts = {
    contract: { draft: (draft.contract || []).length, rendered: contractItems },
    dimension: { draft: dimRows.length, rendered: tableRows(/dimension/i) },
    premises: { draft: (draft.premises || []).length, rendered: tableRows(/premise/i) },
  }
  const shortfalls = Object.keys(counts).filter((k) => counts[k].rendered < counts[k].draft)
  return { counts, shortfalls }
}

// Escape a draft string for a markdown table cell (pipes break columns; newlines break rows).
function mdCell(s) { return String(s == null ? '' : s).replace(/\|/g, '\\|').replace(/\s*\n+\s*/g, ' ').trim() }

// Short, deterministic title for the contract — the intent's first markdown heading, else
// the affected-domain list. No load-bearing meaning; just a human label on the document.
function intentTitle(intentStr, domainList) {
  const h = String(intentStr || '').split('\n').map((l) => l.trim()).find((l) => /^#{1,3}\s+\S/.test(l))
  return h ? h.replace(/^#{1,3}\s+/, '').slice(0, 80) : ((domainList || []).join(', ') || 'plan')
}

// LOSSLESS deterministic render of the four plan-contract artifacts (+ premises) from the
// gated draft. The consolidator (an LLM) reliably DROPS rows retyping a 70-item contract /
// hundreds of matrix cells into markdown — a run rendered 54 of 70 contract items; others
// hit the same class, forcing a `jq` reconstruction from the JSON. Rendering in JS removes
// the only lossy step: no output-token ceiling, every draft row appears verbatim. The
// consolidator now writes only the narrative synthesis; THESE artifacts are the source of
// truth, and the structuralCounts/fidelity checks become a regression guard on this render.
function renderArtifacts(draft) {
  const cells = draft.matrix.cells || []
  const out = []
  out.push('## Contract', '')
  ;(draft.contract || []).forEach((c, i) => out.push(`${i + 1}. ${mdCell(c)}`))
  if (!(draft.contract || []).length) out.push('_(no contract items)_')
  out.push('')
  out.push('## Interaction matrix', '')
  out.push(`> ${(draft.matrix.states || []).length} states × ${(draft.matrix.columns || []).length} columns = ${cells.length} cells · ${cells.filter((c) => c.verdict === 'handled').length} handled / ${cells.filter((c) => c.verdict === 'N/A').length} N·A / **${cells.filter((c) => c.verdict === 'GAP').length} GAP**.`, '')
  out.push('| State | Column | Verdict | Where / justification |', '|---|---|---|---|')
  for (const c of cells) {
    const note = c.verdict === 'handled' ? (c.where || c.justification || '') : (c.justification || c.where || '')
    out.push(`| ${mdCell(c.state)} | ${mdCell(c.column)} | ${c.verdict} | ${mdCell(note)} |`)
  }
  out.push('')
  out.push('## Money dimension table', '')
  out.push('| Variable | Unit / base | Cap | require/check seam |', '|---|---|---|---|')
  for (const r of (draft.dimension.rows || [])) out.push(`| ${mdCell(r.variable)} | ${mdCell(r.unitBase)} | ${mdCell(r.cap)} | ${mdCell(r.seam)} |`)
  if (!(draft.dimension.rows || []).length) out.push('| _(none)_ | | | |')
  const dviol = draft.dimension.violations || []
  if (dviol.length) {
    out.push('', '**Dimension violations:**')
    for (const v of dviol) out.push(`- ${mdCell(v.variable)}: ${mdCell(v.issue)} — ${v.resolution ? mdCell(v.resolution) : '**UNRESOLVED**'}`)
  }
  out.push('')
  out.push('## Precondition diff', '')
  out.push('| Guard | Copy | Old precondition | New reality | Resolution |', '|---|---|---|---|---|')
  for (const p of (draft.precondition || [])) out.push(`| ${mdCell(p.guard)} | ${mdCell(p.copy)} | ${mdCell(p.oldPrecondition)} | ${mdCell(p.newReality)} | ${mdCell(p.resolution)} |`)
  if (!(draft.precondition || []).length) out.push('| _(no copied guards)_ | | | | |')
  out.push('')
  out.push('## Failing-first tests & premises', '')
  out.push('| Premise | require/check seam | Failing-first test | Status |', '|---|---|---|---|')
  for (const p of (draft.premises || [])) out.push(`| ${mdCell(p.premise)} | ${mdCell(p.seam)} | ${mdCell(p.failingTest)} | ${mdCell(p.status || '')} |`)
  if (!(draft.premises || []).length) out.push('| _(none)_ | | | |')
  return out.join('\n')
}

// Seam extraction — the primary code site (Class:line / file.kt:line) a commitment turns
// on. Lets GAP cells / contract items that name the SAME seam be grouped: the headline GAP
// count measures CELLS, but most cells repeat ONE un-built surface across rows (a run:
// 100 GAP cells over ~9 real decisions). gapSeamCount recovers the decision count (B).
// sharedSeams flags seams carrying a committed directive (contract item) AND >=1 other
// reference — the consistency-watch set passed to the consolidator, which is hard-ruled to
// surface any two commitments giving CONFLICTING directives for the same mechanism as a
// `### ⚠️ Contradiction` theme (C — the fork the gate passed); renderVerdict counts those
// themes back out of the narrative so the top-line cannot hide a contradiction.
function extractSeam(s) {
  const m = String(s || '').match(/\b([A-Z][A-Za-z0-9]+(?:\.[a-z]{1,4})?:\d+)/)
  return m ? m[1] : null
}
function gapSeamCount(draft) {
  const seams = new Set()
  let unseamed = 0
  for (const c of (draft.matrix.cells || [])) {
    if (c.verdict !== 'GAP') continue
    const s = extractSeam(c.justification || c.where)
    if (s) seams.add(s)
    else unseamed++
  }
  return { seams: seams.size, unseamed }
}
function sharedSeams(draft) {
  const byKind = new Map()
  const add = (seam, kind) => { if (!seam) return; if (!byKind.has(seam)) byKind.set(seam, { contract: 0, cell: 0 }); byKind.get(seam)[kind]++ }
  for (const it of (draft.contract || [])) add(extractSeam(it), 'contract')
  for (const c of (draft.matrix.cells || [])) if (c.verdict === 'GAP') add(extractSeam(c.justification || c.where), 'cell')
  const out = []
  for (const [seam, g] of byKind) if (g.contract >= 1 && g.contract + g.cell >= 2) out.push({ seam, contract: g.contract, cell: g.cell })
  return out.sort((a, b) => (b.contract + b.cell) - (a.contract + a.cell))
}

// HONEST deterministic verdict header. The programmatic gate PASSES whenever every cell is
// non-empty and justified — which is NOT "no open interactions": a matrix full of
// substantively-justified GAPs passes. A run shipped `gate: PASS, residualGaps: 0` atop 56
// real unresolved GAP cells while the refute loop hit its round cap WITHOUT converging —
// both invisible in the old top-line, which read as "clean/done". This header leads with the
// figures that measure remaining work, BEFORE the gate verdict, and reframes PASS so it
// cannot be misread.
function renderVerdict(draft, m) {
  const cells = draft.matrix.cells || []
  const gapCells = cells.filter((c) => c.verdict === 'GAP')
  const dviol = (draft.dimension.violations || []).filter((v) => !(v.resolution && v.resolution.trim()))
  // (A) Trajectory shape: a FLAT fresh-per-round sequence (11,11,8) means the surface is far
  // from exhausted — independent re-runs keep finding NEW interactions; a DECAYING one
  // (11,5,1) means a round or two more would close it. "did not converge" alone can't tell
  // the planner which — so name the shape.
  const fbr = m.freshByRound || []
  const peak = fbr.length ? Math.max(...fbr) : 0
  const last = fbr.length ? fbr[fbr.length - 1] : 0
  const shape = m.converged ? 'CONVERGED' : (peak > 0 && last <= peak * 0.5 ? 'DECAYING' : 'FLAT')
  const shapeNote = shape === 'FLAT'
    ? ' (final ≈ peak — the interaction surface is far from exhausted; independent re-runs keep finding new interactions, not the same draft re-sampled)'
    : shape === 'DECAYING' ? ' (decaying toward zero — one or two more rounds would likely close it)'
      : ' (a full round surfaced no new refutation)'
  // Decompose the final round's fresh into resolution-attacks vs new-surface: a FLAT shape
  // with a high resolution share means fix-attack equilibrium (each resolver patch mints new
  // attack surface — the loop is generative), NOT that the original surface is unexplored.
  const mix = (m.freshMixByRound || [])[(m.freshMixByRound || []).length - 1]
  const mixNote = mix && mix.resolution + mix.newSurface > 0
    ? ` Final-round mix: ${mix.resolution} attack earlier rounds' RESOLUTIONS vs ${mix.newSurface} new surface — a high resolution share means the loop is in fix-attack equilibrium (each fix mints new attack surface), not still discovering the original surface.`
    : ''
  // Resolution re-refute outcome: the targeted pass over the final round's otherwise-
  // unattacked resolutions. "held" is a real signal (they survived adversarial scrutiny);
  // a fresh>0 outcome means the re-refute's OWN resolutions now ship unattacked — say so.
  const rr = m.reRefute
  const reRefuteNote = rr
    ? ` Resolution re-refute (targeted pass over the final round's ${rr.targets} otherwise-unattacked resolution(s)): ${rr.fresh === 0 ? 'all held — 0 fresh.' : `${rr.fresh} fresh refutation(s), integrated by a single resolver pass — that patch ships unattacked; weigh its resolutions accordingly.`}`
    : ''
  const trajectory = fbr.length ? ` Fresh refutations per round: [${fbr.join(', ')}] — shape **${shape}**${shapeNote}.${mixNote}${reRefuteNote}` : ''
  const refute = m.converged
    ? `converged after ${m.rounds} round(s).${trajectory}`
    : `**did NOT converge** — hit the ${m.maxRounds}-round cap with ${m.lastFresh} fresh refutation(s) still landing in the final round.${trajectory} Treat this contract as a SAMPLE of the interaction space, not an exhaustive enumeration.`
  // (B) GAP clustering: the headline cell count over-states remaining unknowns — most cells
  // are ONE un-built surface repeated across rows. Reduce to distinct seams (+ the Síntese
  // theme count) so the planner reads ~decisions, not ~cells.
  const { seams: gapSeams, unseamed } = gapSeamCount(draft)
  const clusterTotal = gapSeams + unseamed
  const lines = [
    `## deep-plan contract — ${m.title}`,
    ``,
    `### Verdict`,
    ``,
  ]
  // (C) Contradiction surfacing: the gate checks completeness, NOT consistency between
  // commitments — a run shipped contract item 12 ("source from swept RS, NOT bucket")
  // beside cells/Síntese saying "source from bucket delta". The consolidator is hard-ruled to
  // emit `### ⚠️ Contradiction` themes; we count them back out so the top-line leads with them.
  if (m.contradictions) lines.push(`- **⚠️ Contradictions flagged: ${m.contradictions}** — two commitments give conflicting directives for the same mechanism/seam. RESOLVE THESE FIRST (see the \`⚠️ Contradiction\` theme(s) in ## Síntese); a gate PASS does not detect inconsistency between commitments.`)
  lines.push(
    `- **Unresolved interactions:** ${gapCells.length} substantive GAP cell(s)${dviol.length ? ` + ${dviol.length} unresolved dimension violation(s)` : ''} across a ${(draft.matrix.states || []).length}×${(draft.matrix.columns || []).length} matrix. **This is the real remaining work** — each is an interaction the plan has not closed.`,
    `- **GAP clustering:** those ${gapCells.length} cell(s) reduce to ~${clusterTotal} distinct seam(s)${m.narrativeThemes ? `, grouped into ${m.narrativeThemes} decision theme(s) in ## Síntese` : ''} — the headline counts cells, not independent unknowns; most cells are one un-built surface repeated across rows.`,
    `- **Refute:** ${refute}`,
    `- **Gate:** ${m.gate} — ${m.gate === 'PASS' ? 'every cell is filled and justified' : `${m.residualGaps} cell(s) left empty/unjustified`}. PASS means *no blank or bare-GAP cell*; it does **NOT** mean zero open interactions — see the GAP count above.`,
    `- **Affected domains:** ${(m.domains || []).join(', ') || '(n/a)'}`,
  )
  if (gapCells.length || dviol.length) {
    lines.push('', `### ⚠️ Unresolved — resolve or explicitly accept before finalizing`, '')
    for (const c of gapCells) lines.push(`- GAP: ${c.state} × ${c.column} — ${c.justification || '_(unjustified — must resolve)_'}`)
    for (const v of dviol) lines.push(`- DIM: ${v.variable} — ${v.issue} (unresolved)`)
  }
  return lines.join('\n')
}

// =====================================================================
// SEED MODE — for the engine sanity test: feed an incomplete draft and assert
// the Gate fails/throws.
// =====================================================================
if (seedDraft) {
  phase('Gate')
  const { pass, violations } = gateCheck(seedDraft, (seedDraft._copiedGuards || []))
  log(`seed-mode gate: ${pass ? 'PASS' : 'FAIL'} (${violations.length} violations)`)
  if (!pass && !noThrow) throw new Error('GATE FAIL (seed mode):\n' + violations.map(x => `- ${x.kind}: ${x.detail}`).join('\n'))
  return { gate: pass ? 'PASS' : 'FAIL', violations, contract: seedDraft, seedMode: true }
}

// SEED-PATCH MODE — exercises applyPatch() deterministically for the engine test, the
// same way seedDraft exercises gateCheck (no live agents). ARGS.seedPatch =
// { draft, patch }; returns the merged draft.
const seedPatch = ARGS.seedPatch
if (seedPatch) {
  phase('Gate')
  const merged = applyPatch(seedPatch.draft, seedPatch.patch)
  log('seed-patch mode: merged patch into draft')
  return { merged, seedMode: true }
}

// SEED-FIDELITY MODE — exercises consolidationFidelity() deterministically for the
// engine test. ARGS.seedFidelity = { draft, body }; returns the omission list.
const seedFidelity = ARGS.seedFidelity
if (seedFidelity) {
  phase('Synthesize')
  const missing = consolidationFidelity(seedFidelity.draft, seedFidelity.body)
  log(`seed-fidelity mode: ${missing.length} omission(s)`)
  return { missing, seedMode: true }
}

// SEED-COLUMNS MODE — exercises columnsWithSeam() deterministically for the engine test.
// ARGS.seedColumns = { columns }; returns { kept, dropped }.
const seedColumns = ARGS.seedColumns
if (seedColumns) {
  phase('Enumerate')
  const { kept, dropped } = columnsWithSeam(seedColumns.columns)
  log(`seed-columns mode: kept ${kept.length}, dropped ${dropped.length}`)
  return { kept, dropped, seedMode: true }
}

// SEED-CONTRACT-BLOCK MODE — exercises contractBlockCoverage() deterministically for the
// engine test. ARGS.seedContractBlock = { draft, body }; returns { missingBlock, buried }.
const seedContractBlock = ARGS.seedContractBlock
if (seedContractBlock) {
  phase('Synthesize')
  const r = contractBlockCoverage(seedContractBlock.draft, seedContractBlock.body)
  log(`seed-contract-block mode: missingBlock=${r.missingBlock}, buried=${r.buried.length}`)
  return { ...r, seedMode: true }
}

// SEED-STRIP MODE — exercises stripPreamble() deterministically for the engine test.
// ARGS.seedStrip = { body }; returns the stripped body.
const seedStrip = ARGS.seedStrip
if (seedStrip) {
  phase('Synthesize')
  const stripped = stripPreamble(seedStrip.body)
  log('seed-strip mode: stripped consolidator preamble')
  return { stripped, seedMode: true }
}

// SEED-STRUCTURAL MODE — exercises structuralCounts() deterministically for the engine test.
// ARGS.seedStructural = { draft, body }; returns { counts, shortfalls }.
const seedStructural = ARGS.seedStructural
if (seedStructural) {
  phase('Synthesize')
  const r = structuralCounts(seedStructural.draft, seedStructural.body)
  log(`seed-structural mode: shortfalls=${r.shortfalls.join(',') || 'none'}`)
  return { ...r, seedMode: true }
}

// SEED-MISSING MODE — exercises missingCells() deterministically for the engine test.
// ARGS.seedMissing = { draft }; returns the missing (state,column) pairs.
const seedMissing = ARGS.seedMissing
if (seedMissing) {
  phase('Gate')
  const missing = missingCells(seedMissing.draft)
  log(`seed-missing mode: ${missing.length} missing cell(s)`)
  return { missing, seedMode: true }
}

// SEED-ARTIFACTS MODE — exercises renderArtifacts() deterministically for the engine test.
// ARGS.seedArtifacts = { draft }; returns the rendered markdown.
const seedArtifacts = ARGS.seedArtifacts
if (seedArtifacts) {
  phase('Synthesize')
  const md = renderArtifacts(seedArtifacts.draft)
  log('seed-artifacts mode: rendered the four artifacts deterministically')
  return { md, seedMode: true }
}

// SEED-VERDICT MODE — exercises renderVerdict() deterministically for the engine test.
// ARGS.seedVerdict = { draft, meta }; returns the rendered header.
const seedVerdict = ARGS.seedVerdict
if (seedVerdict) {
  phase('Synthesize')
  const md = renderVerdict(seedVerdict.draft, seedVerdict.meta)
  log('seed-verdict mode: rendered the honest verdict header')
  return { md, seedMode: true }
}

// =====================================================================
// PHASE 1 — ENUMERATE: caller-enumerated columns + state-mutation seams + guard copies
// =====================================================================
phase('Enumerate')
const [cols, seams] = await parallel([
  () => agent(ctx('matrix-columns.md'), { label: 'enumerate:matrix-columns', phase: 'Enumerate', schema: MATRIX_COLUMNS, model: mdl('a2'), agentType: 'general-purpose' }),
  () => agent(ctx('state-mutation-seams.md'), { label: 'enumerate:state-mutation-seams', phase: 'Enumerate', schema: SETTLEMENT_SEAMS, model: mdl('a3'), agentType: 'general-purpose' }),
])
if (!cols || !seams) throw new Error('Enumerate phase failed — matrix columns or state-mutation seams missing.')
// Matrix discipline: keep only columns citing a concrete seam (file:line / source
// file) so the matrix stays dense — a column without one bloats the fill and leaves
// empty cells. A genuinely-missing interaction is re-added by a refuter WITH a site.
const { kept: seamedColumns, dropped: vagueColumns } = columnsWithSeam(cols.columns)
cols.columns = seamedColumns
log(`Enumerated ${seamedColumns.length} matrix columns (dropped ${vagueColumns.length} lacking a concrete seam), ${(cols.states || []).length} new states, ${(seams.copiedGuards || []).length} copied guards.`)
if (vagueColumns.length) log(`  pruned columns (no file:line): ${vagueColumns.map(c => c.name).join(', ')}`)

// =====================================================================
// PHASE 2 — ANALYZE: dimension table + matrix cells (needs columns) + premises
// =====================================================================
phase('Analyze')
const columnList = (cols.columns || []).map(c => c.name).join(', ')
const stateList = (cols.states || []).join(', ')
const [dim, cellsOut, premOut] = await parallel([
  () => agent(ctx('dimension-table.md'), { label: 'analyze:dimension-table', phase: 'Analyze', schema: DIMENSION_TABLE, model: mdl('a1'), agentType: 'general-purpose' }),
  () => agent(
    ctx('lifecycle-matrix.md') + `\n\n=== MATRIX TO FILL ===\nStates (rows): ${stateList}\nColumns: ${columnList}\nAnswer EVERY (state x column) cell handled/N·A/GAP. Column sites:\n` + (cols.columns || []).map(c => `- ${c.name} @ ${c.site} (reads ${c.reads})`).join('\n'),
    { label: 'analyze:matrix-cells', phase: 'Analyze', schema: MATRIX_CELLS, model: mdl('a4'), agentType: 'general-purpose' },
  ),
  () => agent(ctx('test-coverage.md'), { label: 'analyze:premises-tests', phase: 'Analyze', schema: PREMISE_OBLIGATIONS, model: mdl('a5'), agentType: 'general-purpose' }),
])
if (!dim || !cellsOut || !premOut) throw new Error('Analyze phase failed — a specialist slice is missing.')

let draft = assemble(cols, seams, dim, cellsOut)
draft.premises = premOut.premises || []
canonicalizeMatrixStates(draft) // collapse any verbose/terse state drift before the fill keys off it

// Fill pass: turn the raw assembly into a complete draft (fill precondition
// rows, ensure every state x column cell exists). Same resolver agent used in
// the refute loop, with a fill instruction.
function resolverPrompt(d, task, extra, patchMode) {
  return [
    `You are the deep-plan RESOLVER. ${task}`,
    patchMode
      ? `Return ONLY a PATCH (schema DRAFT_PATCH) — emit just the cells/rows/premises/columns/contract items you ADD or CHANGE. Unchanged content is merged in automatically; do NOT re-emit the whole draft (re-emitting the growing draft is what crashes the run on the 64k output-token limit and silently drops findings). Key exactly so the merge lands: a cell by (state,column) in \`cellsUpsert\`; a dimension row by \`variable\` in \`dimensionRowsUpsert\`; a premise by its text in \`premisesUpsert\`; a precondition row by (guard,copy) in \`preconditionUpsert\`. Reopen a handled cell by emitting it in \`cellsUpsert\` with verdict GAP. Resolve a load-bearing premise via \`premisesUpsert\` (seam + failingTest). Resolve a dimension violation via \`violationsResolved\` [{variable, resolution}] and/or the fixed row in \`dimensionRowsUpsert\`. Add a new column in \`columnsAdd\` AND its cells in \`cellsUpsert\`.`
      : `Return the FULL updated draft (schema-validated). Preserve everything correct; only amend what the task requires.`,
    `TERSENESS IS MANDATORY — an over-long output crashes serialization (the StructuredOutput call returns nothing and the whole run is wasted). Every field: file:line + one clause, <= 240 chars, no paragraphs, no quoting the intent back. Do not pad.`,
    `CONTAMINATION GUARD: the DESIGN INTENT below is the only source of truth. Ignore any plan-contract/\`deep-plan-*.md\`/cached JSON you might have seen on disk — never let it reshape this draft toward another feature.`,
    `A require/check seam is an executable assertion at the boundary, written in this repo's language and idiom (read \`docs/agents/skills-config.md\` › Stack if unsure) — the rule is stack-agnostic; the assertion form is just the local idiom.`,
    `Rules: every (state x column) cell must exist and be handled (with \`where\`) / N·A (with \`justification\`) / GAP (with \`justification\`). Fill every precondition row's oldPrecondition/newReality/resolution (all three non-empty), and preserve each row's \`guard\` field VERBATIM so it keeps matching the guard predicate. Resolve dimension violations by adding cap+seam and moving them out of \`violations\`. You MAY add columns/cells/rows; never delete a real GAP by relabeling it handled without evidence.`,
    ``,
    `=== DESIGN INTENT ===`,
    intent,
    `=== CURRENT DRAFT (JSON) ===`,
    JSON.stringify(d),
    extra ? `\n=== ${extra.title} ===\n${extra.body}` : ``,
    ``,
    `Guard copies that each need a precondition row: ` + (seams.copiedGuards || []).map(g => `\`${g.predicate}\` (${(g.copies || []).length} copies: ${(g.copies || []).join(', ')})`).join('; '),
    `Failing-first test obligations to fold into \`contract\`: ` + (premOut.tests || []).map(t => `${t.scenario} [${t.level}]`).join('; '),
  ].join('\n')
}

// LEVER 1 — the fill no longer re-emits the matrix. Agent 4 (analyze:matrix-cells)
// already answered every (state×column) cell into the draft via assemble(); re-emitting
// all N cells as one full DRAFT was the run's long pole (~35 min on a 28×14 matrix)
// AND flirted with the StructuredOutput-returns-nothing crash. The fill now returns a
// PATCH that does exactly three things: fills every precondition row, derives the
// Contract block, and fills ONLY the cells the deterministic check still reports missing.
// Existing cells are trusted (gate + refuters validate them); cartesian completeness is
// enforced by the gate, not by costly full re-emission.
const fillMissing = missingCells(draft)
const filled = await agent(
  resolverPrompt(
    draft,
    `Complete the draft. The matrix cells are ALREADY filled by the matrix specialist — do NOT re-emit existing cells. Do exactly three things: (1) fill EVERY precondition row (oldPrecondition/newReality/resolution all non-empty; preserve each \`guard\` VERBATIM) via \`preconditionUpsert\`; (2) derive the Contract block (wiring counts, predicates, invariants, exact values, files) from the intent and the slices, into \`contractAdd\`; (3) fill ONLY these still-missing cells via \`cellsUpsert\`${fillMissing.length ? ` (${fillMissing.length}): ` + fillMissing.map(m => `[${m.state}] x [${m.column}]`).join('; ') : ' — none, the matrix is already complete, so emit no cells'}.`,
    null,
    true,
  ),
  { label: 'analyze:fill', phase: 'Analyze', schema: DRAFT_PATCH, model: mdl('resolver'), agentType: 'general-purpose' },
)
if (filled) draft = applyPatch(draft, filled)

// =====================================================================
// PHASE 3 — REFUTE: anchoring-free refuters, loop-until-dry. Each fresh
// refutation reopens a cell/row; the resolver integrates it; re-refute.
// =====================================================================
phase('Refute')
const seen = new Set()
let dry = 0
let round = 0
let lastRoundFresh = 0 // fresh refutations in the final executed round — feeds the convergence verdict
const freshByRound = [] // fresh count per round — the trajectory shape (FLAT vs DECAYING) renderVerdict reports
// Per-round decomposition of fresh into resolution-attacks vs new-surface. A run's
// FLAT [30,25,23] decomposed to ~11/16 refutations attacking RESOLUTIONS minted by earlier
// rounds — the loop is GENERATIVE (each resolver patch mints new attack surface), not
// re-sampling an unexplored original surface. The mix is what tells the planner which.
const freshMixByRound = []
// The fresh items the FINAL resolver patch integrated. Non-empty after the loop only when
// it ended at the round cap right after a resolve — i.e. that patch was never attacked.
// A dry round clears it: dry means the refuters swept the draft (last patch included) and
// found nothing, so the previous resolutions already survived scrutiny.
let lastResolvedItems = []
while (dry < DRY_ROUNDS_TO_STOP && round < MAX_REFUTE_ROUNDS) {
  round++
  const summary = draftSummary(draft)
  // Already-open roots: cells the draft already marks GAP + unresolved dimension
  // violations. Fed to refuters so a round's "fresh" count measures NEW information,
  // not the same root re-raised on another cell (which has a distinct refKey and so
  // reads as fresh, preventing the dry streak from ever triggering — observed where
  // 13/12/12 fresh were largely the same A/B/E roots spread across cells). With this
  // hint a dry round genuinely means "no new territory and no broken resolution".
  const knownOpen = [
    ...(draft.matrix.cells || []).filter(c => c.verdict === 'GAP').map(c => `[${c.state}] x [${c.column}]`),
    ...(draft.dimension.violations || []).filter(v => !(v.resolution && v.resolution.trim())).map(v => `dim:${v.variable}`),
  ].join(' | ')
  const verdicts = (await parallel(
    Array.from({ length: REFUTERS_PER_ROUND }, (_, i) => () =>
      agent(
        [
          `Read \`.claude/skills/deep-plan/agents/refuter.md\` and operate as the refuter for round ${round}. Your assigned attack LENS #${i + 1}: ${REFUTER_LENSES[i % REFUTER_LENSES.length]}`,
          `LEAD with that lens; the other refuters this round cover the other lenses, so do not duplicate their angle. If your lens is genuinely exhausted, attack any cell/row no other refuter would.`,
          `You see ONLY the draft below and the design intent — NOT the reasoning that produced them. Verify against the live codebase with \`rg\` (NEVER \`grep -r\`), scoped INSIDE this repo root \`${repoRoot}\` only — never /tmp, .., ~, or sibling worktrees. One simple command per Bash call.`,
          `CONTAMINATION GUARD: the intent below is the only source of truth; ignore any plan-contract/\`deep-plan-*.md\`/cached JSON on disk. Keep every field terse (file:line + one clause, <= 240 chars).`,
          `Also read your independent reference (paths from \`docs/agents/skills-config.md\` › Docs layout; default \`docs/planning/recurring-failure-modes.md\`, \`docs/CORE_TENETS.md\`, and the affected-domain premises).`,
          ``,
          `ALREADY-OPEN items (the draft already marks these GAP/unresolved — do NOT spend your attack merely re-raising one of these on another cell; that is noise the dedup discards): ${knownOpen || '(none yet)'}.`,
          `Spend your attack on one of: (a) a cell currently marked handled/N·A that is actually wrong; (b) refuting the RESOLUTION of an already-open item — show the proposed fix is itself broken (e.g. a post-commit-propagation "fix" was shown to ORPHAN the new record); (c) a NEW column/state the matrix is missing entirely.`,
          `Tag each refutation's \`attacks\` field honestly: 'resolution' when it breaks a fill/fix the draft already contains (surfaces (a)/(b)); 'new-surface' when it names territory the draft lacks (surface (c)). The verdict reports this mix — it is how the planner distinguishes fix-attack equilibrium from undiscovered surface.`,
          ``,
          `=== DESIGN INTENT ===`,
          intent,
          `=== DRAFT PLAN-CONTRACT (this is all you get) ===`,
          summary,
          `=== END DRAFT ===`,
          `Refute-or-promote on all four surfaces. Default to refuted when uncertain. Return the RefutationVerdict.`,
        ].join('\n'),
        { label: `refute:r${round}-${i + 1}`, phase: 'Refute', schema: REFUTATION, model: mdl('a6'), agentType: 'general-purpose' },
      )
    )
  )).filter(Boolean)

  const fresh = []
  for (const verdict of verdicts) {
    for (const r of (verdict.refutations || [])) {
      const k = refKey(r)
      if (!seen.has(k)) { seen.add(k); fresh.push(r) }
    }
    for (const mc of (verdict.missingColumns || [])) {
      const k = `missing-column::${mc.site}`
      if (!seen.has(k)) { seen.add(k); fresh.push({ surface: 'missing-column', target: mc.site, scenario: `reads ${mc.reads}`, forces: `add column for ${mc.site}`, attacks: 'new-surface' }) }
    }
  }

  lastRoundFresh = fresh.length
  freshByRound.push(fresh.length)
  const mixResolution = fresh.filter((r) => r.attacks === 'resolution').length
  freshMixByRound.push({ resolution: mixResolution, newSurface: fresh.length - mixResolution })
  if (!fresh.length) {
    dry++
    lastResolvedItems = []
    log(`Refute round ${round}: dry (${dry}/${DRY_ROUNDS_TO_STOP}).`)
    continue
  }
  dry = 0
  log(`Refute round ${round}: ${fresh.length} fresh refutation(s) — reopening cells/rows.`)
  const resolved = await agent(
    resolverPrompt(draft, 'Integrate the refutations below: add any missing matrix column AND fill its cells, reopen wrongly-handled cells (set to GAP with justification, or fix to handled WITH evidence/where), fix dimension violations, and amend precondition rows whose precondition is shown false.', { title: 'REFUTATIONS TO RESOLVE', body: fresh.map(r => `- [${r.surface}] ${r.target}: ${r.scenario} (forces: ${r.forces})`).join('\n') }, true),
    { label: `refute:resolve-r${round}`, phase: 'Refute', schema: DRAFT_PATCH, model: mdl('resolver'), agentType: 'general-purpose' },
  )
  if (resolved) draft = applyPatch(draft, resolved)
  lastResolvedItems = resolved ? fresh : []
}
log(`Refute loop ended after ${round} round(s).`)

// =====================================================================
// PHASE 3b — RESOLUTION RE-REFUTE: one targeted pass over the final round's resolutions.
// When the loop ends at MAX_REFUTE_ROUNDS right after a resolve, that last patch was
// never adversarially attacked. This pass attacks ONLY those resolutions (not a full
// re-sweep — that's what an independent re-run is for), integrates any break with a
// single resolver call, and stops: the regress ends here by design, with the verdict
// stating that the re-refute's own resolutions ship unattacked. Findings deliberately do
// NOT extend freshByRound — the trajectory measures the main loop's shape.
// =====================================================================
let reRefute = null
if (lastResolvedItems.length) {
  log(`Resolution re-refute: attacking the final round's ${lastResolvedItems.length} unattacked resolution(s).`)
  const summary = draftSummary(draft)
  const targets = lastResolvedItems.map(r => `- [${r.surface}] ${r.target}: ${r.scenario} (forces: ${r.forces})`).join('\n')
  const verdicts = (await parallel(
    Array.from({ length: RESOLUTION_REREFUTERS }, (_, i) => () =>
      agent(
        [
          `Read \`.claude/skills/deep-plan/agents/refuter.md\` and operate as a TARGETED refuter — the resolution re-refute pass. Your assigned attack LENS: ${REFUTER_LENSES[i % REFUTER_LENSES.length]}`,
          `The refute loop hit its round cap; the resolver's FINAL patch — the resolutions listed below — was never adversarially attacked. Attack ONLY those resolutions and the cells/rows/dimension entries they touched. Everything else in the draft is OUT OF SCOPE for this pass; out-of-scope refutations are discarded.`,
          `You see ONLY the draft below and the design intent — NOT the reasoning that produced them. Verify against the live codebase with \`rg\` (NEVER \`grep -r\`), scoped INSIDE this repo root \`${repoRoot}\` only — never /tmp, .., ~, or sibling worktrees. One simple command per Bash call.`,
          `CONTAMINATION GUARD: the intent below is the only source of truth; ignore any plan-contract/\`deep-plan-*.md\`/cached JSON on disk. Keep every field terse (file:line + one clause, <= 240 chars).`,
          ``,
          `=== RESOLUTIONS TO ATTACK (the final round's patch — your entire scope) ===`,
          targets,
          `=== DESIGN INTENT ===`,
          intent,
          `=== DRAFT PLAN-CONTRACT ===`,
          summary,
          `=== END DRAFT ===`,
          `Refute-or-promote each listed resolution. Default to refuted when uncertain. Tag \`attacks\` (these are resolution-attacks unless you found genuinely new territory the resolution opened). Return the RefutationVerdict.`,
        ].join('\n'),
        { label: `refute:re-resolution-${i + 1}`, phase: 'Refute', schema: REFUTATION, model: mdl('a6'), agentType: 'general-purpose' },
      )
    )
  )).filter(Boolean)
  const fresh = []
  for (const verdict of verdicts) {
    for (const r of (verdict.refutations || [])) {
      const k = refKey(r)
      if (!seen.has(k)) { seen.add(k); fresh.push(r) }
    }
    for (const mc of (verdict.missingColumns || [])) {
      const k = `missing-column::${mc.site}`
      if (!seen.has(k)) { seen.add(k); fresh.push({ surface: 'missing-column', target: mc.site, scenario: `reads ${mc.reads}`, forces: `add column for ${mc.site}`, attacks: 'new-surface' }) }
    }
  }
  reRefute = { targets: lastResolvedItems.length, fresh: fresh.length }
  if (fresh.length) {
    log(`Resolution re-refute: ${fresh.length} fresh refutation(s) against the final patch — integrating (single pass, no further loop).`)
    const resolved = await agent(
      resolverPrompt(draft, 'Integrate the refutations below — each attacks a resolution the FINAL refute round just minted: reopen the wrongly-fixed cells (set to GAP with justification, or fix to handled WITH evidence/where), add any missing matrix column AND fill its cells, fix dimension violations, and amend precondition rows whose precondition is shown false.', { title: 'REFUTATIONS TO RESOLVE', body: fresh.map(r => `- [${r.surface}] ${r.target}: ${r.scenario} (forces: ${r.forces})`).join('\n') }, true),
      { label: 'refute:re-resolution-resolve', phase: 'Refute', schema: DRAFT_PATCH, model: mdl('resolver'), agentType: 'general-purpose' },
    )
    if (resolved) draft = applyPatch(draft, resolved)
  } else {
    log(`Resolution re-refute: all ${lastResolvedItems.length} resolution(s) held (0 fresh).`)
  }
}

// =====================================================================
// PHASE 4 — GATE: programmatic completeness. A bounded justify loop, each pass fed the
// deterministic missing-cell list so a matrix grown by a refuter gets its whole new
// row/column filled (not just the cell the refuter named). A residual after the loop is
// RECORDED, never thrown — deep-plan never blocks (SKILL.md Phase 7).
// =====================================================================
phase('Gate')
let { pass, violations } = gateCheck(draft, seams.copiedGuards)
let gateRound = 0
while (!pass && gateRound < GATE_JUSTIFY_ROUNDS) {
  gateRound++
  const empties = missingCells(draft)
  log(`Gate: ${violations.length} violation(s)${empties.length ? `, ${empties.length} empty grid cell(s)` : ''} — justify pass ${gateRound}/${GATE_JUSTIFY_ROUNDS}.`)
  const justified = await agent(
    resolverPrompt(draft, 'The gate found the violations below. Resolve each: fill EVERY empty cell listed (a matrix grown by a refuter leaves new state×column pairs blank — fill ALL of them, not only ones a refuter named), give every GAP a written justification, make every load-bearing premise executable (seam + failing-first test), resolve dimension violations, and complete every per-copy precondition row. A GAP you cannot close must carry an explicit written justification.', { title: 'GATE VIOLATIONS', body: violations.map(x => `- ${x.kind}: ${x.detail}`).join('\n') + (empties.length ? `\n\nEMPTY CELLS TO FILL (every one):\n` + empties.map(m => `- [${m.state}] x [${m.column}]`).join('\n') : '') }, true),
    { label: `gate:justify-${gateRound}`, phase: 'Gate', schema: DRAFT_PATCH, model: mdl('resolver'), agentType: 'general-purpose' },
  )
  if (justified) draft = applyPatch(draft, justified)
  ;({ pass, violations } = gateCheck(draft, seams.copiedGuards))
}
log(`Gate: ${pass ? 'PASS' : 'FAIL'} (${violations.length} residual violation(s)).`)
// A residual-GAP run is NOT fatal. A run threw here and lost ~3M tokens / 2h:
// a refuter grew the matrix, the single justify pass left new cartesian pairs empty, and
// the throw nuked the run before synthesize ever ran. deep-plan never blocks (SKILL.md
// Phase 7) — surface residual GAPs in the result (gate:FAIL + residualGaps + the ⚠️
// Unresolved block renderVerdict emits) and STILL synthesize; a flagged contract is
// incomparably more useful than a 0-byte output. (Seed mode keeps its own throw for the
// gate-detects-incompleteness test; `noThrow` there returns the violation list instead.)
if (!pass) {
  log(`Gate: ${violations.length} residual GAP(s) recorded — proceeding to synthesize (deep-plan never blocks; see result.violations).`)
}

// =====================================================================
// PHASE 5 — SYNTHESIZE: deterministic verdict header + the LLM's narrative + the
// LOSSLESS deterministic artifacts. The four artifacts and the verdict are rendered in JS
// from the gated draft (renderVerdict/renderArtifacts) — the consolidator no longer
// RETYPES them, which is what dropped 16/70 contract items on a run (the recurring
// consolidation-lossiness class). The LLM is left ONLY the narrative synthesis: clustering
// the GAPs/refutations into the BLOCKERs the planner must decide. The fidelity/structural
// checks now verify the engine's own render (a regression guard), not the LLM's retype.
// =====================================================================
phase('Synthesize')
const converged = dry >= DRY_ROUNDS_TO_STOP
const artifacts = renderArtifacts(draft)
// (C) consistency watch: seams carrying a committed directive (contract item) AND >=1 other
// reference are where two parts of the contract can give conflicting directives (the fork
// the gate passed). Hand the list to the consolidator so it refutes or confirms consistency
// and surfaces any clash as a `### ⚠️ Contradiction` theme.
const watchSeams = sharedSeams(draft)
const narrative = stripPreamble(await agent(
  [
    `Read \`.claude/skills/deep-plan/agents/consolidate.md\` and operate as the consolidator.`,
    `The verdict header and the four structured artifacts (Contract, interaction matrix, dimension table, precondition diff, premises) are rendered DETERMINISTICALLY by the engine — do NOT reproduce them, do NOT write a title or any "## Contract"/matrix/table. Your job is the NARRATIVE SYNTHESIS only.`,
    `Cluster the GAP cells and refutations into the handful of THEMES / likely BLOCKERs the planner must decide, in priority order; each theme cites the file:line it turns on and the decision required. Do NOT re-soften any GAP. The gate verdict is fixed: ${pass ? 'PASS' : 'FAIL'} with ${violations.length} residual GAP(s).`,
    `CONSISTENCY WATCH (highest priority): scan ALL commitments — contract items, cells, and your own themes — for any TWO that give CONFLICTING directives for the same mechanism/seam (e.g. "source X from the swept set" vs "source X from the bucket delta"). For each clash emit a \`### ⚠️ Contradiction — <seam>\` theme FIRST, naming both sides and the decision required. The gate cannot detect this; it is the single highest-value thing you produce. Seams already carrying >=2 commitments (start here): ${watchSeams.slice(0, 12).map(s => s.seam).join(', ') || '(none flagged)'}.`,
    `Begin DIRECTLY with the heading \`## Síntese\` — no preamble, no commentary about your task. Keep the heading \`## Síntese\` and your subsection markers \`### \` verbatim — the engine counts themes/contradictions from them. Use \`### <theme>\` subsections, 1–3 sentences each, naming the cells/rows covered.`,
    ``,
    `=== GATED DRAFT (JSON) ===`,
    JSON.stringify(draft),
    `=== RESIDUAL VIOLATIONS ===`,
    violations.map(x => `- ${x.kind}: ${x.detail}`).join('\n') || '(none)',
    ``,
    `Affected domains: ${domains.join(', ')}.`,
  ].join('\n'),
  { label: 'synthesize:consolidate', phase: 'Synthesize', model: mdl('consolidate'), agentType: 'general-purpose' },
))
// Count themes / contradictions back OUT of the narrative so renderVerdict's top-line leads
// with what the consolidator actually flagged (deterministic — reports the LLM's own output).
const narrativeThemes = (String(narrative).match(/^### /gm) || []).length
const contradictions = (String(narrative).match(/^###[^\n]*contradiction/gim) || []).length
const verdictHeader = renderVerdict(draft, {
  title: intentTitle(intent, domains),
  domains,
  gate: pass ? 'PASS' : 'FAIL',
  residualGaps: violations.length,
  converged,
  rounds: round,
  lastFresh: lastRoundFresh,
  freshByRound,
  freshMixByRound,
  reRefute,
  narrativeThemes,
  contradictions,
  maxRounds: MAX_REFUTE_ROUNDS,
})
let body = [verdictHeader, narrative || '## Síntese\n\n_(no narrative produced)_', artifacts].join('\n\n')

// Regression guard on the DETERMINISTIC render (B): with the artifacts rendered in JS, these
// should always be clean — a hit means renderArtifacts dropped something (an engine bug),
// not the old LLM-retype lossiness. Surfaced in the result + logged; no LLM repair pass
// (that pass was the cure for retype-lossiness, which deterministic render eliminates).
const omissions = consolidationFidelity(draft, body)
const coverage = contractBlockCoverage(draft, body)
const structural = structuralCounts(draft, body)
if (omissions.length || coverage.buried.length || coverage.missingBlock || structural.shortfalls.length) {
  log(`⚠️ deterministic-render guard tripped (engine bug — investigate renderArtifacts): ${omissions.length} dropped, ${coverage.buried.length} buried${coverage.missingBlock ? ', NO Contract block' : ''}${structural.shortfalls.length ? `, shortfall [${structural.shortfalls.join(', ')}]` : ''}.`)
}

return {
  gate: pass ? 'PASS' : 'FAIL',
  residualGaps: violations.length,
  violations,
  contract: draft,
  body: body || '(consolidator produced no body)',
  rounds: round,
  freshByRound,
  freshMixByRound,
  reRefute,
  converged,
  contradictions,
  consolidationOmissions: omissions,
  contractBuried: coverage.buried,
  contractBlockMissing: coverage.missingBlock,
  structuralCounts: structural.counts,
  structuralShortfalls: structural.shortfalls,
}
