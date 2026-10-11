param([string]$GitleaksPath, [switch]$SelfTestOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$config = Join-Path $root '.gitleaks.toml'
$temporary = Join-Path ([IO.Path]::GetTempPath()) "SlashDesk secret scan $([guid]::NewGuid().ToString('N'))"
New-Item $temporary -ItemType Directory | Out-Null
try {
    if (-not $GitleaksPath) {
        $archive = Join-Path $temporary 'gitleaks.zip'
        Invoke-WebRequest 'https://github.com/gitleaks/gitleaks/releases/download/v8.30.1/gitleaks_8.30.1_windows_x64.zip' -OutFile $archive
        if ((Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'd29144deff3a68aa93ced33dddf84b7fdc26070add4aa0f4513094c8332afc4e') { throw 'Gitleaks archive checksum differs.' }
        Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $temporary 'tool')
        $GitleaksPath = Join-Path $temporary 'tool/gitleaks.exe'
    }
    $version = & $GitleaksPath version
    if ($LASTEXITCODE -ne 0 -or $version.Trim() -ne '8.30.1') { throw 'Expected Gitleaks 8.30.1.' }
    function Invoke-Scan([string[]]$ScanArguments, [string]$Report) {
        # Reports and logs are transient and redacted; never upload secret matches.
        & $GitleaksPath @ScanArguments --config $config --redact=100 --no-banner --exit-code 23 --report-format json --report-path $Report --log-level error *> (Join-Path $temporary 'scan.log')
        if ($LASTEXITCODE -notin @(0, 23)) { throw 'Secret scanner failed; investigate locally with redaction.' }
        return @(Get-Content -LiteralPath $Report -Raw | ConvertFrom-Json)
    }
    $fixture = Join-Path $temporary 'fixture'
    $catalogFolder = Join-Path $fixture 'src/SlashText/Localization'
    $designFolder = Join-Path $fixture 'docs/design/3.3.0'
    New-Item $catalogFolder, $designFolder -ItemType Directory -Force | Out-Null
    # Synthetic only. Construct in memory to avoid committing a credential-shaped fixture.
    $fake = 'ghp_' + 'aB3dE5fG7hJ9kL2mN4pQ6rS8tU1vW3xY5zA7'
    $catalog = Join-Path $catalogFolder 'en.json'
    Set-Content $catalog ('{"api_key":"' + $fake + '","Ui.ABCD0123ABCD0123":"key"}') -Encoding utf8NoBOM
    Set-Content (Join-Path $designFolder 'contract-manifest.json') ('{"tokens.json":"' + ('a1' * 32) + '"}') -Encoding utf8NoBOM
    $findings = @(Invoke-Scan @('dir', $fixture) (Join-Path $temporary 'fixture.json'))
    if (-not ($findings.RuleID -contains 'github-pat')) { throw 'Synthetic credential was not detected inside a translation file.' }
    if ($findings.RuleID -contains 'generic-api-key') { throw 'Reviewed Ui/hash exceptions produced noise.' }
    # A similar-looking value outside the reviewed paths must still be detected.
    Set-Content $catalog ('{"api_key":"' + $fake.Substring(4) + '"}') -Encoding utf8NoBOM
    $findings = @(Invoke-Scan @('dir', $fixture) (Join-Path $temporary 'generic.json'))
    if (-not ($findings.RuleID -contains 'generic-api-key')) { throw 'Generic credential in translation was incorrectly ignored.' }
    Write-Output 'Secret scanning self-test: OK (synthetic PAT and generic key detected; resource keys/hash remain quiet).'
    if (-not $SelfTestOnly) {
        $shallow = & git -C $root rev-parse --is-shallow-repository
        if ($LASTEXITCODE -ne 0 -or $shallow.Trim() -ne 'false') { throw 'Secret history scan requires a complete checkout.' }
        $findings = @(Invoke-Scan @('git', $root, '--log-opts=--all') (Join-Path $temporary 'history.json'))
        if ($findings.Count -ne 0) { throw "Secret history scan: $($findings.Count) finding(s). Review locally; do not publish matched text." }
        Write-Output 'Secret history scan: OK (all reachable refs; no unresolved findings).'
    }
} finally {
    $target = [IO.Path]::GetFullPath($temporary)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($target.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $target -Leaf) -like 'SlashDesk secret scan *') { Remove-Item -LiteralPath $target -Recurse -Force }
}
