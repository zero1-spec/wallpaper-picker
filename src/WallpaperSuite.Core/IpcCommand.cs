namespace WallpaperSuite.Core;

/// <summary>
/// The IPC protocol: command names, payload encoding and the daemon window
/// class name used to find the daemon's message-only window.
/// </summary>
public static class IpcCommand
{
    /// <summary>Fixed window class of the daemon's hidden message-only window.</summary>
    public const string WindowClassName = "WallpaperSuiteWalldIpcWindow";

    /// <summary>Signature in COPYDATASTRUCT.dwData; foreign WM_COPYDATA messages are ignored.</summary>
    public const uint Magic = 0x5750_5331; // "WPS1"

    /// <summary>Separates command and argument; '|' cannot appear in Windows file paths.</summary>
    public const char ArgumentSeparator = '|';

    public const string Open = "open";
    public const string Next = "next";
    public const string Prev = "prev";
    public const string Random = "random";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string TogglePause = "toggle-pause";
    public const string Set = "set";
    public const string Video = "video";
    public const string VideoStop = "video-stop";
    public const string Quit = "quit";

    public static readonly string[] All =
    {
        Open, Next, Prev, Random, Pause, Resume, TogglePause, Set, Video, VideoStop, Quit,
    };

    public static bool IsKnown(string command) => All.Contains(command, StringComparer.Ordinal);

    public static string Encode(string command, string? argument)
        => string.IsNullOrEmpty(argument) ? command : command + ArgumentSeparator + argument;

    public static bool TryDecode(string payload, out string command, out string? argument)
    {
        command = string.Empty;
        argument = null;
        if (string.IsNullOrEmpty(payload))
        {
            return false;
        }

        var separatorIndex = payload.IndexOf(ArgumentSeparator);
        if (separatorIndex < 0)
        {
            command = payload;
        }
        else
        {
            command = payload[..separatorIndex];
            argument = payload[(separatorIndex + 1)..];
        }

        return IsKnown(command);
    }
}
