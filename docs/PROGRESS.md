# Progress

## 2026-11-10 — Integrated launcher Settings
- Hosted the existing hotkey, login startup, and app rescan controls inside the pre-created launcher below its search bar; the tray and hotkey-conflict routes now enter that view.
- Added gear and `settings` query entry, Search return and Escape behavior, a General/Apps sidebar, and query/result preservation across view switches.
- Kept the single settings model and atomic store, existing launcher backdrop/window configuration, and normal search keyboard handling. A focused navigation-state integration test was added.
- Final Release build succeeded with zero warnings; the one focused xUnit navigation test passed (1/1). No full suite or performance benchmark was run, as requested.
- Published desktop flow verified gear and query entry, General/Apps navigation, login-toggle persistence and restoration, Back/Escape query and result restoration, new-query return to Search, and root Escape dismissal. UI Automation found one WinLight top-level window while Settings was open.
- Dark and light desktop captures showed themed controls, the native Acrylic surface, and rounded launcher corners. The focused xUnit test covers navigation state; the WinUI interaction, theme, and backdrop checks were manual. Search Up/Down/Enter could not be independently driven by the desktop automation because another foreground app reclaimed keyboard focus.

## 2026-11-10 — Direct Pomodoro controls and auto start
- Replaced text actions with 36 px Fluent symbol buttons for start/pause, reset, and skip. Direct Focus, Short break, and Long break buttons sit beside them; three duration fields and an Auto start checkbox form the bottom settings row. The session and length dropdowns are gone.
- Auto start is off by default and persists in `settings.json`. When enabled, a completed focus or break notifies once, then starts the recommended session from the time of completion or recovery; it does not backfill multiple sessions after downtime.
- Published Release UI showed all controls and their accessible names in the embedded Focus section. Direct session buttons selected 5, 15, and 25 minute defaults. The checkbox persisted; recovery checks moved an overdue focus into a running short break and an overdue break into a running focus session. Test settings and session state were restored afterward.
- Release build: 0 warnings, 0 errors; all 171 tests pass. Quick performance measurement remains outstanding because Antigravity scripter quota is unavailable; physical sleep/resume and drag checks remain manual.

## 2026-11-10 — Embedded Pomodoro launcher controls
- Moved full timer controls into a pre-created Focus section above ordinary search results. Three-character prefixes of `pomodoro` and `timer` reveal it without Enter; the separate full timer window was removed. Keyboard Enter activates the selected timer action, Up/Down can move between Focus and ordinary results, and Escape now dismisses from timer buttons as well as search input.
- Refitted the panel to 260 px within the 640 px launcher, kept duration settings scrollable, and reduced the only popup to a 164×80 floating timer with transparent resting buttons, rounded native corners, and a drag region. The launcher repositions to keep the expanded panel on screen.
- Published Release checks showed `tim` and `pom` opening Focus as the top section with one launcher window; Enter on `tim` started Focus and showed only the floating timer. Clicking it reopened embedded controls, and Esc left the floating timer running. Reset returned the session to Ready and hid the float.
- Release build has zero warnings; all 171 tests pass, including prefix-trigger cases. Antigravity scripter remains unavailable due provider quota, so Quick performance budgets remain unmeasured for this change; physical drag and sleep/resume still need manual checks.

## 2026-11-10 — Pomodoro focus timer
- Added a Core deadline-based timer with focus, short-break and long-break progression, configurable durations, restart recovery, and deterministic state tests. Exact `pomodoro` and `timer` launcher commands open separate controls; active sessions use a dismissible floating window and the existing native tray notification.
- Published Release checks exercised launcher access, focus start, pause/resume, launcher dismissal, floating reopen/dismiss, running-session restart recovery, and overdue completion with the next-break label. The floating view now announces completion and the recommended next session.
- Release build: 0 warnings, 0 errors; all 163 tests pass, including 10 timer tests. No new packages were added.
- Antigravity's read-only optimizer review confirmed deadline-based accuracy and hidden-window timer behavior. Its static-root and focus concerns do not imply repeated growth in the current one-window lifecycle; published keyboard handoff succeeded. Resume re-arming remains intentional to recalculate against the persisted UTC deadline after sleep. The scripter was blocked by provider quota, so `scripts/verify.ps1` and `scripts/perf.ps1 -Quick` have not run; CPU/memory impact is unmeasured. Physical sleep/resume, drag behavior, Narrator, and the visible tray notification still need desktop checks.

## 2026-11-10 — Native launcher backdrop restoration
- The launcher already requested Desktop Acrylic, but assigned it before changing the native window frame; the published bar rendered as a uniform dark fill. The designer found no opaque XAML layer, and Windows transparency effects were enabled.
- Moved the backdrop assignment after native style and DWM setup, then reconnected the same backdrop object after each show because hiding and reopening otherwise returned to the solid fallback.
- Published Release build succeeded with zero warnings. Desktop captures showed the Acrylic surface on first open and after Esc/reopen; over the wallpaper it acquired a green tint instead of the old flat gray. Light and dark themes stayed readable, and the original dark setting was restored.
- Window position, topmost presentation, rounded corners, and foreground focus were observed unchanged. Tests and performance scripts were not run for this focused visual fix.

## 2026-11-10 — Grouped search-result presentation
- Kept the single selectable result list and its existing keyboard and launch actions; presentation now places app results above file and folder results while preserving order within each category.
- Added first-result header metadata for the Apps and Files & Folders sections, with calculator results left headerless and in their existing order relative to apps.
- The list and overlay height now include only visible section headers, within the existing 384 px result viewport. Per request, tests and verification/performance scripts were not run; next is a visual and keyboard check on the desktop.

## 2026-11-10 — Downloads and Documents filename search (performance follow-up pending)
- Added a compiled-in file provider, shared-parent in-memory index, background watcher updates and reconciliation, file/folder shell actions, and a file row with icon and parent path; no new packages or query-time I/O.
- Added isolated tests for matching, ranking, duplicates, nested trees, missing roots, watcher create/rename/move/delete, links, cancellation, and app/calculator priority. Release build has zero warnings and 153 tests pass; published desktop checks opened a Downloads text file and folder successfully.
- BenchmarkDotNet measured 5,000-entry prefix search at 0.207 ms and 250,000-entry no-match at 1.427 ms before the final early-exit change. The machine's Downloads and Documents contain about 247,000 entries.
- Final interactive Quick: snapshot hotkey p95 52.122 ms (<100), query max 20.497 ms (>16), snapshot readiness 1041.974 ms (>1000), idle CPU 0 (<0.1); 1/60 Notepad calibration misses. Private bytes after idle were 186.85 MB (informational in Quick). These results exceed the latest query/startup baseline; feature is not signed off.
- A final worker-retry change passed `scripts/verify.ps1` again (153 tests, zero warnings); the subsequent Quick attempt exited before metric collection, so the preceding calibrated Quick remains the latest usable performance result. Next: isolate the first-query and snapshot-startup regressions under a controlled desktop load, measure the full 50-to-100-cycle private-byte gates, and rerun Quick before release.

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

## 2026-11-08 — Verification harness
- Added `scripts/verify.ps1` for restore, zero-warning Release build, and the complete test suite; numeric performance budgets now live in `scripts/budgets.json`.
- Added a published-app desktop performance harness and `scripts/perf.ps1` with Quick and Full modes, JSON results, raw logs, and a concise summary under `artifacts/perf/`.
- Verification passed: Release build had zero warnings and all 23 tests passed; the console harness also built with zero warnings.
- Three corrected Quick runs and one Full run completed; Notepad calibration misses were 1/60, 0/60, 0/60, and 0/60, with app-side injection misses at zero. Each summary includes matching pre/post snapshot and HKCU Run checks and ends with the manual-restart instruction.
- Snapshot startup passed in all runs (472–634 ms); cold startup is now informational. Hotkey p95 remains below the 100 ms gate but is slower than the original 11.1 / 14.7 ms baseline. Query timing covered 163 apps; the 5,000-item UI budget and 24-hour plateau remain unverified. Working set is informational.
- All raw logs and results remain in timestamped `artifacts/perf/` directories; the latest Full report is `artifacts/perf/summary.txt`. The previously resident app remains stopped as directed.

## 2026-11-08 — Search provider refactor

- Captured 47 current top-eight search outputs over a deterministic 320-entry synthetic index and fixed frecency state; the golden test passed before production code changed.
- Added a compiled-in AppsProvider, weighted SearchEngine, closed LaunchApp action, and action executor. AppSearch still performs the same matching and frecency calculation; Views and Styles were untouched.
- Added engine tests for merge, weights, stable ties, provider errors, cancellation, and stale results. Release build and tests passed locally with zero warnings; final verification and Quick measurements are next.

## 2026-11-08 — Provider refactor verification

- The unchanged 47-query golden test passes after the refactor; Release build has zero warnings and all 30 tests pass.
- Two final-source Quick runs measured snapshot hotkey p95 at 33.448/35.746 ms versus the Full reference 25.002 ms, and snapshot query p50 at 0.315/0.317 ms versus 0.327 ms.
- Cold query p50 was 0.421/0.386 ms versus Full 0.303 ms. Each Quick run had 18 samples versus 97 in Full; cold and snapshot use the same query path and indexed 163 apps, so this remains an informational short-run variance caveat.

## 2026-11-08 — Inline calculator

- Added a compiled-in calculator provider with handwritten precedence parsing, invariant result formatting, percentage handling, functions/constants, and early exits for app-name queries.
- Added CopyText execution with clipboard retry and inline failure handling; calculator rows show a Fluent glyph and expression subtitle in the existing 48 px result template.
- Added 106 table cases under Turkish and French cultures plus subtitle, length, depth, cancellation, integration, and allocation checks; the original 47-query app golden test remains unchanged. Final verification and Quick performance measurement are next.
- Two Quick attempts exposed an existing startup failure when Windows denied `GetCursorPos` before index publication. Overlay placement now logs that failure and falls back to the primary display; normal cursor-monitor placement is unchanged. Release build has zero warnings and all 142 tests pass; rerun Quick to verify startup.

## 2026-11-08 — Calculator hot-path follow-up

- A valid Quick run had 0/60 harness calibration misses but slower hotkey, query, and startup timings than the Full reference; optimizer review found unnecessary merge allocation for app-only queries and empty-query engine work during show.
- SearchEngine now returns a sole sorted weight-1 provider result directly after checking all providers, and empty queries cancel any in-flight search without allocating a new query; weighted merges and stale-result handling remain intact.
- Added regression tests for both paths. Release build has zero warnings and all 144 tests pass; the unchanged 47-query app golden test remains green. Final Quick remeasurement is next.

## 2026-11-08 — Result-template hot-path follow-up

- A calibrated Quick after engine shortcuts still had slow repeated app-row updates; optimizer review isolated the calculator glyph/subtitle controls added to every app row.
- Split the result templates once at startup: app rows use the original image and single-title tree, while calculator rows use the Fluent glyph and subtitle. Template selection adds no per-query construction beyond the selected rows.
- Release build remains warning-free and all 144 tests pass. The app golden test is unchanged; a published Quick rerun will measure the template effect separately from system load.

## 2026-11-08 — Calculator desktop publish check

- Stopped the existing workspace WinLight process and republished the current working-tree source at HEAD to `publish/WinLight` in Release.
- Confirmed the published `WinLight.App.pri` matches the build output by SHA-256; the published app started and remained resident in desktop session 1.
- Alt+Space opened the bar. UI Automation read `12*8 = 96` as the first result and `pi = 3.14159265359` as the sole result for `=pi`; Esc dismissed the bar. No perf or broader tests were run by request.

## 2026-11-10 — Document file search filter

- File indexing now includes .docx, .pdf, .xls, .xlsx, .xlsm, .xlsb, .md, .txt, .ppt, and .pptx files; folders are still indexed regardless of name.
- Initial scans and watcher updates use the same extension filter, including case-insensitive rename handling.
- Added regression coverage for allowed files, skipped files, folders, and watcher renames. Release build succeeded with zero warnings; tests were not run by request.
- Quick performance: snapshot hotkey p95 23.049 ms, query max 11.597 ms, and snapshot readiness 499.007 ms passed; idle CPU 0.286% exceeded its 0.1% gate. Cold full-scan readiness was informational at 1936.722 ms.
