# PostToolUse hook -- fires after every Bash tool call.
# If the command was `gh pr merge ...` (squash/rebase/merge to main),
# the hook polls main-branch workflows and injects a system-reminder
# with any in_progress run IDs so Claude is forced to watch them.
#
# Background: validate.md Adim 18 + INSTRUCTIONS.md sec 3.2 require
# Claude to gh run watch every post-merge run to concluded. This hook
# is the mechanical guardrail -- model bypass alone is not enough.
# ASCII-only: PowerShell 5.1 reads BOM-less UTF-8 as ANSI and mangles
# em-dashes / section signs into a parser error.

$ErrorActionPreference = 'SilentlyContinue'

# Read stdin payload (hook input JSON from Claude Code).
$payload = [Console]::In.ReadToEnd()

if ([string]::IsNullOrWhiteSpace($payload)) { exit 0 }

try {
    $json = $payload | ConvertFrom-Json
} catch {
    exit 0
}

$cmd = $json.tool_input.command
if ([string]::IsNullOrWhiteSpace($cmd)) { exit 0 }

# Only fire on gh pr merge invocations (squash button etc.).
if ($cmd -notmatch '(^|[\s&;|])gh\s+pr\s+merge\b') { exit 0 }

# Give GitHub a few seconds to register the merge commit + dispatch workflows.
Start-Sleep -Seconds 6

# Fetch in_progress runs on main; gh's --jq uses embedded jq so no system jq needed.
$runs = & gh run list --branch main --limit 6 `
    --json databaseId,status,workflowName `
    --jq '[.[] | select(.status != "completed") | "\(.workflowName) (id=\(.databaseId))"] | join(", ")' 2>$null

if ([string]::IsNullOrWhiteSpace($runs)) { exit 0 }

$context = "POST-MERGE CI WATCH ZORUNLU (validate.md Adim 18 / INSTRUCTIONS.md sec 3.2). main branch in_progress run(lar): $runs. Her birini gh run watch <ID> --exit-status ile concluded olana kadar izle, sonucu raporla. Atlanmasi yasak -- task PR / chore PR / docs PR ayrimi yok."

$output = [ordered]@{
    hookSpecificOutput = [ordered]@{
        hookEventName     = 'PostToolUse'
        additionalContext = $context
    }
}

# Compress to single-line JSON for hook protocol; UTF-8 without BOM.
$jsonOut = $output | ConvertTo-Json -Depth 5 -Compress
[Console]::Out.Write($jsonOut)
