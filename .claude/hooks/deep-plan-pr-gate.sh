#!/usr/bin/env bash
# deep-plan PR gate — PreToolUse hook on Bash. (Optional; wire it up in the consuming
# repo's .claude/settings.json as a PreToolUse(Bash) hook to enable.)
#
# Blocks `gh pr create` on a branch that touches a SENSITIVE domain unless a COMPLETE
# plan-contract exists (a `## Contract` block, no GATE FAIL, no unjustified GAP,
# Residual GAPs: 0). Which domains are sensitive is read from the repo's config
# (docs/agents/skills-config.md › Sensitive domains) — NOT hardcoded. If the repo
# declares no sensitive domains (or has no config), the gate never blocks.
#
# Fail-open by design: any ambiguity that isn't a clear "incomplete contract on a
# sensitive branch" allows the command through. The override token is always available.
#
# Contract:
#   exit 0            -> allow the gh pr create
#   exit 2 + stderr   -> block; stderr is surfaced to the agent (PreToolUse convention)
#
# Override: put `[deep-plan-override: <reason>]` anywhere in the gh pr create args, or
# set env DEEP_PLAN_OVERRIDE=<reason>. Audited to stderr but always allows.

set -uo pipefail

PROJECT_DIR="${CLAUDE_PROJECT_DIR:-}"
if [ -z "$PROJECT_DIR" ]; then
  PROJECT_DIR="$(git rev-parse --show-toplevel 2>/dev/null || pwd)"
fi
CONFIG="$PROJECT_DIR/docs/agents/skills-config.md"

# --- 1. Read the tool input and extract the bash command -------------------
if [ ! -t 0 ]; then
  payload="$(cat 2>/dev/null || true)"
else
  payload=""
fi
cmd="$(printf '%s' "$payload" | python3 -c 'import sys,json
try:
    d=json.load(sys.stdin)
    print((d.get("tool_input") or {}).get("command",""))
except Exception:
    print("")' 2>/dev/null || true)"

# --- 2. Only act on `gh pr create` -----------------------------------------
case "$cmd" in
  *"gh pr create"*) : ;;
  *) exit 0 ;;
esac

# --- 3. Override ------------------------------------------------------------
if [ -n "${DEEP_PLAN_OVERRIDE:-}" ]; then
  echo "deep-plan PR gate: overridden via DEEP_PLAN_OVERRIDE=${DEEP_PLAN_OVERRIDE} — allowing." >&2
  exit 0
fi
case "$cmd" in
  *"[deep-plan-override:"*)
    echo "deep-plan PR gate: overridden via [deep-plan-override:] token in PR args — allowing." >&2
    exit 0
    ;;
esac

cd "$PROJECT_DIR" 2>/dev/null || exit 0
git rev-parse --git-dir >/dev/null 2>&1 || exit 0

# --- 4. Which domains are sensitive? (from config, not hardcoded) ----------
# Extract the comma-separated token list under "## Sensitive domains" — the first line
# in that section that is purely lowercase identifiers separated by commas (prose
# explainer lines and `<!-- -->` comments are skipped; `none`/`(none …)` yields nothing).
sensitive_regex() {
  [ -f "$CONFIG" ] || return 1
  awk '
    /^##[[:space:]]+[Ss]ensitive domains/ {inblk=1; next}
    inblk && /^##[[:space:]]/ {inblk=0}
    inblk {print}
  ' "$CONFIG" \
  | sed 's/<!--.*-->//' | tr -d '`' \
  | while IFS= read -r line; do
      t="$(printf '%s' "$line" | sed 's/^[[:space:]]*//; s/[[:space:]]*$//')"
      [ -z "$t" ] && continue
      if printf '%s' "$t" | grep -qE '^[a-z][a-z0-9_-]*([[:space:]]*,[[:space:]]*[a-z][a-z0-9_-]*)*$'; then
        printf '%s' "$t" | tr ',' '\n' | sed 's/[[:space:]]//g' \
          | grep -vxE '(none|empty)' | grep -E '^[a-z]'
        break
      fi
    done | sort -u | paste -sd'|' -
}

SENS_RE="$(sensitive_regex || true)"
# No config, or no sensitive domains declared → gate does not apply.
[ -z "$SENS_RE" ] && exit 0

changed="$(git diff --name-only origin/main...HEAD 2>/dev/null || true)"
[ -z "$changed" ] && changed="$(git diff --name-only origin/main 2>/dev/null || true)"
if ! printf '%s\n' "$changed" | grep -Eq "($SENS_RE)"; then
  exit 0  # not a sensitive-domain change — gate does not apply.
fi

# --- 5. Find a complete plan-contract for this repo ------------------------
is_complete_contract() {
  local f="$1"
  grep -Eq '^##+ *Contract' "$f" 2>/dev/null || return 1
  grep -Eiq 'gate[*_: ]+fail' "$f" 2>/dev/null && return 1
  if grep -Eiq 'residual gaps' "$f" 2>/dev/null; then
    grep -Eiq 'residual gaps[^0-9]*0' "$f" 2>/dev/null || return 1
  fi
  if grep -Eiq '\| *GAP *\|' "$f" 2>/dev/null; then
    return 1
  fi
  return 0
}

found_complete=0
checked=0

# 5a. Repo-local contract artifacts committed ON THIS BRANCH (most robust).
if [ -d "$PROJECT_DIR/.claude/deep-plan" ]; then
  for f in "$PROJECT_DIR"/.claude/deep-plan/*.md; do
    [ -e "$f" ] || continue
    rel="${f#"$PROJECT_DIR"/}"
    printf '%s\n' "$changed" | grep -Fq "$rel" || continue
    checked=$((checked+1))
    if is_complete_contract "$f"; then found_complete=1; break; fi
  done
fi

# 5b. Session plans describing THIS branch's change. Repo-local .claude/.plans/ first
# (where /refine writes; gitignored), then the global ~/.claude/plans/. "Belongs" means
# the plan references a file this branch actually changes.
scan_plans_dir() {
  local plans_dir="$1"
  [ -d "$plans_dir" ] || return 0
  while IFS= read -r f; do
    [ -e "$f" ] || continue
    grep -Eq '^##+ *Contract' "$f" 2>/dev/null || continue
    belongs=0
    while IFS= read -r tok; do
      [ -n "$tok" ] || continue
      if printf '%s\n' "$changed" | grep -Fq "$tok"; then belongs=1; break; fi
    done < <(grep -oE '[A-Za-z0-9_./-]+/[A-Za-z0-9_./-]+\.[A-Za-z0-9]+' "$f" 2>/dev/null | sort -u | head -40)
    [ "$belongs" -eq 1 ] || continue
    checked=$((checked+1))
    if is_complete_contract "$f"; then found_complete=1; return 0; fi
  done < <(ls -t "$plans_dir"/*.md 2>/dev/null | head -10)
}

[ "$found_complete" -eq 0 ] && scan_plans_dir "$PROJECT_DIR/.claude/.plans"
[ "$found_complete" -eq 0 ] && scan_plans_dir "$HOME/.claude/plans"

if [ "$found_complete" -eq 1 ]; then
  exit 0
fi

# --- 6. Block --------------------------------------------------------------
cat >&2 <<EOF
🚫 deep-plan PR gate: blocked.

This branch touches a sensitive domain ($(printf '%s\n' "$changed" | grep -Eo "($SENS_RE)" | sort -u | paste -sd, -)) but no COMPLETE plan-contract was found (checked $checked candidate(s)).

Before opening this PR:
  1. Author/finish the plan-contract — run /deep-plan (it fills the interaction matrix,
     dimension table, and precondition diff, and gates on completeness).
  2. Run /verify-plan and resolve every Missing / Diverged / unresolved-GAP.
  3. Make the contract discoverable: deep-plan's "write artifacts" option, or commit it
     under .claude/deep-plan/<branch>.md.

A complete contract has a "## Contract" block and no incompleteness markers: no
"Gate: FAIL", "Residual GAPs: 0" (when present), no bare "| GAP |" cells.

Sensitive domains are read from docs/agents/skills-config.md › Sensitive domains. To
override for a genuine exception (audited): add [deep-plan-override: <reason>] to the
gh pr create args, or set DEEP_PLAN_OVERRIDE=<reason>.
EOF
exit 2
