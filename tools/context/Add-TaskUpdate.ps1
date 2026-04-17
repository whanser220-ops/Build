[CmdletBinding()]
param(
    [string]$Task,

    [Parameter(Mandatory = $true)]
    [string]$Summary,

    [string]$ArtifactPath,

    [string]$NextStep,

    [string]$Decision
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$memoryRoot = Join-Path $repoRoot '.workspace\memory'
$taskStatePath = Join-Path $memoryRoot 'task_state.md'
$decisionsPath = Join-Path $memoryRoot 'decisions.md'
$timestamp = Get-Date -Format 'yyyy-MM-dd HH:mm:ss'

if (-not (Test-Path -LiteralPath $memoryRoot)) {
    New-Item -ItemType Directory -Path $memoryRoot -Force | Out-Null
}

if (-not (Test-Path -LiteralPath $taskStatePath)) {
    Set-Content -LiteralPath $taskStatePath -Encoding utf8 -Value @'
# Current Task

- Task:
- Last Updated:
- Owner:

# What We Know

- Add only the highest-signal facts needed for the next step.

# Next Steps

1. Add the next concrete action.

# Recent Updates
'@
}

$taskStateContent = Get-Content -LiteralPath $taskStatePath -Raw

if ($Task) {
    $taskStateContent = [regex]::Replace(
        $taskStateContent,
        '(?ms)^# Current Task\s+- Task:.*?- Last Updated:.*?- Owner:.*?(?=\n# What We Know)',
        "# Current Task`r`n`r`n- Task: $Task`r`n- Last Updated: $timestamp`r`n- Owner: Codex`r`n"
    )
} else {
    $taskStateContent = [regex]::Replace(
        $taskStateContent,
        '(?m)^- Last Updated:.*$',
        "- Last Updated: $timestamp"
    )
}

if ($NextStep) {
    $taskStateContent = [regex]::Replace(
        $taskStateContent,
        '(?ms)^# Next Steps\s+1\..*?(?=\n# Recent Updates)',
        "# Next Steps`r`n`r`n1. $NextStep`r`n"
    )
}

Set-Content -LiteralPath $taskStatePath -Encoding utf8 -Value $taskStateContent

$entryLines = @(
    "## $timestamp",
    '',
    "- Summary: $Summary",
    "- Artifact: $(if ($ArtifactPath) { $ArtifactPath } else { 'n/a' })",
    "- Next: $(if ($NextStep) { $NextStep } else { 'n/a' })",
    ''
)

Add-Content -LiteralPath $taskStatePath -Encoding utf8 -Value $entryLines

if ($Decision) {
    if (-not (Test-Path -LiteralPath $decisionsPath)) {
        Set-Content -LiteralPath $decisionsPath -Encoding utf8 -Value "# Decisions`r`n"
    }

    $decisionLines = @(
        "- Time: $timestamp",
        "- Decision: $Decision",
        "- Why: Recorded during task progress.",
        "- Impact: Future runs can reuse this choice without rediscovery.",
        ''
    )

    Add-Content -LiteralPath $decisionsPath -Encoding utf8 -Value $decisionLines
}

[pscustomobject]@{
    TaskStatePath = $taskStatePath
    DecisionsPath = if ($Decision) { $decisionsPath } else { $null }
    UpdatedAt     = $timestamp
}
