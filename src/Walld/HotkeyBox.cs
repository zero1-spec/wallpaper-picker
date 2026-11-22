using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Read-only text box that records a hotkey combination ("Alt+Shift+W").
/// Click or tab into it to start recording; Esc cancels, Backspace/Delete
/// clears the shortcut. Requires at least one modifier (Ctrl/Alt/Shift/Win).
/// </summary>
public sealed class HotkeyBox : TextBox
{
    private HotkeySpec _spec = HotkeySpec.Empty;
    private HotkeySpec _recordStart = HotkeySpec.Empty;
    private bool _recording;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        Cursor = Cursors.Default;
        TextAlign = HorizontalAlignment.Center;
        BackColor = SystemColors.Window;
        Text = "(none)";
    }

    public HotkeySpec Spec => _spec;

    public void SetSpec(HotkeySpec spec)
    {
        _spec = spec;
        UpdateDisplay();
    }

    protected override void OnEnter(EventArgs e)
    {
        base.OnEnter(e);
        BeginRecording();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        BeginRecording();
    }

    protected override void OnLeave(EventArgs e)
    {
        base.OnLeave(e);
        EndRecording(cancel: true);
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (_recording)
        {
            var key = keyData & Keys.KeyCode;
            if (key == Keys.Escape)
            {
                EndRecording(cancel: true);
                return true; // Esc cancels recording instead of closing the dialog
            }

            if (key == Keys.Enter)
            {
                return true; // keep the form's AcceptButton out of the recorder
            }
        }

        return base.ProcessDialogKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!_recording)
        {
            return;
        }

        e.Handled = true;
        e.SuppressKeyPress = true;

        if (e.KeyCode is Keys.Back or Keys.Delete or Keys.Clear)
        {
            Commit(HotkeySpec.Empty); // Backspace/Delete clears the shortcut
            return;
        }

        if (IsModifierKey(e.KeyCode))
        {
            return; // user is still choosing modifiers
        }

        var modifiers = ReadModifiers(e);
        if (modifiers == HotkeyModifiers.None)
        {
            Text = "(add Ctrl, Alt or Shift)";
            return;
        }

        Commit(new HotkeySpec { Modifiers = modifiers, Key = e.KeyCode });
    }

    private void BeginRecording()
    {
        if (_recording)
        {
            return;
        }

        _recording = true;
        _recordStart = _spec.Clone();
        BackColor = SystemColors.Info;
        Text = "(press keys)";
    }

    private void EndRecording(bool cancel)
    {
        if (!_recording)
        {
            UpdateDisplay();
            return;
        }

        _recording = false;
        if (cancel)
        {
            _spec = _recordStart;
        }

        BackColor = SystemColors.Window;
        UpdateDisplay();
    }

    private void Commit(HotkeySpec spec)
    {
        _recording = false;
        _recordStart = spec;
        _spec = spec;
        BackColor = SystemColors.Window;
        UpdateDisplay();
    }

    private void UpdateDisplay() => Text = _spec.IsEmpty ? "(none)" : _spec.ToString();

    private static HotkeyModifiers ReadModifiers(KeyEventArgs e)
    {
        var modifiers = HotkeyModifiers.None;
        if (e.Control) modifiers |= HotkeyModifiers.Ctrl;
        if (e.Alt) modifiers |= HotkeyModifiers.Alt;
        if (e.Shift) modifiers |= HotkeyModifiers.Shift;
        // Win is not part of KeyEventArgs.Modifiers; read the key state directly.
        if (WinApi.GetKeyState(WinApi.VK_LWIN) < 0 || WinApi.GetKeyState(WinApi.VK_RWIN) < 0)
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private static bool IsModifierKey(Keys key) => key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
        or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey
        or Keys.LMenu or Keys.RMenu or Keys.LWin or Keys.RWin;
}
