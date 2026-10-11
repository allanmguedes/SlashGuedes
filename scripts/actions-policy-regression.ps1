$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'actions-policy-smoke.ps1'
$source = Join-Path $PSScriptRoot '../.github/workflows'
$fixture = Join-Path ([IO.Path]::GetTempPath()) "SlashDesk actions tests $([guid]::NewGuid().ToString('N'))"
$cases = @(
    @{ file = 'codeql.yml'; old = '      security-events: write'; replacement = '      contents: write'; label = 'CodeQL contents elevation' },
    @{ file = 'codeql.yml'; old = '      security-events: write'; replacement = "      security-events: write`n      actions: write"; label = 'CodeQL extra permission' },
    @{ file = 'build.yml'; old = 'actions/checkout@[0-9a-f]{40}\s+# v4\.4\.0'; replacement = 'github/codeql-action/init@24c54180a607b1449ed407dd24f251e4e9147c8d # v4.38.3'; label = 'CodeQL outside analyzer' },
    @{ file = 'build.yml'; old = '    runs-on: windows-latest'; replacement = "    permissions:`n      contents: read`n      security-events: write`n    runs-on: windows-latest"; label = 'security upload outside CodeQL' },
    @{ file = 'build.yml'; old = 'actions/checkout@[0-9a-f]{40}'; replacement = 'actions/checkout@v4'; label = 'mutable tag' },
    @{ file = 'build.yml'; old = 'actions/checkout@[0-9a-f]{40}'; replacement = 'actions/checkout@1234567'; label = 'short SHA' },
    @{ file = 'build.yml'; old = '# v4\.4\.0'; replacement = '# undocumented'; label = 'missing version' },
    @{ file = 'build.yml'; old = 'actions/checkout@'; replacement = 'unreviewed/checkout@'; label = 'unreviewed repository' },
    @{ file = 'build.yml'; old = 'persist-credentials: false'; replacement = 'persist-credentials: true'; label = 'persistent read credentials' },
    @{ file = 'build.yml'; old = 'permissions:\s*\r?\n  contents: read'; replacement = "permissions:`n  contents: write"; label = 'workflow write permission' },
    @{ file = 'build.yml'; old = '    runs-on: windows-latest'; replacement = "    permissions:`n      contents: write`n    runs-on: windows-latest"; label = 'build write permission' },
    @{ file = 'build.yml'; old = '  pull_request:'; replacement = '  pull_request_target:'; label = 'privileged PR trigger' },
    @{ file = 'build.yml'; old = '  pull_request:'; replacement = "  'pull_request_target':"; label = 'quoted privileged PR trigger' },
    @{ file = 'build.yml'; old = '(?s)on:\r?\n.*?\r?\npermissions:'; replacement = "on: [push, pull_request_target]`n`npermissions:"; label = 'inline privileged PR trigger' },
    @{ file = 'build.yml'; old = 'persist-credentials: false'; replacement = "persist-credentials: false`n          token: `${{ secrets.EXTRA_TOKEN }}"; label = 'custom token' },
    @{ file = 'build.yml'; old = 'persist-credentials: false'; replacement = "persist-credentials: false`n          allow-unsafe-pr-checkout: true"; label = 'unsafe checkout' },
    @{ file = 'build.yml'; old = 'persist-credentials: false'; replacement = '# no explicit credential policy'; label = 'implicit credentials' },
    @{ file = 'build.yml'; old = 'permissions:\s*\r?\n  contents: read'; replacement = 'permissions: read-all'; label = 'broad scalar permission' },
    @{ file = 'tag-release.yml'; old = 'persist-credentials: true'; replacement = 'persist-credentials: false'; label = 'missing push credential exception' },
    @{ file = 'tag-release.yml'; old = '    environment: release-approval'; replacement = ''; label = 'missing tag approval environment' },
    @{ file = 'release.yml'; old = '    environment: release-approval'; replacement = '    environment: unprotected'; label = 'wrong release approval environment' }
)
try {
    New-Item -Path $fixture -ItemType Directory | Out-Null
    $files = @(Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Extension -in @('.yml', '.yaml') })
    foreach ($file in $files) { Copy-Item -LiteralPath $file.FullName -Destination $fixture }
    & $checker -WorkflowDirectory $fixture | Out-Null
    foreach ($case in $cases) {
        $target = Join-Path $fixture $case.file
        $original = Get-Content -LiteralPath $target -Raw
        $pattern = [regex]::new($case.old)
        if (-not $pattern.IsMatch($original)) { throw "Mutation target missing: $($case.label)" }
        $replacement = $case.replacement
        # MatchEvaluator preserves literal expression/variable syntax in fixtures.
        $mutated = $pattern.Replace($original, [Text.RegularExpressions.MatchEvaluator]{ param($match) $replacement }, 1)
        Set-Content -LiteralPath $target $mutated -NoNewline
        $rejected = $false
        try { & $checker -WorkflowDirectory $fixture | Out-Null } catch { $rejected = $true }
        Set-Content -LiteralPath $target $original -NoNewline
        if (-not $rejected) { throw "Policy regression accepted: $($case.label)" }
    }
    & $checker -WorkflowDirectory $fixture | Out-Null
    Write-Output "Actions policy regression: OK ($($cases.Count) invalid configurations rejected; valid workflows accepted)"
} finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixture)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($resolvedFixture.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolvedFixture -Leaf) -like 'SlashDesk actions tests *') {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
