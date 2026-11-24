using System.Text.Json;
using WallpaperSuite.Core;

namespace WallpaperSuite.Tests;

public sealed class HotkeySpecTests
{
    [Theory]
    [InlineData("Alt+Shift+W", HotkeyModifiers.Alt | HotkeyModifiers.Shift, "W")]
    [InlineData("Ctrl+Alt+Right", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, "Right")]
    [InlineData("Ctrl+1", HotkeyModifiers.Ctrl, "D1")]
    public void Parse_ValidCombination_SetsModifiersAndKey(string text, HotkeyModifiers modifiers, string keyName)
    {
        HotkeySpec spec = HotkeySpec.Parse(text);

        Assert.Equal(modifiers, spec.Modifiers);
        Assert.Equal(keyName, spec.Key.ToString());
        Assert.True(spec.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("Alt+Fake")]
    [InlineData("A+B")]
    public void Parse_InvalidString_YieldsEmptySpec(string text)
    {
        HotkeySpec spec = HotkeySpec.Parse(text);

        Assert.True(spec.IsEmpty);
        Assert.Equal(string.Empty, spec.ToString());
    }

    [Fact]
    public void ToString_RoundTripsThroughParse()
    {
        HotkeySpec original = HotkeySpec.Parse("Ctrl+Alt+Right");

        string text = original.ToString();

        Assert.Equal("Ctrl+Alt+Right", text);
        Assert.Equal(original.Modifiers, HotkeySpec.Parse(text).Modifiers);
        Assert.Equal(original.Key, HotkeySpec.Parse(text).Key);
    }

    [Fact]
    public void JsonRoundTrip_PreservesReadableString()
    {
        var options = new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        HotkeySpec spec = HotkeySpec.Parse("Alt+Shift+W");

        string json = JsonSerializer.Serialize(spec, options);
        Assert.Contains("Alt+Shift+W", json);
        Assert.DoesNotContain("\\u002B", json);

        HotkeySpec? restored = JsonSerializer.Deserialize<HotkeySpec>(json, options);

        Assert.NotNull(restored);
        Assert.Equal(spec.Modifiers, restored!.Modifiers);
        Assert.Equal(spec.Key, restored.Key);
    }
}