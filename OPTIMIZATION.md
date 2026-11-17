# Performance notes

The private 3.3 development build (before public 0.1.0 versioning) compared the same built-in scenario on one Windows machine, using .NET 10.0.15 and eight disposable fixture applications. User windows and data were excluded. These are measurements from that run, not a performance guarantee for other hardware or target applications.

| Scenario | 3.2 baseline | Optimized |
| --- | ---: | ---: |
| 300 agent status events, total handler time | 3,912 ms | 2.05 ms |
| File writes in those event handlers | 600 | 0 |
| Grid recalculations in those event handlers | 300 | 0 |
| Allocations during 300 background polls | 8,527 KiB | 52 KiB |
| Time for those polls | 5.13 ms | 0.44 ms |
| 24 tab switches, total | 657 ms | 393 ms |
| File writes in switch handlers | 46 | 0 |
| Capture eight windows | 344 ms | 275 ms |
| Geometry settling after maximize/restore, mean of 20 cycles | 114 ms | 85 ms |
| Same resize cycles, p95 | 168 ms | 111 ms |
| Container private memory at end of scenario | 76.4 MiB | 42.1 MiB |

## Implementation

Status events update only the affected panel and tab indicators. They do not persist transient status or invalidate the entire layout. Four named-pipe listeners accept concurrent notifications.

Autosaves coalesce for 250 ms. The UI takes immutable snapshots; file I/O runs in the background. Ordered snapshots and a shared write gate prevent a late background write from replacing a newer explicit save. Atomic replacement, durable group backups, and recovery journaling before native attachment are retained.

Native placement uses final rectangles in a batch rather than first moving to the previous size. Ordinary host movement updates grouped windows in the Windows move loop. Divider updates coalesce to a frame, selection changes only decoration, and geometry/mask verification runs periodically. The panel-card cache is bounded at 96 entries.

Window titles update through native events. Automatic discovery is cached for 2.5 seconds and invalidated by window creation/closure; a manual picker requests a fresh list. Unresponsive target windows are skipped on ordinary synchronization and modal-input paths, then reconciled after recovery.

Background decoding runs in a pool of at most two workers. Superseded loads are cancelled. Static images are scaled to a bounded useful display resolution. GIF frames reuse the previous frame and copy directly into the bitmap buffer. Theme changes do not reread the file; minimizing pauses animation. In the test image case, persistent pixel buffers fell from about 96 MiB to 14 MiB at a 1920 × 960 target size; driver/GPU resources are not included.

## Reproduce and validate

Run `WindowDock.exe --perf-test ./artifacts/performance.json` from a Windows checkout or development distribution. The scenario uses a separate profile and its own windows. Compare the same machine, display scaling, runtime, build configuration, and scenario. Keep raw measurements when evaluating a change.

`./build.ps1 -Test` checks schema compatibility, ordered persistence, recovery, native placement, focus-preserving scroll paths, bounded IPC, cancellation and resource lifetime, and an intentionally unresponsive fixture. Reports are written to `artifacts/modern-core-tests.txt` and `artifacts/avalonia-tests.txt`.

Third-party applications paint their own contents. Container optimizations reduce redundant work in WindowDock; they do not accelerate another application's rendering engine.