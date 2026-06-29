---
flow: <short-name>
covers:
  # Path globs. A change touching any of these makes this flow's lens run in deep-review.
  # Omit / leave empty to make the flow's lens run on every heavy review.
  - <glob>
  - <glob>
sensitive: <true|false>   # optional — is a mistake in this flow expensive/irreversible?
---

# <Flow name>

<One or two paragraphs describing the flow as it actually is — the path data / state / money
takes through the system, start to end. Write it like a core-tenet for this flow: declarative
and explanatory, NOT a list of "check that…" instructions. This is the shared mental model a
reviewer loads before judging any change to the flow. The lens derives its own questions from
it; you supply the truth.>

## Entities & lifecycle

<The records / objects this flow touches and the states they move through, in order. Note the
transitions and where they happen. If a record can be created by more than one path, list every
creator — the un-enumerated writer is a classic bug source.>

## Invariants

<What must always hold for this flow to be correct. Falsifiable statements — each phrased so a
test could break if it were violated. These are the premises specific to this flow.>

- <invariant>
- <invariant>

## Load-bearing quantities

<For flows that compute or move a value (money, counts, coordinates, timing): each value, what
it is, its unit/base, and its cap. This is what the derived-quantity lens leans on. Omit the
section if the flow carries no such quantity.>

- `<name>`: <what it is> — base/unit `<…>`, capped at `<…>`.

## Failure modes

<What goes wrong when an invariant breaks — the concrete downstream damage, and what is
expensive or irreversible about getting this flow wrong. This is what tells the lens how hard to
push and how to rank severity.>

- <failure mode>
