param([switch]$Apply)
$ErrorActionPreference = 'Stop'
$repo = 'MohamedXIV/Frostark'
function Api([string]$Path, [string]$Method = 'GET', [string]$Field = '', [long]$Id = 0) {
    $argv = @('api', "repos/$repo/$Path", '-H', 'X-GitHub-Api-Version: 2026-03-10')
    if ($Method -ne 'GET') { $argv += @('-X', $Method, '-F', "$Field=$Id") }
    $raw = & gh @argv
    if ($LASTEXITCODE -ne 0) { throw "GitHub API failed: $Path" }
    return ($raw | ConvertFrom-Json)
}
$issues = @(Api 'issues?state=all&per_page=100' | Where-Object { -not $_.pull_request })
$byNumber = @{}
foreach ($issue in $issues) { $byNumber[[int]$issue.number] = $issue }
$plans = @()
foreach ($issue in $issues | Where-Object { $_.number -ge 13 -and $_.number -le 43 }) {
    $header = ($issue.body -split "`n")[0]
    if ($header -notmatch '\*\*Parent Epic:\*\* #(\d+)') { throw "Missing parent for #$($issue.number)" }
    $parent = [int]$Matches[1]
    if ($parent -lt 2 -or $parent -gt 12) { throw 'Unexpected parent outside approved Epics.' }
    $deps = @()
    if ($header -match '\*\*Dependencies:\*\* (.*)') {
        $deps = @([regex]::Matches($Matches[1], '#(\d+)') | ForEach-Object { [int]$_.Groups[1].Value })
    }
    foreach ($dep in $deps) { if (-not $byNumber.ContainsKey($dep)) { throw "Unknown prerequisite #$dep" } }
    $plans += [pscustomobject]@{number=[int]$issue.number; parent=$parent; dependencies=$deps}
}
# Validate the documented graph before any mutation.
$visiting = @{}; $visited = @{}
function Visit([int]$Number) {
    if ($visiting[$Number]) { throw "Dependency cycle at #$Number" }
    if ($visited[$Number]) { return }
    $visiting[$Number] = $true
    foreach ($dep in ($plans | Where-Object number -eq $Number).dependencies) { Visit $dep }
    $visiting[$Number] = $false; $visited[$Number] = $true
}
foreach ($plan in $plans) { Visit $plan.number }
if (-not $Apply) { $plans | ConvertTo-Json -Depth 4; return }
$parents = @{}
foreach ($parent in 2..12) { $parents[$parent] = @(Api "issues/$parent/sub_issues?per_page=100") }
$verifiedParents = 0; $verifiedDependencies = 0
foreach ($plan in $plans | Sort-Object number) {
    if ($plan.number -notin $parents[$plan.parent].number) {
        Api "issues/$($plan.parent)/sub_issues" 'POST' 'sub_issue_id' $byNumber[$plan.number].id | Out-Null
    }
    $actualParent = Api "issues/$($plan.number)/parent"
    if ($actualParent.number -ne $plan.parent) { throw 'Parent verification failed.' }
    $verifiedParents++
    $existing = @(Api "issues/$($plan.number)/dependencies/blocked_by?per_page=100")
    foreach ($dep in $plan.dependencies) {
        if ($dep -notin $existing.number) { Api "issues/$($plan.number)/dependencies/blocked_by" 'POST' 'issue_id' $byNumber[$dep].id | Out-Null }
    }
    $actual = @(Api "issues/$($plan.number)/dependencies/blocked_by?per_page=100")
    foreach ($dep in $plan.dependencies) {
        if ($dep -notin $actual.number) { throw 'Dependency verification failed.' }
        $verifiedDependencies++
    }
    Write-Host "Verified #$($plan.number): parent #$($plan.parent), prerequisites $($plan.dependencies -join ',')"
}
@{parents=$verifiedParents; dependencies=$verifiedDependencies; cycleCheck='PASS'} | ConvertTo-Json
