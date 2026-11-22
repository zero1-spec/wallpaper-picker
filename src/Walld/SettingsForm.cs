using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Settings dialog. Created on demand by the daemon, disposed on close.
/// Save persists config.json atomically, re-registers hotkeys live and
/// reports registration conflicts in a message box.
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly Dictionary<string, string> ActionLabels = new()
    {
        [AppConfig.ActionOpenPicker] = "Open picker",
        [AppConfig.ActionNextWallpaper] = "Next wallpaper",
        [AppConfig.ActionPreviousWallpaper] = "Previous wallpaper",
        [AppConfig.ActionTogglePause] = "Pause / resume",
    };

    private readonly AppConfig _draft;
    private readonly Func<AppConfig, IReadOnlyList<HotkeyRegistrationFailure>> _apply;

    private readonly HotkeyBox _openPickerBox = new();
    private readonly HotkeyBox _nextBox = new();
    private readonly HotkeyBox _previousBox = new();
    private readonly HotkeyBox _togglePauseBox = new();

    private readonly CheckBox _startWithWindows = new() { Text = "Start with Windows", AutoSize = true };
    private readonly CheckBox _autoChangeEnabled = new() { Text = "Enable auto change", AutoSize = true };
    private readonly ComboBox _themeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 121 };
    private readonly ComboBox _nextModeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 121 };
    private readonly NumericUpDown _intervalBox = new() { Minimum = 1, Maximum = 1440, Width = 60 };
    private readonly Button _saveButton = new() { Text = "Save", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true };

    public SettingsForm(AppConfig current, Func<AppConfig, IReadOnlyList<HotkeyRegistrationFailure>> apply)
    {
        _draft = current.Clone(); // edits apply to a copy so Cancel discards them
        _apply = apply;

        Text = "WallpaperSuite Settings";
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(430, 380);

        BuildLayout();
        LoadValues();

        _saveButton.Click += OnSave;
        _cancelButton.Click += (_, _) => Close();
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private void BuildLayout()
    {
        // Hotkeys group: label | HotkeyBox | Reset, one row per action.
        var hotkeysGrid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 };
        hotkeysGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        hotkeysGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hotkeysGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        AddHotkeyRow(hotkeysGrid, "Open picker:", _openPickerBox, AppConfig.ActionOpenPicker);
        AddHotkeyRow(hotkeysGrid, "Next wallpaper:", _nextBox, AppConfig.ActionNextWallpaper);
        AddHotkeyRow(hotkeysGrid, "Previous wallpaper:", _previousBox, AppConfig.ActionPreviousWallpaper);
        AddHotkeyRow(hotkeysGrid, "Pause / resume:", _togglePauseBox, AppConfig.ActionTogglePause);

        var hotkeysGroup = new GroupBox { Text = "Hotkeys", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        hotkeysGroup.Controls.Add(hotkeysGrid);

        var themeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        themeRow.Controls.Add(new Label { Text = "Theme:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        themeRow.Controls.Add(_themeBox);

        var intervalRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        intervalRow.Controls.Add(new Label { Text = "Change wallpaper every", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        intervalRow.Controls.Add(_intervalBox);
        intervalRow.Controls.Add(new Label { Text = "minutes", AutoSize = true, Margin = new Padding(6, 8, 0, 0) });

        var nextModeRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        nextModeRow.Controls.Add(new Label { Text = "Next wallpaper:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) });
        nextModeRow.Controls.Add(_nextModeBox);

        var generalGrid = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1 };
        generalGrid.Controls.Add(_startWithWindows);
        generalGrid.Controls.Add(themeRow);
        generalGrid.Controls.Add(_autoChangeEnabled);
        generalGrid.Controls.Add(intervalRow);
        generalGrid.Controls.Add(nextModeRow);

        var generalGroup = new GroupBox { Text = "General", Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(8) };
        generalGroup.Controls.Add(generalGrid);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
        };
        buttons.Controls.Add(_saveButton);
        buttons.Controls.Add(_cancelButton);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(hotkeysGroup, 0, 0);
        root.Controls.Add(generalGroup, 0, 1);
        root.Controls.Add(buttons, 0, 3);

        Controls.Add(root);
    }

    private void AddHotkeyRow(TableLayoutPanel grid, string label, HotkeyBox box, string action)
    {
        var row = grid.RowCount++;
        grid.Controls.Add(
            new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 9, 0, 0) },
            0, row);
        box.Dock = DockStyle.Fill;
        grid.Controls.Add(box, 1, row);
        var reset = new Button { Text = "Reset", AutoSize = true };
        reset.Click += (_, _) => box.SetSpec(AppConfig.DefaultHotkeys()[action].Clone());
        grid.Controls.Add(reset, 2, row);
    }

    private void LoadValues()
    {
        _openPickerBox.SetSpec(_draft.GetHotkey(AppConfig.ActionOpenPicker));
        _nextBox.SetSpec(_draft.GetHotkey(AppConfig.ActionNextWallpaper));
        _previousBox.SetSpec(_draft.GetHotkey(AppConfig.ActionPreviousWallpaper));
        _togglePauseBox.SetSpec(_draft.GetHotkey(AppConfig.ActionTogglePause));

        _startWithWindows.Checked = _draft.StartWithWindows;
        _autoChangeEnabled.Checked = _draft.AutoChangeEnabled;

        _nextModeBox.Items.AddRange(new object[] { "Random", "Sequential" });
        _nextModeBox.SelectedIndex = string.Equals(
            _draft.NextMode,
            AppConfig.NextModeSequential,
            StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        _themeBox.Items.AddRange(new object[] { "Auto", "Light", "Dark" });
        _themeBox.SelectedIndex = _draft.Theme.ToLowerInvariant() switch
        {
            "light" => 1,
            "dark" => 2,
            _ => 0,
        };

        _intervalBox.Value = Math.Clamp(_draft.AutoChangeIntervalMinutes, 1, 1440);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var duplicates = FindDuplicateHotkeys();
        if (duplicates.Count > 0)
        {
            MessageBox.Show(
                this,
                "Two actions cannot use the same hotkey:\n\n" + string.Join("\n", duplicates),
                "WallpaperSuite",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _draft.StartWithWindows = _startWithWindows.Checked;
        _draft.AutoChangeEnabled = _autoChangeEnabled.Checked;
        _draft.Theme = _themeBox.SelectedIndex switch { 1 => "light", 2 => "dark", _ => "auto" };
        _draft.AutoChangeIntervalMinutes = (int)_intervalBox.Value;
        _draft.NextMode = _nextModeBox.SelectedIndex == 1
            ? AppConfig.NextModeSequential
            : AppConfig.NextModeRandom;
        _draft.Hotkeys[AppConfig.ActionOpenPicker] = _openPickerBox.Spec.Clone();
        _draft.Hotkeys[AppConfig.ActionNextWallpaper] = _nextBox.Spec.Clone();
        _draft.Hotkeys[AppConfig.ActionPreviousWallpaper] = _previousBox.Spec.Clone();
        _draft.Hotkeys[AppConfig.ActionTogglePause] = _togglePauseBox.Spec.Clone();

        var failures = _apply(_draft);
        if (failures.Count > 0)
        {
            var lines = failures.Select(f => $"- {ActionLabels.GetValueOrDefault(f.Action, f.Action)}: {f.Spec}");
            MessageBox.Show(
                this,
                "These hotkeys are already in use by another program and were not registered:\n\n"
                + string.Join("\n", lines)
                + "\n\nPick different keys and save again.",
                "Hotkey conflict",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        Close();
    }

    private List<string> FindDuplicateHotkeys()
    {
        var entries = new (HotkeyBox Box, string Label)[]
        {
            (_openPickerBox, ActionLabels[AppConfig.ActionOpenPicker]),
            (_nextBox, ActionLabels[AppConfig.ActionNextWallpaper]),
            (_previousBox, ActionLabels[AppConfig.ActionPreviousWallpaper]),
            (_togglePauseBox, ActionLabels[AppConfig.ActionTogglePause]),
        };

        return entries
            .Where(entry => !entry.Box.Spec.IsEmpty)
            .GroupBy(entry => entry.Box.Spec.ToString(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key} ({string.Join(", ", group.Select(entry => entry.Label))})")
            .ToList();
    }
}
