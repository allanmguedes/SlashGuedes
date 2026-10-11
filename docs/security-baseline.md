# Continuous security baseline

This baseline implements A-004/#94. It adds checks and evidence; it does not
approve dependency upgrades, certify the app, or replace analysis B/#86 and
native OCR review #97. Existing PR/build/release-approval controls remain.

## Checks and scope

| Control | Runs | Failure/result |
| --- | --- | --- |
| CodeQL C#, Actions and Python, security-extended | PR to main, main push, Monday, manual | Results in GitHub code scanning; manual build includes actual WPF/XAML; Python includes inventory/build helpers. Does not inspect third-party native binaries. |
| Gitleaks CLI 8.30.1 | Same triggers in Security Baseline | All reachable Git refs; synthetic self-test; finding or scanner error fails. Redacted logs/reports stay transient and are deleted. |
| NuGet audit | Security Baseline and release validation | Direct/transitive advisories at every severity and audit-source errors fail; clean JSON report retained. |
| Action pin/permission policy | Existing Build/Capture and Security Baseline | Unreviewed Action, mutable ref or permission elevation fails; CodeQL only gets security-events:write in its analyzer job. |
| Inventory / CycloneDX 1.6 | Security Baseline and release validation | Actual portable/installed bytes, embedded resources and hashes, cache-matched package origins, runtime versions, declared licenses and explicit unknowns. |

CodeQL and the baseline are additional PR checks. `build` remains the currently
required server check; adding required security checks/severity gates is a
separate decision after reviewing initial results. `pull_request_target` and
`workflow_run` are not used. Forks receive no repository secrets; GitHub applies
its reduced token policy, including its code-scanning PR upload handling.

The public repository can use these GitHub security features. We do not enable
paid private-repository products or add external credentials. Standard hosted
runner usage follows GitHub's public repository policy.

## Repository settings

Desired settings are versioned in `.github/repository-controls/security-features.json`:
Dependabot alerts/security-update PRs, secret scanning and push protection.
The Actions allowlist adds only `github/codeql-action/init@*` and
`github/codeql-action/analyze@*`; full SHA remains mandatory. Local workflows pin
both to verified upstream commit `24c54180a607b1449ed407dd24f251e4e9147c8d`,
version v4.38.3 (the commit signature was verified via GitHub API).
These files do not apply settings. Read back the native repository and Actions
APIs after an authorized administrative change. Advanced CodeQL setup is the
versioned workflow; do not simultaneously enable default setup.

## Secret exceptions and safe triage

`.gitleaks.toml` extends default rules. Two AND-constrained exceptions address
analysis A's reviewed false positives: a `Ui.` key with exactly 16 uppercase
hex digits in the three translation catalogs, and the exact `tokens.json`
SHA-256 field in the frozen visual contract. Translation values are scanned.
No entire catalog, rule, directory or commit is ignored.

The self-test constructs a synthetic PAT in memory and a generic key inside a
temporary translation file, proves detection, and checks resource/hash noise.
It does not contact a token provider or push test secrets. This proves the
local scanner; GitHub push protection is verified as enabled via API, not
claimed to be tested with a real credential. Never post a secret in an issue,
PR, artifact or log to demonstrate detection.

On an alert, record its rule/advisory identifier, component/version/hash,
affected source path, entry point, actual app reach, preconditions, impact,
decision and related correction issue. Keep matched secret text private.
For a real credential, revoke/rotate first, then handle history exposure; do
not dismiss it merely because current code no longer contains it. For a
false positive, require evidence and a narrowly scoped exception with a
detection regression. No automatic dismissal or automatic upgrade/merge.

Initial triage: analysis A's `generic-api-key` alerts on Ui identifiers and
the design hash are non-authentication metadata. Default scanning identified
them; the reviewed exceptions remove this noise while the synthetic credentials
remain detectable in the same translation directory. This is tracked in #94.
Native OCR findings stay open in #97: wrapper version 5.2.0 is not evidence of
native Tesseract 5.2.0. Exact measured x64 hashes map to the separately observed
Tesseract 5.0.0/Leptonica 1.82.0 exports from analysis A. Changed hashes lose
that version assertion and require re-triage.

## Inventory limits and release evidence

`scripts/security-inventory.ps1` uses a dependency-free .NET metadata tool and
Python standard-library parser. It reads .NET bundle 6.0 or the installed
publish directory without loading/executing app/native assemblies. Each shipped
file/resource gets SHA-256; emoji ZIP entries are individually inventoried.
Matching resolved NuGet cache bytes identifies package origins even when the
assembly name/version differs (for example ScreenRecorderLib.Reference).
Unmatched items and unresolved licenses stay explicit as `NOASSERTION`; #96
must review native transitive notices and legacy/compiled assets. The SBOM is
an inventory/composition, not a complete native dependency graph or license
certification. This baseline does not upgrade OCR or rewrite third-party notices.

Artifacts contain `inventory.json`, `sbom.cdx.json` and NuGet results. PR evidence
retention is 14 days; release inventory retention is 30 days. Preserve accepted
release evidence with the release preparation dossier (#70/#95) before expiry.
Portable ZIP layout remains unchanged. Never present an earlier pilot's inventory
as the final release: compare executable SHA-256 and source commit on every build.

Local checks (PowerShell 7, .NET 10, Python 3, Git):

```powershell
./scripts/security-secrets.ps1
./scripts/security-dependencies.ps1
./scripts/security-dependencies-regression.ps1
python -m unittest discover -s scripts/security -p 'test_*.py'
./scripts/security-inventory.ps1 -Executable publish-portable/SlashDesk.exe -OutputDirectory component-inventory/portable
./scripts/security-inventory.ps1 -Executable publish-installed/SlashDesk.exe -OutputDirectory component-inventory/installed -Layout directory
```

## Update policy

Dependabot proposes NuGet and Action updates weekly with a limit of five PRs
per ecosystem. NuGet minor/patch version updates are grouped; majors stay
separate. Security-update PRs are reviewed individually. Actions majors/runtime
migrations require a deliberate compatibility review; review them monthly even
when ignored by automatic version updates. Never freeze servicing indefinitely.

Keep .NET setup on `10.0.x` to receive servicing updates. Every self-contained
release must rebuild with the supported patched SDK/runtime; updating the PC's
runtime does not patch a previously distributed EXE. Record actual included
runtime versions in inventory, review Microsoft security advisories monthly and
before release, and plan support-lifecycle upgrades explicitly. Scanner CLI pins
and verified archive hashes are reviewed monthly and immediately on relevant
security advisories. Dependabot can propose pins; maintainers verify upstream
commit/version/provenance and CI before authorizing integration.

References: [CodeQL advanced setup](https://docs.github.com/en/code-security/how-tos/find-and-fix-code-vulnerabilities/configure-code-scanning/configuring-advanced-setup-for-code-scanning),
[CodeQL supported languages](https://codeql.github.com/docs/codeql-overview/supported-languages-and-frameworks/),
[NuGet auditing](https://learn.microsoft.com/nuget/concepts/auditing-packages),
[Gitleaks](https://github.com/gitleaks/gitleaks),
[CycloneDX 1.6 schema](https://github.com/CycloneDX/specification/blob/master/schema/bom-1.6.schema.json).
