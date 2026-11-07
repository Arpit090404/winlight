# WinLight reliability decisions

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
