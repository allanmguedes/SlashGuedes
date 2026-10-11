param([string]$WorkflowDirectory = (Join-Path $PSScriptRoot '../.github/workflows'))

$ErrorActionPreference = 'Stop'
$allowedActions = @('actions/checkout', 'actions/setup-dotnet', 'actions/upload-artifact', 'actions/download-artifact', 'github/codeql-action/init', 'github/codeql-action/analyze')
$writeJobs = @('tag-release.yml/create-tag', 'release.yml/publish-release')
$files = @(Get-ChildItem -LiteralPath $WorkflowDirectory -File | Where-Object { $_.Extension -in @('.yml', '.yaml') })
if ($files.Count -eq 0) { throw 'No workflows found.' }
$references = 0
$checkouts = 0
$writes = [Collections.Generic.List[string]]::new()

# This guard checks this repository's block-style workflow policy. actionlint
# validates YAML/Actions syntax separately; upstream identity is reviewed on PRs.
foreach ($file in $files) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    $lines = $text -split '\r?\n'
    if ($text -notmatch '(?m)^permissions:\r?\n  contents: read\r?$') {
        throw "Workflow must default to contents:read: $($file.Name)"
    }
    if ($text -notmatch '(?m)^on:\r?$') {
        throw "Use block-style workflow triggers for policy review: $($file.Name)"
    }
    if ($text -match '(?m)^  [''"]?(pull_request_target|workflow_run)[''"]?:') {
        throw "Privileged trigger requires a separate security review: $($file.Name)"
    }
    if ($text -match '(?m)^  pull_request:' -and $text -match '\$\{\{\s*(secrets\.|github\.token)' ) {
        throw "Pull request workflow must not explicitly request secrets/tokens: $($file.Name)"
    }
    $job = ''
    $jobStart = 0
    $insideJobs = $false
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        if ($line -eq 'jobs:') { $insideJobs = $true }
        if ($insideJobs -and $line -match '^  ([a-zA-Z0-9_-]+):\s*$') { $job = $Matches[1]; $jobStart = $index }
        $context = "$($file.Name)/$job"

        if ($line -match '^(\s*)permissions:(.*)$') {
            $indent = $Matches[1].Length
            if ($indent -notin @(0, 4) -or $Matches[2].Trim().Length -ne 0) {
                throw "Use explicit block-style permissions: $context"
            }
            $entries = [Collections.Generic.List[string]]::new()
            for ($next = $index + 1; $next -lt $lines.Count; $next++) {
                $member = $lines[$next]
                if ($member.Trim().Length -eq 0 -or $member.TrimStart().StartsWith('#')) { continue }
                $memberIndent = $member.Length - $member.TrimStart().Length
                if ($memberIndent -le $indent) { break }
                if ($memberIndent -ne $indent + 2) { throw "Unexpected permission indentation: $context" }
                $entries.Add($member.Trim())
            }
            $codeqlPermissions = $indent -eq 4 -and $context -ceq 'codeql.yml/analyze' -and
                (($entries | Sort-Object) -join ',') -ceq 'contents: read,security-events: write'
            if (-not $codeqlPermissions -and ($entries.Count -ne 1 -or $entries[0] -cnotmatch '^contents: (read|write)$')) {
                throw "Only reviewed contents and CodeQL security-events permissions are allowed: $context"
            }
            if ($entries[0] -eq 'contents: write') {
                if ($indent -ne 4 -or $context -cnotin $writeJobs) { throw "Unexpected write permission: $context" }
                $jobEnd = $lines.Count
                for ($next = $index + 1; $next -lt $lines.Count; $next++) {
                    if ($lines[$next] -match '^  [a-zA-Z0-9_-]+:') { $jobEnd = $next; break }
                }
                $environments = @($lines[$jobStart..($jobEnd - 1)] | Where-Object { $_ -match '^    environment:' })
                if ($environments.Count -ne 1 -or $environments[0].Trim() -cne 'environment: release-approval') {
                    throw "Publication job must use release-approval environment: $context"
                }
                $writes.Add($context)
            }
        }

        if ($line -match '^\s+(?:-\s+)?uses:\s*(.*)$') {
            $reference = $Matches[1]
            if ($reference -cnotmatch '^(?<action>[a-zA-Z0-9_-]+/[a-zA-Z0-9_-]+(?:/[a-zA-Z0-9_-]+)?)@(?<sha>[0-9a-f]{40})\s+#\s+(?<version>v[0-9]+\.[0-9]+\.[0-9]+)\s*$') {
                throw "Action requires full commit SHA and version comment: $context"
            }
            $action = $Matches.action
            if ($action -cnotin $allowedActions) { throw "Action requires allowlist review: $action" }
            if ($action.StartsWith('github/codeql-action/') -and $context -cne 'codeql.yml/analyze') { throw "CodeQL action outside reviewed analyzer: $context" }
            $references++
            if ($action -eq 'actions/checkout') {
                $checkouts++
                $settings = [Collections.Generic.List[string]]::new()
                for ($next = $index + 1; $next -lt $lines.Count; $next++) {
                    $member = $lines[$next]
                    if ($member -match '^      - |^  [a-zA-Z0-9_-]+:') { break }
                    $settings.Add($member)
                }
                $settingsText = $settings -join "`n"
                $expected = if ($context -ceq 'tag-release.yml/create-tag') { 'true' } else { 'false' }
                $credentialSettings = @($settings | Where-Object { $_ -match '^          persist-credentials:' })
                if ($credentialSettings.Count -ne 1 -or $credentialSettings[0].Trim() -cne "persist-credentials: $expected") {
                    throw "Checkout credential persistence must be $expected`: $context"
                }
                if ($settingsText -match '(?m)^          token:' -or $settingsText -match '(?m)^          allow-unsafe-pr-checkout:\s*(?!false\s*$)') {
                    throw "Custom tokens/unsafe checkout require review: $context"
                }
            }
        }
    }
}
if ($writes.Count -ne 2 -or @($writes | Sort-Object -Unique).Count -ne 2) {
    throw 'Only the two tag/release publication jobs should have write permission.'
}
Write-Output "Actions policy smoke: OK ($($files.Count) workflows; $references pins; $checkouts checkouts; 2 write jobs)"
