# Clipboard privacy

Automatic expansion uses keyboard input for short single-line plain text. Long,
multiline and rich text temporarily use the clipboard. Those payloads include
`ExcludeClipboardContentFromMonitorProcessing` and raw little-endian DWORD zero
values for `CanIncludeInClipboardHistory` and `CanUploadToCloudClipboard`. WPF
publishes these registered formats as byte streams, not boxed .NET values.
These are the [official Windows clipboard privacy formats](https://learn.microsoft.com/en-us/windows/win32/dataxchg/clipboard-formats#cloud-clipboard-and-clipboard-history-formats).

CF_UNICODETEXT, ANSI and OEM fallback use Windows CRLF line endings (including
plain multiline snippets); existing CRLF is not doubled. CF_HTML offsets and
embedded local images keep the existing renderer. A dedicated message-only window publishes immediate native
data. Native handles are prepared before emptying the clipboard; Windows owns
only handles successfully passed to `SetClipboardData`. Saved handles are
separate copies and are released after cleanup.

OEM text and the input locale are included explicitly. Windows otherwise adds
these formats on close, advancing the sequence after the guarded write; that
must not be mistaken for a concurrent user copy. The isolated native test
checks empty restoration after closing and reading the real clipboard.

The previous clipboard is captured before deleting the trigger. An empty
clipboard is a valid snapshot, including an initial sequence number of zero.
Each publish/restore compares the sequence number, and after publication also
the owner window, **inside the same OpenClipboard lock as the write**. Other
applications cannot change it between comparison and restoration. Every busy
retry checks again. A new user copy interrupts subsequent clipboard segments
and survives cleanup. Expansion cancellation does not cancel restoration.
See [OpenClipboard](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-openclipboard)
and [GetClipboardSequenceNumber](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-getclipboardsequencenumber).

Snapshot preparation is all-or-nothing: unavailable/unsupported native data,
more than 256 formats, an individual native item above 64 MiB or a combined
snapshot above 128 MiB refuses clipboard expansion before deleting the trigger.
This does not truncate or clear the user's original clipboard. Ordinary text,
HTML/RTF, bitmaps/DIB, file lists and copyable registered byte formats retain
their representations. Owner-managed private formats (0x200–0x3ff), owner-display or unrenderable formats may be
refused. Snapshots are temporary process memory, not app files or logs.

Snapshots duplicate Win32 handles while the clipboard is locked, including
externally supplied Unicode/ANSI/OEM text, locale and opaque registered bytes.
They do not convert the previous clipboard into WPF objects or deserialize
arbitrary editor-specific formats. Opaque bytes and NUL markers are retained.
Discarded zero-byte allocation handles cannot be published by Windows and are
refused before clipboard mutation.
OLE's `DataObject` / `Ole Private Data` owner bookkeeping is omitted; it is
not portable data and is recreated by the next OLE owner. An unrenderable WPF
`System.Drawing.Bitmap` alias is omitted only when concrete CF_BITMAP is present;
the concrete bitmap itself must be successfully copied. The native test uses
both OLE publishers and direct Win32 publishers, then pastes long, multiline
and rich payloads into a real private multiline EDIT control. The 12 combinations
also restore native Unicode/ANSI text, empty clipboard and previous bitmap.

Modern Notepad can additionally advertise `EnterpriseDataProtectionId` while
`GetClipboardData` returns NULL with error zero (observed in the user's Windows
build 26300). This exact unavailable optional hint is omitted. A non-null value
is preserved byte-for-byte; explicit access failures and unknown unavailable
formats still refuse expansion before the trigger is deleted. No enterprise
identity is fabricated, and no Windows policy is modified. Additional native
tests exercise long/multiline/rich paste and restoration with the unavailable
hint and with a populated synthetic identity (18 total editor combinations).

Busy access has eight bounded retries. If cleanup cannot regain access, a
completed expansion reports that the text was inserted but the clipboard could
not be restored; diagnostic output records only the event, not clipboard data.
This is not an unconditional recovery guarantee against a permanently busy or
hostile clipboard owner. Broader native-call/resource concerns remain in #89.

**Intentional copies keep existing behavior.** Copying a capture, OCR result or
code block (including capture's chosen copy rule) follows the user's Windows
history/synchronization settings; this change does not silently exclude them.
Settings and the local help for Shortcuts/Capture/Settings explain this policy.

The flags do not erase old history entries, provide encryption or prevent other
apps from observing clipboard events/content. Windows synchronization is
separate from app telemetry. No cloud synchronization or absolute secrecy is
claimed to have been physically verified by automated tests.

Tests serialize real WPF payloads to native storage and inspect the raw DWORDs,
Unicode, CF_HTML and image bytes without writing to the interactive clipboard.
A fake transport exercises empty/prior content, partial writes, cancellation,
ownership changes and bounded retries. A separate child can test the real
backend on a private noninteractive Windows station. If Windows denies creation
or selection of that station before testing begins (error 5), it reports
**UNAVAILABLE**, not PASS, and never falls back to the user's clipboard. Backend
failures after isolation are failures. Controlled pilot validation of Win+V,
target applications and actual clipboard restoration remains required.
