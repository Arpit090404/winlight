using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

internal static class Program
{
    private const uint InputKeyboard = 1;
    private const uint KeyUp = 0x0002;
    private const uint Unicode = 0x0004;
    private const int SwRestore = 9;
    private static readonly string PerfPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinLight", "logs", "perf.log");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate,
        Converters = { new JsonStringEnumConverter() }
    };
    private static DateTimeOffset _eventSinceUtc;
    private static int _sendInputRejectedCalls;

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0].Equals("calibrate", StringComparison.OrdinalIgnoreCase))
            {
                using JsonDocument budgets = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "budgets.json")));
                int attempts = budgets.RootElement.GetProperty("harness_calibration").GetProperty("attempts").GetInt32();
                Calibration calibration = CalibrateNotepad(attempts);
                int misses = calibration.TextMisses + calibration.HotkeyMisses;
                int checks = attempts * 2;
                Console.WriteLine($"Notepad calibration: {misses}/{checks} misses ({misses / (double)checks:P1}); text {calibration.TextMisses}/{attempts}; Alt+Space {calibration.HotkeyMisses}/{attempts}");
                Console.WriteLine($"Focus misses: {calibration.ForegroundMisses}; clipboard read exceptions: {calibration.ClipboardReadErrors}; SendInput rejected calls: {calibration.InputRejectedCalls}");
                return 0;
            }
            if (args.Length == 2 && args[0].Equals("summarize", StringComparison.OrdinalIgnoreCase))
            {
                string resultPath = Path.GetFullPath(args[1]);
                var saved = JsonSerializer.Deserialize<RunResults>(File.ReadAllText(resultPath), JsonOptions)
                    ?? throw new InvalidDataException("Could not read saved performance results.");
                foreach (Metric metric in saved.Metrics.Where(metric => metric.Name.Contains("hotkey", StringComparison.Ordinal)))
                    metric.Target = 60;
                string summaryPath = Path.Combine(Path.GetDirectoryName(resultPath)!, "summary.txt");
                WriteSummary(saved, summaryPath);
                Console.WriteLine(File.ReadAllText(summaryPath));
                return saved.Metrics.Any(metric => metric.Gate && !metric.Pass) ? 1 : 0;
            }
            if (args.Length != 3) throw new ArgumentException("Usage: WinLight.PerfHarness <Quick|Full> <published-exe> <artifact-dir>");
            bool full = args[0].Equals("Full", StringComparison.OrdinalIgnoreCase);
            string exe = Path.GetFullPath(args[1]);
            string output = Path.GetFullPath(args[2]);
            Directory.CreateDirectory(output);
            Run(exe, output, full);
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Harness failed: {error.GetType().Name}: {error.Message}");
            return 1;
        }
    }

    private static void Run(string exe, string output, bool full)
    {
        var active = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe));
        try
        {
            if (active.Length != 0)
            {
                string found = string.Join(", ", active.Select(process => $"PID {process.Id} at {SafePath(process)}"));
                throw new InvalidOperationException($"A WinLight process is already running ({found}); close it before measuring to avoid signaling a pre-existing instance.");
            }
        }
        finally { foreach (var process in active) process.Dispose(); }

        var logsDir = Path.GetDirectoryName(PerfPath)!;
        Directory.CreateDirectory(logsDir);
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinLight");
        string snapshot = Path.Combine(dataDir, "index.json");
        string snapshotBackup = snapshot + ".harness-backup";
        bool hadSnapshot = File.Exists(snapshot);
        if (hadSnapshot) File.Copy(snapshot, snapshotBackup, true);

        var results = new RunResults { Mode = full ? "Full" : "Quick", StartedUtc = DateTimeOffset.UtcNow };
        try
        {
            using JsonDocument budgetDocument = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "budgets.json")));
            int injectionTrials = budgetDocument.RootElement.GetProperty("harness_calibration").GetProperty("attempts").GetInt32();
            results.HarnessCalibrationAttempts = injectionTrials;
            results.HarnessUnreliableAboveRate = budgetDocument.RootElement.GetProperty("harness_calibration").GetProperty("unreliable_miss_rate_above").GetDouble();
            Calibration calibration = CalibrateNotepad(injectionTrials);
            results.HarnessTextInjectionMisses = calibration.TextMisses;
            results.HarnessHotkeyInjectionMisses = calibration.HotkeyMisses;
            results.HarnessForegroundMisses = calibration.ForegroundMisses;
            results.HarnessClipboardReadErrors = calibration.ClipboardReadErrors;
            results.HarnessSendInputRejectedCalls = calibration.InputRejectedCalls;
            results.HarnessInjectionMisses = calibration.TextMisses + calibration.HotkeyMisses;
            results.HarnessInjectionTrials = injectionTrials * 2;

            File.Delete(snapshot);
            var cold = RunApp(exe, "cold", output, full, doIdle: false, doPlateau: false);
            results.Scenarios.Add(cold);

            if (File.Exists(snapshot))
            {
                var warm = RunApp(exe, "snapshot", output, full, doIdle: true, doPlateau: true);
                results.Scenarios.Add(warm);
            }
            else results.Notes.Add("Snapshot was not produced after cold scan; snapshot startup could not be measured.");
            results.Notes.Add("The 24-hour memory plateau budget requires a separate overnight run; only the short cycle checkpoint was sampled here.");
        }
        finally
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
            {
                try { if (!process.HasExited) process.Kill(true); } catch { }
                process.Dispose();
            }
            if (hadSnapshot) File.Copy(snapshotBackup, snapshot, true); else File.Delete(snapshot);
            File.Delete(snapshotBackup);
        }

        ApplyBudgets(results, Path.Combine(AppContext.BaseDirectory, "budgets.json"));
        results.FinishedUtc = DateTimeOffset.UtcNow;
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(results, JsonOptions));
        WriteSummary(results, Path.Combine(output, "summary.txt"));
        Console.WriteLine(File.ReadAllText(Path.Combine(output, "summary.txt")));
        if (results.Metrics.Any(metric => metric.Gate && !metric.Pass)) Environment.ExitCode = 1;
    }

    private static string SafePath(Process process)
    {
        try { return process.MainModule?.FileName ?? "unknown path"; }
        catch { return "path unavailable"; }
    }

    private static Calibration CalibrateNotepad(int trials)
    {
        string file = Path.Combine(Path.GetTempPath(), $"WinLightHarness-{Guid.NewGuid():N}.txt");
        File.WriteAllText(file, string.Empty);
        HashSet<int> existing = Process.GetProcessesByName("Notepad").Select(p => { int id = p.Id; p.Dispose(); return id; }).ToHashSet();
        using Process notepad = Process.Start(new ProcessStartInfo("notepad.exe", $"\"{file}\"") { UseShellExecute = true })!;
        nint window = 0;
        int textMisses = 0, hotkeyMisses = 0, foregroundMisses = 0, clipboardReadErrors = 0;
        int rejectedBefore = Volatile.Read(ref _sendInputRejectedCalls);
        try
        {
            try { notepad.WaitForInputIdle(10000); } catch (InvalidOperationException) { }
            WaitUntil(() => (window = FindWindowContaining(Path.GetFileName(file))) != 0, TimeSpan.FromSeconds(15));
            if (window == 0) throw new InvalidOperationException("Could not find the calibration Notepad window.");
            for (int i = 0; i < trials; i++)
            {
                string sample = "WinLightHarness" + i.ToString(CultureInfo.InvariantCulture);
                ShowWindow(window, SwRestore);
                SetForegroundWindow(window);
                if (!WaitUntil(() => GetForegroundWindow() == window, TimeSpan.FromSeconds(1))) foregroundMisses++;
                Thread.Sleep(100);
                SendCtrlA(); SendUnicode(sample); SendCtrlA(); SendCtrlC();
                bool copied = WaitUntil(() =>
                {
                    try { return string.Equals(Clipboard.GetText(), sample, StringComparison.Ordinal); }
                    catch (System.Runtime.InteropServices.ExternalException) { clipboardReadErrors++; return false; }
                }, TimeSpan.FromSeconds(1));
                if (!copied) textMisses++;

                SendAltSpace();
                bool menuOpened = WaitUntil(() => FindWindow("#32768", null) != 0, TimeSpan.FromSeconds(1));
                if (!menuOpened) hotkeyMisses++;
                SendVirtualKey(0x1B);
                Thread.Sleep(80);
            }
        }
        finally
        {
            if (window != 0 && IsWindowVisible(window))
            {
                ShowWindow(window, SwRestore); SetForegroundWindow(window); Thread.Sleep(100);
                SendCtrlA(); SendVirtualKey(0x08); SendCtrlS(); SendCtrlW();
                WaitUntil(() => FindWindowContaining(Path.GetFileName(file)) == 0, TimeSpan.FromSeconds(5));
            }
            foreach (Process process in Process.GetProcessesByName("Notepad"))
            {
                try { if (!existing.Contains(process.Id) && !process.HasExited) process.Kill(true); } catch { }
                process.Dispose();
            }
            File.Delete(file);
        }
        return new Calibration(textMisses, hotkeyMisses, foregroundMisses, clipboardReadErrors,
            Volatile.Read(ref _sendInputRejectedCalls) - rejectedBefore);
    }

    private static Scenario RunApp(string exe, string label, string output, bool full, bool doIdle, bool doPlateau)
    {
        var scenario = new Scenario { Name = label };
        _eventSinceUtc = DateTimeOffset.UtcNow.AddMilliseconds(-50);
        var start = Stopwatch.StartNew();
        using Process app = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })!;
        scenario.ProcessId = app.Id;
        WaitUntil(() => HasEvent("index_usable"), TimeSpan.FromSeconds(90));
        scenario.StartupUsableMs = EventValue("index_usable");
        scenario.IndexCompleteMs = EventValueOrNull("index_complete");
        scenario.IndexItems = EventDetailInt("index_usable", "items");
        scenario.SnapshotLoadMs = EventValueOrNull("index_snapshot_load");
        scenario.StartupElapsedMs = start.Elapsed.TotalMilliseconds;
        scenario.Checkpoints.Add(Sample(app, "usable"));

        string[] queries = ["calc", "vsc", "note", "xyz"];
        int cycles = full ? 30 : 5;
        for (int i = 0; i < cycles; i++)
        {
            int beforeShow = EventCount("hotkey_first_frame");
            SendAltSpace();
            if (WaitUntil(() => EventCount("hotkey_first_frame") > beforeShow, TimeSpan.FromSeconds(3)))
            {
                scenario.ShowImmediateMs.Add(EventValueAt("hotkey_first_frame", beforeShow));
                Thread.Sleep(40);
                string query = queries[i % queries.Length];
                int beforeQuery = EventCount("query_update");
                SendUnicode(query);
                if (WaitUntil(() => EventCount("query_update") > beforeQuery, TimeSpan.FromSeconds(2)))
                {
                    Thread.Sleep(80);
                    var queryEvents = ReadEvents("query_update");
                    for (int queryIndex = beforeQuery; queryIndex < queryEvents.Count; queryIndex++)
                        scenario.QueryMs.Add(double.Parse(queryEvents[queryIndex][2], CultureInfo.InvariantCulture));
                }
                else scenario.QueryInjectionMisses++;
                SendVirtualKey(0x1B);
                Thread.Sleep(100);
            }
            else scenario.ShowInjectionMisses++;
        }
        scenario.Checkpoints.Add(Sample(app, "after-immediate-cycles"));

        if (doIdle)
        {
            Thread.Sleep(full ? 300000 : 60000);
            int idleCycles = full ? 20 : 1;
            for (int i = 0; i < idleCycles; i++)
            {
                int beforeShow = EventCount("hotkey_first_frame");
                SendAltSpace();
                if (WaitUntil(() => EventCount("hotkey_first_frame") > beforeShow, TimeSpan.FromSeconds(3)))
                    scenario.ShowAfterIdleMs.Add(EventValueAt("hotkey_first_frame", beforeShow));
                else scenario.ShowInjectionMisses++;
                SendVirtualKey(0x1B); Thread.Sleep(250);
            }

            using var cpu = Process.GetProcessById(app.Id);
            TimeSpan cpuStart = cpu.TotalProcessorTime;
            Thread.Sleep(60000);
            cpu.Refresh();
            double cpuSeconds = (cpu.TotalProcessorTime - cpuStart).TotalSeconds;
            scenario.IdleCpuSeconds = cpuSeconds;
            scenario.IdleCpuPercentOneCore = cpuSeconds / 60.0 * 100.0;
            scenario.Checkpoints.Add(Sample(app, "idle-60s"));
        }

        if (doPlateau)
        {
            int plateau = full ? 100 : 10;
            for (int i = 0; i < plateau; i++)
            {
                SendAltSpace(); Thread.Sleep(120); SendVirtualKey(0x1B); Thread.Sleep(80);
            }
            scenario.Checkpoints.Add(Sample(app, $"after-{plateau}-show-hide"));
        }

        File.WriteAllLines(Path.Combine(output, $"{label}.perf.log"), ReadCurrentEventLines());
        if (!app.HasExited) { app.Kill(true); app.WaitForExit(5000); }
        return scenario;
    }

    private static Checkpoint Sample(Process process, string label)
    {
        process.Refresh();
        return new Checkpoint { Label = label, PrivateBytesMb = process.PrivateMemorySize64 / 1048576d,
            WorkingSetMb = process.WorkingSet64 / 1048576d, Handles = process.HandleCount, Threads = process.Threads.Count };
    }

    private static void ApplyBudgets(RunResults results, string path)
    {
        string json = File.ReadAllText(path);
        using JsonDocument doc = JsonDocument.Parse(json);
        double p95 = Number(doc.RootElement, "hotkey_to_visible_ms", "p95_max");
        double queryMax = Number(doc.RootElement, "keystroke_to_results_ms", "max");
        double startupMax = Number(doc.RootElement, "startup_to_usable_index_ms", "max");
        double cpuMax = Number(doc.RootElement, "idle_cpu_percent_one_core", "max");
        bool privateGate = doc.RootElement.GetProperty("private_bytes_mb").GetProperty("gate").GetBoolean();
        foreach (var scenario in results.Scenarios)
        {
            double targetShowMs = Number(doc.RootElement, "hotkey_to_visible_ms", "target_ms");
            AddDistribution(results, scenario.Name + " hotkey immediate", scenario.ShowImmediateMs, "ms", p95, "p95", targetShowMs);
            if (scenario.ShowAfterIdleMs.Count != 0) AddDistribution(results, scenario.Name + " hotkey after idle", scenario.ShowAfterIdleMs, "ms", p95, "p95", targetShowMs);
            AddDistribution(results, scenario.Name + " query", scenario.QueryMs, "ms", queryMax, "max");
            AddMetric(results, $"{scenario.Name} query index coverage", scenario.IndexItems, $"items of {Number(doc.RootElement, "keystroke_to_results_ms", "index_items")}", Number(doc.RootElement, "keystroke_to_results_ms", "index_items"), false,
                scenario.IndexItems >= Number(doc.RootElement, "keystroke_to_results_ms", "index_items"));
            bool startupGate = scenario.Name == "snapshot"
                ? doc.RootElement.GetProperty("startup_to_usable_index_ms").GetProperty("snapshot_gate").GetBoolean()
                : doc.RootElement.GetProperty("startup_to_usable_index_ms").GetProperty("cold_gate").GetBoolean();
            AddMetric(results, $"{scenario.Name} usable index", scenario.StartupUsableMs ?? double.NaN, "ms", startupMax, startupGate);
            if (scenario.IdleCpuPercentOneCore is double cpu) AddMetric(results, $"{scenario.Name} idle CPU", cpu, "% one core", cpuMax, true);
            foreach (Checkpoint cp in scenario.Checkpoints)
            {
                double workingSetBudget = Number(doc.RootElement, "idle_working_set_mb", "max");
                string resources = $"MB working set; private {cp.PrivateBytesMb:0.##} MB; {cp.Handles} handles; {cp.Threads} threads";
                AddMetric(results, $"{scenario.Name} {cp.Label} memory/resources", cp.WorkingSetMb, resources, workingSetBudget, false, cp.WorkingSetMb < workingSetBudget);
            }
            AddMetric(results, $"{scenario.Name} harness injection misses", scenario.ShowInjectionMisses + scenario.QueryInjectionMisses, $"/ {scenario.ShowInjectionMisses + scenario.QueryInjectionMisses + scenario.ShowImmediateMs.Count + scenario.QueryMs.Count}", 0, false, scenario.ShowInjectionMisses + scenario.QueryInjectionMisses == 0);
        }
        AddMetric(results, "Notepad text injection misses", results.HarnessTextInjectionMisses, $"/ {results.HarnessInjectionTrials / 2}", 0, false, results.HarnessTextInjectionMisses == 0);
        AddMetric(results, "Notepad Alt+Space injection misses", results.HarnessHotkeyInjectionMisses, $"/ {results.HarnessInjectionTrials / 2}", 0, false, results.HarnessHotkeyInjectionMisses == 0);
        AddMetric(results, "Notepad harness injection misses", results.HarnessInjectionMisses, $"/ {results.HarnessInjectionTrials}", 0, false, results.HarnessInjectionMisses == 0);
    }

    private static double Number(JsonElement root, string metric, string field) => root.GetProperty(metric).GetProperty(field).GetDouble();
    private static void AddMetric(RunResults r, string name, double value, string unit, double budget, bool gate, bool? pass = null, double? target = null) =>
        r.Metrics.Add(new Metric { Name = name, Value = double.IsFinite(value) ? value : null, Unit = unit, Budget = budget, Target = target, Gate = gate, Pass = pass ?? (double.IsFinite(value) && value < budget) });
    private static void AddDistribution(RunResults r, string name, List<double> values, string unit, double budget, string gateStatistic, double? target = null)
    {
        double p50 = Percentile(values, .50), p95 = Percentile(values, .95), max = Percentile(values, 1);
        AddMetric(r, name + " p50", p50, unit, budget, gateStatistic == "p50", !double.IsNaN(p50) && p50 < budget, target);
        AddMetric(r, name + " p95", p95, unit, budget, gateStatistic == "p95", !double.IsNaN(p95) && p95 < budget, target);
        AddMetric(r, name + " max", max, unit, budget, gateStatistic == "max", !double.IsNaN(max) && max < budget, target);
    }
    private static double Percentile(List<double> values, double percentile)
    {
        if (values.Count == 0) return double.NaN;
        double[] sorted = values.Order().ToArray();
        return sorted[(int)Math.Ceiling(percentile * sorted.Length) - 1];
    }
    private static string Format(double value) => double.IsNaN(value) ? "n/a" : value.ToString("0.###", CultureInfo.InvariantCulture);
    private static void WriteSummary(RunResults results, string path)
    {
        double missRate = results.HarnessInjectionTrials == 0 ? 0 : results.HarnessInjectionMisses / (double)results.HarnessInjectionTrials;
        var lines = new List<string> { $"Mode: {results.Mode}; harness injection misses (Notepad): {results.HarnessInjectionMisses}/{results.HarnessInjectionTrials} ({missRate:P1}); text {results.HarnessTextInjectionMisses}/{results.HarnessInjectionTrials / 2}; Alt+Space {results.HarnessHotkeyInjectionMisses}/{results.HarnessInjectionTrials / 2}" };
        if (missRate > results.HarnessUnreliableAboveRate)
            lines.Add($"UNRELIABLE: harness injection miss rate exceeds {results.HarnessUnreliableAboveRate:P0}; treat app timings as inconclusive.");
        lines.Add($"Calibration diagnostics: focus misses {results.HarnessForegroundMisses}; clipboard read exceptions {results.HarnessClipboardReadErrors}; SendInput rejected calls {results.HarnessSendInputRejectedCalls}.");
        lines.AddRange(results.Metrics.Select(m => m.Value is null
            ? $"{m.Name}: {m.Unit}; budget informational; {(m.Pass ? "PASS" : "FAIL")} [info]"
            : $"{m.Name}: {Format(m.Value.Value)} {m.Unit}; budget {(m.Name.Contains("index coverage", StringComparison.Ordinal) ? $">={Format(m.Budget)} items" : m.Unit.Contains("informational", StringComparison.Ordinal) ? "informational" : m.Budget == 0 ? "0" : $"<{Format(m.Budget)}")}{(m.Target is double target ? $"; target {Format(target)} {m.Unit}" : "")}; {(m.Pass ? "PASS" : "FAIL")}{(m.Gate ? " [gate]" : " [info]")}"));
        lines.AddRange(results.Notes.Select(n => $"Note: {n}"));
        if (lines.Count > 40) lines = [.. lines.Take(39), "Additional metrics are in results.json."];
        File.WriteAllLines(path, lines);
    }

    private static bool HasEvent(string key) => EventCount(key) > 0;
    private static int EventCount(string key) => ReadEvents(key).Count;
    private static List<string[]> ReadEvents(string key)
    {
        return ReadCurrentEventLines().Where(l => l.Contains($"|{key}|", StringComparison.Ordinal)).Select(l => l.Split('|')).ToList();
    }
    private static List<string> ReadCurrentEventLines()
    {
        var lines = new List<string>();
        foreach (string path in new[] { PerfPath + ".old", PerfPath })
        {
            if (!File.Exists(path)) continue;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);
            while (reader.ReadLine() is { } line)
            {
                int separator = line.IndexOf('|');
                if (separator > 0 && DateTimeOffset.TryParse(line[..separator], CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out DateTimeOffset timestamp) && timestamp >= _eventSinceUtc)
                    lines.Add(line);
            }
        }
        return lines;
    }
    private static double EventValue(string key) => EventValueOrNull(key) ?? throw new InvalidOperationException($"Missing {key} performance event.");
    private static double? EventValueOrNull(string key) => ReadEvents(key).FirstOrDefault() is { Length: >= 3 } row && double.TryParse(row[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : null;
    private static double EventValueAt(string key, int index) => double.Parse(ReadEvents(key)[index][2], CultureInfo.InvariantCulture);
    private static int EventDetailInt(string key, string name)
    {
        string? detail = ReadEvents(key).FirstOrDefault()?[3];
        string? value = detail?.Split(';').FirstOrDefault(part => part.StartsWith(name + "=", StringComparison.Ordinal))?.Split('=', 2).Last();
        return int.TryParse(value, out int count) ? count : 0;
    }
    private static bool WaitUntil(Func<bool> predicate, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout) { if (predicate()) return true; Thread.Sleep(40); }
        return predicate();
    }
    private static bool SendAltSpace() => SendVirtualKey(0x12) & SendVirtualKey(0x20) & SendVirtualKey(0x20, true) & SendVirtualKey(0x12, true);
    private static bool SendCtrlA() => SendVirtualKey(0x11) & SendVirtualKey(0x41) & SendVirtualKey(0x41, true) & SendVirtualKey(0x11, true);
    private static bool SendCtrlC() => SendVirtualKey(0x11) & SendVirtualKey(0x43) & SendVirtualKey(0x43, true) & SendVirtualKey(0x11, true);
    private static bool SendCtrlS() => SendVirtualKey(0x11) & SendVirtualKey(0x53) & SendVirtualKey(0x53, true) & SendVirtualKey(0x11, true);
    private static bool SendCtrlW() => SendVirtualKey(0x11) & SendVirtualKey(0x57) & SendVirtualKey(0x57, true) & SendVirtualKey(0x11, true);
    private static bool SendVirtualKey(ushort vk, bool keyUp = false)
    {
        var input = new Input { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = vk, Flags = keyUp ? KeyUp : 0 } } };
        if (SendInput(1, [input], Marshal.SizeOf<Input>()) == 1) return true;
        Interlocked.Increment(ref _sendInputRejectedCalls);
        return false;
    }
    private static bool SendUnicode(string value)
    {
        bool accepted = true;
        foreach (char ch in value)
        {
            var down = new Input { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { Scan = ch, Flags = Unicode } } };
            var up = new Input { Type = InputKeyboard, Data = new InputUnion { Keyboard = new KeyboardInput { Scan = ch, Flags = Unicode | KeyUp } } };
            if (SendInput(2, [down, up], Marshal.SizeOf<Input>()) != 2)
            {
                Interlocked.Increment(ref _sendInputRejectedCalls);
                accepted = false;
            }
        }
        return accepted;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit, Size = 32)] private struct InputUnion { [FieldOffset(0)] public KeyboardInput Keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput { public ushort VirtualKey; public ushort Scan; public uint Flags; public uint Time; public nint ExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hwnd, int command);
    private static nint FindWindowContaining(string substring)
    {
        nint found = 0;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var title = new System.Text.StringBuilder(512);
            GetWindowText(hwnd, title, title.Capacity);
            if (title.ToString().Contains(substring, StringComparison.OrdinalIgnoreCase)) { found = hwnd; return false; }
            return true;
        }, 0);
        return found;
    }
    private delegate bool EnumWindowsCallback(nint hwnd, nint state);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, nint state);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint hwnd, System.Text.StringBuilder text, int maxCount);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint FindWindow(string? className, string? windowName);

    private sealed class Calibration(int textMisses, int hotkeyMisses, int foregroundMisses, int clipboardReadErrors, int inputRejectedCalls) { public int TextMisses { get; } = textMisses; public int HotkeyMisses { get; } = hotkeyMisses; public int ForegroundMisses { get; } = foregroundMisses; public int ClipboardReadErrors { get; } = clipboardReadErrors; public int InputRejectedCalls { get; } = inputRejectedCalls; }
    private sealed class RunResults { public string Mode { get; set; } = ""; public DateTimeOffset StartedUtc { get; set; } public DateTimeOffset FinishedUtc { get; set; } public int HarnessCalibrationAttempts { get; set; } public double HarnessUnreliableAboveRate { get; set; } public int HarnessInjectionTrials { get; set; } public int HarnessInjectionMisses { get; set; } public int HarnessTextInjectionMisses { get; set; } public int HarnessHotkeyInjectionMisses { get; set; } public int HarnessForegroundMisses { get; set; } public int HarnessClipboardReadErrors { get; set; } public int HarnessSendInputRejectedCalls { get; set; } public List<Scenario> Scenarios { get; } = []; public List<Metric> Metrics { get; } = []; public List<string> Notes { get; } = []; }
    private sealed class Scenario { public string Name { get; set; } = ""; public int ProcessId { get; set; } public int IndexItems { get; set; } public double? StartupUsableMs { get; set; } public double? StartupElapsedMs { get; set; } public double? IndexCompleteMs { get; set; } public double? SnapshotLoadMs { get; set; } public int ShowInjectionMisses { get; set; } public int QueryInjectionMisses { get; set; } public double? IdleCpuSeconds { get; set; } public double? IdleCpuPercentOneCore { get; set; } public List<double> ShowImmediateMs { get; } = []; public List<double> ShowAfterIdleMs { get; } = []; public List<double> QueryMs { get; } = []; public List<Checkpoint> Checkpoints { get; } = []; }
    private sealed class Checkpoint { public string Label { get; set; } = ""; public double PrivateBytesMb { get; set; } public double WorkingSetMb { get; set; } public int Handles { get; set; } public int Threads { get; set; } }
    private sealed class Metric { public string Name { get; set; } = ""; public double? Value { get; set; } public string Unit { get; set; } = ""; public double Budget { get; set; } public double? Target { get; set; } public bool Gate { get; set; } public bool Pass { get; set; } }
}
