using System.Windows;
using System.Windows.Input;

namespace WallPicker;

internal static class PickerFocus
{
    public static void Take(Window window)
    {
        window.Focus();
        Keyboard.Focus(window);
    }
}
