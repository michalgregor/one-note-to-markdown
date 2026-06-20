#!/usr/bin/env bash
#
# block-ai-pr-issue.sh
#
# GitHub Copilot preToolUse hook. Stops an AI agent from OPENING a GitHub issue
# or pull request. Humans raise issues and PRs after reviewing the code and
# proving it works. See .github/copilot-instructions.md.
#
# Hook contract (preToolUse):
#   stdin  : JSON describing the tool about to run (toolName, toolArgs).
#   stdout : to BLOCK, print one JSON object:
#              {"permissionDecision":"deny","permissionDecisionReason":"..."}
#            to ALLOW, print nothing.
#   exit   : always 0. Command preToolUse hooks are fail-closed, so a non-zero
#            exit would itself deny the tool. We only ever deny on a positive
#            match, and we fall through to allow on anything unexpected so a bug
#            in this hook can never brick the agent on unrelated tools.
#
# No "set -e": grep exits 1 when there is no match, which is our normal allow
# path, and "set -e" would turn that into a crash (deny everything).

input="$(cat 2>/dev/null)" || exit 0
[ -n "$input" ] || exit 0

# Lowercase once for case-insensitive matching. We do NOT normalize hyphens
# here because CLI flags (-f, -X, --data) must stay intact for section 3.
lower="$(printf '%s' "$input" | tr '[:upper:]' '[:lower:]')"

deny() {
  printf '%s\n' '{"permissionDecision":"deny","permissionDecisionReason":"Blocked by repo policy (.github/hooks): AI agents must not open GitHub issues or pull requests. A human reviews the code, proves it works, and opens it themselves. See .github/copilot-instructions.md."}'
  exit 0
}

# 1) MCP / tool names that create issues or PRs. Matches hyphen or underscore
#    styles, and also catches the review/comment create variants as a bonus.
#    Read tools (list_issues, search_issues, pull_request_read, get_issue) do
#    NOT contain these substrings, so they are allowed.
if printf '%s' "$lower" | grep -Eq 'create[-_]pull[-_]request|create[-_]issue'; then
  deny
fi

# 2) GitHub CLI: "gh pr create" and "gh issue create". The leading boundary
#    keeps words that merely end in "gh" (like "high pr create") from matching.
if printf '%s' "$lower" | grep -Eq '(^|[^a-z0-9_])gh[[:space:]]+(pr|issue)[[:space:]]+create'; then
  deny
fi

# 3) Defense in depth: a REST client (gh api / curl / wget / Invoke-*) hitting a
#    /pulls or /issues endpoint with a write method. All three conditions are
#    required so plain GET reads of pulls/issues are still allowed.
if printf '%s' "$lower" | grep -Eq '(gh[[:space:]]+api|curl|wget|invoke-restmethod|invoke-webrequest)' \
   && printf '%s' "$lower" | grep -Eq '(/pulls|/issues)' \
   && printf '%s' "$lower" | grep -Eq '(-x[[:space:]]*post|--request[[:space:]]*post|--method[[:space:]]*post|-method[[:space:]]+post|[[:space:]]-f[[:space:]]|--field|[[:space:]]-d[[:space:]]|--data|--input[[:space:]])'; then
  deny
fi

# No match. Allow by printing nothing and exiting clean.
exit 0
