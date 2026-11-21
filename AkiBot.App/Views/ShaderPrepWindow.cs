using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AkiBot.App.Services;

namespace AkiBot.App.Views
{
    // Shown by Play (and --play, so RecompLauncher too) while the shaders were never prepared for this renderer, list
    // and graphics driver (ShaderPrep.Needed): Kyty builds the pipeline list, then the game starts by itself.
    // "Play now" stops it (the next Play prepares again); closing the window stops it and starts nothing.
    public class ShaderPrepWindow : Window
    {
        public enum Outcome { Done, PlayNow, Closed }

        private readonly LauncherService _launcher;
        private readonly Dictionary<string, string> _variables;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly TextBlock _status;
        private readonly TextBlock _elapsed;
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly DispatcherTimer _timer;
        private Outcome _outcome = Outcome.Closed;
        private bool _finished;

        private ShaderPrepWindow(LauncherService launcher, Dictionary<string, string> variables)
        {
            _launcher = launcher;
            _variables = variables;
            Title = Loc.T("Prep.Title");
            Width = 560;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.CanMinimize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            if (TryFindResource("MainWindowStyle") is Style windowStyle) Style = windowStyle;
            try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Resources/akibot.ico")); } catch { }

            var panel = new StackPanel { Margin = new Thickness(32) };
            panel.Children.Add(Text(Loc.T("Prep.Heading"), "TitleTextStyle", new Thickness(0, 0, 0, 12)));
            panel.Children.Add(Text(Loc.T("Prep.Intro"), "BodyTextStyle", new Thickness(0, 0, 0, 20)));
            var bar = new ProgressBar { Height = 14, IsIndeterminate = true };
            if (TryFindResource("ModernProgressBarStyle") is Style barStyle) bar.Style = barStyle;
            panel.Children.Add(bar);
            _status = Text(Loc.T("Prep.Starting"), "BodyTextStyle", new Thickness(0, 12, 0, 0));
            panel.Children.Add(_status);
            _elapsed = Text("", "MutedTextStyle", new Thickness(0, 4, 0, 0));
            panel.Children.Add(_elapsed);
            panel.Children.Add(Text(Loc.T("Prep.PlayNowHint"), "MutedTextStyle", new Thickness(0, 20, 0, 10)));
            var playNow = new Button { Content = Loc.T("Prep.PlayNow"), HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 180, Padding = new Thickness(16, 8, 16, 8) };
            if (TryFindResource("SecondaryButtonStyle") is Style buttonStyle) playNow.Style = buttonStyle;
            playNow.Click += (s, e) => { _outcome = Outcome.PlayNow; _stop.Cancel(); };
            panel.Children.Add(playNow);
            Content = panel;

            // On the game's screen when one is chosen (KYTY_GPU_DISPLAY=N, 1 = first screen), without taking the focus.
            var display = variables.TryGetValue("KYTY_GPU_DISPLAY", out var chosen) ? chosen : Environment.GetEnvironmentVariable("KYTY_GPU_DISPLAY");
            // Counted as the game counts them: the primary screen first (KYTY_GPU_DISPLAY=2 = the other screen, test
            // 2026-11-22), not in Screen.AllScreens' own order.
            var screens = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(System.Windows.Forms.Screen.AllScreens, screen => screen.Primary ? 0 : 1));
            if (int.TryParse(display, out var number) && number >= 2 && number <= screens.Count)
            {
                var area = screens[number - 1].WorkingArea;
                ShowActivated = false;
                SourceInitialized += (s, e) =>
                {
                    var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
                    if (!GetWindowRect(handle, out var rect)) return;
                    int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
                    SetWindowPos(handle, IntPtr.Zero, area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, 0, 0, 0x0001 | 0x0004 | 0x0010);
                };
            }

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += (s, e) => _elapsed.Text = Loc.T("Prep.Elapsed", (int)_clock.Elapsed.TotalSeconds);
            Loaded += async (s, e) => await RunAsync();
            Closing += (s, e) => { if (!_finished) _stop.Cancel(); };
        }

        private TextBlock Text(string text, string style, Thickness margin)
        {
            var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = margin };
            if (TryFindResource(style) is Style found) block.Style = found;
            return block;
        }

        private async Task RunAsync()
        {
            _clock.Start();
            _timer.Start();
            var status = new Progress<string>(text => _status.Text = text);
            ShaderPrep.Result result;
            try { result = await _launcher.PrepareShadersAsync(_variables, status, _stop.Token); }
            catch { result = ShaderPrep.Result.Failed; }
            _timer.Stop();
            _finished = true;
            // A failure does not keep anyone from playing: the game starts, and the next Play tries again.
            if (!_stop.IsCancellationRequested) _outcome = Outcome.Done;
            Close();
        }

        private struct Rect { public int Left, Top, Right, Bottom; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);

        // Before Play: prepares the shaders. False: the window was closed (the game is not started).
        public static bool PrepareBeforePlay(LauncherService launcher, Dictionary<string, string> variables)
        {
            var app = Application.Current;
            var window = new ShaderPrepWindow(launcher, variables);
            var owner = app?.MainWindow;
            if (owner != null && owner != window && owner.IsVisible) window.Owner = owner;
            // With --play (RecompLauncher) this is the app's only window: closing it must not end the app, which
            // still has to start the game (test 2026-11-22: the launcher quit right after starting it).
            var mode = app?.ShutdownMode ?? ShutdownMode.OnExplicitShutdown;
            if (app != null) app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try { window.ShowDialog(); }
            finally { if (app != null) app.ShutdownMode = mode; }
            return window._outcome != Outcome.Closed;
        }
    }
}
