$ErrorActionPreference = 'Stop'

function Assert-Condition($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Get-WorkflowScript([string]$Workflow, [string]$Step) {
    $lines = $Workflow -split '\r?\n'
    $start = [array]::IndexOf($lines, "      - name: $Step")
    Assert-Condition ($start -ge 0) "Workflow step missing: $Step"
    $code = [Collections.Generic.List[string]]::new()
    $inRun = $false
    for ($index = $start + 1; $index -lt $lines.Count; $index++) {
        $line = $lines[$index]
        if (-not $inRun) {
            if ($line -eq '        run: |') { $inRun = $true }
            elseif ($line -match '^      - name:|^  [a-z]') { break }
            continue
        }
        if ($line -match '^          ') { $code.Add($line.Substring(10)) }
        elseif ($line.Trim().Length -eq 0) { $code.Add('') }
        else { break }
    }
    Assert-Condition ($code.Count -gt 0) "Workflow script missing: $Step"
    $source = $code -join "`n"
    Assert-Condition (-not $source.Contains('${{')) "Expression interpolated into PowerShell: $Step"
    return [scriptblock]::Create($source)
}

$tagWorkflow = Get-Content '.github/workflows/tag-release.yml' -Raw
$releaseWorkflow = Get-Content '.github/workflows/release.yml' -Raw
$scripts = @{
    stable = Get-WorkflowScript $tagWorkflow 'Validate stable tag and main'
    create = Get-WorkflowScript $tagWorkflow 'Create annotated stable tag'
    release = Get-WorkflowScript $releaseWorkflow 'Resolve and validate release tag'
    source = Get-WorkflowScript $releaseWorkflow 'Validate project version and tag source'
    publish = Get-WorkflowScript $releaseWorkflow 'Create GitHub Release'
    package = Get-WorkflowScript $releaseWorkflow 'Package and calculate SHA-256'
}

foreach ($workflow in @($tagWorkflow, $releaseWorkflow)) {
    Assert-Condition ($workflow -match '(?m)^permissions:\r?\n  contents: read\r?$') 'Workflow default must be read-only.'
    Assert-Condition (([regex]::Matches($workflow, 'contents: write')).Count -eq 1) 'Only the publication job may write.'
}
Assert-Condition ($tagWorkflow.Contains('RELEASE_TAG: ${{ inputs.tag }}')) 'Stable input must be passed through env.'
Assert-Condition ($releaseWorkflow.Contains('RELEASE_TAG_INPUT: ${{ inputs.release_tag }}')) 'Release input must be passed through env.'
Assert-Condition ($tagWorkflow -match '(?s)  create-tag:\s+needs: validate-tag\s+runs-on: ubuntu-latest\s+permissions:\s+contents: write') 'Tag creation must depend on read-only validation.'
Assert-Condition ($releaseWorkflow -match '(?s)  publish-release:\s+needs: validate-build-release\s+runs-on: windows-latest\s+permissions:\s+contents: write') 'Publication must depend on read-only build/validation.'
Assert-Condition ($tagWorkflow.Contains('ref: ${{ needs.validate-tag.outputs.sha }}')) 'Tag creation must use the validated commit.'
Assert-Condition ($releaseWorkflow.Contains('ref: refs/tags/${{ steps.version.outputs.tag }}')) 'Release checkout must use an explicit tag ref.'
Assert-Condition ($releaseWorkflow.Contains('ref: ${{ needs.validate-build-release.outputs.sha }}')) 'Publication must use the validated commit.'
$releaseSteps = @('Resolve and validate release tag', 'Checkout tag', 'Validate project version and tag source', 'Build Release x64', 'Behavioral smoke tests', 'Upload temporary Actions artifacts')
$lastStep = -1
foreach ($step in $releaseSteps) {
    $position = $releaseWorkflow.IndexOf("      - name: $step")
    Assert-Condition ($position -gt $lastStep) "Release steps out of order: $step"
    $lastStep = $position
}

$sha = '1234567890123456789012345678901234567890'
$otherSha = 'abcdefabcdefabcdefabcdefabcdefabcdefabcd'
$environmentNames = @('RELEASE_TAG', 'RELEASE_VERSION', 'VALIDATED_SHA', 'RELEASE_TAG_INPUT', 'RELEASE_BASE_VERSION', 'RELEASE_PRERELEASE', 'PORTABLE_ZIP', 'PORTABLE_CHECKSUM', 'GITHUB_OUTPUT', 'GITHUB_EVENT_NAME', 'GITHUB_REF_NAME', 'GITHUB_REPOSITORY')
$savedEnvironment = @{}
foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$savedExitCode = $global:LASTEXITCODE
$fixture = Join-Path ([IO.Path]::GetTempPath()) "SlashDesk release tests $([guid]::NewGuid().ToString('N'))"
$caseCount = 0

function Invoke-Case([string]$Kind, [string]$Tag, [hashtable]$Options = @{}) {
    $script:caseCount++
    $state = @{
        calls = [Collections.Generic.List[object]]::new()
        main = $sha; head = $sha; tag = $sha
        duplicate = $false; fetchExit = 0; refExit = 1; tagExit = 0; pushExit = 0; ghExit = 0; resolveExit = 0
    }
    foreach ($key in $Options.Keys) { $state[$key] = $Options[$key] }
    function git {
        $state.calls.Add(@{ command = 'git'; arguments = @($args) })
        $global:LASTEXITCODE = 0
        switch ($args[0]) {
            'fetch' { $global:LASTEXITCODE = $state.fetchExit }
            'show-ref' { $global:LASTEXITCODE = if ($state.duplicate) { 0 } else { $state.refExit } }
            'rev-parse' {
                $global:LASTEXITCODE = $state.resolveExit
                switch ($args[1]) {
                    'origin/main' { $state.main }
                    'HEAD' { $state.head }
                    default { $state.tag }
                }
            }
            'config' { }
            'tag' { $global:LASTEXITCODE = $state.tagExit }
            'push' { $global:LASTEXITCODE = $state.pushExit }
            default { throw 'Unexpected git command in regression test.' }
        }
    }
    function gh {
        $state.calls.Add(@{ command = 'gh'; arguments = @($args) })
        $global:LASTEXITCODE = $state.ghExit
    }
    $env:RELEASE_TAG = $Tag
    $env:RELEASE_TAG_INPUT = $Tag
    $env:RELEASE_VERSION = '3.2.0'
    $env:VALIDATED_SHA = $sha
    $env:RELEASE_BASE_VERSION = '3.2.0'
    $env:RELEASE_PRERELEASE = 'false'
    $env:GITHUB_EVENT_NAME = 'workflow_dispatch'
    $env:GITHUB_REF_NAME = $Tag
    $env:GITHUB_REPOSITORY = 'example/SlashText'
    $env:PORTABLE_ZIP = 'release-assets/SlashDesk-3.2.0-portable-win-x64.zip'
    $env:PORTABLE_CHECKSUM = "$env:PORTABLE_ZIP.sha256"
    $env:GITHUB_OUTPUT = Join-Path $fixture 'outputs.txt'
    foreach ($name in $environmentNames) {
        if ($Options.ContainsKey($name)) { [Environment]::SetEnvironmentVariable($name, [string]$Options[$name], 'Process') }
    }
    Set-Content $env:GITHUB_OUTPUT '' -NoNewline
    $projectVersion = if ($Options.ContainsKey('projectVersion')) { $Options.projectVersion } else { '3.2.0' }
    Set-Content 'src/SlashText/SlashText.csproj' "<Project><PropertyGroup><Version>$projectVersion</Version></PropertyGroup></Project>"
    $failure = $null
    try { & $scripts[$Kind] | Out-Null } catch { $failure = $_ }
    $outputs = @{}
    foreach ($line in (Get-Content $env:GITHUB_OUTPUT)) {
        $parts = $line.Split('=', 2)
        if ($parts.Count -eq 2) { $outputs[$parts[0]] = $parts[1] }
    }
    return @{ error = $failure; outputs = $outputs; calls = $state.calls }
}

function Assert-Rejected($Result, [string]$Label) {
    Assert-Condition ($null -ne $Result.error) "Invalid case accepted: $Label"
    Assert-Condition ($Result.outputs.Count -eq 0) "Invalid case emitted trusted outputs: $Label"
    Assert-Condition (@($Result.calls | Where-Object { $_.command -eq 'gh' -or $_.arguments[0] -in @('tag', 'push') }).Count -eq 0) "Invalid case reached publication: $Label"
}

try {
    New-Item (Join-Path $fixture 'src/SlashText') -ItemType Directory -Force | Out-Null
    Push-Location $fixture
    # These are inert malformed data, including quotes and multiline output attempts.
    $invalid = @('', '3.2.0', 'V3.2.0', 'v03.2.0', 'v3.02.0', 'v3.2.00', ' v3.2.0', 'v3.2.0 ', 'v-3.2.0', 'v٣.2.0', "v3.2.0'", 'v3.2.0"', 'v3.2.0;data', 'v3.2.0`data', 'v3.2.0$(data)', "v3.2.0`n", "v3.2.0`r`n", "v3.2.0`ntag=v9.9.9", 'v3.2.0+build')
    foreach ($tag in $invalid) {
        Assert-Rejected (Invoke-Case stable $tag) 'stable malformed input'
        $result = Invoke-Case release $tag
        Assert-Rejected $result 'manual malformed input'
        Assert-Condition ($result.calls.Count -eq 0) 'Malformed release input must fail before accessing git.'
        Assert-Rejected (Invoke-Case release $tag @{ GITHUB_EVENT_NAME = 'push' }) 'push malformed input'
    }
    foreach ($tag in @('v3.2.0-alpha.1', 'v3.2.0-rc.1')) { Assert-Rejected (Invoke-Case stable $tag) 'prerelease passed to stable workflow' }
    foreach ($tag in @('v3.2.0-', 'v3.2.0-rc..1', 'v3.2.0-rc.01', 'v3.2.0-01', 'v3.2.0-.rc')) { Assert-Rejected (Invoke-Case release $tag) 'malformed prerelease' }
    foreach ($version in @('0.0.0', '3.2.0', '3.3.0')) {
        $result = Invoke-Case stable "v$version" @{ projectVersion = $version }
        Assert-Condition ($null -eq $result.error -and $result.outputs.tag -eq "v$version" -and $result.outputs.version -eq $version -and $result.outputs.sha -eq $sha) 'Stable validation lost valid outputs.'
    }
    foreach ($tag in @('v3.2.0', 'v3.2.0-alpha.1', 'v3.2.0-rc.1', 'v3.2.0-alpha-1', 'v3.2.0-0')) {
        foreach ($event in @('workflow_dispatch', 'push')) {
            $result = Invoke-Case release $tag @{ GITHUB_EVENT_NAME = $event }
            Assert-Condition ($null -eq $result.error -and $result.outputs.tag -eq $tag -and $result.outputs.version -eq $tag.Substring(1) -and $result.outputs.stableVersion -eq '3.2.0') 'Release validation lost valid outputs.'
            Assert-Condition ($result.outputs.prerelease -eq ($tag.Contains('-')).ToString().ToLowerInvariant()) 'Incorrect prerelease flag.'
        }
    }
    $result = Invoke-Case release 'ignored invalid manual input' @{ GITHUB_EVENT_NAME = 'push'; GITHUB_REF_NAME = 'v3.2.0' }
    Assert-Condition ($null -eq $result.error -and $result.outputs.tag -eq 'v3.2.0') 'Push must use its tag ref.'
    Assert-Rejected (Invoke-Case release 'v3.2.0' @{ GITHUB_EVENT_NAME = 'pull_request' }) 'unsupported event'
    foreach ($options in @(@{ projectVersion = '3.3.0' }, @{ duplicate = $true }, @{ refExit = 2 }, @{ fetchExit = 1 }, @{ resolveExit = 1 }, @{ head = $otherSha })) {
        Assert-Rejected (Invoke-Case stable 'v3.2.0' $options) 'stable source/duplicate/error guard'
    }
    foreach ($options in @(@{ projectVersion = '3.3.0' }, @{ resolveExit = 1 }, @{ tag = $otherSha })) {
        Assert-Rejected (Invoke-Case source 'v3.2.0' $options) 'existing tag source/version guard'
    }
    $result = Invoke-Case source 'v3.2.0'
    Assert-Condition ($null -eq $result.error -and $result.outputs.sha -eq $sha) 'Existing tag source must return its immutable commit.'
    foreach ($options in @(@{ main = $otherSha }, @{ head = $otherSha }, @{ duplicate = $true }, @{ fetchExit = 1 }, @{ refExit = 2 }, @{ resolveExit = 1 })) {
        Assert-Rejected (Invoke-Case create 'v3.2.0' $options) 'tag creation recheck'
    }
    Assert-Rejected (Invoke-Case create "v3.2.0`n") 'creation input defense'
    $result = Invoke-Case create 'v3.2.0'
    $tagCall = @($result.calls | Where-Object { $_.arguments[0] -eq 'tag' })
    $pushCall = @($result.calls | Where-Object { $_.arguments[0] -eq 'push' })
    Assert-Condition ($null -eq $result.error -and $tagCall.Count -eq 1 -and ($tagCall[0].arguments -join '|') -eq "tag|-a|v3.2.0|$sha|-m|SlashDesk 3.2.0" -and ($pushCall[0].arguments -join '|') -eq 'push|origin|refs/tags/v3.2.0') 'Annotated tag must target the validated commit without force.'
    $result = Invoke-Case create 'v3.2.0' @{ tagExit = 1 }
    Assert-Condition ($null -ne $result.error -and @($result.calls | Where-Object { $_.arguments[0] -eq 'push' }).Count -eq 0) 'Failed tag creation must not push.'
    Assert-Condition ($null -ne (Invoke-Case create 'v3.2.0' @{ pushExit = 1 }).error) 'Failed push must fail the job.'
    $result = Invoke-Case publish 'v3.2.0'
    $arguments = $result.calls[0].arguments
    Assert-Condition ($null -eq $result.error -and $result.calls.Count -eq 1 -and ($arguments[0..2] -join '|') -eq 'release|create|v3.2.0' -and $arguments -contains '--verify-tag' -and $arguments -contains '--generate-notes' -and $arguments -notcontains '--prerelease' -and $arguments -contains $env:PORTABLE_ZIP -and $arguments -contains $env:PORTABLE_CHECKSUM) 'Stable publication arguments changed.'
    New-Item 'docs/releases' -ItemType Directory -Force | Out-Null
    Set-Content 'docs/releases/3.2.0.md' 'Fixture release notes'
    $result = Invoke-Case publish 'v3.2.0-rc.1' @{ RELEASE_VERSION = '3.2.0-rc.1'; RELEASE_PRERELEASE = 'true' }
    $arguments = $result.calls[0].arguments
    Assert-Condition ($null -eq $result.error -and $arguments -contains '--notes-file' -and $arguments -contains 'docs/releases/3.2.0.md' -and $arguments -notcontains '--generate-notes' -and $arguments -contains '--prerelease' -and $arguments -contains 'SlashDesk 3.2.0-rc.1') 'Prerelease/notes publication arguments changed.'
    Assert-Condition ($null -ne (Invoke-Case publish 'v3.2.0' @{ ghExit = 1 }).error) 'Failed GitHub publication must fail the job.'
    New-Item 'publish-portable', 'publish-installed' -ItemType Directory | Out-Null
    Set-Content 'publish-portable/SlashDesk.exe' 'Fixture executable bytes'
    Set-Content 'publish-installed/SlashDesk.exe' 'Fixture installed bytes'
    $result = Invoke-Case package 'v3.2.0'
    Assert-Condition ($null -eq $result.error -and $result.outputs.portableZip -eq $env:PORTABLE_ZIP -and $result.outputs.portableChecksum -eq $env:PORTABLE_CHECKSUM) 'Packaging outputs must identify the publication assets.'
    $hash = (Get-FileHash $result.outputs.portableZip -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksum = Get-Content $result.outputs.portableChecksum -Raw
    Assert-Condition ($checksum -eq "$hash  SlashDesk-3.2.0-portable-win-x64.zip") 'Published checksum must describe the actual ZIP.'
    Expand-Archive -LiteralPath $result.outputs.portableZip -DestinationPath 'unpacked'
    $files = @(Get-ChildItem 'unpacked' -File)
    Assert-Condition ($files.Count -eq 1 -and $files[0].Name -eq 'SlashDesk.exe') 'Portable ZIP must contain only the executable.'
    Write-Output "Release input regression: OK ($caseCount behavioral cases; git/gh simulated)"
} finally {
    if ((Get-Location).Path -eq $fixture) { Pop-Location }
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    $global:LASTEXITCODE = $savedExitCode
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolvedFixture -Leaf) -like 'SlashDesk release tests *') {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
