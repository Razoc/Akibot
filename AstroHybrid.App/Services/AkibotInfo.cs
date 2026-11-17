using System;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace AstroHybrid.App.Services
{
    // Everything Akibot-specific in one place: where it updates from, which runtime it installs, and the small
    // per-user file that remembers the install folder and the language.
    public static class AkibotInfo
    {
        public const string Repo = "Razoc/Akibot";
        public const string RepoUrl = "https://github.com/" + Repo;
        public const string LatestReleaseApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string LauncherAssetName = "Akibot.exe";
        public const string ExeName = "Akibot.exe";

        // The runtime (kyty_gpu.dll, PS5 system libraries, relinker, DLSS files...) comes from the official
        // AstroHybrid release, pinned by version and SHA-256. A new Akibot version can move the pin; installed
        // copies then offer to update the runtime.
        public const string RuntimeVersion = "1.2.1";
        public const string RuntimeUrl = "https://github.com/imnotsharingmyname/pcportastrobot/releases/download/v1.2.1/AstroBotPC-AstroHybrid-1.2.1.zip";
        public const string RuntimeSha256 = "21cf26b5df7336d6c01ee9f5b66a3c6374846315ab076af221436492c5a07159";
        public const long RuntimeZipBytes = 199_035_853;
        public const long NeededBytes = 3L * 1024 * 1024 * 1024; // runtime + game builds, with margin
        public const string RuntimeVersionFile = "akibot-runtime.txt";

        // Files of the upstream launcher at the zip root: Akibot replaces them.
        public static bool IsUpstreamLauncherFile(string zipEntry)
        {
            if (zipEntry.Contains("/") || zipEntry.Contains("\\")) return false;
            var name = zipEntry.ToLowerInvariant();
            return name == "astrohybrid.exe" || name == "astrohybrid.exe.config" || name.EndsWith(".dll");
        }

        public static Version CurrentVersion => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
        public static string CurrentVersionText => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

        public static string UserDataDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Akibot");

        public static string DefaultInstallDirectory()
        {
            var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";
            return Path.Combine(root, "Akibot");
        }

        public static bool IsInstallFolder(string? dir) =>
            !string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "Run-AstroHybrid.bat")) &&
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
