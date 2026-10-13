# External file processing limits (#105)

These limits apply to imported files and backups selected for restoration. Rejection/cancellation occurs before replacing active data. The ordinary local snippet repository remains readable with its existing behavior; this is not a destructive migration or a retroactive per-snippet quota for installed data.

| Input | Budget |
|---|---|
| Backup ZIP on disk | 2 GiB + 64 MiB for container overhead |
| ZIP central directory | 16 MiB; at most 10,001 records including the manifest/directories |
| Manifest | 8 MiB, declared and actual bytes; JSON default maximum depth 64 |
| Archive paths | 32,767 characters, including manifest references |
| Expanded backup files | 256 MiB per asset, 64 MiB per root data file, 2 GiB total including manifest |
| External Markdown/JSON/YAML import | 64 MiB actual encoded bytes |
| Imported shortcuts | 10,000 source items; 1,048,576 content characters per item |
| Import metadata | 16,384 characters per name/category/trigger; Markdown metadata record also 16,384 characters |
| Markdown/YAML lines | 200,000, including CR-only input; checked before splitting |
| Editor image file | 64 MiB encoded bytes; PNG, JPEG or BMP |
| Image dimensions | At most 32,000 px per side and 64,000,000 pixels |
| Image decoding | Two background decoder slots shared with history thumbnails |

The ZIP footer and actual central record structure are checked before `ZipArchive` materializes entries. ZIP64 footer metadata is supported within the same budgets; multi-volume ZIPs and inconsistent directories are rejected. Local/central declared sizes are compared when available (ordinary headers without data descriptors). Declared entry lengths/counts/aggregate expansion are checked before manifest parsing or hashes. Bounded streams also enforce actual produced bytes. Restore validation and staging share one read-only archive handle, preventing replacement by an ordinary concurrent writer between these stages.

Limits for manifests and payloads also apply when creating a backup, so the app does not publish a new backup that its restore policy refuses. A source growing beyond its initial read budget aborts creation and removes the temporary archive. Existing backups are retained on rejection. No arbitrary compression-ratio threshold rejects otherwise valid highly compressible content; the absolute expanded-byte budgets and cancellation bound the work.

Import parsing/conversion runs outside the dispatcher. Counts include duplicate/invalid Text Blaze source items, and YAML inline lists are checked before allocating all their strings. Generated conversion/YAML regexes have a one-second timeout. Raw and converted content are checked. The external Markdown duplicate check uses the existing trigger normalization with a set to avoid quadratic import scans.

Images are checked through small managed headers before native pixel decoding. JPEG header scanning is limited to 1 MiB. Width/height are checked again after decoding. GIF continues to be treated as recording media, not an editable still. TIFF/other formats selected through All files are refused by the editor; the explicitly listed PNG/JPEG/BMP formats are supported. Thumbnails request bounded dimensions without upscaling and decode outside the dispatcher. Header limits are resource guards, not a sandbox or a complete codec validation guarantee. Two simultaneous 64 MP decodes can still require substantial memory; editor history/resource tuning remains part of #89.

Cancelable operations show a separate themed progress window that remains interactive while owner controls are disabled. Closing it requests cancellation and keeps it visible until safe completion. Import/restore preserve their confirmation/backup/atomic-write or rollback flows. Cancellation is checked during reads/hashes/extraction and between restore changes; rollback does not use the canceled token. Once the commit has succeeded, cancellation is disabled while the app refreshes its state. Cancellation of a native image decoder is cooperative before/after its bounded call, not unsafe thread termination. An image load also rechecks its session/record and document revision so an older background result cannot replace newer work.

Validation uses small fixtures and crafted ZIP metadata, not large decompression bombs. The suite covers exact boundaries, oversized declarations, actual read growth, malformed small inputs, rollback after partial apply, prior backup preservation, ordinary larger inputs, existing local content compatibility and bounded image headers. WPF checks cover cancel in three languages/two themes, disabled owner controls, safe close/commit behavior, editor preservation and dispatcher responsiveness. They also exercise the actual asynchronous image loader with the pilot's oversized header, unsupported and truncated files, both in an empty editor and after opening a blue image. Expected invalid-data exceptions produce a localized rejection without closing the app or replacing the current pixels, path or details; invalid history thumbnails retain their fallback.
