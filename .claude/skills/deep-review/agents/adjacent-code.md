# Lens — Adjacent Code Paths

Your focus is **code NOT in the diff that is broken by the changes**. This is
the highest-value review category — these bugs are invisible in a diff-only
review.

## Your inputs

You receive **Phase 1 context**: the diff, change metadata, the core-tenets doc,
relevant schema/premises docs, the repo's conventions doc, list of changed
files, list of affected domains. Premises/tenets paths come from **Docs layout**
(`docs/agents/skills-config.md`); if absent, default to `docs/CORE_TENETS.md` and
`docs/{domain}/premises.md` and say so. Detect a changed file's domain via
**Domains** (`docs/agents/skills-config.md` — path-glob → domain).

If you have file-access tools (Read, Grep): use them liberally — your job
demands exhaustive search across the codebase. Scope grep with `rg` to the repo
root. If you do NOT have those tools (e.g. a static-prompt environment), reason
from the embedded diff plus the project conventions in the repo's conventions
doc, and flag predicted-affected sites as "verify" findings.

**Execute this checklist systematically. Do every step — do not skip.**

## Step 1: Extract semantic changes from the diff

Scan the diff and produce a list. For each item, note the type:
- [ ] New enum values added
- [ ] Changed method/function signatures (params added/removed/retyped)
- [ ] New status values or status transitions
- [ ] Changed filter/query conditions (WHERE clauses, stream/collection filters)
- [ ] New entity fields
- [ ] Changed business logic (calculations, conditionals)

## Step 2: For EACH semantic change, find adjacent references

If you have grep available (`rg`), run searches per change:

```
# For new enum values:
rg for the ENUM_VALUE_NAME across the source tree
rg for status != / status NOT IN / status == patterns for the related entity

# For changed methods:
rg for the METHOD_NAME across the source tree

# For the domain's core ledger/aggregate writers (ALWAYS run these):
rg for the constructors / save-sites of the domain's central records (the
   entities listed under the affected domain's schema doc), and for the
   derived-quantity names the change touches (totals, balances, proportions).
```

If you don't have grep, reason about which files (by the package/module + naming
conventions in the repo's conventions doc) are likely to reference the changed
elements, and flag those as "Verify" findings.

## Step 3: For each hit, read and verify

For every file that references the changed element:
1. Read the full method/function containing the reference (or predict it from
   surrounding code if no Read tool).
2. Ask: "Does this handle the new behavior correctly?"
3. If NO → finding. Note the file, line, and what breaks.

## Step 4: New ledger/aggregate writes × existing unique constraints

For every INSERT/`save` site the diff adds on a state-bearing or
ledger/aggregate table (any table where a duplicate row corrupts a total,
balance, or audit trail):

1. Grep the schema/migration source for the table name and list EVERY unique and
   partial-unique index/constraint on it (`uq_*`, `CREATE UNIQUE INDEX ... WHERE`,
   or the equivalent in this repo's schema definitions). The dangerous ones are
   out-of-diff — they predate the new writer.
2. Derive the new write's key tuple for each constraint. Ask: can the
   surrounding control flow produce two rows with the same tuple — a loop over a
   collection (per-parent, per-child, per-batch iteration), a retry, a
   multi-entity trigger? A reference id that is **loop-invariant** (a parent id,
   an episode id) while the loop iterates siblings is the canonical collision: a
   per-source `forEach` inserting rows keyed by a loop-invariant id collides on
   the 2nd iteration, and the failure is missed whenever every test uses a single
   parent.
3. A constraint violation inside an open transaction aborts the entire
   operation — classify the finding as the *operation's* failure (the action
   that errors out), not as "an insert error".

## Step 5: Priority targets (always consider, even if not in diff)

Detected from the affected domains — the canonical broad-blast-radius sites:
- Manual / internal-API write paths that bypass the primary flow
- External provider / webhook event handlers
- Scheduled jobs querying by status
- Response mappers — `when`/`switch` on enums: does the `else`/`default` branch
  silently miscategorize?
- Transfer/payout-equivalent or proportion calculations — do bounded values stay
  inside their bound (e.g. a proportion in [0.0, 1.0])?

## Step 6: Check the inverse

Does the new code handle all EXISTING scenarios? If a new method filters by
status, does it account for ALL possible states (including the
overpayment/cancellation/expiration-equivalent edge states of this domain)?

## Output format

Use the standard severity-bucket format (Blockers / High / Medium / Low /
Positive observations). For each finding include:
- File and line (or package/module + predicted file when reasoning)
- The semantic change that breaks it
- What goes wrong (concrete scenario)
- Suggested fix

If you cannot point to a specific file but have STRONG priors that an adjacent
path is affected, list it under Medium with the predicted file name + reason.

If you have NO findings at all, write a single line: `_No findings._`
