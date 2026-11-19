using System;
using System.IO;

using AkiBot.App.Models;

namespace AkiBot.App.Services
{
    // The settings AkiBot picks for a PC: a steady game first (60 FPS where the processor can hold it),
    // frame generation never on by default. Used by "Best settings for this PC" and, once, by the first
    // Play after a setup (ApplyBestOnStart: setup wizard, RecompLauncher's Easy Setup).
    public static class RecommendedSettings
    {
        private static readonly int[] TargetValues = { 0, 50, 60, 90, 120, 144, 165 };

        public static void Apply(AppSettings settings, HardwareInfo pc)
        {
            var gpu = pc.SelectedGpu;
            bool integrated = gpu != null && gpu.IsIntegrated && !gpu.IsDiscrete;

            // Picture size: the largest 16:9 size the screen holds (1280x720 on integrated graphics).
            var widths = new[] { 1280, 1600, 1920, 2560 };
            var heights = new[] { 720, 900, 1080, 1440 };
            int size = 0;
            for (int i = 0; i < widths.Length; i++)
                if (widths[i] <= pc.ScreenWidth && heights[i] <= pc.ScreenHeight)
                    size = i;
            settings.Size = integrated ? 0 : size;
            settings.Ultrawide = false;
            settings.Fullscreen = true;

            // The game is held back by the processor (one emulator thread): 60 FPS where there are threads to
            // spare for it, else 30, the steadiest. Measured: 16 threads hold ~60, 8 threads ~26.
            int threads = pc.CpuThreads > 0 ? pc.CpuThreads : Environment.ProcessorCount;
            settings.Fps = !integrated && threads >= 12 ? 5 : 0; // 5 = 60 FPS, 0 = 30 FPS
            settings.VSync = false;
            settings.Speed = 0;   // real time
            settings.Lighting = 0; // faster lighting

            // DLSS: a sharper picture at the game's own resolutions (no FPS cost worth avoiding); FSR is
            // left to the player on other cards.
            settings.Upscale = pc.SupportsDlss ? 1 : 0;
            settings.LowLatency = pc.SupportsReflex;

            // Frame generation stays off; its on-screen target follows the screen for when the player
            // turns it on (2x then caps the game at half of it).
            settings.FrameGenMode = 0;
            int goal = pc.RefreshRate >= 120 ? 120 : pc.RefreshRate >= 90 ? 90 : 60;
            settings.FrameGenTarget = Math.Max(0, Array.IndexOf(TargetValues, goal));

            settings.ApplyBestOnStart = false;
        }

        // RecompLauncher's Easy Setup or a player may give the dump's eboot.bin, or a folder above the dump:
        // the folder that holds eboot.bin, when there is exactly one.
        public static string DumpFolder(string path)
        {
            var full = Path.GetFullPath(path.Trim().Trim('"'));
            if (File.Exists(full))
                full = Path.GetDirectoryName(full) ?? full;
            if (!Directory.Exists(full) || File.Exists(Path.Combine(full, "eboot.bin")))
                return full;
            var found = new System.Collections.Generic.List<string>();
            void Look(string dir, int depth)
            {
                if (found.Count > 1) return;
                string[] subs;
                try { subs = Directory.GetDirectories(dir); } catch { return; }
                foreach (var sub in subs)
                {
                    if (File.Exists(Path.Combine(sub, "eboot.bin"))) found.Add(sub);
                    else if (depth > 1) Look(sub, depth - 1);
                    if (found.Count > 1) return;
                }
            }
            Look(full, 2);
            return found.Count == 1 ? found[0] : full;
        }
    }
}
