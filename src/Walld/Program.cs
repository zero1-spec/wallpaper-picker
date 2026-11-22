using WallpaperSuite.Core;

namespace Walld;

internal static class Program
{
    private const string MutexName = @"Local\WallpaperSuite.Walld";

    [STAThread]
    private static int Main(string[] args)
    {
        InstallExceptionHandlers();

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        var cli = CliOptions.Parse(args);
        SimpleLogger.Info(
            $"walld {typeof(Program).Assembly.GetName().Version} starting (pid {Environment.ProcessId}, " +
            $"args: {(args.Length == 0 ? "-" : string.Join(' ', args))})");

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // A daemon is already running: forward the command (or just signal
            // "open") to it and exit immediately.
            ForwardToRunningInstance(cli);
            return 0;
        }

        // We are the only daemon. "quit" with nothing running is a no-op.
        if (cli is { Command: IpcCommand.Quit })
        {
            SimpleLogger.Info("quit requested but no daemon is running");
            return 0;
        }

        try
        {
            // A command given while no daemon was running is executed after startup.
            (string Command, string? Argument)? pendingCommand =
                cli is { IsBackground: false, Command: not null } ? (cli.Command, cli.Argument) : null;

            using var context = new WalldContext(pendingCommand);
            Application.Run(context);
            SimpleLogger.Info("walld stopped");
            return 0;
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("fatal error, daemon exiting", ex);
            return 1;
        }
        finally
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // We never actually owned it; nothing to release.
            }
        }
    }

    private static void ForwardToRunningInstance(CliOptions cli)
    {
        if (cli is { IsBackground: true, Command: null })
        {
            return; // autostart duplicate: stay silent
        }

        var command = cli.Command ?? IpcCommand.Open; // plain second launch opens the picker
        TrySend(command, cli.Argument);
    }

    private static void TrySend(string command, string? argument)
    {
        // Small bounded retry: a second instance may race the first one's startup.
        for (var attempt = 0; ; attempt++)
        {
            if (IpcClient.Send(command, argument))
            {
                SimpleLogger.Info($"forwarded command '{command}' to the running daemon");
                return;
            }

            if (attempt >= 10)
            {
                break;
            }

            Thread.Sleep(100);
        }

        SimpleLogger.Warn($"could not deliver command '{command}': daemon window not found");
    }

    private static void InstallExceptionHandlers()
    {
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => SimpleLogger.Error("unhandled UI-thread exception", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => SimpleLogger.Error(
            "unhandled exception",
            e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString() ?? "unknown"));
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            SimpleLogger.Error("unobserved task exception", e.Exception);
            e.SetObserved();
        };
    }
}

/// <summary>Parsed command line: at most one command plus an optional argument.</summary>
internal sealed record CliOptions(string? Command, string? Argument, bool IsBackground)
{
    public static CliOptions Parse(string[] args)
    {
        const string backgroundFlag = "--background";
        var isBackground = args.Contains(backgroundFlag, StringComparer.OrdinalIgnoreCase);
        var rest = args.Where(a => !string.Equals(a, backgroundFlag, StringComparison.OrdinalIgnoreCase)).ToArray();

        string? command = null;
        string? argument = null;
        if (rest.Length > 0)
        {
            var candidate = rest[0].ToLowerInvariant();
            if (IpcCommand.IsKnown(candidate))
            {
                command = candidate;
                if (command == IpcCommand.Set || command == IpcCommand.Video)
                {
                    if (rest.Length >= 2)
                    {
                        argument = rest[1];
                    }
                    else
                    {
                        SimpleLogger.Warn($"{command} command requires a file path argument");
                    }
                }
            }
            else
            {
                    SimpleLogger.Warn(
                        $"unknown argument '{rest[0]}'; usage: walld [open|next|prev|random|pause|resume|toggle-pause|set <path>|video <path>|video-stop|quit] [--background]");
            }
        }

        return new CliOptions(command, argument, isBackground);
    }
}
