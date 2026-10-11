# GitHub Actions: pins, permissions and updates

All external Actions use full commit SHAs and an inline semantic version comment.
The comment is for maintenance; the SHA is the executable reference. Dependabot
supports both SHA updates and same-line version comments.

## Reviewed pins (2026-10-11)

| Official repository | Version | Full commit |
| --- | --- | --- |
| [actions/checkout](https://github.com/actions/checkout/releases/tag/v4.4.0) | v4.4.0 | [11d5960a326750d5838078e36cf38b85af677262](https://github.com/actions/checkout/commit/11d5960a326750d5838078e36cf38b85af677262) |
| [actions/setup-dotnet](https://github.com/actions/setup-dotnet/releases/tag/v4.3.1) | v4.3.1 | [67a3573c9a986a3f9c594539f4ab511d57bb3ce9](https://github.com/actions/setup-dotnet/commit/67a3573c9a986a3f9c594539f4ab511d57bb3ce9) |
| [actions/upload-artifact](https://github.com/actions/upload-artifact/releases/tag/v4.6.2) | v4.6.2 | [ea165f8d65b6e75b540449e92b4886f43607fa02](https://github.com/actions/upload-artifact/commit/ea165f8d65b6e75b540449e92b4886f43607fa02) |
| [actions/download-artifact](https://github.com/actions/download-artifact/releases/tag/v4.3.0) | v4.3.0 | [d3f86a106a0bac45b974a628896c90dbdf5c8093](https://github.com/actions/download-artifact/commit/d3f86a106a0bac45b974a628896c90dbdf5c8093) |

Each SHA was resolved from the official repository's `v4` ref and checked against
the exact release tag. Release notes, Action manifests, runtime and package
metadata were reviewed. All four manifests use Node 20; this change preserves the
existing v4 runtime and artifact interfaces. checkout v4.4.0 changes unsafe
`pull_request_target` defaults; these workflows use ordinary `pull_request` and
do not enable `allow-unsafe-pr-checkout`. Verified commit signatures establish
provenance information, not a guarantee that all upstream code is safe.

## Permissions and checkout credentials

Every workflow defaults to `contents: read`. Unspecified permissions are not
granted by these explicit maps. Only `tag-release.yml/create-tag` and
`release.yml/publish-release` request `contents: write`; they depend on successful
read-only validation/build jobs.

All checkouts set `persist-credentials: false`, except the validated tag creation
job, which needs `true` for its `git push`. Release publication uses `GH_TOKEN`
only on the publishing step. Build/pilot jobs have no explicit secret/token
references, keep the normal PR trigger and do not add privileged triggers or
automatic approval/merge logic. Forks keep the existing read-only PR execution
model and may still need the repository's usual approval to run.

`scripts/actions-policy-smoke.ps1` checks every workflow against the reviewed
four-repository allowlist, full SHAs, version comments, explicit checkout
credential choices and the two permitted write jobs. The guard supports the
repository's block-style YAML conventions; it is not a general YAML parser or
an upstream vulnerability scanner. `actions-policy-regression.ps1` confirms that
15 invalid configurations fail while the valid workflow set passes. Both run
before compilation in Build Windows and Capture OCR Pilot Windows. actionlint
provides complementary Actions/YAML syntax validation during maintenance.

## Updating pins

`.github/dependabot.yml` requests weekly updates on Monday at 09:00 in
America/Sao_Paulo. Minor/patch updates are grouped; major upgrades are excluded
from routine version updates and require a separate runtime/compatibility review.
This configuration only covers Actions; application dependency/security scanning
is tracked in #94.

For each update PR:

1. Confirm every new SHA belongs to the intended official repository and resolves
   to the stated release tag. Inspect the upstream release/diff and manifest,
   including runtime, inputs, authentication, artifact behavior and post steps.
   A full SHA check alone cannot perform this review.
2. Keep the full SHA and readable version comment on every affected `uses` line;
   update the reviewed-pins table and review date above. Update the allowlist only
   when adding an Action deliberately, with the reason recorded in the PR.
3. Run `pwsh -NoProfile -File scripts/actions-policy-smoke.ps1`,
   `pwsh -NoProfile -File scripts/actions-policy-regression.ps1`,
   `pwsh -NoProfile -File scripts/release-workflow-smoke.ps1` and actionlint.
4. Require successful Build Windows and Capture OCR Pilot Windows for the exact
   PR commit. Build first exercises all four Actions, including a small artifact
   upload/download round trip with matching SHA-256 in a read-only job. The
   probe artifact expires after one day. The build validates WPF controls, installed/portable publishing,
   packaging and clean-folder startup. Capture OCR also exercises the packaged
   OCR and recording surfaces. If changing a pilot's behavior or runtime,
   dispatch that pilot against the PR branch as an additional check.
5. Review and approve integration explicitly. Dependabot only proposes the PR;
   there is no automatic merge. Real tag/release publication belongs to the
   release process, not pin-update tests.

For example, `git ls-remote https://github.com/actions/checkout.git
refs/tags/v4.4.0` resolves the exact tag. If a tag is annotated, resolve the peeled
commit too; do not confuse the tag-object SHA with the executable commit.

## Repository-wide policy evaluation

The live repository currently allows all Actions and does not require SHA pins.
Its token default is already read-only, with Action PR approval disabled
(checked 2026-10-11). The versioned guard narrows the workflow allowlist, but does
not change those server settings.

Recommended server policy after pinned workflows are integrated:

- Require full commit SHAs (`sha_pinning_required: true`).
- Use selected Actions: `actions/checkout@*`, `actions/setup-dotnet@*`,
  `actions/upload-artifact@*`, `actions/download-artifact@*`; disable blanket
  GitHub-owned and verified-creator allowances.
- Keep token defaults read-only and PR approval disabled.

Apply and verify this with the repository controls in #91, after integration,
so the existing main workflows can finish and pending branches can be rebased.
Older branches with mutable refs will need updates before their workflows run
under the stricter server policy. The scanners planned in #94 may require a
deliberate extension of the allowlist. This PR changes versioned workflow policy;
it does not preemptively change repository-wide execution settings.

References: [GitHub secure use](https://docs.github.com/en/actions/reference/security/secure-use),
[Dependabot supported ecosystems](https://docs.github.com/en/code-security/reference/supply-chain-security/supported-ecosystems-and-repositories),
[Dependabot options](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference),
[Actions permissions API](https://docs.github.com/en/rest/actions/permissions).
