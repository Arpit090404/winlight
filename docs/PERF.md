# WinLight performance baseline — 2026-11-06

Measured with the unpackaged, self-contained **published Release** executable in `publish/WinLight`, on the normal Windows desktop (`WINLIGHT_PERF=1`). Process ID 7428; Windows 11 10.0.26300.9711, .NET 10.0.11, Intel Core Ultra 5 225H. No behavior tuning was made in this pass. Percentiles use the nearest-rank method.

| Budget metric | Budget | Observed | Result |
|---|---:|---:|---|
| Hotkey receipt → first visible, focused `CompositionTarget.Rendering` callback, 50 immediate shows | p95 < 100 ms (target < 60 ms) | p50 **7.284 ms**, p95 **11.059 ms**, max **24.808 ms** | **PASS** |
| Same hotkey measure, 20 shows after 5 minutes idle | p95 < 100 ms (target < 60 ms) | p50 **10.115 ms**, p95 **14.653 ms**, max **22.330 ms** | **PASS** |
| TextChanged → result collection and ListView height updated, real 169-item index | < 16 ms for **5,000 rendered** items | 33 completed queries: p50 **2.111 ms**, p95 **3.936 ms**, max **8.283 ms** | **FAIL — full 5,000-item render budget unverified** |
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

BenchmarkDotNet's initial sandboxed generated-project restore failed on NuGet vulnerability metadata; the normal-desktop run completed all nine cases. The short run has three measured iterations per case, so these are baseline estimates rather than a long-run distribution. The 5,000-item **rendered-result** budget and 24-hour memory stability remain unverified.
