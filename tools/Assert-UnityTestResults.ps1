param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Path)) {
    throw "Unity test result XML was not found: $Path"
}

[xml]$document = Get-Content $Path
$testRun = $document."test-run"
if ($null -eq $testRun) {
    throw "Unity test result XML does not contain a test-run node: $Path"
}

$result = [string]$testRun.result
$total = [int]$testRun.total
$passed = [int]$testRun.passed
$failed = [int]$testRun.failed
$skipped = [int]$testRun.skipped

Write-Host "Unity test results: result=$result total=$total passed=$passed failed=$failed skipped=$skipped"

if ($result -eq "Passed" -and $failed -eq 0) {
    exit 0
}

$failedCases = $document.SelectNodes("//test-case[@result='Failed']")
if ($failedCases -and $failedCases.Count -gt 0) {
    Write-Host "Failed test cases:"
    $failedCases | Select-Object -First 20 | ForEach-Object {
        $message = $_.failure.message.InnerText.Trim()
        Write-Host "- $($_.fullname): $message"
    }
}

throw "Unity tests failed: result=$result total=$total passed=$passed failed=$failed skipped=$skipped"
