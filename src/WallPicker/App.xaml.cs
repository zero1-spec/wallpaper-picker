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

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out _ownsMutex);
        if (!_ownsMutex)
        {
            ActivateExistingInstance();
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown(0);
            return;
        }

        // Register built-in themes
        ThemeRegistry.Register(new SkewedAccordionTheme());
        ThemeRegistry.Register(new CenteredCarouselTheme());
        ThemeRegistry.Register(new ClassicGridTheme());

        var configStore = new JsonStore<AppConfig>(AppPaths.ConfigFile);
        AppConfig config = configStore.Load();
        config.EnsureDefaults();

        // Parse --theme argument
        string? themeArg = e.Args.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(themeArg))
        {
            string themeId = themeArg["--theme=".Length..].Trim();
            config.ActiveWallpaperTheme = ThemeRegistry.ValidateOrDefault(themeId);
            configStore.Save(config);
        }

        string? folder = ResolveInitialFolder(e.Args.Where(a => !a.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase)).ToArray(), config, configStore);
        if (string.IsNullOrWhiteSpace(folder))
        {
            Shutdown(0);
            return;
        }

        var window = new MainWindow(configStore, config, folder);
        MainWindow = window;
        window.ShowOnCursorMonitor();
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

    private static string? ResolveInitialFolder(string[] args, AppConfig config, JsonStore<AppConfig> configStore)
    {
        string? argFolder = args.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a) && !a.StartsWith("--", StringComparison.Ordinal));
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

        // The first instance may still be creating its window; give it a moment.
        for (int attempt = 0; attempt < 20 && hwnd == IntPtr.Zero; attempt++)
        {
            hwnd = NativeMethods.FindWindow(null, PickerWindowTitle);
            if (hwnd == IntPtr.Zero)
            {
                Thread.Sleep(50);
            }
        }

        if (hwnd == IntPtr.Zero)
        {
            using Process current = Process.GetCurrentProcess();
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

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        SimpleLogger.Error("WallPicker unhandled UI exception", e.Exception);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        SimpleLogger.Error("WallPicker unhandled domain exception", e.ExceptionObject as Exception);
    }
}