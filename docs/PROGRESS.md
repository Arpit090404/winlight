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
