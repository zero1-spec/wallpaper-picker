using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace WallpaperSuite.Core;

/// <summary>Modifier flags. Values intentionally match the Win32 MOD_* constants.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// A key combination such as "Alt+Shift+W". Serializes to / parses from that
/// readable string form in config.json.
/// </summary>
[JsonConverter(typeof(HotkeySpecJsonConverter))]
public sealed class HotkeySpec
{
    public static readonly HotkeySpec Empty = new();

    public HotkeyModifiers Modifiers { get; set; }

    public Keys Key { get; set; }

    /// <summary>No key recorded (the action's hotkey is disabled).</summary>
    public bool IsEmpty => Key == Keys.None;

    /// <summary>A usable hotkey needs at least one modifier plus a real key.</summary>
    public bool IsValid => Key != Keys.None && Modifiers != HotkeyModifiers.None;

    public HotkeySpec Clone() => new() { Modifiers = Modifiers, Key = Key };

    public override string ToString()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var text = new StringBuilder();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) text.Append("Ctrl+");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) text.Append("Alt+");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) text.Append("Shift+");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) text.Append("Win+");
        text.Append(KeyDisplayName(Key));
        return text.ToString();
    }

    /// <summary>Parses "Alt+Shift+W" (or "" for none). Never throws; garbage yields an empty spec.</summary>
    public static HotkeySpec Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new HotkeySpec();
        }

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var modifiers = HotkeyModifiers.None;
        var key = Keys.None;
        foreach (var part in parts)
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= HotkeyModifiers.Ctrl;
                    continue;
                case "alt":
                    modifiers |= HotkeyModifiers.Alt;
                    continue;
                case "shift":
                    modifiers |= HotkeyModifiers.Shift;
                    continue;
                case "win":
                case "windows":
                case "meta":
                    modifiers |= HotkeyModifiers.Win;
                    continue;
            }

            var parsedKey = ParseKeyName(part);
            if (parsedKey is null || key != Keys.None)
            {
                return new HotkeySpec(); // unknown token or two keys: treat as "none"
            }

            key = parsedKey.Value;
        }

        return new HotkeySpec { Modifiers = modifiers, Key = key };
    }

    private static Keys? ParseKeyName(string token)
    {
        // Friendly digit names ("1") map back to Keys.D1..D9.
        if (token.Length == 1 && token[0] is >= '0' and <= '9')
        {
            return (Keys)((int)Keys.D0 + (token[0] - '0'));
        }

        if (!Enum.TryParse<Keys>(token, ignoreCase: true, out var key) || key == Keys.None)
        {
            return null;
        }

        return key;
    }

    private static string KeyDisplayName(Keys key)
    {
        if ((int)key is >= (int)Keys.D0 and <= (int)Keys.D9)
        {
            return ((int)key - (int)Keys.D0).ToString();
        }

        return key.ToString();
    }
}

/// <summary>Reads/writes a HotkeySpec as its string form ("Alt+Shift+W" / "").</summary>
public sealed class HotkeySpecJsonConverter : JsonConverter<HotkeySpec>
{
    public override HotkeySpec Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => reader.TokenType == JsonTokenType.String ? HotkeySpec.Parse(reader.GetString()) : new HotkeySpec();

    public override void Write(Utf8JsonWriter writer, HotkeySpec value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
