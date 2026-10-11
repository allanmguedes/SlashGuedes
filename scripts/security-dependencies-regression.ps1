$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'security-dependencies.ps1'
$temporary = Join-Path ([IO.Path]::GetTempPath()) "SlashDesk dependency tests $([guid]::NewGuid().ToString('N'))"
New-Item $temporary -ItemType Directory | Out-Null
$previousExit = $global:LASTEXITCODE
try {
    # Invoke the real audit script with a local executable boundary stand-in.
    # The scanner/source is unavailable here on purpose; no network upgrades.
    function dotnet {
        if ($args[0] -eq 'restore') {
            foreach ($flag in @('-p:NuGetAudit=true', '-p:NuGetAuditMode=all', '-p:NuGetAuditLevel=low')) {
                if ($args -notcontains $flag) { throw "Audit mode regressed: $($args -join '|')" }
            }
            if (-not ($args | Where-Object { $_ -like '-p:WarningsAsErrors=NU1900*NU1905' })) { throw 'Fail-on-advisory/source settings regressed.' }
            $global:LASTEXITCODE = $auditFixtureRestoreExit
            return
        }
        $global:LASTEXITCODE = $auditFixtureListExit
        return $auditFixtureReport
    }
    $clean = '{"version":1,"sources":["https://api.nuget.org/v3/index.json"],"projects":[{"path":"fixture.csproj"}]}'
    $cases = @(
        @{ label = 'clean'; restore = 0; list = 0; report = $clean; reject = $false },
        @{ label = 'source outage or restore advisory'; restore = 1; list = 0; report = $clean; reject = $true },
        @{ label = 'query failure'; restore = 0; list = 1; report = $clean; reject = $true },
        @{ label = 'invalid JSON'; restore = 0; list = 0; report = 'incomplete'; reject = $true },
        @{ label = 'empty report'; restore = 0; list = 0; report = '{}'; reject = $true },
        @{ label = 'query problem'; restore = 0; list = 0; report = $clean.Replace('"projects":', '"problems":["fixture source failure"],"projects":'); reject = $true },
        @{ label = 'direct advisory'; restore = 0; list = 0; report = $clean.Replace('"path":"fixture.csproj"', '"path":"fixture.csproj","frameworks":[{"topLevelPackages":[{"id":"fixture","vulnerabilities":[{"severity":"Low"}]}]}]'); reject = $true },
        @{ label = 'transitive advisory'; restore = 0; list = 0; report = $clean.Replace('"path":"fixture.csproj"', '"path":"fixture.csproj","frameworks":[{"transitivePackages":[{"id":"fixture","vulnerabilities":[{"severity":"Critical"}]}]}]'); reject = $true }
    )
    foreach ($case in $cases) {
        $auditFixtureRestoreExit = $case.restore; $auditFixtureListExit = $case.list; $auditFixtureReport = $case.report
        $rejected = $false
        $failure = ''
        try { & $checker -OutputDirectory $temporary | Out-Null } catch { $rejected = $true; $failure = $_.Exception.Message }
        if ($rejected -ne $case.reject) { throw "Dependency audit regression: $($case.label) — $failure" }
    }
    Write-Output "Dependency audit regression: OK ($($cases.Count) cases, including direct/transitive findings and source outages)."
} finally {
    $global:LASTEXITCODE = $previousExit
    $target = [IO.Path]::GetFullPath($temporary)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($target.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $target -Leaf) -like 'SlashDesk dependency tests *') { Remove-Item -LiteralPath $target -Recurse -Force }
}
