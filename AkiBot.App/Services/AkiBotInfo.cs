using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace AkiBot.App.Services
{
    // Everything AkiBot-specific in one place: where it updates from, which runtime it installs, and the small
    // per-user file that remembers the install folder and the language.
    public static class AkiBotInfo
    {
        public const string Repo = "Razoc/AkiBot";
        public const string RepoUrl = "https://github.com/" + Repo;
        public const string LatestReleaseApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string LauncherAssetName = "AkiBot.exe";
        public const string ExeName = "AkiBot.exe";

        // The runtime (kyty_gpu.dll, PS5 system libraries, relinker, DLSS and FSR files...) is AkiBot's own
        // package (Tools\release\make_runtime.ps1), pinned by version and SHA-256. A new AkiBot version can move the pin; installed
        // copies then offer to update the runtime.
        public const string RuntimeVersion = "0.3.0";
        public const string RuntimeUrl = "https://github.com/" + Repo + "/releases/download/v0.3.0/AkiBot-runtime-0.3.0.zip";
        public const string RuntimeSha256 = "f196ad64c2404036ef0ae793b5fe11071e0538ae2c851dc2b383adc8143bfefa";
        public const long RuntimeZipBytes = 201_001_753;
        public const long NeededBytes = 3L * 1024 * 1024 * 1024; // runtime + game builds, with margin
        public const string RuntimeVersionFile = "akibot-runtime.txt";

        // Launcher files and scripts at the zip root: AkiBot replaces them (it starts the game itself).
        public static bool IsUpstreamLauncherFile(string zipEntry)
        {
            if (zipEntry.Contains("/") || zipEntry.Contains("\\")) return false;
            var name = zipEntry.ToLowerInvariant();
            return name == "akibot.exe" || name == "akibot.exe.config" || name.EndsWith(".dll") ||
                name.EndsWith(".bat") || name.EndsWith(".cmd") || name.EndsWith(".sh");
        }

        public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

        public static string UserDataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AkiBot");

        public static string DefaultInstallDirectory()
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
            return Path.Combine(root, "AkiBot");
        }

        public static bool IsInstallFolder(string? dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "Build", "RunHybrid", "kyty_gpu.dll")) &&
            File.Exists(Path.Combine(dir, "tools", "relinker.exe"));

        public static string InstalledRuntimeVersion(string installDir)
        {
            try { return File.ReadAllText(Path.Combine(installDir, RuntimeVersionFile)).Trim(); }
            catch { return ""; }
        }

        public class UserData
        {
            public string? InstallDir { get; set; }
            public string? Language { get; set; }
        }

        private static string UserDataFile => Path.Combine(UserDataDirectory, "akibot.json");

        public static UserData LoadUserData()
        {
            try
            {
                if (File.Exists(UserDataFile))
                    return JsonSerializer.Deserialize<UserData>(File.ReadAllText(UserDataFile)) ?? new UserData();
            }
            catch { }
            return new UserData();
        }

        public static void SaveUserData(UserData data)
        {
            Directory.CreateDirectory(UserDataDirectory);
            var tmp = UserDataFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(UserDataFile)) File.Delete(UserDataFile);
            File.Move(tmp, UserDataFile);
        }

        public static string FormatBytes(long bytes)
        {
            string[] units = Loc.Language == Loc.French ? new[] { "o", "Ko", "Mo", "Go", "To" } : new[] { "B", "KB", "MB", "GB", "TB" };
            double size = bytes; int i = 0;
            while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
            return $"{size:0.#} {units[i]}";
        }
    }
}
