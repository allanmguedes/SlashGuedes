# Local diagnostics and temporary data

Operational logs contain fixed application event names and centrally permitted
numeric, boolean, correlation-ID and enumerated fields. Exception messages,
document text, clipboard contents, pixels, paths, filenames, window titles,
device identifiers and arbitrary objects/strings are omitted. Exception type
and HRESULT remain available. Adding a diagnostic event/field requires explicit
review of `DiagnosticPolicy`; unknown values are omitted.

Additional managed diagnostics are **off by default**. Enable or disable them
in **Settings → Local data → Additional diagnostics**. The existing
`SLASHDESK_DIAGNOSTICS=1` launch setting also enables them; the control indicates
that the preference was set at launch. Both log channels use the same policy.
ScreenRecorderLib raw file logging is disabled, including when additional
diagnostics are enabled. Old recognized native log files are removed when they
are safely available for maintenance.

## Log budgets

- UTC daily files; at most **1 MiB per file**, with one previous chunk (`.1`).
- At most **10 MiB total** for recognized operational/functional logs.
- Files last written more than **7 days** ago are removed; oldest files are
  removed earlier when needed for the total budget.
- Existing managed logs are subject to retention too. Older records are not
  retroactively sanitized; the central field policy applies to new records.
- Files outside the recognized log naming patterns are untouched. Reparse
  points/hard links are refused, parent directories are pinned, and a maintenance
  lease prevents concurrent processes from rotating each other's files.
- An inaccessible/busy log, maintenance lock or oversized directory inventory
  causes a write to be skipped. Diagnostic failures never interrupt app work.
  Existing inaccessible files may exceed the policy until access is restored;
  no new entries are added when their size cannot be safely accounted for.

## OCR and GIF session data

New sessions live under `DataDirectory/Temporary-v1`, separate from saved
captures and reusable OCR models. Each session has its kind, random ID, data-root
identity, creation time, owner PID and process start identity in a bounded
ownership record. The application holds a file lease; an OCR child holds a
shared lease too. GIF previews retain their lease until the result is disposed.

Normal success, cancellation and failure clean only the session's recognized
flat files. On app startup, after acquiring the single-instance mutex, a
background sweep removes sessions only when ownership matches, the session is
at least **2 minutes old**, the original process is provably gone, and the lease
can be acquired exclusively. PID reuse is distinguished by start identity.
An inaccessible owner is treated conservatively as still live.

The sweep scans at most 256 sessions per run and checks its 5-second time budget
between sessions; each session has a bounded inventory. Unknown files,
subdirectories, malformed records, links, inaccessible files and unexpected
ownership veto deletion. No recursive directory deletion is used. Locked or
permission-denied residues can be retried on a later startup. A startup before
the two-minute grace period also leaves the session for a later startup.

Legacy unmarked `%TEMP%` OCR/GIF directories are **not automatically deleted**:
they do not establish ownership or liveness under this protocol. Reusable public
OCR models, user backups, saved captures and documents are outside the sweep.

## Interrupted MP4

MP4 working files (`.<name>.<id>.recording.mp4`) remain in the chosen recording
folder. They are never cleaned automatically. They may be incomplete and are not
guaranteed playable. No recovery/export algorithm is introduced here.

Separate bounded private metadata in `RecordingRecovery-v1` contains the partial
path and process/session identity. It is not a diagnostic log and is excluded
from the existing explicit backup source list. Settings displays a notice for a
nonempty partial from an interrupted or terminated owner. Successful completion
removes only its own marker, and proven missing partials of dead owners remove
only stale metadata. The app never scans, modifies or cleans the output folder.
Untrusted/unreadable paths and busy partials are omitted from the notice.

## Validation

The general smoke suite covers filtering, opt-in/out, rotation, aggregate/age
budgets, busy/read-only logs, controlled subprocess termination, live parent and
worker leases, PID reuse, permission-denied cleanup, unmarked/unknown contents,
junctions, partial notices and exclusions. All inputs are synthetic.

`SlashDesk.exe --retention-smoke <evidence-directory>` runs a synthetic real OCR
worker, cancellation and Settings controls in Portuguese, English and Spanish,
both themes, compact/wide windows and 100/150% rendered scaling. It does not
read/write the clipboard or capture the user's screen. Screenshots are rendered
fixtures; physical monitor DPI and real abnormal-driver MP4 recovery remain
separate validations.

Related: #108, #86; transaction/concurrency analyses #87 and #89.
