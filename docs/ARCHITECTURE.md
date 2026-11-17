# Architecture and scanning behavior

## Components

- `DriveSpaceAnalyzer.Core`: drive discovery, metadata-only filesystem adapter, iterative two-phase scanner, checked 64-bit aggregation, progress estimation, immutable-after-publication snapshot and indexed result queries. No WPF reference.
- `DriveSpaceAnalyzer.App`: WPF views, commands and view models. The main view model owns scan cancellation and dispatcher-bound progress. Each result window owns its own collection views, searches, sort state and filter timer.
- `DriveSpaceAnalyzer.Tests`: aggregation, thresholds, filtering, numeric ordering, errors, partial results, links, cancellation, deep trees, 64-bit overflow, progress/ETA and physical read-only fixtures.
- `DriveSpaceAnalyzer.Wpf.Tests`: Windows STA integration test using real controls and windows, including actual double-click event routing.
- `installer/`, `scripts/`, `.github/workflows/`: self-contained publishing, per-user installer and Windows release validation.

## Single discovery, single analysis

Phase 1 enumerates directory names, excludes reparse points and assigns stable integer IDs. Every parent precedes its children. Each node stores its path, parent ID and immediate child IDs. Discovery is iterative; directory depth does not consume the call stack.

Phase 2 enumerates files once per discovered directory, reads size and modification metadata and retains a record only when logical length is strictly greater than the scan threshold. Hidden/system entries are included. Each node initially receives only its directly contained byte/file totals.

Reverse identifier order then adds each child's totals into its parent once. Parent totals include every accessible descendant, and descendant counts exclude the node itself. The root is retained for summary statistics but excluded from ranked results. No ancestor calculation rescans its subtree. The mandatory two phases enumerate directory names and file names separately; there is no separate per-folder recursive sizing pass.

Snapshots publish after the worker has stopped modifying the index. Main results contain all non-root folders. Drill-down left panels look up only immediate child IDs; right panels walk the selected subtree's in-memory child lists and qualifying file lists. They never enumerate the filesystem. Creating arrays and preparing large result sets happens on a worker before collection views are installed on the dispatcher. A scan's threshold and outcome remain attached to all windows, even when the main window starts a different scan.

## Progress and completion

Worker reports are throttled to at most one per 200 milliseconds, except phase boundaries and the final report. `Progress<T>` captures WPF's synchronization context. A dispatcher timer maintains elapsed time even while no worker progress is arriving. Conflicting settings and drive controls are disabled while scanning.

Discovery has no known total: the progress bar is indeterminate and the estimate reads “Calculating estimate…”. File analysis measures processed directories divided by discovered directories. This represents directory processing, not bytes, I/O or a guarantee about time remaining. ETA uses an exponentially smoothed rate (weight 0.25) after at least three samples, three directories and two seconds. Excessive sample variability suppresses the number. Aggregation is indeterminate. Only an unwarned successful final outcome displays 100%; warned, failed and canceled outcomes retain an incomplete label and remain below 100%.

Cancellation is cooperative at directory and entry boundaries in both phases. Already collected data is still aggregated entirely in memory. No partly scanned directory is represented as complete. Normal blocking Windows metadata calls cannot be interrupted safely by a cancellation token.

## Error and resource policy

Each enumeration and metadata read handles access, I/O, disappearance, path and security errors. A failed child does not stop its siblings. Incompleteness propagates to ancestors. An unavailable root is a failed scan; inaccessible descendants or skipped reparse content yield warnings. Counts represent skipped/error events, so a directory failing in both phases can contribute two events. Diagnostic strings are capped at 200, while the total count is retained.

Byte/count arithmetic is checked. Overflow produces a failed, partial snapshot rather than negative or wrapped totals. Filesystem enumeration is lazy and all iterator resources are disposed by `foreach`, including on cancellation. The scanner uses one worker and no concurrent disk enumeration. Virtualized grids do not materialize a visual row for every result.

All paths and sizes describe a changing live filesystem. Reparse checks reduce traversal risk but do not provide an atomic guarantee against a concurrently replaced directory. Hidden/inaccessible data is never estimated or silently inserted into totals. File lengths are logical; hard links can count the same physical allocation more than once, and sparse/compressed files can differ substantially from allocated space.
