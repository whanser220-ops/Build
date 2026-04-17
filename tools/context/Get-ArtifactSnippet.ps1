[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Path,

    [int]$Head,

    [int]$Tail = 40,

    [string]$Pattern,

    [int]$Context = 2
)

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$candidatePath = $Path

if (-not [System.IO.Path]::IsPathRooted($candidatePath)) {
    $candidatePath = Join-Path $repoRoot $candidatePath
}

$resolvedPath = (Resolve-Path -LiteralPath $candidatePath).Path

if ($PSBoundParameters.ContainsKey('Head') -and $PSBoundParameters.ContainsKey('Tail') -and -not $Pattern) {
    throw 'Use either -Head or -Tail when reading a file without -Pattern.'
}

if ($Pattern) {
    $matches = Select-String -Path $resolvedPath -Pattern $Pattern -Context $Context, $Context

    if (-not $matches) {
        Write-Output "No matches found in $resolvedPath"
        return
    }

    foreach ($match in $matches) {
        Write-Output ("=" * 80)
        Write-Output ("Match at line {0}" -f $match.LineNumber)
        foreach ($line in $match.Context.PreContext) {
            Write-Output $line
        }
        Write-Output $match.Line
        foreach ($line in $match.Context.PostContext) {
            Write-Output $line
        }
    }

    return
}

if ($PSBoundParameters.ContainsKey('Head')) {
    Get-Content -LiteralPath $resolvedPath -Head $Head
    return
}

Get-Content -LiteralPath $resolvedPath -Tail $Tail
