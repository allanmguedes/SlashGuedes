# Repository and release controls

Desired settings are recorded in `.github/repository-controls/*.json`. Server
settings require an explicit authorized administrative change; editing these
files alone does not apply them. Audit evidence records the effective API state
and controlled test results for #91.

## Main and release tags

`SlashDesk main` is an active branch ruleset targeting `refs/heads/main`:

- PR required; squash is the allowed merge method and history must be linear.
- The Build Windows check is required and the branch must be up to date. Its
  actual check context is `build`, originating from GitHub Actions app ID 15368.
  Do not substitute the workflow display name for this check context.
- Branch deletion and non-fast-forward updates are prohibited.
- No permanent bypass actors. Zero required approving reviews accommodates the
  single maintainer; code-owner review and approval by another last-push reviewer
  are not required. Open review conversations must be resolved.

`SlashDesk immutable release tags` targets `refs/tags/v*`. Updates, deletion and
non-fast-forward changes are prohibited, without bypass actors. Creation remains
available to authorized writers so the existing GITHUB_TOKEN flow needs no PAT
or new installed App. This is an immutability policy, not a claim that only one
workflow can create tags at the platform level.

The normal creation path is Tag Stable Release on `main`: version matching,
duplicate check, immutable source SHA and main recheck, followed by explicit
environment approval. Release publication additionally verifies that the tag's
commit is in the fetched `origin/main` history. A tag pointing to a feature-only
commit is rejected before build/publication. Existing official v3.2.0 is in main
history; historical tags can still be published if their project version matches.

## Explicit publication approval

Both write jobs use `environment: release-approval`, after read-only validation
or build. Its required reviewer is the sole maintainer `lucasllira`. Self-review
is allowed so that this maintainer can approve a run they initiated; administrative
bypass is disabled. There are no environment secrets added by these controls.

Only workflow refs matching branch `main` or tags `v*` can use this environment.
For manual release dispatches, the selected workflow ref is `main`; the release
input separately chooses an existing tag, which is validated before checkout.
These environment ref restrictions do not replace SemVer, project-version and
main-history checks.

For a real release:

1. Prepare/integrate the version and notes through a checked PR to main.
2. Obtain explicit user authorization for the intended production tag/release.
3. Dispatch Tag Stable Release from main with the intended stable version. After
   validation, the authorized maintainer reviews the run and approves its
   `release-approval` deployment before the tag is created.
4. Dispatch Release Windows from main for that existing tag. Tags pushed with
   GITHUB_TOKEN do not automatically start another workflow; do not introduce a
   PAT just to evade that behavior. A tag created by another authorized mechanism
   may trigger the existing tag-push workflow.
5. Review the completed validation/build/artifacts and approve the publishing
   job's `release-approval` deployment. Rejection keeps the publication from
   executing. Approval of tag creation does not silently approve release
   publication in a separate run.

Repository owners retain administrative powers, including direct API access and
configuration changes. These controls protect the normal repository/workflow
paths; they cannot remove the owner's authority. An agent must not infer
production publication authorization from approval of tests or a code PR.

## Verification and emergency procedure

Verify the effective server state with authenticated GitHub API reads:

- `/repos/lucasllira/SlashText/rules/branches/main` and `/rulesets`: PR, strict
  `build`/15368 check, deletion/non-fast-forward rules, and no bypass actors.
- `/rulesets/{id}`: correct tag target and immutable `v*` policy.
- `/environments/release-approval`: required reviewer 113149653,
  `prevent_self_review: false`, `can_admins_bypass: false`; deployment branch
  policies contain branch main and tag v* only.
- `/actions/permissions`, `/actions/permissions/selected-actions` and
  `/actions/permissions/workflow`: SHA required, only the four reviewed Actions,
  read-only token default and no Action PR approval.

Local/CI tests also run `actions-policy-smoke.ps1`,
`actions-policy-regression.ps1` and `release-workflow-smoke.ps1` (17 invalid policy
cases and 106 release behavioral cases). They check environment bindings and
the tag-ancestry failure paths along with previous guards. Live server state
cannot be proved by these offline tests alone.

Use disposable refs with the same rules to test rejected direct updates,
deletion, force updates and merge without the required check. A separately
authorized fixed test workflow can demonstrate a waiting environment, approval,
tag creation, and refused tag update/deletion without a production release.
Remove temporary PRs/refs/environment and temporary rule targets afterward,
preserving the production rules.

There is no routine `--admin` merge bypass or permanent tag mutation exception.
If recovery needs a rule/environment change, record the reason, exact affected
rule/ref and rollback plan in an issue, obtain explicit user authorization,
apply the smallest temporary exception, restore immediately and read back the
effective rules. Single-maintainer operation does not justify keeping safeguards
disabled. New collaborators must be evaluated before changing review requirements.

Older branches with mutable Action refs must be updated/rebased before running
under SHA enforcement. The scanners in #94 may require a deliberately reviewed
allowlist extension. See [Actions maintenance](github-actions-maintenance.md).

References: [GitHub rules API](https://docs.github.com/en/rest/repos/rules),
[environment API](https://docs.github.com/en/rest/deployments/environments),
[reviewing deployments](https://docs.github.com/en/actions/how-tos/deploy/configure-and-manage-deployments/review-deployments),
[GITHUB_TOKEN workflow triggering](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow).
