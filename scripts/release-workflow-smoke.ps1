$ErrorActionPreference = 'Stop'

$path = '.github/workflows/release.yml'
if (-not (Test-Path $path)) {
    throw 'Workflow de Release ausente.'
}

$workflow = Get-Content $path -Raw
foreach ($required in @(
    "tags:",
    "'v*.*.*'",
    'contents: write',
    'dotnet restore SlashText.sln',
    'dotnet build SlashText.sln --configuration Release',
    'SlashText.SmokeTests',
    'PublishProfile=Portable',
    'PublishProfile=Installed',
    'Get-FileHash $portableZip -Algorithm SHA256',
    'gh @arguments',
    '--generate-notes',
    '--prerelease'
)) {
    if (-not $workflow.Contains($required)) {
        throw "Workflow de Release incompleto: $required"
    }
}

if ($workflow.Contains('pull_request:') -or $workflow.Contains('branches: [main]')) {
    throw 'O workflow de Release não pode publicar em push comum ou pull request.'
}

# Execute the production PowerShell blocks with isolated files and fake git/gh.
# No tag, network call, or release is created by these regression tests.
& "$PSScriptRoot/release-input-regression.ps1"

Write-Output 'Release workflow smoke: OK'
