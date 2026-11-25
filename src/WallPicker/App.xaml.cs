using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WallpaperSuite.Core;
using WallpaperSuite.Core.Themes;
using WallPicker.Themes;

namespace WallPicker;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\WallpaperSuite.WallPicker";
    internal const string PickerWindowTitle = "WallPicker - WallpaperSuite";

    private Mutex? _instanceMutex;
    private bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
            if (!_ownsMutex)
            {
                ActivateExistingInstance();
                _instanceMutex.Dispose();
                _instanceMutex = null;
                Shutdown(0);
                return;
            }

            var configStore = new JsonStore<AppConfig>(AppPaths.ConfigFile);

            // Register built-in themes
            ThemeRegistry.Register(new ArcCarouselTheme(configStore));
            ThemeRegistry.Register(new SkewedAccordionTheme());
            ThemeRegistry.Register(new CenteredCarouselTheme());
            ThemeRegistry.Register(new ClassicGridTheme());

            AppConfig config = configStore.Load();
            config.EnsureDefaults();

            // Parse --theme <id> / --theme=<id> and optional folder path without confusing flags for folders.
            (string? themeId, string? folderArg) = ParseArgs(e.Args);
            if (!string.IsNullOrWhiteSpace(themeId))
            {
                config.ActiveWallpaperTheme = ThemeRegistry.ValidateOrDefault(themeId);
                configStore.Save(config);
            }

            string? folder = ResolveInitialFolder(folderArg, config, configStore);
            if (string.IsNullOrWhiteSpace(folder))
            {
                Shutdown(0);
                return;
            }

            string activeThemeId = ThemeRegistry.ValidateOrDefault(config.ActiveWallpaperTheme);
            if (string.Equals(activeThemeId, ThemeRegistry.DefaultThemeId, StringComparison.OrdinalIgnoreCase))
            {
                var window = new MainWindow(configStore, config, folder);
                MainWindow = window;
                window.ShowOnCursorMonitor();
                return;
            }

            IWallpaperTheme? theme = ThemeRegistry.Get(activeThemeId);
            if (theme is not null)
            {
                ShutdownMode = ShutdownMode.OnLastWindowClose;
                theme.Show(config, folder, selectedPath => ApplySelectedWallpaper(configStore, config, selectedPath));
                return;
            }

            var fallbackWindow = new MainWindow(configStore, config, folder);
            MainWindow = fallbackWindow;
            fallbackWindow.ShowOnCursorMonitor();
        }
        catch (Exception ex)
        {
            LogCrash("WallPicker startup exception", ex);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_instanceMutex is not null)
        {
            if (_ownsMutex)
            {
                try
                {
                    _instanceMutex.ReleaseMutex();
                }
                catch
                {
                    // Ignore if already abandoned.
                }
            }

            _instanceMutex.Dispose();
            _instanceMutex = null;
        }

        base.OnExit(e);
    }

    private static (string? ThemeId, string? FolderArg) ParseArgs(string[] args)
    {
        string? themeId = null;
        string? folderArg = null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (string.Equals(arg, "--theme", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    themeId = args[++i].Trim();
                }
            }
            else if (arg.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase))
            {
                themeId = arg["--theme=".Length..].Trim();
            }
            else if (!arg.StartsWith("--", StringComparison.Ordinal) && folderArg is null)
            {
                folderArg = arg;
            }
        }

        return (themeId, folderArg);
    }

    private static void ApplySelectedWallpaper(JsonStore<AppConfig> configStore, AppConfig config, string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
        {
            SimpleLogger.Warn($"WallPicker selected missing wallpaper '{selectedPath}'");
            return;
        }

        string? cacheKey = WallpaperLibrary.TryComputeKey(selectedPath);
        if (WallpaperApplier.Apply(selectedPath, config.FitMode, cacheKey, out string? error))
        {
            config.LastApplied = selectedPath;
            configStore.Save(config);
            WallpaperEffects.AfterApply(config, selectedPath);
        }
        else
        {
            SimpleLogger.Warn($"WallPicker failed to apply '{selectedPath}': {error}");
        }
    }

    private static string? ResolveInitialFolder(string? argFolder, AppConfig config, JsonStore<AppConfig> configStore)
    {
        if (!string.IsNullOrWhiteSpace(argFolder) && Directory.Exists(argFolder))
        {
            config.RootFolder = argFolder;
            configStore.Save(config);
            return argFolder;
        }

        if (!string.IsNullOrWhiteSpace(config.RootFolder) && Directory.Exists(config.RootFolder))
        {
            return config.RootFolder;
        }

        // No main folder yet: open straight onto the built-in wallpapers (and any
        // other library folders) instead of asking. "O" still picks a folder.
        List<string> library = WallpaperSources.GetLibraryFolders(config);
        if (library.Count > 0)
        {
            return library[0];
        }

        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose wallpaper folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
        {
            return null;
        }

        config.RootFolder = dialog.SelectedPath;
        configStore.Save(config);
        return config.RootFolder;
    }

    private static void ActivateExistingInstance()
    {
        IntPtr hwnd = IntPtr.Zero;
        using Process current = Process.GetCurrentProcess();

        // The first instance may still be creating its window; give it a moment.
        // Use process enumeration (title-independent) because themes change the
        // window title, so FindWindow by exact title is unreliable.
        for (int attempt = 0; attempt < 20 && hwnd == IntPtr.Zero; attempt++)
        {
            foreach (Process proc in Process.GetProcessesByName(current.ProcessName))
            {
                using (proc)
                {
                    if (proc.Id != current.Id && proc.MainWindowHandle != IntPtr.Zero)
                    {
                        hwnd = proc.MainWindowHandle;
                        break;
                    }
                }
            }

            if (hwnd == IntPtr.Zero)
            {
                Thread.Sleep(50);
            }
        }

        if (hwnd != IntPtr.Zero)
        {
            ForceForeground(hwnd);
        }
    }

    /// <summary>
    /// Brings an existing picker to the foreground. Windows normally blocks
    /// <c>SetForegroundWindow</c> for a background process, so temporarily attach
    /// to the current foreground thread's input queue first.
    /// </summary>
    private static void ForceForeground(IntPtr hwnd)
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        uint foregroundThread = foreground == IntPtr.Zero
            ? 0
            : NativeMethods.GetWindowThreadProcessId(foreground, out _);
        uint currentThread = NativeMethods.GetCurrentThreadId();

        bool attached = false;
        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            attached = NativeMethods.AttachThreadInput(currentThread, foregroundThread, true);
        }

        try
        {
            NativeMethods.ShowWindow(
                hwnd,
                NativeMethods.IsIconic(hwnd) ? NativeMethods.SW_RESTORE : NativeMethods.SW_SHOW);
            NativeMethods.BringWindowToTop(hwnd);
            NativeMethods.SetForegroundWindow(hwnd);
            NativeMethods.SetActiveWindow(hwnd);
            NativeMethods.SetFocus(hwnd);
        }
        finally
        {
            if (attached)
            {
                NativeMethods.AttachThreadInput(currentThread, foregroundThread, false);
            }
        }
    }

    private static readonly object CrashLogLock = new();

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("WallPicker unhandled UI exception", e.Exception);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogCrash("WallPicker unhandled domain exception", e.ExceptionObject as Exception);
    }

    private static void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        LogCrash("WallPicker unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private static void LogCrash(string context, Exception? exception)
    {
        SimpleLogger.Error(context, exception);

        lock (CrashLogLock)
        {
            try
            {
                Directory.CreateDirectory(AppPaths.LogsDir);
                string logPath = Path.Combine(AppPaths.LogsDir, "wallpicker.log");
                string details = exception is null
                    ? context
                    : $"{context}{Environment.NewLine}{exception}";
                File.AppendAllText(
                    logPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERROR] {details}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never throw inside a crash handler.
            }
        }
    }
}