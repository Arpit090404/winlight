# Progress

## 2026-11-06 — Resident app scaffold
- Created `WinLight.sln` with Core, App, Tests, and Benchmarks projects on .NET 10; the App is unpackaged and self-contained.
- Added the pre-created hidden WinUI window, H.NotifyIcon tray icon, Alt+Space message-only hotkey window, single-instance mutex and named-pipe signal, and per-user login startup.
- `dotnet build -c Release`: 0 warnings, 0 errors. `dotnet test -c Release`: 1 passed.
- Interactive run: Alt+Space showed and then hid the native window; a second launch reopened the first instance; focus loss hid the window. No latency measurement yet.
- Next: app indexing, in-memory fuzzy search, launching, and performance measurements.

## 2026-11-06 — Overlay and app results
- Diagnosed the blank list: the scaffold had no index, query handling, ListView, or launch handler; the normal window also had no overlay presenter or foreground focus retry.
- Added a borderless topmost Acrylic overlay, cursor-monitor placement, DWM rounded corners, background AppsFolder/Start Menu indexing with debounced and stale-open refresh, fuzzy results, frecency, and keyboard/mouse launching.
- Gated diagnostics measured 234 indexed apps after startup and 8 bound empty-query results; `calc` narrowed to 5 and `notepad` to 1.
- Interactive checks: Alt+Space, typing, Up/Down, Enter launching Calculator and Notepad, result click, and focus-loss dismissal all passed; Release build has 0 warnings and 6 tests pass.
- Next: measure hotkey/search latency and memory, add shell icons, then designer styling.

## 2026-11-06 — Duplicate identities and empty query
- Logged 282 raw shell/shortcut entries before edits; examples included AppsFolder/shortcut pairs for Android Studio and Antigravity, casing variants of Steam, and three Ollama shortcuts resolving to one executable.
- Core now groups by AUMID or normalized target plus name and arguments, prefers AppsFolder, and retains distinct same-name targets; the interactive index contains 169 entries.
- Empty or whitespace query hides the list and leaves a 56 px bar; typing `calc` showed 3 results and a 200 px overlay, then clearing it returned to 56 px.
- Added `IsResultsVisible`, a view-only partial hook for the designer, and asynchronous shell icon view models; Release build has zero warnings and 12 tests pass.
- Next: designer implements search and row layout, themes, motion, and accessibility in Views/Styles/Assets, then verify light/dark appearance.

## 2026-11-06 — Windows 11 view pass
- Added a 56 px borderless search bar and 48 px result rows with 32 px shell icons, a theme divider, and an accent selection pill.
- The list uses a 120 ms visibility fade when system animations are enabled; no keystroke animation was added.
- Light and dark screenshots were checked; Android Studio and Ollama each appeared once, and clearing the query left only the bar with clean resizing.
- Release build finished with zero warnings and all 12 tests passed. High Contrast was not visually toggled during this pass.
- Next: measure hotkey/search latency and memory, and verify High Contrast and 200% text scaling.

## 2026-11-06 — Search text alignment
- Adjusted the search TextBox padding so the placeholder and caret sit at y267, near the 56 px bar center at y268 (previously y255), confirmed in before/after screenshots.
- Release build had zero warnings; all 12 tests passed, and the running app opened and dismissed with Esc.
- Next: verify High Contrast and 200% text scaling, then measure latency and memory.

## 2026-11-06 — Transparent shell icons
- Replaced `Image.FromHbitmap` with a 32-bit pixel copy that retains the shell bitmap's alpha channel before PNG encoding; result rows continue to show icons on the native list surface without an icon tile.
- Extracted the Antigravity shortcut icon through the new path: 256 transparent pixels and 4 partially transparent pixels in its 32×32 PNG.
- Release build completed with zero warnings and all 12 tests passed; the PNG extraction path was exercised against a real shortcut.
- A sandboxed launch exited during WinUI startup with COM error 0x800F1000; launching the rebuilt executable with normal desktop permissions left the resident process running and responding. Next: visually verify several classic and packaged icons, then measure latency and memory.

## 2026-11-06 — Standalone release
- Published the existing unpackaged, self-contained WinUI app into `publish/WinLight`; the folder contains the runtime and app dependencies and starts via `WinLight.App.exe` without a terminal.
- Fixed publish to copy `WinLight.App.pri`, which WinUI needs to resolve `LauncherResources.xaml`; the first publish omitted it and stalled during startup.
- Launched the published executable with normal desktop permissions: the resident process remained responsive, accepted the named-pipe show signal, and registered the published path for login startup.
- Release build has zero warnings and all 12 tests pass. Next: visually inspect icons and measure latency and idle memory.

## 2026-11-06 — Launcher window ordering
- Reasserted the native topmost Z order each time the search window appears, before foreground activation.
- Published and restarted the standalone app; a show signal brought its window to the foreground, and the window's native topmost flag was set.
- Release build completed with zero warnings and all 12 tests passed.
- Next: visually inspect icons and measure latency and idle memory.

## 2026-11-06 — Icon placeholder removal
- Removed the Fluent placeholder glyph layered underneath each shell icon in the result row; transparent icon pixels now show the launcher surface directly.
- Published and restarted the standalone app; its resident process responded to a show signal.
- Release build completed with zero warnings and all 12 tests passed.
- Next: visually inspect classic and packaged icon shapes, then measure latency and idle memory.

## 2026-11-06 — Performance baseline
- Added `WINLIGHT_PERF=1` instrumentation for hotkey frame, query update, startup/index refresh, and icon cold/cache timings; the default path does not log.
- Measured the published Release app on the normal desktop: hotkey p95 11.059 ms immediately and 14.653 ms after idle; first usable index 2.169 s.
- Hidden idle CPU was 0 over 60 s, but working set rose from 198.22 MB at 1 minute to 237.22 MB at 10 minutes; 5,000-item core search was about 0.12 ms in BenchmarkDotNet.
- Full results and coverage limits are in `docs/PERF.md`; Release build had zero warnings and all 12 tests passed. Next: optimizer read-only analysis before any tuning.

## 2026-11-07 — Snapshot startup and performance follow-up
- Split the published scan: AppsFolder 615 ms, Start Menu 1,130 ms including 1,108 ms of shortcut target resolution; retained the single STA COM scan and measured deduplication without changing it.
- Added versioned, atomically written `index.json`; warm startup published 168 apps in 545 ms, then replaced them after a background scan. Missing/corrupt/old snapshots fall back to scanning, and cached missing targets produce the inline launch error.
- Attributed 1,000 no-query and 1,000 query cycles before memory changes: private bytes kept rising while icon cache/queue stayed at 0 or 3/0 and live managed bytes remained small; no cache, COM, GC, or publish tuning was applied.
- The published rerun had hotkey p95 27.350 ms immediately and 27.249 ms after idle, 30/30 typed queries, zero idle CPU, but 209.43 MB idle working set. Full before/after data and coverage limits are in `docs/PERF.md`.
- Release build has zero warnings and all 17 tests pass. Next: isolate native memory growth and measure 5,000-index full UI rendering before further tuning.

## 2026-11-08 — Reliability and Settings handoff
- Restarting Explorer recreated the tray icon while WinLight stayed resident; user-assisted lock/unlock and sleep/wake each recovered Alt+Space, while fast user switching and mixed-DPI placement remain untested (one display available).
- A controlled hotkey conflict opened Settings and notified through the tray; changing the binding took effect immediately, rollback preserved the old binding on failure, and the login toggle and Rescan command were exercised.
- A simulated fullscreen foreground window exposed a premature focus-loss hide; the activation gate fixed it. The user also confirmed the overlay appeared and accepted typing over a UAC-launched Notepad.
- The tray Settings entry opened the themed 440×340 settings window; Tab and Shift+Tab now retain normal keyboard navigation. The designer reported a zero-warning Release build; tests were intentionally not rerun after the view pass.
- Next: verify fast user switching and mixed-DPI placement when available, then revisit the unresolved native-memory growth recorded in `docs/PERF.md`.

## 2026-11-08 — Tray Settings command fix
- Reproduced the tray Settings no-op: the published app logged no XAML Click event. H.NotifyIcon's default native popup invokes each MenuFlyoutItem Command, so the three Click handlers were never called.
- Wired Settings, Rescan apps, and Exit through RelayCommand, with Settings and Exit queued onto the UI dispatcher. The user clicked Settings in the republished app and confirmed it opened in front with focus; gated logs showed the command on the UI thread and `settings_show_end visible=True`.
- Release build completed with zero warnings. Tests were not rerun for this narrow UI event fix; Rescan and Exit command wiring was reviewed in code, while their tray routes remain to be hand-checked.
