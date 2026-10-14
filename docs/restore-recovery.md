# Restore recovery

An interrupted restore is recovered before data layout defaults and application modules are loaded. Recovery returns the previous saved data set. If the available recovery material cannot be verified or accessed, SlashDesk does not load a partially restored profile: it shows a themed warning and closes. The available material is retained, and the warning can open the backups folder.

A restore records its progress and verifies retained originals before modifying active files or directories. Recovery can be repeated after another interruption. A confirmed commit stays applied; later housekeeping errors do not turn it into an aborted restore. User changes that do not match the saved or replacement data are preserved rather than silently overwritten. Normal cancellation completes recovery before returning the cancellation result.

The data layout keeps small private identity/pending markers and an owned restore journal in Backups. These are not entries permitted in imported backup ZIPs. Recovery uses relative, restricted targets, supports relocation of a portable data folder, refuses directory links and invalid records, and serializes operations with a file lease. Original files are copied and verified; they are not consumed by rollback. Temporary recovery copies are reused on retry. An in-process protection gate prevents JSON/history writes and snapshots of an uncertain set. Queued input/capture callbacks are ignored when the running app must close for recovery.

One application instance acquires the existing single-instance mutex before preparing/recovering data. UI maintenance continues to block conflicting operations. Portable startup probes may bypass that mutex only in an isolated pilot and never start hooks.

## Scope and limits

- Backup schema 1/2 and user file formats remain compatible. Full application-content validation is tracked separately in #117; protecting incomplete reads is #116; retention of old Markdown backups is #119.
- Incomplete legacy restore folders without a trustworthy journal are retained and cause protected startup. There is no guessed automatic replay of those files. If reopening still fails, diagnose the preserved material and use a verified backup; do not delete markers or recovery folders to suppress the warning.
- Normal file/layout errors remain actionable. Recovery may need the other application to release a file or storage to become writable. Never promise that reopening will resolve every cause.
- Original and replacement copies consume additional space. Limits are bounded by the existing backup budgets and journal bounds; exhausted space is a failure path, not a reason to discard originals.
- Controlled process-exit tests verify lifecycle/consistency, not physical power-loss or storage-controller guarantees. Local Windows drive boundaries are required for applying/recovering these transactions. A path that cannot be verified is refused before unsafe mutations.
- Unsaved image edits live in memory. The restore confirmation tells the user to save them first because unresolved recovery requires closing the app. The warning also explains that unsaved changes may be lost.
- Use the current app to finish recovery before deliberately opening an older binary that does not understand this journal.

## Validation

`RestoreRecoveryChecks` is part of the regular smoke suite. Its workers operate on inert data and terminate only themselves. It covers each file/directory apply boundary, directory detachment, interruption during rollback, prepared/committed states, a blocked rollback, verified new files originally absent, invalid/corrupt identity/journal/material, changed targets, missing backups, relocation, cancellation, repeated startup, and protected writes. Existing history trust, input-budget, backup and retention checks remain enabled.

`--restore-recovery-smoke <output>` uses isolated WPF fixtures to check the two messages in three languages/two themes, safe keyboard defaults, focus, accessible names and layout. It never creates MainWindow or starts devices/hooks. The published pilot supports `--restore-recovery-demo recover|blocked <output>` for explicitly marked fictitious demonstrations; the worker/demonstration modes require pilot metadata. The demonstrations finish with coherent fictitious data and leave a result file. They do not reproduce faults in the user's working profile.
