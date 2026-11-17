using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using AstroHybrid.App.Models;
using AstroHybrid.App.Services;
using AstroHybrid.App.Views;
using AstroHybrid.App.ViewModels;

namespace AstroHybrid.App
{
    public partial class App : Application
    {
        private SettingsManager _settingsManager = null!;
        private AppSettings _settings = null!;
        private static string? _root;

        // Akibot ships as one exe: its library DLLs are embedded resources, loaded here when the CLR cannot
        // find them next to the exe (any version is accepted, which also replaces the binding redirects).
        static App()
        {
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var name = new AssemblyName(args.Name).Name + ".dll";
                using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
                if (stream == null) return null;
                var bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);
                return Assembly.Load(bytes);
            };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var userData = AkibotInfo.LoadUserData();
            Loc.Init(userData.Language);
            AkibotUpdater.CleanUpOldExe();

            // Set up global exception handling
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                LogException(ex);
                MessageBox.Show(Loc.T("Common.UnexpectedError", ex?.Message ?? ""), "Akibot", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            };

            DispatcherUnhandledException += (sender, args) =>
            {
                LogException(args.Exception);
                MessageBox.Show(Loc.T("Common.UnexpectedError", args.Exception.Message), "Akibot", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
                Shutdown(1);
            };

            // AstroHybrid.exe --setup "<dump folder>": the setup wizard's work without a window (exit code 0 on
            // success; progress and errors in Logs\setup.log).
            if (e.Args.Length == 2 && e.Args[0] == "--setup")
            {
                RunSetupWithoutWindow(e.Args[1]);
                return;
            }
            // Akibot.exe --install "<folder>": the install window's work without a window (exit code 0 on
            // success; progress in <folder>\Logs\install.log). No shortcuts.
            if (e.Args.Length == 2 && e.Args[0] == "--install")
            {
                RunInstallWithoutWindow(e.Args[1]);
                return;
            }
            // AstroHybrid.exe --remove-game: removes what the setup built (Installation page, Uninstall).
            if (e.Args.Length == 1 && e.Args[0] == "--remove-game")
            {
                RemoveGameWithoutWindow();
                return;
            }

            try
            {
                // Not inside an install folder: go to the saved install, or install now.
                if (FindInstallUpwards() == null)
                {
                    StartFromOutsideInstall(userData);
                    return;
                }
                OfferRuntimeUpdate();

                // Initialize settings
                InitializeSettings();
                LogDebug($"Settings loaded. FirstRunComplete: {_settings.FirstRunComplete}");

                // A game already set up in Build\RunHybrid (for example by the old launcher) is used as it
                // is: the setup wizard would rebuild it from other parts.
                if (!_settings.FirstRunComplete && ExistingInstallation())
                {
                    _settings.FirstRunComplete = true;
                    _settingsManager.Save(_settings);
                    LogDebug("Existing installation in Build\\RunHybrid found, setup skipped.");
                }

                // Show setup wizard or main window based on first run
                if (!_settings.FirstRunComplete)
                {
                    LogDebug("Showing SetupWizard...");
                    // The wizard is the only window for a while: closing it must not end the app.
                    ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    var setupWizard = new SetupWizard(GetRootDirectory());
                    var result = setupWizard.ShowDialog();
                    LogDebug($"SetupWizard closed with result: {result}");

                    // Reload settings after setup: the wizard saved them through its own manager, so read the
                    // file again instead of this manager's cached copy.
                    _settingsManager = new SettingsManager(GetRootDirectory());
                    _settings = _settingsManager.Load();
                    LogDebug($"Settings reloaded. FirstRunComplete: {_settings.FirstRunComplete}");

                    if (_settings.FirstRunComplete)
                    {
                        LogDebug("Showing MainWindow...");
                        ShowMainWindow();
                    }
                    else
                    {
                        LogDebug("Setup not complete, shutting down.");
                        Shutdown(0);
                    }
                }
                else
                {
                    LogDebug("First run complete, showing MainWindow...");
                    ShowMainWindow();
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
                MessageBox.Show(Loc.T("Common.StartupError", ex.ToString()), "Akibot", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
            }
        }

        // Akibot.exe started outside an install folder (for example from Downloads).
        private void StartFromOutsideInstall(AkibotInfo.UserData userData)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var installDir = AkibotInfo.IsInstallFolder(userData.InstallDir) ? userData.InstallDir : null;
            if (installDir == null)
            {
                string? folder = null;
                while (true)
                {
                    var window = new InstallWindow { StartFolder = folder };
                    window.ShowDialog();
                    if (window.LanguageChanged) { folder = window.ChosenFolder; continue; }
                    installDir = window.InstalledDirectory;
                    break;
                }
                if (installDir == null) { Shutdown(0); return; }
            }
            else
            {
                // Already installed: a newer Akibot.exe replaces the installed one.
                var installed = Path.Combine(installDir, AkibotInfo.ExeName);
                try
                {
                    var installedVersion = File.Exists(installed) ? AssemblyName.GetAssemblyName(installed).Version : null;
                    if (installedVersion == null || installedVersion < AkibotInfo.CurrentVersion)
                        AkibotInstaller.CopySelf(installDir);
                }
                catch { }
            }
            Process.Start(new ProcessStartInfo(Path.Combine(installDir, AkibotInfo.ExeName)) { UseShellExecute = true, WorkingDirectory = installDir });
            Shutdown(0);
        }

        // An install whose runtime pin changed offers to update it; the setup wizard then rebuilds the game.
        private void OfferRuntimeUpdate()
        {
            var root = GetRootDirectory();
            var installed = AkibotInfo.InstalledRuntimeVersion(root);
            if (installed == "" || installed == AkibotInfo.RuntimeVersion)
                return;
            if (MessageBox.Show(Loc.T("Runtime.UpdateAvailable", AkibotInfo.RuntimeVersion, installed), "Akibot",
                    MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var window = new InstallWindow(root);
            if (window.ShowDialog() != true)
                return; // keep the current runtime
            InitializeSettings();
            _settings.FirstRunComplete = false; // rebuild AstroBot.exe with the new runtime
            _settingsManager.Save(_settings);
        }

        private async Task CheckForAkibotUpdateAsync()
        {
            try
            {
                var release = await AkibotUpdater.CheckAsync();
                if (release == null) return;
                if (MessageBox.Show(Loc.T("Update.AskInstall", release.VersionText, AkibotInfo.CurrentVersionText), "Akibot",
                        MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                    return;
                await AkibotUpdater.InstallAndRestartAsync(release, null);
            }
            catch (Exception ex)
            {
                LogException(ex);
                MessageBox.Show(Loc.T("Update.Failed", ex.Message), "Akibot", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LogDebug(string message)
        {
            try
            {
                var logPath = Path.Combine(GetRootDirectory(), "Logs", "akibot-debug.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch { }
        }

        private async void RunSetupWithoutWindow(string dump)
        {
            var root = GetRootDirectory();
            var log = Path.Combine(root, "Logs", "setup.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                var progress = new Progress<(int Percent, string Message)>(p =>
                    File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {p.Percent,3}% {p.Message}\r\n"));
                var features = await new GameSetup(root).RunAsync(dump, progress);
                InitializeSettings();
                _settings.FirstRunComplete = true;
                _settings.ApplyBestOnStart = true;
                _settings.GameDumpPath = dump;
                _settings.GameVersion = features.Version;
                _settings.GameTitleId = features.TitleId;
                _settingsManager.Save(_settings);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FAILED: {ex}\r\n");
                Shutdown(2);
            }
        }

        private async void RunInstallWithoutWindow(string folder)
        {
            var dir = Path.GetFullPath(folder);
            var log = Path.Combine(dir, "Logs", "install.log");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                var last = "";
                var progress = new Progress<(int Percent, string Message)>(p =>
                {
                    if (p.Message == last) return;
                    last = p.Message;
                    File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {p.Percent,3}% {p.Message}\r\n");
                });
                await AkibotInstaller.InstallRuntimeAsync(dir, progress, System.Threading.CancellationToken.None);
                AkibotInstaller.CopySelf(dir);
                var data = AkibotInfo.LoadUserData();
                data.InstallDir = dir;
                AkibotInfo.SaveUserData(data);
                File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] done: runtime {AkibotInfo.RuntimeVersion}\r\n");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(log, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FAILED: {ex}\r\n"); } catch { }
                Shutdown(2);
            }
        }

        private async void RemoveGameWithoutWindow()
        {
            try
            {
                await new InstallationManager(GetRootDirectory()).UninstallAsync(true);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                LogException(ex);
                Shutdown(2);
            }
        }

        private static bool ExistingInstallation()
        {
            var runHybrid = Path.Combine(GetRootDirectory(), "Build", "RunHybrid");
            return File.Exists(Path.Combine(runHybrid, "AstroBot.exe")) && File.Exists(Path.Combine(runHybrid, "kyty_gpu.dll"));
        }

        private void InitializeSettings()
        {
            var rootDir = GetRootDirectory();
            Directory.CreateDirectory(Path.Combine(rootDir, "Build"));

            _settingsManager = new SettingsManager(rootDir);
            _settings = _settingsManager.Load();
        }

        private void ShowMainWindow()
        {
            var mainWindow = new MainWindow();
            var rootDir = GetRootDirectory();
            mainWindow.DataContext = new MainWindowViewModel(rootDir);
            MainWindow = mainWindow;
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            mainWindow.Show();
            _ = CheckForAkibotUpdateAsync();
        }

        private void InitializeApplication()
        {
            // Ensure required directories exist
            var rootDir = GetRootDirectory();
            var dirs = new[]
            {
                Path.Combine(rootDir, "Build"),
                Path.Combine(rootDir, "Build", "RunHybrid"),
                Path.Combine(rootDir, "Logs"),
                Path.Combine(rootDir, "Build", "launcher")
            };

            foreach (var dir in dirs)
            {
                Directory.CreateDirectory(dir);
            }
        }

        // The game folder: the first folder from the exe upwards that holds Run-AstroHybrid.bat, so the
        // app works next to the old launcher and from its own build folder; else the exe's folder.
        private static string GetRootDirectory()
        {
            return _root ??= FindInstallUpwards() ?? Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
        }

        private static string? FindInstallUpwards()
        {
            var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
            for (var dir = new DirectoryInfo(exeDir); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Run-AstroHybrid.bat")))
                {
                    return dir.FullName;
                }
            }
            return null;
        }

        private void LogException(Exception? ex)
        {
            try
            {
                var logPath = Path.Combine(GetRootDirectory(), "Logs", "akibot-error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }
    }
}