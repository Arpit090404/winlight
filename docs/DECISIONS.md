# WinLight reliability decisions

## Filename search

- FileSearchProvider is the third compiled-in provider, weighted below apps and calculator. It reuses the existing normalization and fuzzy matching categories, returns at most eight results, and reads an immutable in-memory array on queries. It searches names only, including subfolder names, and does no query-time filesystem work.
- Only the current user's Downloads and Documents trees are scanned. Reparse-point directories and files are skipped to avoid following links out of scope. Paths are deduplicated case-insensitively; distinct same-name files remain distinct. Missing and inaccessible paths are logged and skipped.
- File scanning starts after the existing app index finishes its first full refresh so it cannot compete with the app snapshot startup gate or first app search. A single below-normal-priority background worker owns full scans and watcher changes, then atomically publishes a new array. FileSystemWatcher events are batched and update only affected paths or subtrees. Buffer errors trigger a full reconcile and watcher recreation; a stale index also reconciles when the launcher is next shown after the existing app-index refresh interval. A missing root is watched from its parent so creation triggers a scan. No idle polling timer runs.
- File entries share directory parent nodes rather than retaining a full path per file. Each searchable entry retains its name, normalized name, directory flag, and compact character mask; full paths are assembled only for selected results and watcher changes navigate the tree by path segments. The mask rejects names missing any query letter before fuzzy scoring. This keeps query work and retained strings lower on large Documents trees without changing match semantics.
- File entries are not persisted. The existing snapshot is app-specific and the file tree changes more frequently; a second snapshot has no measured startup benefit yet and would add storage and stale-result handling. File search becomes available after its asynchronous scan, without delaying app or calculator startup.
- File and folder actions use the closed ResultAction executor. Files open through their registered shell association; folders open in Explorer. Activation checks existence and uses the existing inline error path on failure.

## Compiled-in result providers

- The app composes providers directly at startup. Providers return normalized 0–1 scores, support cancellable asynchronous queries, and carry closed result actions; v1 registers synchronous AppsProvider and CalculatorProvider with LaunchApp and CopyText actions.
- AppsProvider wraps the existing index, matcher, and frecency calculation. The engine applies provider weights and stable tie ordering, discards stale queries, and logs and skips provider failures. No plugin loading, hosted service, or query-time I/O is added.
- CalculatorProvider is the second compiled-in provider, weighted above AppsProvider so a valid expression wins even against a maximum-frecency app. It parses complete arithmetic expressions in memory with invariant decimals, rejects non-finite and invalid results, and returns a CopyText action. Ordinary app-name queries exit cheaply; calculator results are not cached by the app icon path so their clipboard text stays current.

## Calculator syntax and trigger

- A handwritten span tokenizer and precedence-climbing parser evaluate expressions; no `eval`, scripting runtime, plugin, or query-time I/O is used. Exponentiation is right-associative and binds tighter than unary minus; trigonometric functions use radians.
- Candidate queries start with a number, dot, parenthesis, sign, `=`, or a known function/constant. A complete expression with an operator, function, or percent yields one result; leading `=` also permits a bare number or constant. Bare numbers/constants and incomplete expressions yield none otherwise.
- Unknown app-name words exit without allocation before parsing. Results use invariant decimal input and ordinary output with up to 12 significant digits; invalid, non-finite, overflow, and divide-by-zero expressions yield none.

## Startup performance gates

- Keep the under-one-second usable-index gate on snapshot-backed startup, where the cached index is available before the background shell scan finishes.
- Record cold full-scan startup against the same reference as an informational metric, not a release gate. Shell AppsFolder enumeration and Start Menu shortcut target resolution are environment-dependent and can take longer than one second; the background scan is intentionally allowed to finish after the launcher becomes usable.
- The harness reports both cold and snapshot startup so a cold-scan slowdown remains visible without making the snapshot fast path's gate meaningless.

## Short-run memory gates

- Working set is informational because Windows can reclaim and account for shared pages differently across runs. Do not emit a working-set budget failure.
- Gate private bytes after 50 show/hide warm-up cycles at less than 130 MB, then gate growth from 50 to 100 cycles at less than 5%. These checkpoints expose retained private allocations while allowing startup and JIT warm-up to settle. The 24-hour plateau remains a separate informational check until it is routinely measured.

## Hotkey and shell lifecycle

- A message-only window owns `RegisterHotKey`. A separate hidden top-level native window receives `TaskbarCreated`, `WM_WTSSESSION_CHANGE`, and `WM_POWERBROADCAST` because broadcast messages do not reach message-only windows.
- After Explorer restarts, H.NotifyIcon's built-in taskbar recreation is supplemented by `ForceCreate(false)` on `TaskbarCreated`. Session reconnect, unlock, and power resume re-register the configured hotkey. A conflict logs, notifies through the tray, and opens Settings.
- Changing hotkeys unregisters the old binding, tries the new binding, and restores the old one if registration fails. Settings remain unchanged on failure.

## Windows foreground limits

- A normal-user app cannot take foreground focus from every elevated or exclusive-fullscreen surface. WinLight asks Windows to show and focus its topmost overlay; Windows may deny focus across integrity levels or when a game owns exclusive display input. It cannot bypass UAC secure desktop. The launcher remains resident and the tray/settings path remains available.
- A clicked, borderless, maximized topmost test window initially exposed a transient WinUI deactivation during show. Focus-loss dismissal is now armed only after the search box has focus and WinLight owns foreground. In the retest, Alt+Space focused the overlay (first frame 77.642 ms), and clicking the test window dismissed it. This covers a simulated fullscreen window, not a game in exclusive fullscreen mode.
- With a UAC-launched Notepad foreground, the user observed WinLight appear and accept typing after Alt+Space. The test process was launched with RunAs, but its token integrity was not independently read; the app still cannot appear on the UAC secure desktop.
- Automated session-unlock and power-resume messages exercise the callback, but they are not a substitute for real lock, sleep, or user switching. Physical-transition results are recorded separately in `docs/PROGRESS.md`.
- The user completed real lock/unlock and sleep/wake checks. The resident process survived, and each return logged a successful hotkey re-registration followed by a focused frame. The older test build logged only a generic resume reason, so the log alone cannot separate power resume from unlock. Fast user switching was not exercised because no second-account transition was observed.
- This test machine exposes one 1920×1200 display. Cursor-monitor placement is implemented with `DisplayArea.GetFromPoint` and per-window DPI, but mixed-DPI two-monitor behavior cannot be verified here.

## Exception boundary

- App-owned UI dispatcher callbacks, native window callbacks, background loops, and async work log exceptions without allowing those errors to leave their callback. The WinUI `UnhandledException` handler logs and marks recoverable XAML exceptions handled; unobserved task failures are observed and logged.
- Windows cannot promise recovery from process-corrupting failures such as `StackOverflowException`, `FailFast`, native access violations, or a forced termination. These are outside managed exception handling.
