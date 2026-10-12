# Capture history file access

History metadata does not authorize access to arbitrary files. All history consumers use `CaptureService.ResolveFilePath`: automatic previews, the editor, open/copy actions, deletion and retention. Invalid or unauthorized references resolve to an empty path. Metadata-only removal remains available.

## Trust and compatibility

- A completed local capture/recording or a newly saved edit is registered as local. Custom local output directories and relative portable paths remain supported.
- Records without the additive `requiresFileApproval` property default to requiring confirmation. This includes older histories whose origin cannot be determined.
- Restoring either supported backup schema forces this property to `true` in staging, after archive integrity validation and before transactional application. An archive cannot import a trusted value. Existing unknown JSON fields remain preserved.
- An explicit history action offers a themed confirmation with the resolved path. Cancel is the keyboard default. Confirmation is limited to the current service session and the record/path/size/write-time fingerprint; reload or file changes require confirmation again. The persisted flag is never cleared by approval.
- A restore detaches the editor's previous destination while retaining edited pixels, so subsequent saving requires a destination rather than silently writing the pre-restore path.
- Automatic retention removes metadata but never deletes files of records requiring confirmation, including ones approved for viewing during the session.

Older application versions ignore the new property and do not implement this protection. Do not use an older executable to evaluate this security fix.

## File policy

Only local, fully qualified drive paths to PNG, JPEG, GIF or MP4 are eligible. The filename extension, declared media kind and file header must agree. The header probe is bounded; it is a file-type guard, not a full media decoder or malware scanner. Native decoder hardening is tracked separately.

UNC/network and device paths, alternate streams, reserved device names, invalid portable traversal and reparse points (including ancestors) are rejected. An invalid relative reference never falls back to a different absolute target. The ancestor check runs from root to file before reading its header. For redirected/network destinations, use an ordinary local copy to access it through history; this deliberately includes cloud placeholders identified as reparse points.

Each action revalidates the path and header. A process already able to modify files under the same user account can still race filesystem operations or alter local configuration; this policy is not an OS sandbox or protection against a compromised account. Session fingerprints detect ordinary file changes and are not cryptographic provenance. Windows media associations and media decoder behavior remain operating-system/dependency responsibilities.

## Validation

`CaptureHistoryTrustChecks` runs in the normal smoke suite with isolated synthetic files. It covers both backup schemas, forced confirmation, legacy defaults, path/type rejection, portable relocation, custom destinations, session approval invalidation, metadata-only removal, safe retention, authorized edits/deletion, duplicate IDs and transactional rollback. It never executes a media payload, uses the real clipboard or records a device.

Additional local WPF validation for #104 exercised the actual confirmation method in Portuguese, English and Spanish with light/dark themes and both choices. A real junction fixture was rejected. Screenshots and detailed evidence remain in the private audit workspace.

Manual pilot check: restore a backup of fictitious captures; verify there are no automatic previews before confirmation, Cancel does not open/copy/edit/delete a file, Continue authorizes the displayed local media for the session, and removing only the history entry preserves the file. Reopen the pilot to confirm session approval was not persisted. New captures should remain available immediately. Do not use real sensitive data for this check.
