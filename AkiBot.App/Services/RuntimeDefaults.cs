using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace AkiBot.App.Services
{
    // The runtime defaults the game needs, applied by the launcher itself (they used to live in a launch
    // script). A variable already set (by the settings or by the user's environment) always wins, as before.
    public static class RuntimeDefaults
    {
        public const string HostModule = "akibot_host.dll";
        public const string PresetFile = "u59-preset.json";

        public static void Apply(StringDictionary env, string runHybrid)
        {
            env["APS5_GPU_ENGINE"] = "kyty";
            // Keep commit-epoch fastpath off and buffer-range fixes on so cloth, blankets and bubbles
            // do not corrupt into stray polygons (issues #30, #35).
            env.Remove("KYTY_BARRIER_BATCH");
            // Dispatch overlap off (from the upstream port, AstroHybrid 1.2.13, its #154 #65): it lets two
            // consecutive compute dispatches run unordered, safe on the PS5 GPU but not on a PC GPU, where a
            // dispatch waiting on the previous one can take the whole card until Windows resets it (error 321)
            // and causes 300 ms freezes. Kyty turns it on by default. Sky Garden 2026-11-22 (RTX 5070): same
            // FPS (58.1-59.0); 1% low 29.7 / 30.2 off against 55.7 / 31.4 on, within the run-to-run noise.
            // AKIBOT_DISPATCH_OVERLAP=1 brings it back.
            env["KYTY_DISPATCH_OVERLAP"] = Get(env, "AKIBOT_DISPATCH_OVERLAP") == "1" ? "1" : "0";
            env.Remove("KYTY_WAVE64_WARPS");
            env["AKIBOT_COMMIT_EPOCH_FASTPATH"] = "0";
            env["KYTY_BUFFER_RANGE_FIXES"] = "1";

            // Hang watchdog (A/B 2026-11-07, OVERNIGHT.md): the preset's KYTY_HANG_WATCHDOG=auto turns it on for
            // RTX 50 GPUs, where it costs the command processor ~1.8 ms per frame. AKIBOT_HANG_WATCHDOG=1 brings it back.
            if (!Has(env, "KYTY_HANG_WATCHDOG") && Get(env, "AKIBOT_HANG_WATCHDOG") != "1")
                env["KYTY_HANG_WATCHDOG"] = "0";

            // Persistent shader and pipeline cache (2026-11-08): without the title ID Kyty's caches stay closed and
            // shaders compile during play (100-117 ms hitches). AKIBOT_SHADER_CACHE=0 turns it off.
            if (!Has(env, "AKIBOT_CACHE_TITLE_ID") && Get(env, "AKIBOT_SHADER_CACHE") != "0")
                env["AKIBOT_CACHE_TITLE_ID"] = "PPSA21567";

            // Shader list for the first start (2026-11-21): Build\RunHybrid\PipelineLists\PPSA21567.kyplst, made from
            // recorded 01.007 runs (offsets and hashes only, no game code). The dump calls itself PPSA21564 and the
            // hybrid renderer has no version, so the list is taken "cross" (each shader still matched by hash).
            // AKIBOT_SHADER_LIST=0 turns it off.
            if (Get(env, "AKIBOT_SHADER_LIST") != "0" && File.Exists(ShaderListPath(runHybrid)))
            {
                Default(env, "KYTY_PIPELINE_LIST", "1");
                Default(env, "KYTY_PIPELINE_LIST_CROSS", "1");
            }

            // Kyty engine preset (Build\RunHybrid\u59-preset.json).
            foreach (var kvp in LoadPreset(Path.Combine(runHybrid, PresetFile)))
                Default(env, kvp.Key, kvp.Value);

            // Prefer the soft-assert NonRT build for the command-buffer overflow path (issue #25).
            if (!Has(env, "AKIBOT_EXE"))
            {
                foreach (var exe in new[] { "AstroBot-NonRT-SoftAssert.exe", "AstroBot-NonRT.exe", "AstroBot.exe" })
                {
                    if (File.Exists(Path.Combine(runHybrid, exe)) || exe == "AstroBot.exe")
                    {
                        env["AKIBOT_EXE"] = exe;
                        break;
                    }
                }
            }

            env["KYTY_GPU_HOST_MODULE"] = HostModule;
            // The game reads L2/R2 only when scePadGetTriggerEffectState answers as a DualSense does (2026-11-07).
            Default(env, "APS5_PAD_TRIGGER_STATE", "1");
            // Older Astro Bot builds (PPSA21564 01.007.000, issue #27) mount their saves through the transfer call.
            Default(env, "APS5_SAVEDATA_TRANSFER_FALLBACK", "1");
            // Controller accelerometer and gyroscope (shake and tilt moves).
            Default(env, "AKIBOT_PAD_MOTION", "1");
            // Command processor at THREAD_PRIORITY_HIGHEST (A/B 2026-11-07).
            Default(env, "KYTY_CP_PRIORITY", "2");
            // Draw-prep helpers at the CP's priority with short spins (~1.2 ms less CP time per frame).
            if (Get(env, "AKIBOT_DRAW_PREP_TUNING") != "0")
            {
                Default(env, "KYTY_DRAW_PREP_PRIORITY", "2");
                Default(env, "KYTY_DRAW_PREP_SPIN_US", "10");
                Default(env, "KYTY_DRAW_PREP_COLD_SPIN_US", "5");
            }
            Default(env, "AKIBOT_PRIORITY", "abovenormal");
            Default(env, "KYTY_GPU_WIDTH", "1920");
            Default(env, "KYTY_GPU_HEIGHT", "1080");
        }

        public static string ShaderListPath(string runHybrid) =>
            Path.Combine(runHybrid, "PipelineLists", "PPSA21567.kyplst");

        public static void SetPriority(Process process, string? priority)
        {
            try
            {
                switch ((priority ?? "").ToLowerInvariant())
                {
                    case "normal": process.PriorityClass = ProcessPriorityClass.Normal; break;
                    case "high": process.PriorityClass = ProcessPriorityClass.High; break;
                    case "belownormal": process.PriorityClass = ProcessPriorityClass.BelowNormal; break;
                    default: process.PriorityClass = ProcessPriorityClass.AboveNormal; break;
                }
            }
            catch { }
        }

        private static Dictionary<string, string> LoadPreset(string file)
        {
            var values = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(file)) return values;
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                foreach (var p in doc.RootElement.EnumerateObject())
                    values[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();
            }
            catch { }
            return values;
        }

        private static bool Has(StringDictionary env, string key) => !string.IsNullOrEmpty(env[key]);

        private static string Get(StringDictionary env, string key) => env[key] ?? "";

        // "If not defined, set": an empty value leaves the variable unset, as the script did.
        private static void Default(StringDictionary env, string key, string value)
        {
            if (!Has(env, key) && !string.IsNullOrEmpty(value)) env[key] = value;
        }
    }
}
