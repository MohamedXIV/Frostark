param(
    [ValidateSet('Import', 'EditMode', 'PlayMode')][string]$Mode = 'Import',
    [ValidateRange(30, 1800)][int]$TimeoutSeconds = 600,
    [ValidatePattern('^[a-zA-Z0-9_\.]*$')][string]$TestFilter = '',
    [string]$EditorPath = 'F:\Program Files\Unity\6000.6.0f1\Editor\Unity.exe'
)
$ErrorActionPreference = 'Stop'
$project = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$requiredVersion = (Get-Content (Join-Path $project 'ProjectSettings/ProjectVersion.txt') | Select-Object -First 1) -replace 'm_EditorVersion: ', ''
if ($requiredVersion -ne '6000.6.0f1') {
    throw "Unsupported project version: $requiredVersion"
}
if (-not (Test-Path -LiteralPath $EditorPath -PathType Leaf)) {
    throw 'Required Unity Editor is missing at specified EditorPath.'
}

# 1. Respect Editor locking
$running = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" | Where-Object {
    $_.CommandLine -and $_.CommandLine.IndexOf($project, [StringComparison]::OrdinalIgnoreCase) -ge 0
}
if ($running) {
    $lockResponse = [ordered]@{
        ok = $false
        mode = $Mode
        error = 'EDITOR_LOCKED'
        message = 'Unity Editor is currently open for this project. Save and close it before running headless validation.'
    }
    $lockResponse | ConvertTo-Json -Depth 4
    exit 1
}

$logs = Join-Path $project 'Logs'
New-Item -ItemType Directory -Path $logs -Force | Out-Null
$log = Join-Path $logs ('validate-' + $Mode.ToLowerInvariant() + '.log')
$arguments = @('-batchmode', '-nographics', '-projectPath', ('"' + $project + '"'), '-logFile', ('"' + $log + '"'))

if ($Mode -eq 'Import') {
    $arguments += @('-quit', '-executeMethod', 'FrostArk.Bootstrap.BootstrapSetup.ReportReadiness')
} elseif ($Mode -eq 'PlayMode') {
    $results = Join-Path $logs ('results-' + $Mode.ToLowerInvariant() + '.xml')
    if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results }
    if ($TestFilter -and $TestFilter -like '*CombatPlayModeTests*') {
        $arguments += @('-executeMethod', 'FrostArk.Unity.Editor.InteractivePlayModeRunner.RunCombatPlayModeTests')
    } else {
        $arguments += @('-executeMethod', 'FrostArk.Unity.Editor.InteractivePlayModeRunner.RunAllPlayModeTests')
    }
} else {
    $results = Join-Path $logs ('results-' + $Mode.ToLowerInvariant() + '.xml')
    if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results }
    $arguments += @('-runTests', '-testPlatform', $Mode, '-testResults', ('"' + $results + '"'))
    if ($TestFilter) {
        $arguments += @('-testFilter', $TestFilter)
    }
}

$editorSettingsFile = Join-Path $project 'ProjectSettings/EditorSettings.asset'
$origEditorSettingsBytes = if (Test-Path -LiteralPath $editorSettingsFile) { [IO.File]::ReadAllBytes($editorSettingsFile) } else { $null }

try {
    $process = Start-Process -FilePath $EditorPath -ArgumentList $arguments -WorkingDirectory $project -WindowStyle Hidden -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force
        throw "Unity validation timed out after $TimeoutSeconds seconds. Log: $log"
    }
    $process.Refresh()
    $code = $process.ExitCode
} finally {
    if ($null -ne $origEditorSettingsBytes) {
        if (Test-Path -LiteralPath $editorSettingsFile) {
            $currentBytes = [IO.File]::ReadAllBytes($editorSettingsFile)
            $isSame = ($currentBytes.Length -eq $origEditorSettingsBytes.Length)
            if ($isSame) {
                for ($i = 0; $i -lt $currentBytes.Length; $i++) {
                    if ($currentBytes[$i] -ne $origEditorSettingsBytes[$i]) {
                        $isSame = $false
                        break
                    }
                }
            }
            if (-not $isSame) {
                [IO.File]::WriteAllBytes($editorSettingsFile, $origEditorSettingsBytes)
            }
        } else {
            [IO.File]::WriteAllBytes($editorSettingsFile, $origEditorSettingsBytes)
        }
    } else {
        if (Test-Path -LiteralPath $editorSettingsFile) {
            Remove-Item -LiteralPath $editorSettingsFile -Force
        }
    }
}

# Check compiler/package errors in log
$errors = Select-String -LiteralPath $log -Pattern 'error CS\d{4}|Scripts have compiler errors|Failed to resolve packages'
if ($errors) {
    $errResponse = [ordered]@{
        ok = $false
        mode = $Mode
        exitCode = $code
        error = 'COMPILATION_ERROR'
        message = 'Unity reported compilation or package resolution errors.'
        log = $log
    }
    $errResponse | ConvertTo-Json -Depth 4
    exit 1
}

$summary = [ordered]@{
    ok = ($code -eq 0)
    mode = $Mode
    editorVersion = $requiredVersion
    exitCode = $code
    log = $log
    tests = 'NOT RUN'
}

if ($Mode -in @('EditMode', 'PlayMode')) {
    if (-not (Test-Path -LiteralPath $results)) {
        throw "Unity exited ($code) without generating test results file: $results. Log: $log"
    }
    [xml]$report = Get-Content -LiteralPath $results -Raw
    $run = $report.'test-run'
    $total = [int]$run.total
    $passed = [int]$run.passed
    $failed = [int]$run.failed
    $skipped = [int]$run.skipped
    $duration = [double]$run.duration
    $resultStatus = [string]$run.result

    $failures = @()
    if ($failed -gt 0) {
        $failedNodes = $report.SelectNodes("//test-case[@result='Failed']")
        if ($failedNodes) {
            foreach ($node in $failedNodes) {
                $failures += [ordered]@{
                    name = $node.name
                    message = if ($node.failure) { $node.failure.message.InnerText.Trim() } else { 'Unknown error' }
                }
            }
        }
    }

    $summary.ok = ($code -eq 0 -and $failed -eq 0)
    $summary.tests = [ordered]@{
        result = $resultStatus
        total = $total
        passed = $passed
        failed = $failed
        skipped = $skipped
        duration = $duration
        failures = $failures
        results = $results
    }
}

$summary | ConvertTo-Json -Depth 4
if (-not $summary.ok) { exit 1 }
