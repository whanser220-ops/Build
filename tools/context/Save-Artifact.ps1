[CmdletBinding(DefaultParameterSetName = 'FromContent')]
param(
    [ValidateSet('logs', 'docs', 'web', 'json', 'analysis')]
    [string]$Category = 'logs',

    [string]$Name = 'artifact',

    [string]$Extension = 'txt',

    [string]$Summary = '',

    [string]$Source = '',

    [Parameter(Mandatory = $true, ParameterSetName = 'FromPath')]
    [string]$InputPath,

    [Parameter(Mandatory = $true, ParameterSetName = 'FromContent')]
    [string]$Content,

    [Parameter(ValueFromPipeline = $true, ParameterSetName = 'FromPipeline')]
    [string]$InputObject
)

begin {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $artifactRoot = Join-Path $repoRoot ".workspace\artifacts\$Category"
    $buffer = New-Object System.Text.StringBuilder
}

process {
    if ($PSCmdlet.ParameterSetName -eq 'FromPipeline' -and $null -ne $InputObject) {
        [void]$buffer.AppendLine($InputObject)
    }
}

end {
    if (-not (Test-Path -LiteralPath $artifactRoot)) {
        New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
    }

    $safeName = ($Name -replace '[^A-Za-z0-9._-]', '-').Trim('-')
    if ([string]::IsNullOrWhiteSpace($safeName)) {
        $safeName = 'artifact'
    }

    $safeExtension = $Extension.Trim().TrimStart('.')
    if ([string]::IsNullOrWhiteSpace($safeExtension)) {
        $safeExtension = 'txt'
    }

    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $baseName = "$timestamp-$safeName"
    $artifactPath = Join-Path $artifactRoot "$baseName.$safeExtension"
    $metadataPath = Join-Path $artifactRoot "$baseName.meta.json"

    switch ($PSCmdlet.ParameterSetName) {
        'FromPath' {
            $resolvedInputPath = (Resolve-Path -LiteralPath $InputPath).Path
            Copy-Item -LiteralPath $resolvedInputPath -Destination $artifactPath -Force
        }
        'FromContent' {
            Set-Content -LiteralPath $artifactPath -Value $Content -Encoding utf8
        }
        'FromPipeline' {
            $pipelineContent = $buffer.ToString()
            if ([string]::IsNullOrWhiteSpace($pipelineContent)) {
                throw 'No pipeline content was received. Pass -Content, -InputPath, or pipe text into the script.'
            }
            Set-Content -LiteralPath $artifactPath -Value $pipelineContent -Encoding utf8
        }
    }

    $artifactItem = Get-Item -LiteralPath $artifactPath
    $metadata = [ordered]@{
        createdAt    = (Get-Date).ToString('o')
        category     = $Category
        name         = $safeName
        extension    = $safeExtension
        summary      = $Summary
        source       = $Source
        artifactPath = $artifactPath
        inputPath    = if ($PSCmdlet.ParameterSetName -eq 'FromPath') { $resolvedInputPath } else { $null }
        bytes        = $artifactItem.Length
    }

    $metadata | ConvertTo-Json | Set-Content -LiteralPath $metadataPath -Encoding utf8

    [pscustomobject]@{
        ArtifactPath = $artifactPath
        MetadataPath = $metadataPath
        Summary      = $Summary
        Bytes        = $artifactItem.Length
    }
}
