# WinLight performance baseline — 2026-11-06

Measured with the unpackaged, self-contained **published Release** executable in `publish/WinLight`, on the normal Windows desktop (`WINLIGHT_PERF=1`). Process ID 7428; Windows 11 10.0.26300.9711, .NET 10.0.11, Intel Core Ultra 5 225H. No behavior tuning was made in this pass. Percentiles use the nearest-rank method.

| Budget metric | Budget | Observed | Result |
|---|---:|---:|---|
| Hotkey receipt → first visible, focused `CompositionTarget.Rendering` callback, 50 immediate shows | p95 < 100 ms (target < 60 ms) | p50 **7.284 ms**, p95 **11.059 ms**, max **24.808 ms** | **PASS** |
| Same hotkey measure, 20 shows after 5 minutes idle | p95 < 100 ms (target < 60 ms) | p50 **10.115 ms**, p95 **14.653 ms**, max **22.330 ms** | **PASS** |
| TextChanged → result collection and ListView height updated, real 169-item index | < 16 ms at 5,000 indexed items (the list renders at most 8 rows) | 33 completed queries: p50 **2.111 ms**, p95 **3.936 ms**, max **8.283 ms** | **FAIL — full 5,000-item render budget unverified** |
| Process start → first usable index | < 1,000 ms | **2,168.917 ms** (169 deduplicated items) | **FAIL** |
| Hidden idle CPU over 60 seconds | ~0% | **0 CPU seconds**, 0.000% of one core; 27 threads, 1,033 handles at end | **PASS** |
| Idle working set at 1 minute | < 100 MB | **198.22 MB** working set; 88.82 MB private bytes | **FAIL** |
| No working-set growth over 24 hours | No growth | **198.22 → 235.26 → 237.22 MB** at 1 minute, after 100 show/hide cycles, and after 10 minutes; 24 hours not measured | **FAIL — growth observed; 24-hour limit unverified** |
| Release build | Zero warnings | `dotnet build -c Release`: **0 warnings, 0 errors** | **PASS** |

The hotkey timing endpoint is the first WinUI `CompositionTarget.Rendering` callback after the overlay is visible and the TextBox reports focus. It does **not** measure the GPU present event. The query timing endpoint is the ListView collection/height update, **not** the rendered frame. Of 35 injected query attempts (`c`, `calc`, `vsc`, `note`, `xyz`, repeated), 33 produced a final `TextChanged` result; two produced no query event and are excluded from the latency percentiles. The two misses are retained as a reliability finding; synthetic input/focus was not isolated. Result counts were `c`: 8, `calc`: 3, `vsc`: 5, `note`: 8, `xyz`: 0.

After measurement, the published app was restarted with `WINLIGHT_PERF=0`. One Alt+Space show/hide cycle left `perf.log` at **855 lines before and after**; the app remained responsive. The disabled path creates no writer or performance log entries, though the compiled gate branches remain.

| Additional measure | Result |
|---|---:|
| First index enumeration/refresh | **1,435.176 ms** for 282 raw shell/shortcut entries; 169 after deduplication (one sample) |
| Process start → index complete | **2,168.998 ms** |
| Icon cold queue → decoded `BitmapImage` assigned | 49 items: p50 **80.985 ms**, p95 **279.299 ms**, max **324.622 ms** |
| Cached icon lookup | 566 items: p50 **0.000 ms**, p95 **0.003 ms**, max **0.037 ms** |
| Working set / private bytes after 100 show/hide cycles | **235.26 MB / 109.67 MB**, 32 threads, 1,205 handles |
| Working set / private bytes after 10 minutes | **237.22 MB / 111.62 MB**, 33 threads, 1,207 handles |
| Growth from 1-minute to 10-minute checkpoint | **+39.00 MB working set**, **+22.80 MB private bytes**, **+166 handles**, **+4 threads** |

The 100-cycle checkpoint includes 50 immediate hotkey cycles, 20 after the five-minute idle, and 30 query show/hide cycles. The 10-minute checkpoint followed five supplemental queries. The 60-second CPU sample was taken while the window was hidden and untouched, after benchmark activity ended.

## BenchmarkDotNet: in-memory matching and ranking

`MemoryDiagnoser`, `ShortRunJob`, .NET 10 Release. Each call searches a synthetic index and returns the ranked top eight. These timings exclude WinUI binding and rendering.

| Index size | Prefix `calc` mean / allocated | Acronym `vsc` mean / allocated | No match `xyz` mean / allocated | 16 ms core-search comparison |
|---:|---:|---:|---:|---|
| 200 | 4.693 µs / 416 B | 4.631 µs / 416 B | 4.932 µs / 328 B | **PASS** |
| 1,000 | 23.192 µs / 416 B | 24.109 µs / 416 B | 23.302 µs / 328 B | **PASS** |
| 5,000 | 117.648 µs / 416 B | 111.468 µs / 416 B | 118.661 µs / 328 B | **PASS** |

BenchmarkDotNet's initial sandboxed generated-project restore failed on NuGet vulnerability metadata; the normal-desktop run completed all nine cases. The short run has three measured iterations per case, so these are baseline estimates rather than a long-run distribution. The 5,000-indexed-item full UI update and 24-hour memory stability remain unverified.

## Stage 3 published Release follow-up — 2026-11-07

Measured with `WINLIGHT_PERF=1` on the normal desktop, published process 2008, Windows 11 and .NET 10 on the same machine as the baseline. The index snapshot held 275 raw entries and published 168 deduplicated apps; the background scan replaced it with 168 apps. No icon-cache cap, COM-thread parallelization, working-set trimming, publish setting, GC setting, or show-path workaround was applied. Values below use nearest-rank percentiles. The hotkey endpoint remains the first WinUI rendering callback after focus, not GPU present; query timing still stops at list update, not rendering.

| Budget metric | Budget | Before | After | Result after |
|---|---:|---:|---:|---|
| 50 immediate hotkey shows, p50 / p95 / max | p95 < 100 ms; target < 60 ms | 7.284 / 11.059 / 24.808 ms | **14.148 / 27.350 / 35.429 ms**; 50/50 shown | **PASS**; slower than baseline |
| 20 shows after 5 minutes untouched, p50 / p95 / max | p95 < 100 ms; target < 60 ms | 10.115 / 14.653 / 22.330 ms | **15.735 / 27.249 / 31.341 ms**; 20/20 shown | **PASS**; slower than baseline |
| TextChanged → list update, p50 / p95 / max | < 16 ms at 5,000 indexed items (the list renders at most 8 rows) | 2.111 / 3.936 / 8.283 ms; 33 of 35 final queries | **1.395 / 2.849 / 9.512 ms**; 30 of 30 varied final queries, 168 indexed apps | **FAIL — 5,000-item full UI render unverified** |
| Process start → usable index | < 1,000 ms | 2,168.917 ms | **544.564 ms** from versioned snapshot; snapshot load itself 37.602 ms | **PASS** |
| Process start → full index complete | Full scan may finish later | 2,168.998 ms | **1,966.115 ms** after background scan | **PASS** |
| Hidden idle CPU over 60 seconds | ~0% | 0 CPU seconds | **0 CPU seconds**, 28 threads and 1,013 handles at 1 minute | **PASS** |
| Idle working set at 1 minute | < 100 MB | 198.22 MB WS / 88.82 MB private | **209.43 MB WS / 97.91 MB private** | **FAIL** |
| Working set after 100 cycles and at 10 minutes | No growth over 24 hours | 235.26 → 237.22 MB WS; 109.67 → 111.62 MB private | **236.80 → 236.63 MB WS; 111.55 → 111.16 MB private** | **FAIL**; short idle plateau, 24 hours unverified |
| Release build and tests | Zero warnings; tests pass | 0 warnings; 12 tests | **0 warnings; 17 of 17 tests pass** | **PASS** |

The published app had no `index.json` in the first snapshot-enabled cold run; it fell back to a full scan, reached usable at **2,192.075 ms**, and wrote the snapshot. Subsequent warm runs reached usable at **655.330**, **735.594**, **760.202**, and **544.564 ms**. Missing, corrupt, and old-version snapshots return no entries and let the background scan proceed; these cases and a valid round trip are covered by xUnit tests. A loaded snapshot item with a missing local target is rejected before `ShellExecuteEx`, so the existing launch catch shows the inline error. The next Start Menu scan omits seven shortcuts whose resolved targets no longer exist (for example Hydra, Smart Browser, and Adobe Promotion); AppsFolder remains authoritative for packaged apps. The missing-target launch branch was covered by a Core predicate test, not a live stale-click test.

| Scan phase, final warm run | Duration / count |
|---|---:|
| AppsFolder enumeration | **477.024 ms**, 159 entries |
| Start Menu enumeration, including target resolution | **911.593 ms**, 123 shortcuts |
| Cumulative `.lnk` target resolution within Start Menu | **886.825 ms**, 123 shortcuts |
| Entire background scan | **1,394.346 ms**, 275 raw entries after stale-target filtering |
| `AppDeduplicator.Deduplicate` on snapshot / scanned entries | **24.799 / 13.228 ms**, 275 → 168; algorithm unchanged |

The pre-fix timing split was AppsFolder **615.400 ms**, Start Menu **1,129.587 ms**, and cumulative `.lnk` target resolution **1,108.336 ms** inside a **1,751.811 ms** scan; deduplication took **21.840 ms** for 282 → 169 entries. Shortcut resolution dominated Start Menu time. It uses shell COM objects created on a single STA thread; no parallel target-resolution change was made because sharing those apartment-bound objects across worker threads is unsafe. The snapshot makes that scan nonblocking for first usable state.

### Memory attribution before memory changes

Both 1,000-cycle runs below were on the published pre-snapshot process before any icon-cache or COM-lifetime changes. They were sequential, so the second run started warm. Values are working set / private MB, then handles / threads.

| Run | Start | Midpoint (500) | End (1,000) | Plateau? |
|---|---:|---:|---:|---|
| No queries | **237.07 / 113.04; 1,176 / 31** | **253.23 / 131.31; 1,178 / 30** | **296.48 / 175.65; 1,169 / 28** | **No**; bytes rose, handles flat |
| With `calc` queries | **296.48 / 175.65; 1,169 / 28** | **320.48 / 199.42; 1,177 / 32** | **337.37 / 215.85; 1,172 / 28** | **No**; bytes rose, handles flat |

A fresh snapshot-enabled diagnostic process repeated 1,000 no-query cycles: **190.61 / 85.21; 1,004 / 35** at start, **221.86 / 111.79; 1,010 / 30** at midpoint, and **263.07 / 154.85; 1,009 / 28** at end. Another 1,000 `calc` cycles rose from **263.55 / 155.37; 1,009 / 28** to **305.38 / 188.47; 1,137 / 31** and **349.93 / 233.28; 1,134 / 29**. Gated in-process samples showed **0 cached icons and 0 queued icons** throughout the no-query run, **3 cached and 0 queued** during the query run, and only about **2.6–4.8 MB live managed bytes**. The repeated-cycle growth is therefore outside the retained managed icon cache; native WinUI allocations or allocator warm-up remain unisolated. The final ten-minute short idle period plateaued after 100 cycles, but the 1,000-cycle runs did not. No cache, decode-size, COM-release, or working-set change was justified by these data. `dotnet-counters` and `dotnet-gcdump` were unavailable in this environment; 24-hour stability remains unverified.

### Input reliability and other after measures

With focus, active-state, and foreground HWND logging enabled, **100 of 100** injected show-then-type attempts with a 35 ms post-show wait delivered the final `calc` TextChanged event; the TextBox reported `FocusState=Keyboard` and the foreground HWND matched the overlay. With zero wait, the overlay was not yet visible at the immediate check in **93 of 100** attempts, and many keys did not reach the TextBox. This supports injection-before-focus timing as the earlier 2-of-35 misses. The `active` flag sometimes lagged while foreground and keyboard focus already matched; no application focus workaround was added. The immediate-check figure is not an eventual hotkey-failure count.

| Additional after measure | Before | After |
|---|---:|---:|
| Cold icon queue → decoded image assigned, p50 / p95 / max | 80.985 / 279.299 / 324.622 ms (49) | **220.601 / 304.746 / 306.568 ms** (23) |
| Cached icon lookup, p50 / p95 / max | 0 / 0.003 / 0.037 ms (566) | **0 / 0.002 / 0.003 ms** (335) |
| Working set / private after 100 cycles | 235.26 / 109.67 MB | **236.80 / 111.55 MB**, 1,186 handles, 37 threads |
| Working set / private after 10 minutes | 237.22 / 111.62 MB | **236.63 / 111.16 MB**, 1,166 handles, 30 threads |

The rerun BenchmarkDotNet `MemoryDiagnoser` means remained close to baseline: at 200 apps, prefix/acronym/no-match **4.607 / 4.656 / 4.777 µs**; at 1,000, **22.732 / 22.291 / 23.182 µs**; at 5,000, **112.596 / 111.694 / 119.930 µs**. Allocations remained **416 B** for prefix/acronym and **328 B** for no-match. Core matching passes a 16 ms comparison, while 5,000-item full WinUI rendering and GPU present timing remain unmeasured.
