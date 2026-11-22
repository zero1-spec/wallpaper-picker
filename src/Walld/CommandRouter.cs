using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Single place where every command is dispatched — from IPC (WM_COPYDATA),
/// global hotkeys and the tray menu alike.
/// </summary>
internal sealed class CommandRouter(WalldContext owner)
{
    public void ExecuteAction(string actionName)
    {
        var command = actionName switch
        {
            AppConfig.ActionOpenPicker => IpcCommand.Open,
            AppConfig.ActionNextWallpaper => IpcCommand.Next,
            AppConfig.ActionPreviousWallpaper => IpcCommand.Prev,
            AppConfig.ActionTogglePause => IpcCommand.TogglePause,
            _ => null,
        };

        if (command is not null)
        {
            Execute(command);
        }
        else
        {
            SimpleLogger.Warn($"unknown action '{actionName}'");
        }
    }

    public void Execute(string command, string? argument = null)
    {
        SimpleLogger.Info(
            $"command '{command}'" + (string.IsNullOrEmpty(argument) ? string.Empty : $" arg '{argument}'"));

        switch (command)
        {
            case IpcCommand.Open:
                owner.OpenPicker();
                break;

            case IpcCommand.Next:
                owner.NextWallpaper();
                break;

            case IpcCommand.Prev:
                owner.PreviousWallpaper();
                break;

            case IpcCommand.Random:
                owner.RandomWallpaper();
                break;

            case IpcCommand.Set:
                owner.SetWallpaper(argument);
                break;

            case IpcCommand.Video:
                owner.SetLiveWallpaper(argument);
                break;

            case IpcCommand.VideoStop:
                owner.StopLiveWallpaper();
                break;

            case IpcCommand.Quit:
                owner.Shutdown();
                break;

            case IpcCommand.Pause:
                owner.SetAutoChange(enabled: false);
                break;

            case IpcCommand.Resume:
                owner.SetAutoChange(enabled: true);
                break;

            case IpcCommand.TogglePause:
                owner.SetAutoChange(!owner.Config.AutoChangeEnabled);
                break;

            default:
                SimpleLogger.Warn($"unknown command '{command}'");
                break;
        }
    }
}
