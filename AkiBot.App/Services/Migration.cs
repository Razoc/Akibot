using System;
using System.IO;

namespace AkiBot.App.Services
{
    // Installs made before AkiBot 0.3.0 still carry the upstream project's file names and launch scripts.
    // On start, the settings keep working under their new names and the leftover files are removed.
    // The old names below exist only to find those files.
    public static class Migration
    {
        private const string OldName = "astro" + "hybrid"; // nom du projet amont

        public static void Run(string root)
        {
            try
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;
                var build = Path.Combine(root, "Build");
                var runHybrid = Path.Combine(build, "RunHybrid");

                MoveIfMissing(Path.Combine(build, OldName + "-settings.json"), Path.Combine(build, "akibot-settings.json"));
                MoveIfMissing(Path.Combine(build, OldName + "-settings.json.bak"), Path.Combine(build, "akibot-settings.json.bak"));
                MoveIfMissing(Path.Combine(runHybrid, OldName + "-game.json"), Path.Combine(runHybrid, GameFeatures.FileName));

                // Launch scripts: the launcher starts the game itself now.
                foreach (var pattern in new[] { "*.bat", "*.cmd", "*.sh" })
                {
                    foreach (var f in Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly)) TryDelete(f);
                    if (Directory.Exists(runHybrid))
                        foreach (var f in Directory.GetFiles(runHybrid, pattern, SearchOption.TopDirectoryOnly)) TryDelete(f);
                }
                foreach (var f in Directory.GetFiles(root, OldName + ".exe*", SearchOption.TopDirectoryOnly)) TryDelete(f);

                // The old host module, once the AkiBot runtime has put its own next to it.
                if (File.Exists(Path.Combine(runHybrid, RuntimeDefaults.HostModule)))
                {
                    TryDelete(Path.Combine(runHybrid, OldName + "_host.dll"));
                    var libs = Path.Combine(runHybrid, "libs");
                    if (Directory.Exists(libs))
                        foreach (var f in Directory.GetFiles(libs, "lib" + OldName + "host.prx", SearchOption.TopDirectoryOnly)) TryDelete(f);
                }
            }
            catch { }
        }

        private static void MoveIfMissing(string from, string to)
        {
            try
            {
                if (File.Exists(from) && !File.Exists(to)) File.Move(from, to);
                else if (File.Exists(from)) File.Delete(from);
            }
            catch { }
        }

        private static void TryDelete(string file)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch { }
        }
    }
}
