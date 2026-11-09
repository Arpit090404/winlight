# AGENTS.md — WinLight (Windows Spotlight-style launcher)

A keyboard-first app launcher for Windows 11 (Windows 10 22H2 best-effort). Press a global hotkey, type a few letters, press Enter, and the app launches. It must be **instant, reliable, and indistinguishable from a first-party Windows surface**.

Agents available in this repo (`.codex/agents/`): `coder`, `designer`, `optimizer`. Read the "Ownership" section before editing anything.

---

## 1. Product scope

**v1 (build only this):**
- Global hotkey toggles a centered search window (default `Alt+Space`, configurable).
- Search installed apps (classic Win32 and packaged/UWP/MSIX) by name, with fuzzy matching.
- Search file and folder names under the current user's Downloads and Documents folders.
- Evaluate complete arithmetic expressions inline and copy the selected result to the clipboard.
- Rank results by match quality plus frecency (frequency and recency of past launches).
- Keyboard: type to search, Up/Down to move, Enter to launch, Ctrl+Enter to launch as admin, Esc or focus loss to dismiss.
- Runs resident in the tray, starts at login, single instance.
- Empty query shows only the search bar; the result list appears only once the user types.

**Planned (do NOT build until asked; approach is decided when the phase starts):**
- Pomodoro timer.

**Out of scope (do not add unless asked):** web search, clipboard history, plugins, telemetry, network access, auto-update, cloud anything.

---

## 2. Tech stack (fixed — do not substitute)

| Concern | Choice |
|---|---|
| Language / runtime | C# on the latest .NET LTS (check `dotnet --list-sdks`; target `net10.0-windows10.0.19041.0` or newer LTS) |
| UI | WinUI 3 via Windows App SDK (latest stable), **unpackaged**, self-contained |
| Native interop | `Microsoft.Windows.CsWin32` source generator for P/Invoke (hotkey, DWM, shell). No hand-written `DllImport` unless CsWin32 cannot express it |
| MVVM | `CommunityToolkit.Mvvm` (source generators, no reflection) |
| Tray icon | `H.NotifyIcon.WinUI` |
| Tests | xUnit for `WinLight.Core` |
| Benchmarks | BenchmarkDotNet for hot paths |
| Storage | One small JSON file in `%LOCALAPPDATA%\WinLight\` (settings + usage counts), written atomically (temp file + replace). No database in v1 |

**Dependency rule:** the list above is the entire allowed set. Adding any NuGet package requires a written justification in the PR/summary. No Electron, WebView, Tauri, Python, or Node anywhere.

**Why this stack:** WinUI 3 gives real Fluent styling (Mica/Acrylic, theme resources, rounded corners, accent colors) with no emulation. A resident C# process with a pre-created hidden window avoids cold start. Keep all search logic in a UI-free library so it is testable and benchmarkable.

---

## 3. Repository layout

```
winlight/
├── AGENTS.md
├── .codex/                      # agent definitions + config
├── src/
│   ├── WinLight.Core/           # UI-free: indexing, matching, ranking, frecency, settings
│   ├── WinLight.App/            # WinUI 3 app: window, tray, hotkey, launching, views
│   │   ├── Views/               # XAML + view code-behind        (designer owns)
│   │   ├── Styles/              # ResourceDictionaries, templates (designer owns)
│   │   ├── Assets/              # icons, images                   (designer owns)
│   │   ├── ViewModels/          #                                  (coder owns)
│   │   └── Native/              # hotkey, DWM, shell interop       (coder owns)
│   ├── WinLight.Tests/
│   └── WinLight.Benchmarks/
└── docs/PROGRESS.md             # short running log of what is done / next
```

---

## 4. Ownership and workflow

Only **one agent writes to a given area at a time.** Parallel work is allowed only when file ownership does not overlap.

| Agent | Sandbox | May edit | Must not edit |
|---|---|---|---|
| `coder` | write | `WinLight.Core`, `ViewModels/`, `Native/`, tests, benchmarks, project files, `docs/PROGRESS.md` | Visual styling, XAML layout/templates |
| `designer` | write | `Views/`, `Styles/`, `Assets/` only | Search, indexing, launching, hotkey, interop logic |
| `optimizer` | **read-only** | Nothing | Everything. Reports findings; `coder` applies fixes |
| `scripter` | write | scripts/, tools/, scripts/budgets.json, docs/PERF.md | src/|

Handoff order: `coder` builds a working skeleton → `coder` adds features → `designer` and `optimizer` run in parallel → `coder` applies the optimizer's fixes and any designer-requested hooks → re-measure.

After each phase, append 3–5 lines to `docs/PROGRESS.md`: what changed, what was measured, what is next.

---

## 5. Architecture requirements

1. **Resident process.** The app starts at login (per-user `HKCU\...\Run` entry, not a scheduled task, no admin) and lives in the tray. Single instance via a named mutex; a second launch signals the first (named pipe) to show the window, then exits.
2. **Pre-created window.** Create the search window once at startup, hidden and fully laid out. Showing it must not construct UI, allocate large objects, or touch disk. Hide on Esc, Enter (after launch), and loss of focus.
3. **Global hotkey** via `RegisterHotKey` on a message-only window. Handle registration failure (hotkey taken) with a visible tray notification and a settings fallback; never crash or silently fail.
4. **App index.** Enumerate the `shell:AppsFolder` virtual folder (covers both classic and packaged apps, with AppUserModelIDs). Also scan both Start Menu `Programs` folders (all-users and per-user) for `.lnk`. De-duplicate by target/AUMID. Build the index off the UI thread at startup, keep it in memory, and refresh incrementally with `FileSystemWatcher` (debounced) plus a periodic cheap reconcile. Never block hotkey response on indexing.
5. **Launching.** Launch via `ShellExecuteEx` with the item's parsed path or `shell:AppsFolder\<AUMID>`. Support run-as-admin (Ctrl+Enter). Launch failures must show an inline message, never an unhandled exception.
6. **Search.** Custom in-memory fuzzy matcher: prefix > word-boundary/acronym > substring > subsequence, with case-insensitive, diacritic-insensitive matching. Precompute normalized names at index time. Score = match quality + frecency boost. Return top ~8 results. No allocations on the keystroke path beyond the result list.
7. **Icons.** Load asynchronously via `IShellItemImageFactory`, cache in memory (and optionally on disk), show a neutral placeholder first. Icon loading must never delay result text appearing.
8. **Threading.** UI thread does UI only. Indexing, icon extraction, and file IO run on background threads. Debounce nothing on the keystroke path that makes typing feel laggy; instead make matching fast enough not to need it.
9. **Resilience.** Wrap background loops with logging and restart. One bad shortcut or malformed `.lnk` must never break the index. Log to `%LOCALAPPDATA%\WinLight\logs\` with size-capped rotation; no network, no telemetry.
10. **Permissions.** Runs as the normal user. Never require admin to run.

---

## 6. Performance budgets (the optimizer enforces these)

[`scripts/budgets.json`](scripts/budgets.json) is the sole source of numeric budgets. It defines the hotkey, query, snapshot-ready, cold-start informational, idle CPU, idle working-set, memory-plateau, and build-warning limits. The query budget applies to the stated index size and visible-row cap. Snapshot-backed index readiness is gating; cold full-scan readiness is informational as recorded in `docs/DECISIONS.md`. Keep numeric thresholds in that file rather than duplicating them here.

Measure, do not guess. Use BenchmarkDotNet for matching/ranking, `dotnet-counters` / Windows Performance Recorder / PerfView for runtime behavior, and a stopwatch from `WM_HOTKEY` receipt to first rendered frame for show latency. Report numbers, not impressions.

---

## 7. Design rules (the designer enforces these)

Goal: it should look like it shipped with Windows 11.

- **Materials:** system backdrop (Mica Alt or Desktop Acrylic) on the window; rely on the OS for the window's rounded corners and border (set the DWM corner preference to rounded; do not fake corners with a custom shape).
- **Theme:** follow system light/dark and the user's accent color live. Use `{ThemeResource}` brushes only. **No hardcoded colors**, no custom palettes.
- **Typography:** Segoe UI Variable via the default WinUI type ramp (`BodyTextBlockStyle`, `BodyStrongTextBlockStyle`, `SubtitleTextBlockStyle`). No custom fonts.
- **Icons:** Segoe Fluent Icons glyphs for UI chrome; app icons come from the shell.
- **Layout:** 4 px spacing grid. Search box ~48–56 px tall, result rows ~48 px, window width ~640 px, positioned horizontally centered on the active monitor at roughly one-fifth down from the top. Respect per-monitor DPI.
- **Selection and hover:** use the standard list-item visual states and theme brushes (subtle fill for hover, accent indicator for selection), matching WinUI `ListView` behavior.
- **Search-result presentation:** show an Apps header above app results and a Files & Folders header above file and folder results; Apps precede Files & Folders, each category retains its ranked order, and empty categories have no header. Headers are not selectable; calculator results keep their existing row presentation and behavior.
- **Motion:** show/hide transitions ≤ 150 ms, opacity and slight translate only, using standard easing. Honor the system "animation effects" setting and disable animation when it is off. Never animate on the keystroke path.
- **Keyboard and accessibility:** fully keyboard-operable, visible focus, `AutomationProperties.Name` on every interactive element, Narrator announces the selected result, usable under High Contrast themes and 200% text scaling.
- **Empty and error states:** a quiet "No results" row; inline launch-failure message in theme error colors. No dialogs.
- **Restraint:** no gradients, shadows beyond what the backdrop provides, decorative animations, or branding. Less chrome is more native.

The designer may ask the coder for view-model properties or commands; it must not implement logic itself.

## Verification

- Run `scripts/verify.ps1` after every change.
- Run `scripts/perf.ps1 -Quick` after any change that touches the show path, search path, or indexing.
- Report only the generated `artifacts/perf/summary.txt`; open raw logs only when a metric fails.
- `scripts/verify.ps1` restores, builds Release with zero warnings, and runs all tests. `scripts/perf.ps1` publishes and measures the app on the current user's desktop; use `-Full` for the complete measurement set.
- The perf wrapper records and restores a resident WinLight process from this workspace; it refuses to stop an instance outside the workspace. It restores the prior snapshot and login Run value after measurement.

---

## 8. Coding conventions

- Nullable reference types enabled, warnings as errors, `LangVersion` latest.
- Prefer `Span`/pooling and precomputed data on hot paths; avoid LINQ, closures, and string allocations in per-keystroke code.
- `WinLight.Core` has **no dependency on WinUI or Windows App SDK**, so tests and benchmarks run headless.
- Async all the way for IO; never `.Result` / `.Wait()` on the UI thread.
- Small files, small types, clear names. Comments explain *why*, not *what*.
- Every Core behavior (matching, ranking, frecency, de-duplication) gets unit tests. Add a regression test with every bug fix.

---

## 9. Commands

```powershell
dotnet restore
dotnet build -c Release
dotnet test
dotnet run --project src/WinLight.App -c Release
dotnet run --project src/WinLight.Benchmarks -c Release
```

Run build and tests before declaring any task done. If a command fails, fix the cause; do not skip, comment out, or weaken tests.

---

## 10. Definition of done

A task is done when:
1. `dotnet build -c Release` has zero warnings and `dotnet test` passes.
2. The behavior works when actually run (hotkey shows the window, search works, launch works), not just compiles.
3. No performance budget in section 6 regressed (or the regression is explained and flagged).
4. Scope respected: nothing outside v1 added, no new dependencies without justification.
5. `docs/PROGRESS.md` updated.

---

## 11. Hard don'ts

- Don't poll or run timers while the window is hidden.
- Don't index the whole disk or touch the network.
- Don't require admin, write to Program Files, or modify system settings beyond the user's own startup entry.
- Don't swallow exceptions silently; log them.
- Don't edit files outside your ownership area.
- Don't mark work complete without running the app.

## Providers

Providers are compiled into the app and registered in the plain App composition root. Return immediately and cheaply when a query is irrelevant; do no I/O on the query path. Honor cancellation tokens, and report scores normalized to 0–1 within each provider. Keep provider dependencies in constructors. v1 registers AppsProvider, CalculatorProvider, and FileSearchProvider only.
