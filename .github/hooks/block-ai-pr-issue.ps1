# block-ai-pr-issue.ps1
#
# GitHub Copilot preToolUse hook (Windows / PowerShell). Stops an AI agent from
# OPENING a GitHub issue or pull request. Humans raise issues and PRs after
# reviewing the code and proving it works. See .github/copilot-instructions.md.
#
# This is the PowerShell mirror of block-ai-pr-issue.sh. Keep the two in sync.
#
# Hook contract (preToolUse):
#   stdin  : JSON describing the tool about to run (toolName, toolArgs).
#   stdout : to BLOCK, print one JSON object:
#              {"permissionDecision":"deny","permissionDecisionReason":"..."}
#            to ALLOW, print nothing.
#   exit   : always 0. We only deny on a positive match, and on any unexpected
#            error we fall through to allow so a bug in this hook can never brick
#            the agent on unrelated tools.

try {
    # No piped input means nothing to inspect. Allow.
    if (-not [Console]::IsInputRedirected) { exit 0 }

    $payload = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($payload)) { exit 0 }

    $lower = $payload.ToLowerInvariant()

    $denyJson = '{"permissionDecision":"deny","permissionDecisionReason":"Blocked by repo policy (.github/hooks): AI agents must not open GitHub issues or pull requests. A human reviews the code, proves it works, and opens it themselves. See .github/copilot-instructions.md."}'

    # 1) MCP / tool names that create issues or PRs (hyphen or underscore style).
    #    Read tools like list_issues / search_issues / pull_request_read are not
    #    matched.
    if ($lower -match 'create[-_]pull[-_]request|create[-_]issue') {
        Write-Output $denyJson
        exit 0
    }

    # 2) GitHub CLI: "gh pr create" and "gh issue create".
    if ($lower -match '(^|[^a-z0-9_])gh\s+(pr|issue)\s+create') {
        Write-Output $denyJson
        exit 0
    }

    # 3) Defense in depth: a REST client hitting a /pulls or /issues endpoint with
    #    a write method. All three required so plain GET reads stay allowed.
    if (($lower -match 'gh\s+api|curl|wget|invoke-restmethod|invoke-webrequest') -and
        ($lower -match '/pulls|/issues') -and
        ($lower -match '-x\s*post|--request\s*post|--method\s*post|-method\s+post|\s-f\s|--field|\s-d\s|--data|--input\s')) {
        Write-Output $denyJson
        exit 0
    }

    # No match. Allow.
    exit 0
}
catch {
    # Never brick the agent over unrelated tools because of a hook bug.
    exit 0
}
