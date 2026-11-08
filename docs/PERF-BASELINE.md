# Full performance baseline — 2026-11-08

Source: latest completed Full run, `Full-20261108-175410-12472`.
Future perf runs should compare the same metrics and sample sizes against this stored report. This run predates the corrected cycle-aligned 50-to-100 sampling in `scripts/perf.ps1`; its 50-cycle sample was compared with the harness checkpoint after 100 additional cycles (about 150 total), so its 7.05% growth result does not validate the new 50-to-100 gate.

```text
Mode: Full; harness injection misses (Notepad): 2/60 (3.3%); text 1/30; Alt+Space 1/30
Calibration diagnostics: focus misses 0; clipboard read exceptions 0; SendInput rejected calls 0.
cold hotkey immediate p50: 24.095 ms; budget <100; target 60 ms; PASS [info]
cold hotkey immediate p95: 37.13 ms; budget <100; target 60 ms; PASS [gate]
cold hotkey immediate max: 42.793 ms; budget <100; target 60 ms; PASS [info]
cold query p50: 0.303 ms; budget <16; PASS [info]
cold query p95: 3.333 ms; budget <16; PASS [info]
cold query max: 21.227 ms; budget <16; FAIL [gate]
cold usable index: 972.532 ms; budget <1000; PASS [info]
cold usable memory/resources: working set 169.648 MB (informational); private 78.66 MB (informational); 968 handles; 39 threads
cold after-immediate-cycles memory/resources: working set 227.977 MB (informational); private 111.61 MB (informational); 1201 handles; 45 threads
cold harness injection misses: 2 / 128; budget 0; FAIL [info]
snapshot hotkey immediate p50: 16.578 ms; budget <100; target 60 ms; PASS [info]
snapshot hotkey immediate p95: 25.002 ms; budget <100; target 60 ms; PASS [gate]
snapshot hotkey immediate max: 25.127 ms; budget <100; target 60 ms; PASS [info]
snapshot hotkey after idle p50: 8.549 ms; budget <100; target 60 ms; PASS [info]
snapshot hotkey after idle p95: 16.983 ms; budget <100; target 60 ms; PASS [gate]
snapshot hotkey after idle max: 22.932 ms; budget <100; target 60 ms; PASS [info]
snapshot query p50: 0.327 ms; budget <16; PASS [info]
snapshot query p95: 2.278 ms; budget <16; PASS [info]
snapshot query max: 28.521 ms; budget <16; FAIL [gate]
snapshot usable index: 758.709 ms; budget <1000; PASS [gate]
snapshot idle CPU: 0 % one core; budget <0.1; PASS [gate]
snapshot usable memory/resources: working set 167.055 MB (informational); private 77.09 MB (informational); 943 handles; 38 threads
snapshot after-immediate-cycles memory/resources: working set 229.199 MB (informational); private 111.02 MB (informational); 1204 handles; 46 threads
snapshot idle-60s memory/resources: working set 226.789 MB (informational); private 107.62 MB (informational); 1185 handles; 28 threads
snapshot after-100-show-hide memory/resources: working set 235.652 MB (informational); private 115.26 MB (informational); 1184 handles; 26 threads
snapshot harness injection misses: 1 / 132; budget 0; FAIL [info]
Notepad text injection misses: 1 / 30; budget 0; FAIL [info]
Notepad Alt+Space injection misses: 1 / 30; budget 0; FAIL [info]
Notepad harness injection misses: 2 / 60; budget 0; FAIL [info]
Note: The 24-hour memory plateau budget requires a separate overnight run; only the short cycle checkpoint was sampled here.
Private bytes after 50 cycles: 107.67 MB; budget <130 MB; PASS [gate]
Private bytes after 100 cycles: 115.26 MB (measurement)
Private bytes growth 50 to 100 cycles: 7.05%; budget <5%; FAIL [gate]
State restore: snapshot MATCH (SHA256 1E230AEF0842539D5F9BCEB14B219A40D3F96DEDCA387364DFAE66BED3402427); HKCU Run MATCH (WinLight="C:\Users\Arpit\Documents\Coding\winlight\publish\WinLight\WinLight.App.exe" (String))
WinLight was stopped; restart it manually.
```
