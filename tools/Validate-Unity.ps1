param(
    [ValidateSet('Import', 'EditMode')][string]$Mode = 'Import',
    [ValidateRange(60, 1800)][int]$TimeoutSeconds = 600,
    [string]$EditorPath = 'F:\Program Files\Unity\6000.6.0f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$requiredVersion = (Get-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1) -replace 'm_EditorVersion: ', ''
if ($requiredVersion -ne '6000.6.0f1') { throw "Unsupported project version: $requiredVersion" }
if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) { throw 'Required Unity Editor is missing.' }
$running = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
    $_.CommandLine -and $_.CommandLine.IndexOf($project, [StringComparison]::OrdinalIgnoreCase) -ge 0
}
if ($running) { throw 'This project is already open. Save and close its Editor before headless validation.' }
$logs = Join-Path $project 'Logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$log = Join-Path $logs ('bootstrap-' + $Mode.ToLowerInvariant() + '.log')
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $project + '"'), '-logFile', ('"' + $log + '"'))
if ($Mode -eq 'Import') { $arguments += @('-quit', '-executeMethod', 'FrostArk.Bootstrap.BootstrapSetup.ReportReadiness') }
else {
    $results = Join-Path $logs 'bootstrap-editmode.xml'
    if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results }
    $arguments += @('-runTests', '-testPlatform', 'EditMode', '-testFilter', 'FrostArk.Tests.PackageSmokeTests', '-testResults', ('"' + $results + '"'))
}
$process = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WorkingDirectory $project -WindowStyle Hidden -PassThru
if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
    Stop-Process -Id $process.Id
    throw "Unity validation timed out. Log: $log"
}
$process.Refresh()
$code = $process.ExitCode
if ($code -ne 0) { throw "Unity failed with exit code $code. Log: $log" }
$errors = Select-String -LiteralPath $log -Pattern 'error CS\d{4}|Scripts have compiler errors|Failed to resolve packages'
if ($errors) { throw "Unity reported compile/package errors. Log: $log" }
$summary = [ordered]@{mode=$Mode; editorVersion=$requiredVersion; exitCode=$code; log=$log; tests='NOT RUN'}
if ($Mode -eq 'EditMode') {
    if (-not (Test-Path -LiteralPath $results)) { throw 'Unity exited without test results.' }
    [xml]$report = Get-Content -LiteralPath $results -Raw
    $run = $report.'test-run'
    if ([int]$run.total -lt 2 -or [int]$run.failed -gt 0 -or $run.result -ne 'Passed') { throw 'Bootstrap tests did not all pass; inspect the NUnit XML.' }
    $summary.tests = @{total=[int]$run.total; passed=[int]$run.passed; failed=[int]$run.failed; results=$results}
}
$summary | ConvertTo-Json -Depth 4
