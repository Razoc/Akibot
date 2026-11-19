using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AkiBot.App.Models;

namespace AkiBot.App.Services
{
    public class LauncherService
    {
        private readonly string _rootDirectory;
        private readonly SettingsManager _settingsManager;

        public LauncherService(string rootDirectory)
        {
            _rootDirectory = rootDirectory;
            _settingsManager = new SettingsManager(rootDirectory);
        }

        // Starts the game directly (no script): the settings' variables, then the runtime defaults the old
        // launch script applied, then AstroBot.exe in Build\RunHybrid with its output in Logs.
        public async Task<int> LaunchGameAsync(AppSettings settings, HardwareInfo? hardware = null)
        {
            var runHybrid = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            if (!File.Exists(Path.Combine(runHybrid, "AstroBot.exe")))
            {
                throw new FileNotFoundException("AstroBot.exe not found in RunHybrid", runHybrid);
            }

            settings.LastLaunched = DateTime.Now;
            _settingsManager.Save(settings);

            return await StartGameAsync(BuildEnvironmentVariables(settings, hardware), Array.Empty<string>(), hardware);
        }

        // AkiBot.exe --run [game arguments]: the game with the runtime defaults only, the caller's environment
        // and its arguments (measurement scripts: AKIBOT_* variables set beforehand, "-lvl <level>"...).
        public Task<int> RunWithDefaultsAsync(string[] gameArgs)
        {
            var runHybrid = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            if (!File.Exists(Path.Combine(runHybrid, "AstroBot.exe")))
            {
                throw new FileNotFoundException("AstroBot.exe not found in RunHybrid", runHybrid);
            }
            return StartGameAsync(new Dictionary<string, string>(), gameArgs);
        }

        private async Task<int> StartGameAsync(Dictionary<string, string> variables, string[] gameArgs, HardwareInfo? hardware = null)
        {
            var runHybrid = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            var psi = new ProcessStartInfo
            {
                WorkingDirectory = runHybrid,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var env = psi.EnvironmentVariables;
            foreach (var kvp in variables)
            {
                env[kvp.Key] = kvp.Value;
            }
            RuntimeDefaults.Apply(env, runHybrid);
            // One line per frame for akibot-summary.txt (FPS, 1% low, stutters); a path set beforehand wins.
            if (string.IsNullOrEmpty(env["KYTY_FRAME_TIMES_LOG"]))
                env["KYTY_FRAME_TIMES_LOG"] = SessionReport.FrameTimesPath(_rootDirectory);

            psi.FileName = Path.Combine(runHybrid, env["AKIBOT_EXE"]);
            var args = new List<string>();
            if (env["AKIBOT_SKIP_INTRO"] == "1") args.Add("-skipIntro");
            args.AddRange(gameArgs);
            psi.Arguments = string.Join(" ", args.ConvertAll(a => a.Length > 0 && a.IndexOfAny(new[] { ' ', '\t', '"' }) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\""));

            var logs = Path.Combine(_rootDirectory, "Logs");
            Directory.CreateDirectory(logs);
            // What the game is started with (AkiBot, AnyPS5 and Kyty variables), to compare runs when one fails.
            try
            {
                var launch = new List<string> { $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {psi.FileName} {psi.Arguments}" };
                foreach (string key in env.Keys)
                {
                    var upper = key.ToUpperInvariant();
                    if (upper.StartsWith("AKIBOT_") || upper.StartsWith("APS5_") || upper.StartsWith("ANYPS5_") || upper.StartsWith("KYTY_"))
                        launch.Add($"{upper}={env[key]}");
                }
                launch.Sort(1, launch.Count - 1, StringComparer.Ordinal);
                File.WriteAllLines(Path.Combine(logs, "akibot-launch.log"), launch);
            }
            catch { }
            using var outLog = new StreamWriter(Path.Combine(logs, "hybrid-run.out.log"), false);
            using var errLog = new StreamWriter(Path.Combine(logs, "hybrid-run.err.log"), false);
            using var report = new SessionReport(_rootDirectory, logs, env, $"{psi.FileName} {psi.Arguments}", hardware);
            using var process = new Process { StartInfo = psi };
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data == null) return;
                if (SessionReport.IsFileOpenTrace(e.Data)) { report.NoteFileOpen(); return; }
                lock (outLog) outLog.WriteLine(e.Data);
                report.Observe(e.Data, false);
            };
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data == null) return;
                lock (errLog) errLog.WriteLine(e.Data);
                report.Observe(e.Data, true);
            };

            // The game may take the foreground: started from RecompLauncher (or --play without a window), its
            // window otherwise opens behind and SDL ignores the controller and the Escape key.
            AllowSetForegroundWindow(ASFW_ANY);
            process.Start();
            RuntimeDefaults.SetPriority(process, env["AKIBOT_PRIORITY"]);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _ = Task.Run(() => BringGameToFront(process));

            // WaitForExit() without a timeout also waits for the end of both outputs.
            await Task.Run(() => process.WaitForExit());
            report.Finish(process.ExitCode);
            return process.ExitCode;
        }

        // Waits for the game's window (up to 2 minutes, the first start builds shaders) and puts it in front once.
        private static void BringGameToFront(Process process)
        {
            try
            {
                var until = DateTime.UtcNow.AddMinutes(2);
                while (DateTime.UtcNow < until)
                {
                    if (process.HasExited) return;
                    process.Refresh();
                    var window = process.MainWindowHandle;
                    if (window != IntPtr.Zero && IsWindowVisible(window))
                    {
                        if (GetForegroundWindow() == window) return;
                        // Windows only lets the foreground thread hand the focus over: join its input for the call.
                        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
                        var ownThread = GetCurrentThreadId();
                        var joined = foregroundThread != 0 && foregroundThread != ownThread && AttachThreadInput(ownThread, foregroundThread, true);
                        try
                        {
                            if (IsIconic(window)) ShowWindow(window, SW_RESTORE);
                            BringWindowToTop(window);
                            SetForegroundWindow(window);
                        }
                        finally
                        {
                            if (joined) AttachThreadInput(ownThread, foregroundThread, false);
                        }
                        return;
                    }
                    System.Threading.Thread.Sleep(250);
                }
            }
            catch { }
        }

        private const int ASFW_ANY = -1;
        private const int SW_RESTORE = 9;
        [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool join);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

        private Dictionary<string, string> BuildEnvironmentVariables(AppSettings settings, HardwareInfo? hardware)
        {
            var env = new Dictionary<string, string>();

            // AkiBot in-game menu (F1 / Escape): reads and saves this same settings file, in the launcher's language.
            env["AKIBOT_SETTINGS_FILE"] = Path.Combine(_rootDirectory, "Build", "akibot-settings.json");
            env["AKIBOT_LANG"] = Loc.Language;

            // Graphics card (issue #24): an explicit choice names the card for the AnyPS5 renderer.
            // kyty_gpu.dll prefers a discrete card over integrated graphics by
            // itself, so an automatic choice passes nothing and the renderer keeps that preference.
            var gpu = hardware?.SelectedGpu;
            if (hardware != null && hardware.SelectedGpuIndex >= 0 && gpu != null)
            {
                env["ANYPS5_GPU"] = GpuMatchToken(gpu.Name);
            }

            // Lighting/renderer selection
            // What this game build supports (GameSetup); no file = the original 01.019.000 install.
            var runHybrid = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            var features = GameFeatures.Load(runHybrid);
            // Prefer the soft-assert variant so a command-buffer overflow follows the
            // engine's failure path instead of crashing in command_buffer_writer (issue #25).
            var lightingExes = settings.Ultrawide
                ? new[] { "AstroBot-NonRT-Ultrawide-SoftAssert.exe", "AstroBot-Ultrawide.exe" }
                : new[] { "AstroBot-NonRT-SoftAssert.exe", "AstroBot.exe" };
            if (settings.Lighting >= 0 && settings.Lighting < lightingExes.Length)
            {
                var exe = lightingExes[settings.Lighting];
                if (settings.Lighting == 0 && !File.Exists(Path.Combine(runHybrid, exe)))
                    exe = settings.Ultrawide ? "AstroBot-NonRT-Ultrawide.exe" : "AstroBot-NonRT.exe";
                env["AKIBOT_EXE"] = File.Exists(Path.Combine(runHybrid, exe)) ? exe : "AstroBot.exe";
            }

            // FPS limit
            var fpsValues = new[] { "30", "35", "45", "50", "55", "60", "unlimited" };
            if (settings.Fps >= 0 && settings.Fps < fpsValues.Length)
            {
                env["AKIBOT_FPS_LIMIT"] = fpsValues[settings.Fps];
            }

            // Window size
            // Indices 0..3 = 16:9, indices 4..5 = 21:9 ultrawide (#58)
            var sizeValues = new[,] { { 1280, 720 }, { 1600, 900 }, { 1920, 1080 }, { 2560, 1440 }, { 2560, 1080 }, { 3440, 1440 } };
            if (settings.Size >= 0 && settings.Size < sizeValues.GetLength(0))
            {
                int width = sizeValues[settings.Size, 0];
                int height = sizeValues[settings.Size, 1];
                if (settings.Ultrawide && settings.Size < 4)
                {
                    width = settings.Size == 3 ? 3440 : 2560;
                    height = settings.Size == 3 ? 1440 : 1080;
                }
                env["KYTY_GPU_WIDTH"] = width.ToString();
                env["KYTY_GPU_HEIGHT"] = height.ToString();
            }

            // Fullscreen
            env["KYTY_GPU_FULLSCREEN"] = settings.Fullscreen ? "1" : "0";
            // Started from RecompLauncher's Big Picture mode (controller, couch): always full screen.
            if (Environment.GetEnvironmentVariable("RECOMPLAUNCHER_MODE") == "bigpicture")
                env["KYTY_GPU_FULLSCREEN"] = "1";

            // Game speed
            // Always real time, where the game version is known: it writes one global at a fixed address.
            env["AKIBOT_GAME_SPEED"] = features == null || features.RealTimeSpeed ? "1" : "0";
            if (features != null && features.GameStepAddress != 0)
                env["AKIBOT_GAME_STEP_ADDR"] = "0x" + features.GameStepAddress.ToString("x");
            var speedValues = new[] { "100", "95", "90", "85", "80", "75" };
            if (settings.Speed >= 0 && settings.Speed < speedValues.Length)
            {
                env["AKIBOT_GAME_SPEED_PERCENT"] = speedValues[settings.Speed];
            }

            // Performance overlay
            var overlayValues = new[] { "", "fps", "compact", "full" };
            if (settings.Overlay >= 0 && settings.Overlay < overlayValues.Length)
            {
                env["AKIBOT_OVERLAY"] = overlayValues[settings.Overlay];
            }

            // NVIDIA options, as AstroBotLauncher.cs: DLSS and frame generation need the CUDA address
            // window and the NVIDIA runtime files next to the game.
            if (settings.Upscale != 0 || settings.FrameGenMode != 0)
            {
                env["AKIBOT_CUDA_HOLE"] = "1";
                env["AKIBOT_DLSS_PATH"] = Path.Combine(_rootDirectory, "Build", "RunHybrid");
            }
            if (settings.Upscale == 6)
            {
                // AMD FSR 3.1 (AkiBot): same pass as DLSS, amd_fidelityfx_vk.dll next to the game.
                env["AKIBOT_FSR"] = "1";
            }
            else if (settings.Upscale != 0)
            {
                env["AKIBOT_DLSS"] = "1";
                // Upscale: 1 DLSS, 5 DLAA. DLSS runs at the game's own resolutions, so the old presets
                // (2 Balanced, 3 Performance, 4 Ultra Performance) gained nothing: they run as DLSS.
                env["AKIBOT_DLSS_QUALITY"] = settings.Upscale == 5 ? "dlaa" : "quality";
            }

            // Frame generation plan (AstroBotLauncher.FrameGenPlan): the game is never capped below 30.
            // Auto with a target starts at 2x with the game capped at half the target and lets the game
            // raise the multiplier; fixed modes cap the game at target / multiplier; no lock = uncapped.
            if (settings.FrameGenMode == 5)
            {
                // AMD FSR 3 frame generation (AkiBot): always 1 generated frame, the game at half the target.
                var fsrTargets = new[] { 0, 50, 60, 90, 120, 144, 165 };
                int fsrGoal = settings.FrameGenTarget >= 0 && settings.FrameGenTarget < fsrTargets.Length ? fsrTargets[settings.FrameGenTarget] : 60;
                env["AKIBOT_FSRFG"] = "1";
                env["AKIBOT_FPS_LIMIT"] = fsrGoal == 0 ? "unlimited" : Math.Max(30, fsrGoal / 2).ToString();
            }
            else if (settings.FrameGenMode != 0)
            {
                var targetValues = new[] { 0, 50, 60, 90, 120, 144, 165 };
                int goal = settings.FrameGenTarget >= 0 && settings.FrameGenTarget < targetValues.Length
                    ? targetValues[settings.FrameGenTarget] : 60;
                int mult = settings.FrameGenMode == 1 ? 2 : Math.Min(Math.Max(settings.FrameGenMode, 2), 4);
                int limit = goal == 0 ? 0 : Math.Max(30, goal / mult);
                env["AKIBOT_DLSSG"] = "1";
                env["AKIBOT_DLSSG_FRAMES"] = (mult - 1).ToString();
                if (settings.FrameGenMode == 1 && goal != 0)
                {
                    env["AKIBOT_DLSSG_TARGET"] = goal.ToString();
                    env["AKIBOT_DLSSG_MAX_FRAMES"] = "3";
                }
                env["AKIBOT_FPS_LIMIT"] = limit == 0 ? "unlimited" : limit.ToString();
            }

            // NVIDIA Reflex
            if (settings.LowLatency)
            {
                env["AKIBOT_LOW_LATENCY"] = "1";
            }

            // Controller settings
            var padValues = new[] { "auto", "ps", "xbox", "switch", "generic" };
            if (settings.Controller >= 0 && settings.Controller < padValues.Length)
            {
                env["AKIBOT_PAD_PREFER"] = padValues[settings.Controller];
            }

            // Vibration strength in percent (0-200); the Switch boost follows it instead of being a flat
            // 96 of 255, which made low strengths louder than the setting itself (#36).
            int rumblePercent = settings.EffectiveRumblePercent;
            env["AKIBOT_RUMBLE_SCALE"] = rumblePercent.ToString();
            env["AKIBOT_RUMBLE_SWITCH_FLOOR"] = RumbleSetting.SwitchFloor(rumblePercent, settings.SwitchBoost).ToString();

            // Wireless DualSense features (Bluetooth HID audio/haptics) - issue #34
            env["AKIBOT_DS_WIRELESS_SPEAKER"] = settings.DualSenseWirelessSpeaker ? "1" : "0";
            env["AKIBOT_DS_WIRELESS_HAPTICS"] = settings.DualSenseWirelessHaptics ? "1" : "0";

            // Window title
            env["AKIBOT_WINDOW_TITLE"] = "Astro Bot";

            // Game language (AnyPS5 console language number); Auto maps the Windows display language.
            int language = settings.Language >= 0 && settings.Language <= 30 ? settings.Language : WindowsLanguage();
            env["ANYPS5_LANGUAGE"] = language.ToString();

            // VSync: Vulkan FIFO presentation (the default is immediate presentation; skipped when DLSS Frame Generation is active).
            if (settings.VSync && settings.FrameGenMode == 0) env["KYTY_GPU_PRESENT_MODE"] = "Fifo";

            // Advanced page: variables RuntimeDefaults reads (it keeps its defaults when unset).
            if (settings.HangWatchdog) env["AKIBOT_HANG_WATCHDOG"] = "1";
            if (!settings.ShaderCache) env["AKIBOT_SHADER_CACHE"] = "0";
            if (!settings.DrawPrepTuning) env["AKIBOT_DRAW_PREP_TUNING"] = "0";
            if (settings.Priority == "normal" || settings.Priority == "abovenormal" || settings.Priority == "high")
                env["AKIBOT_PRIORITY"] = settings.Priority;
            if (settings.CpPriority == 1 || settings.CpPriority == 2) env["KYTY_CP_PRIORITY"] = settings.CpPriority.ToString();
            env["AKIBOT_PAD_MOTION"] = settings.MotionControls ? "1" : "0";
            if (!settings.TriggerState) env["APS5_PAD_TRIGGER_STATE"] = "0";
            if (settings.SkipIntro) env["AKIBOT_SKIP_INTRO"] = "1";

            // Cloth/blanket/bubble stability (#30, #35): keep commit epoch fast path off and enable buffer range fixes.
            env["AKIBOT_COMMIT_EPOCH_FASTPATH"] = "0";
            env["KYTY_BUFFER_RANGE_FIXES"] = "1";
            env["APS5_SAVEDATA_TRANSFER_FALLBACK"] = "1";

            // AkiBot: on game versions other than the tested 01.019.000 (PPSA21564 01.007.000 confirmed), the
            // guest write watch corrupts what the background loader reads at the end of the Crash Site tutorial:
            // black screen or OdxAsyncLoader access violation (#54 family). Turning it off lets the game go on.
            // An APS5_NO_WRITE_WATCH already set in the environment (0 or 1) wins.
            bool untestedVersion = features != null && !string.IsNullOrEmpty(features.Version) && features.Version != GameFeatures.TestedVersion;
            if (untestedVersion && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APS5_NO_WRITE_WATCH")))
                env["APS5_NO_WRITE_WATCH"] = "1";

            return env;
        }

        // The renderer selects a card by name (ANYPS5_GPU, a case-insensitive substring of the Vulkan
        // device name). The model token is used instead of the full Windows name so it still matches
        // the shorter name Vulkan reports ("NVIDIA GeForce RTX 4050 Laptop GPU" -> "RTX 4050").
        public static string GpuMatchToken(string name)
        {
            var match = Regex.Match(name, @"(?:RTX|GTX)\s*\d{3,4}(?:\s*(?:Ti|SUPER))?", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value;
            match = Regex.Match(name, @"Arc\s*[AB]?\d{3}", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value;
            match = Regex.Match(name, @"(?:UHD|Iris(?:\s*Xe)?|HD)\s*Graphics(?:\s*\d{3})?", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value;
            match = Regex.Match(name, @"RX\s*\d{4}(?:\s*XT)?", RegexOptions.IgnoreCase);
            if (match.Success) return match.Value;
            return name;
        }

        // The console language number for the Windows display language; English (United States) when unknown.
        public static int WindowsLanguage()
        {
            var culture = System.Globalization.CultureInfo.CurrentUICulture;
            string name = culture.Name, two = culture.TwoLetterISOLanguageName;
            switch (name)
            {
                case "pt-BR": return 17;
                case "en-GB": return 18;
                case "fr-CA": return 22;
                case "zh-TW": case "zh-HK": case "zh-MO": return 10;
            }
            if (two == "es") return name == "es-ES" ? 3 : 20;
            if (two == "zh") return 11;
            switch (two)
            {
                case "ja": return 0;
                case "en": return 1;
                case "fr": return 2;
                case "de": return 4;
                case "it": return 5;
                case "nl": return 6;
                case "pt": return 7;
                case "ru": return 8;
                case "ko": return 9;
                case "fi": return 12;
                case "sv": return 13;
                case "da": return 14;
                case "nb": case "nn": case "no": return 15;
                case "pl": return 16;
                case "tr": return 19;
                case "ar": return 21;
                case "cs": return 23;
                case "hu": return 24;
                case "el": return 25;
                case "ro": return 26;
                case "th": return 27;
                case "vi": return 28;
                case "id": return 29;
                case "uk": return 30;
                default: return 1;
            }
        }

        public bool IsGameRunning()
        {
            var processes = Process.GetProcessesByName("AstroBot");
            return processes.Length > 0;
        }

        public void KillGameProcesses()
        {
            foreach (var proc in Process.GetProcessesByName("AstroBot"))
            {
                try { proc.Kill(); } catch { }
            }
        }
    }
}
