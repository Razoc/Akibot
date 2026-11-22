using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AkiBot.App.Models;
using Microsoft.Win32;

namespace AkiBot.App.Services
{
    // Logs\akibot-summary.txt: one short file that says what a player's session looked like, so a report sent
    // through RecompLauncher (which keeps only the end of each long log) is enough to see the PC, the settings,
    // the frame rate, the stutters and the errors. Rewritten every 30 seconds and when the game closes, so it
    // is there even when the launcher is stopped with the game.
    public sealed class SessionReport : IDisposable
    {
        // A .log: a RecompLauncher review attaches one .log of the Logs folder, never a .txt.
        public const string FileName = "akibot-summary.log";
        private const string OldFileName = "akibot-summary.txt";
        public const string FrameTimesFile = "akibot-frame-times.csv";

        private const int MaxProblems = 200;
        private const int ShownProblems = 40;
        private const double PauseMs = 1000;       // longer frames are loading screens, menus or a hidden window
        private const double StutterMs = 50;

        private readonly string _summaryPath;
        private readonly string? _frameTimesPath;
        private readonly string _rootDirectory;
        private readonly Dictionary<string, string> _settings;
        private readonly string _commandLine;
        private readonly DateTime _started = DateTime.Now;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly object _lock = new();
        private readonly Task<HardwareInfo?> _hardware;
        private readonly Timer _timer;

        private readonly List<string> _startup = new();
        private readonly List<(double Seconds, string Level)> _levels = new();
        private readonly List<(string Name, double Seconds)> _loads = new();
        private readonly Dictionary<string, Problem> _problems = new();
        private readonly Queue<string> _lastOut = new();
        private readonly Queue<string> _lastErr = new();
        private readonly List<string> _crash = new();
        private int _fileOpens, _dlssCreated, _otherProblems, _missingFiles, _crashLinesLeft;
        private int? _exitCode;

        private sealed class Problem
        {
            public string Example = "";
            public int Count;
            public double FirstSeconds;
            public bool FromErr;
        }

        // Startup lines worth keeping: the card, its driver and memory, the Windows setup, upscaler start.
        private static readonly string[] StartupPrefixes =
        {
            "Kyty GPU:", "Kyty GPU capabilities: VRAM", "Kyty platform:", "Kyty V-cache placement", "Kyty CPU placement",
            "DLSS: NGX", "FSR", "Frame generation", "DLSS-G", "Vulkan pipeline cache: load", "Program cache: loaded",
            "Texture sparse residency"
        };

        private static readonly Regex ProblemWords = new(
            @"\b(error|errors|failed|failure|fail|exception|assert|assertion|fatal|crash|crashed|access violation|not implemented|unhandled|abort|aborted|timeout|timed out|hang|hung|device lost|lost device|out of memory)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        // "0 failed", "0 compiles failed": counters with nothing in them.
        private static readonly Regex ZeroCounters = new(@"\b0 (?:[\w-]+ ){0,3}(?:failed|errors?|failures?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Numbers = new(@"0x[0-9a-fA-F]+|\d+(\.\d+)?", RegexOptions.Compiled);
        private static readonly Regex LoadLine = new(@"^GAME: LoadLevelResources: (.+?) : ([\d.]+) sec", RegexOptions.Compiled);

        public SessionReport(string rootDirectory, string logsDirectory, StringDictionary env, string commandLine, HardwareInfo? hardware)
        {
            _rootDirectory = rootDirectory;
            _summaryPath = Path.Combine(logsDirectory, FileName);
            try { File.Delete(Path.Combine(logsDirectory, OldFileName)); } catch { }
            _commandLine = commandLine;
            _settings = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in env.Keys)
            {
                var upper = key.ToUpperInvariant();
                if (upper.StartsWith("AKIBOT_") || upper.StartsWith("APS5_") || upper.StartsWith("ANYPS5_") || upper.StartsWith("KYTY_"))
                    _settings[upper] = env[key] ?? "";
            }
            _settings.TryGetValue("KYTY_FRAME_TIMES_LOG", out var frames);
            _frameTimesPath = string.IsNullOrEmpty(frames) ? null : frames;
            // The hardware scan uses WMI (about a second): never in the way of the game's start.
            _hardware = hardware != null
                ? Task.FromResult<HardwareInfo?>(hardware)
                : Task.Run<HardwareInfo?>(() => { try { return new HardwareDetector().DetectHardware(); } catch { return null; } });
            Write();
            _timer = new Timer(_ => Write(), null, 30000, 30000);
        }

        // Where the game writes one line per frame; outside Logs, so a report never carries it.
        public static string FrameTimesPath(string rootDirectory) => Path.Combine(rootDirectory, "Build", FrameTimesFile);

        // The trace of every file the game opens: thousands of lines a minute, it pushed everything else out
        // of the reports. Counted, not written (failed opens are kept: they go to the error output).
        public static bool IsFileOpenTrace(string line) =>
            line.StartsWith("[src/Open.cpp", StringComparison.Ordinal) && line.Contains("sceKernelOpen]") && !line.Contains("failed");

        public void NoteFileOpen()
        {
            Interlocked.Increment(ref _fileOpens);
        }

        public void Observe(string line, bool fromErr)
        {
            var seconds = _clock.Elapsed.TotalSeconds;
            lock (_lock)
            {
                var last = fromErr ? _lastErr : _lastOut;
                last.Enqueue(Trim(line, 300));
                if (last.Count > 20) last.Dequeue();

                if (line.StartsWith("GAME: Level has started: ", StringComparison.Ordinal))
                {
                    var level = line.Substring("GAME: Level has started: ".Length).Trim();
                    if (level.Length > 0) _levels.Add((seconds, level));
                    return;
                }
                var load = LoadLine.Match(line);
                if (load.Success)
                {
                    if (double.TryParse(load.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
                        _loads.Add((load.Groups[1].Value, s));
                    return;
                }
                if (line.StartsWith("DLSS: Super Resolution feature created", StringComparison.Ordinal))
                {
                    _dlssCreated++;
                    return;
                }
                if (_startup.Count < 40 && line.StartsWith("[src/HostCpu.cpp", StringComparison.Ordinal) && line.Contains("host CPU"))
                {
                    var cpu = line.Substring(line.IndexOf("host CPU", StringComparison.Ordinal));
                    var colon = cpu.IndexOf(':');
                    _startup.Add(colon > 0 ? cpu.Substring(0, colon) : Trim(cpu, 200));
                    return;
                }
                if (_startup.Count < 40 && StartupPrefixes.Any(p => line.StartsWith(p, StringComparison.Ordinal)) && !_startup.Contains(Trim(line, 300)))
                {
                    _startup.Add(Trim(line, 300));
                    if (!line.Contains("fail")) return;
                }

                // The crash report: the exception, where it happened and what was read or written.
                if (line.StartsWith("FATAL:", StringComparison.Ordinal)) _crashLinesLeft = 4;
                if (_crashLinesLeft > 0 && _crash.Count < 12)
                {
                    _crashLinesLeft--;
                    _crash.Add(Trim(line.Trim(), 200));
                }

                // Files the game only tries (optional data, fonts it does not ship): normal, counted.
                if (line.Contains("open failed") && line.Contains("result=0x80020002"))
                {
                    _missingFiles++;
                    return;
                }
                // A shader rewrite that falls back to the original: harmless.
                if (line.Contains("the original is used")) return;
                if (!ProblemWords.IsMatch(ZeroCounters.Replace(line, ""))) return;
                var key = Numbers.Replace(Trim(line, 200), "#");
                if (_problems.TryGetValue(key, out var known))
                {
                    known.Count++;
                }
                else if (_problems.Count < MaxProblems)
                {
                    _problems[key] = new Problem { Example = Trim(line, 300), Count = 1, FirstSeconds = seconds, FromErr = fromErr };
                }
                else
                {
                    _otherProblems++;
                }
            }
        }

        // alsoAppendTo: a log that gets the final summary at its end first (see AppendTo); the summary file
        // is written last, so it is the newest .log of Logs.
        public void Finish(int exitCode, string? alsoAppendTo = null)
        {
            _timer.Dispose();
            lock (_lock) _exitCode = exitCode;
            if (alsoAppendTo != null) AppendTo(alsoAppendTo);
            Write();
        }

        // Copies the final summary to the end of another log (hybrid-run.err.log), so whichever .log a
        // RecompLauncher review attaches carries it; at the end, as the launcher keeps the end of long logs.
        private void AppendTo(string logPath)
        {
            try
            {
                string text;
                lock (_lock) text = Build();
                File.AppendAllText(logPath, Environment.NewLine + text, new UTF8Encoding(false));
            }
            catch { }
        }

        public void Dispose() => _timer.Dispose();

        private static string Trim(string text, int max) => text.Length <= max ? text : text.Substring(0, max) + "…";

        private void Write()
        {
            try
            {
                string text;
                lock (_lock) text = Build();
                var temp = _summaryPath + ".tmp";
                File.WriteAllText(temp, text, new UTF8Encoding(false));
                if (File.Exists(_summaryPath)) File.Replace(temp, _summaryPath, null);
                else File.Move(temp, _summaryPath);
            }
            catch { }
        }

        private string Build()
        {
            var sb = new StringBuilder();
            var elapsed = _clock.Elapsed;
            sb.AppendLine("AkiBot session summary");
            sb.AppendLine("======================");
            sb.AppendLine($"Started      {_started:yyyy-MM-dd HH:mm:ss}, ran {Duration(elapsed.TotalSeconds)}");
            sb.AppendLine($"Result       {ExitText()}");
            sb.AppendLine($"AkiBot       {AkiBotInfo.CurrentVersionText}, runtime {Safe(() => AkiBotInfo.InstalledRuntimeVersion(_rootDirectory))}");
            var features = Safe(() => GameFeatures.Load(Path.Combine(_rootDirectory, "Build", "RunHybrid")));
            sb.AppendLine($"Game         {(features == null ? "unknown (no akibot-game.json)" : $"{features.TitleId} {features.Version}")}");
            var launcher = Environment.GetEnvironmentVariable("RECOMPLAUNCHER") == "1"
                ? $"RecompLauncher ({Environment.GetEnvironmentVariable("RECOMPLAUNCHER_MODE") ?? "desktop"})"
                : "AkiBot";
            sb.AppendLine($"Started from {launcher}");
            if (_crash.Count > 0)
            {
                sb.AppendLine("Crash");
                foreach (var line in _crash) sb.AppendLine("  " + line);
                var level = _levels.Count > 0 ? _levels[_levels.Count - 1].Level : "";
                if (level.Length > 0) sb.AppendLine($"  in level {level}");
            }
            sb.AppendLine();

            sb.AppendLine("PC");
            sb.AppendLine("--");
            sb.AppendLine($"Windows      {WindowsText()}");
            var hw = _hardware.IsCompleted ? _hardware.Result : null;
            if (hw != null)
            {
                sb.AppendLine($"CPU          {hw.CpuName} ({hw.CpuThreads} threads)");
                sb.AppendLine($"Memory       {hw.RamGb} GB");
                foreach (var gpu in hw.Gpus)
                    sb.AppendLine($"GPU          {gpu.Label}, driver {(gpu.DriverVersion.Length > 0 ? gpu.DriverVersion : "unknown")}");
                sb.AppendLine($"Screen       {hw.ScreenWidth}x{hw.ScreenHeight} at {hw.RefreshRate} Hz");
            }
            else
            {
                sb.AppendLine($"CPU          {Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")} ({Environment.ProcessorCount} threads)");
            }
            foreach (var line in _startup) sb.AppendLine($"  {line}");
            sb.AppendLine();

            sb.AppendLine("Settings");
            sb.AppendLine("--------");
            sb.AppendLine($"Program      {Get("AKIBOT_EXE")}");
            sb.AppendLine($"Picture      {Get("KYTY_GPU_WIDTH")}x{Get("KYTY_GPU_HEIGHT")}, {(Get("KYTY_GPU_FULLSCREEN") == "1" ? "fullscreen" : "windowed")}, present {Get("KYTY_GPU_PRESENT_MODE", "immediate")}");
            sb.AppendLine($"Upscaling    {UpscaleText()}");
            sb.AppendLine($"Frame gen    {FrameGenText()}");
            sb.AppendLine($"FPS limit    {Get("AKIBOT_FPS_LIMIT", "default")}");
            sb.AppendLine($"Graphics card choice {Get("ANYPS5_GPU", "automatic")}");
            sb.AppendLine("(every variable the game got: akibot-launch.log)");
            sb.AppendLine();

            AppendFrames(sb);

            sb.AppendLine("Loading");
            sb.AppendLine("-------");
            if (_levels.Count == 0) sb.AppendLine("No level started yet.");
            else sb.AppendLine("Levels       " + string.Join(", ", _levels.Select(l => $"{l.Level} ({Clock(l.Seconds)})")));
            foreach (var load in _loads.OrderByDescending(l => l.Seconds).Take(5))
                sb.AppendLine($"Slow load    {load.Name}: {load.Seconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
            if (_dlssCreated > 0) sb.AppendLine($"DLSS set up  {_dlssCreated} time(s) (each size change rebuilds it)");
            sb.AppendLine($"Files opened {_fileOpens} (not written to hybrid-run.out.log), {_missingFiles} optional file(s) not found");
            sb.AppendLine();

            sb.AppendLine("Errors and warnings");
            sb.AppendLine("-------------------");
            if (_problems.Count == 0) sb.AppendLine("None.");
            foreach (var p in _problems.Values.OrderByDescending(p => p.Count).ThenBy(p => p.FirstSeconds).Take(ShownProblems))
                sb.AppendLine($"{p.Count,6}x  first at {Clock(p.FirstSeconds)}{(p.FromErr ? " [err]" : "")}  {p.Example}");
            if (_problems.Count > ShownProblems) sb.AppendLine($"... {_problems.Count - ShownProblems} more kinds");
            if (_otherProblems > 0) sb.AppendLine($"... and {_otherProblems} lines of other kinds");

            if (_exitCode.HasValue && _exitCode.Value != 0)
            {
                sb.AppendLine();
                sb.AppendLine("Last lines before the game closed");
                sb.AppendLine("---------------------------------");
                foreach (var line in _lastOut) sb.AppendLine("out  " + line);
                foreach (var line in _lastErr) sb.AppendLine("err  " + line);
            }
            return sb.ToString();
        }

        private void AppendFrames(StringBuilder sb)
        {
            sb.AppendLine("Frame rate");
            sb.AppendLine("----------");
            var frames = ReadFrames();
            if (frames.Count < 30)
            {
                sb.AppendLine(_frameTimesPath == null ? "Not recorded (KYTY_FRAME_TIMES_LOG off)." : "Not enough frames yet.");
                sb.AppendLine();
                return;
            }
            var played = frames.Where(f => f.Ms <= PauseMs).ToList();
            var pauses = frames.Count - played.Count;
            sb.AppendLine("All levels   " + Stats(played));
            if (pauses > 0) sb.AppendLine($"Pauses       {pauses} frame(s) over 1 s (loading screens, menus or the window in the background), left out");

            // Per level: frames between one level's start and the next (the game's clock starts with the process).
            if (_levels.Count > 0)
            {
                for (int i = 0; i < _levels.Count; i++)
                {
                    double from = _levels[i].Seconds, to = i + 1 < _levels.Count ? _levels[i + 1].Seconds : double.MaxValue;
                    var part = played.Where(f => f.Seconds >= from && f.Seconds < to).ToList();
                    if (part.Count >= 30) sb.AppendLine($"  {Trim(_levels[i].Level, 28),-28} {Stats(part)}");
                }
            }

            // Minute by minute, to see a drop that comes with time (memory, heat, a cache filling up).
            var minutes = played.GroupBy(f => (int)(f.Seconds / 60)).OrderBy(g => g.Key).ToList();
            if (minutes.Count > 1)
            {
                sb.AppendLine("Per minute   (average / 1% low)");
                var rows = minutes.Select(g => $"{g.Key + 1,3}: {Fps(g.Average(f => f.Ms)),5} / {Fps(Percentile(g.Select(f => f.Ms).ToList(), 0.99)),5}").ToList();
                if (rows.Count > 60) rows = rows.Take(30).Concat(new[] { "  ..." }).Concat(rows.Skip(rows.Count - 30)).ToList();
                for (int i = 0; i < rows.Count; i += 4) sb.AppendLine("  " + string.Join("    ", rows.Skip(i).Take(4)));
            }

            var stutters = played.Where(f => f.Ms > StutterMs).OrderByDescending(f => f.Ms).ToList();
            if (stutters.Count > 0)
            {
                sb.AppendLine($"Worst stutters (of {stutters.Count} frames over {StutterMs:0} ms)");
                foreach (var f in stutters.Take(15).OrderBy(f => f.Seconds))
                    sb.AppendLine($"  {Clock(f.Seconds)}  {f.Ms,6:0} ms  {LevelAt(f.Seconds)}");
            }
            sb.AppendLine();
        }

        private string Stats(List<(double Seconds, double Ms)> frames)
        {
            var ms = frames.Select(f => f.Ms).ToList();
            double total = ms.Sum();
            int over50 = ms.Count(m => m > 50), over100 = ms.Count(m => m > 100), over250 = ms.Count(m => m > 250);
            return $"{Fps(total / ms.Count)} FPS average, 1% low {Fps(Percentile(ms, 0.99))}, 0.1% low {Fps(Percentile(ms, 0.999))}, " +
                   $"{ms.Count} frames; stutters >50 ms {over50}, >100 ms {over100}, >250 ms {over250}";
        }

        private List<(double Seconds, double Ms)> ReadFrames()
        {
            var frames = new List<(double, double)>();
            if (_frameTimesPath == null) return frames;
            try
            {
                using var stream = new FileStream(_frameTimesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var comma = line.IndexOf(',');
                    if (comma <= 0) continue;
                    if (long.TryParse(line.Substring(0, comma), out var t) && long.TryParse(line.Substring(comma + 1), out var interval))
                        frames.Add((t / 1e6, interval / 1000.0));
                }
            }
            catch { }
            return frames;
        }

        private static double Percentile(List<double> values, double p)
        {
            if (values.Count == 0) return 0;
            var sorted = values.OrderBy(v => v).ToList();
            return sorted[Math.Min(sorted.Count - 1, (int)Math.Ceiling(p * sorted.Count) - 1)];
        }

        private static string Fps(double ms) => ms <= 0 ? "-" : (1000.0 / ms).ToString("0.0", CultureInfo.InvariantCulture);

        private string LevelAt(double seconds)
        {
            string level = "";
            foreach (var l in _levels) { if (l.Seconds <= seconds) level = l.Level; else break; }
            return level;
        }

        private static string Clock(double seconds)
        {
            var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"mm\:ss");
        }

        private static string Duration(double seconds) => Clock(seconds);

        private string Get(string key, string fallback = "?") =>
            _settings.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value) ? value : fallback;

        private string UpscaleText()
        {
            if (Get("AKIBOT_FSR", "0") == "1") return "AMD FSR 3.1";
            if (Get("AKIBOT_DLSS", "0") == "1") return $"DLSS {Get("AKIBOT_DLSS_QUALITY", "quality")}";
            return "game's own";
        }

        private string FrameGenText()
        {
            if (Get("AKIBOT_FSRFG", "0") == "1") return "AMD FSR 3 2x";
            if (Get("AKIBOT_DLSSG", "0") == "1")
            {
                int.TryParse(Get("AKIBOT_DLSSG_FRAMES", "1"), out var generated);
                var target = Get("AKIBOT_DLSSG_TARGET", "");
                return $"NVIDIA {generated + 1}x{(target.Length > 0 ? $" (auto, target {target} FPS)" : "")}";
            }
            return "off";
        }

        private string ExitText()
        {
            if (!_exitCode.HasValue) return "still running (or the launcher was stopped with the game)";
            var code = _exitCode.Value;
            var hex = "0x" + unchecked((uint)code).ToString("X8");
            return code switch
            {
                0 => "closed normally (exit code 0)",
                unchecked((int)0xC0000005) => $"crashed: access violation ({hex})",
                unchecked((int)0xC0000409) => $"crashed: the program stopped itself (fail fast, {hex})",
                unchecked((int)0xC00000FD) => $"crashed: stack overflow ({hex})",
                unchecked((int)0xC0000374) => $"crashed: memory corruption ({hex})",
                unchecked((int)0xC0000135) => $"could not start: a DLL is missing ({hex})",
                unchecked((int)0xC0000142) => $"could not start: a DLL failed to load ({hex})",
                unchecked((int)0x80000003) => $"crashed: breakpoint ({hex})",
                unchecked((int)0xC000001D) => $"crashed: illegal instruction, the CPU lacks an instruction ({hex})",
                -1 => "closed by force or killed (exit code -1)",
                _ => $"closed with exit code {code} ({hex})"
            };
        }

        private static string WindowsText()
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                var build = key?.GetValue("CurrentBuildNumber")?.ToString() ?? "";
                var display = key?.GetValue("DisplayVersion")?.ToString() ?? "";
                var ubr = key?.GetValue("UBR")?.ToString() ?? "";
                var name = int.TryParse(build, out var b) && b >= 22000 ? "Windows 11" : "Windows 10";
                return $"{name} {display} (build {build}{(ubr.Length > 0 ? "." + ubr : "")})";
            }
            catch { return Environment.OSVersion.VersionString; }
        }

        private static T? Safe<T>(Func<T> read)
        {
            try { return read(); } catch { return default; }
        }
    }
}
