param([string]$OutputDirectory = 'security-evidence')
$ErrorActionPreference = 'Stop'
New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
# .NET 10's NuGet audit checks direct and transitive dependencies. Source outages
# are failures, not clean reports. No package upgrades or suppressions are applied.
$restoreArguments = @('restore', 'SlashText.sln', '-p:Platform=x64', '-p:NuGetAudit=true',
    '-p:NuGetAuditMode=all', '-p:NuGetAuditLevel=low', '-p:WarningsAsErrors=NU1900%3BNU1901%3BNU1902%3BNU1903%3BNU1904%3BNU1905')
dotnet @restoreArguments
if ($LASTEXITCODE -ne 0) { throw 'NuGet audit failed; triage advisory/source health before integration.' }
$report = dotnet list SlashText.sln package --vulnerable --include-transitive --format json --no-restore
if ($LASTEXITCODE -ne 0) { throw 'NuGet vulnerability report failed.' }
$document = ($report -join "`n") | ConvertFrom-Json
if ($document.version -ne 1 -or @($document.projects).Count -eq 0 -or @($document.sources).Count -eq 0) { throw 'NuGet returned an incomplete audit report.' }
if ($document.problems.Count -gt 0) { throw 'NuGet reported query problems.' }
$count = 0
foreach ($project in $document.projects) {
    foreach ($framework in $project.frameworks) {
        foreach ($package in @($framework.topLevelPackages) + @($framework.transitivePackages)) {
            if ($null -ne $package) { $count += @($package.vulnerabilities).Count }
        }
    }
}
$report | Set-Content (Join-Path $OutputDirectory 'nuget-audit.json') -Encoding utf8NoBOM
if ($count -ne 0) { throw "NuGet audit found $count advisory reference(s); see report and triage policy." }
Write-Output 'NuGet audit: OK (direct/transitive; source failures block the check).'
