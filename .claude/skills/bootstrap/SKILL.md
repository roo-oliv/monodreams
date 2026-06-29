---
name: bootstrap
description: Scaffold the agent-facing documentation the other skills read — CORE_TENETS (cross-cutting invariants), per-domain premises files, and per-flow flow docs (each becomes a dedicated review lens). Run on a repo that doesn't have them yet, or to add a domain/flow. Interview-driven; writes real content, not empty stubs.
disable-model-invocation: true
---

# bootstrap

`deep-review`, `deep-plan`, `refine`, and `implement` are far more useful when the repo
carries two kinds of agent-facing docs:

- **`CORE_TENETS.md`** — the handful of cross-cutting invariants and design principles that
  hold across the whole codebase. "Most of what looks surprising in the code is consistent
  with one of these." Business rules and architectural stances, not API docs.
- **Premises files** — per-domain *technical invariants*: assumptions that, if violated,
  silently break something downstream. Distinct from tenets (broad) and schema (structure).

This skill scaffolds both with **real content mined from the code and from you** — never
empty headings. It is the documentation half of `setup` (which writes config); they pair, but
either can run first. This structure grew in production repos and has been bootstrapped onto
fresh ones successfully; it is deliberately lightweight to start and grows organically.

Interview-driven. Explore → draft from evidence → confirm → write.

## 0. Read the layout

If `docs/agents/skills-config.md` exists, read its **Docs layout** and **Domains** sections —
they tell you where premises go (`docs/{domain}/premises.md` vs colocated `{module}/docs/…`)
and what the domains are. If it doesn't exist, infer the layout from the repo and **offer to
run `setup` first** (config + docs reinforce each other); proceed standalone if the user
declines, and state the layout you assumed.

## 1. Explore the codebase

You are mining invariants, so read for them:

- Top-level module/package layout → the domain list (reconcile with config if present).
- Per domain: the core entities, their lifecycle/state transitions, the services that mutate
  them, and any `require`/`check`/assert/guard already in the code (those are premises someone
  already felt strongly enough to encode).
- Existing scattered docs, READMEs, long comments, ADRs — invariants often already written in
  prose somewhere.
- Tests named like invariants (`should never…`, `…is immutable`, `sum …equals…`).

Delegate broad sweeps to `Explore` subagents; keep only the conclusions.

## 2. CORE_TENETS — interview, then draft

Tenets can't be fully mined — they're the *why*. Interview the user (`AskUserQuestion`):

- "What are the 3–7 things that must always be true in this codebase that a newcomer would get
  wrong?" — the load-bearing invariants.
- "What's the architectural stance — what does this system deliberately do differently from the
  obvious approach, and why?" (e.g. "framework not library", "money is always integer cents",
  "ECS purity: components are pure data".)
- "Who are the readers of these docs?" — if AI agents are first-class readers, say so in the
  file; it changes how much context to write.

Draft `CORE_TENETS.md` as a short numbered list of tenets, each: a one-line claim, then a
paragraph of *why it holds and what breaks without it*. Cite a concrete code example per tenet
where one exists. Keep it tight — a tenet list that sprawls stops being read.

## 3. Premises — one file per domain

For each domain (start with the ones the user cares about most; you needn't do all at once),
write a premises file at the configured path. Each premise is an H2:

```markdown
# {Domain} premises

Brief domain context (2–3 lines).

## {Short declarative premise title}

{One paragraph: what is true and must remain true.}

**Why:** {the business/technical concern that motivates it.}
**Breaks:** {what specifically goes wrong downstream if violated.}
**Tests:** {test class/method that protects it — or `none yet` if pre-existing & untested.}
**Depends on:** {cross-references to premises in other domains, if any.}
```

Rules:
- A premise must be **falsifiable** — phrased so a test *could* break if it were violated. "The
  system is robust" is not a premise; "a ReceivableSettlement is never updated or deleted" is.
- Mine real ones first (from guards/tests/comments in step 1); propose them to the user before
  inventing. `**Tests:** none yet` is an acceptable starting state for a pre-existing invariant
  — flag it as a follow-up, don't block.
- Where a load-bearing invariant has no executable guard, note it — `deep-plan`/`deep-review`
  will later suggest a `require`/`check` seam.

## 4. Flow docs — one per key flow

A **flow** is a path that data / state / money takes through the system that must be reasoned
about as a whole (a payment pipeline, a level-load sequence, an auth handshake). `deep-review`
spawns a dedicated lens per flow doc, so this is where a repo declares the domain-specific review
knowledge that used to be hardcoded. Interview for them:

- "What are the end-to-end flows where a change in one place can break something three steps
  away?" — those are the flows worth a doc.

For each, write a flow doc into the flows dir (config › Flows, default `docs/flows/<flow>.md`)
using the format in [flow.template.md](./flow.template.md). A flow doc reads like a **dedicated
core-tenet for that flow** — descriptive, not "check that…" — but carries the path, the entities
& lifecycle, the invariants, the load-bearing quantities, and the failure modes a reviewer needs.
Set the frontmatter `covers:` globs so the lens only runs when the flow is touched. Mine the real
flow from the code (trace it end-to-end) before writing; don't invent flows the repo doesn't have
— a repo with no load-bearing flows declares none, and the universal lenses still run.

## 5. Index (optional)

If the repo uses a docs index (or the user wants one), write/update `docs/index.md` (or the
repo's convention) listing the tenets file, each domain's premises, and each flow doc with a
one-line hook, so the set is discoverable.

## 6. Confirm and write

Show drafts before writing. Write `CORE_TENETS.md`, the premises files, and the flow docs at the
configured paths. Tell the user which domains still need premises and which flows still need docs
(so they know the coverage is partial and intentional) and that `deep-plan`/`deep-review` will now
read these.
