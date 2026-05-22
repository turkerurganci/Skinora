# PreToolUse hook -- fires before every PowerShell tool call.
# Reads the command from stdin JSON, matches it against a conservative
# regex allowlist, and if matched, returns a permissionDecision=allow
# JSON so Claude Code skips the permission prompt.
#
# If no pattern matches, the hook exits 0 silently and the normal
# permission flow (settings.local.json allowlist / interactive prompt)
# applies. This hook only widens, never denies.
#
# Why this exists: settings.local.json PowerShell entries get
# overwritten by Claude Codes own permission UI when it re-saves the
# file (lost-update race). Hooks are not subject to that path -- they
# run before the permission system entirely.
#
# ASCII-only: PowerShell 5.1 reads BOM-less UTF-8 as ANSI and mangles
# em-dashes / section signs into a parser error.

$ErrorActionPreference = 'SilentlyContinue'

# Read hook input JSON from stdin
$payload = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($payload)) { exit 0 }

try {
    $json = $payload | ConvertFrom-Json
} catch {
    exit 0
}

$cmd = $json.tool_input.command
if ([string]::IsNullOrWhiteSpace($cmd)) { exit 0 }

# Conservative allowlist of read-only / project-locked PowerShell command
# prefixes. Keep these narrow: every entry is a regex anchored at start,
# and the only wildcards are inside the regex itself (no .* tails).
$allowlist = @(
    '^Get-ChildItem(\s|$)',
    '^Get-Item(\s|$)',
    '^Test-Path(\s|$)',
    '^Resolve-Path(\s|$)',
    '^Get-Location(\s|$)',
    '^Write-Output\s+\(Get-Location\)\.Path\s*$',
    '^Set-Location\s+c:/projects/Escrow/[A-Za-z0-9_\-./]+;\s+npx\s+tsc\s+--noEmit(\s|$)',
    '^dotnet\s+test(\s|$)',
    '^dotnet\s+build(\s|$)',
    '^dotnet\s+restore(\s|$)',
    '^dotnet\s+format\s+[^;|&]*--verify-no-changes(\s|$)',
    '^dotnet\s+--info(\s|$)',
    '^dotnet\s+ef\s+migrations\s+list(\s|$)'
)

$logPath = Join-Path $PSScriptRoot 'powershell-allow.log'

foreach ($pattern in $allowlist) {
    if ($cmd -match $pattern) {
        $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        try {
            Add-Content -Path $logPath -Value "$ts ALLOW [$pattern] $cmd" -ErrorAction SilentlyContinue
        } catch { }

        $response = @{
            hookSpecificOutput = @{
                hookEventName            = 'PreToolUse'
                permissionDecision       = 'allow'
                permissionDecisionReason = "powershell-allow.ps1 matched: $pattern"
            }
        } | ConvertTo-Json -Compress -Depth 4

        Write-Output $response
        exit 0
    }
}

# No match -- exit silently, normal permission flow applies.
exit 0
