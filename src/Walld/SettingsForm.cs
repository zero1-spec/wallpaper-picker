using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Settings dialog with General / Folders / Display / Hotkeys tabs. Created on
/// demand by the daemon, disposed on close. Edits go to a copy of the config so
/// Cancel discards them; Save persists config.json atomically, re-registers
/// hotkeys live and reports registration conflicts in a message box.
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

    private static readonly string[] FitModes = { "fill", "fit", "stretch", "center", "tile" };

    private readonly AppConfig _draft;
    private readonly Func<AppConfig, IReadOnlyList<HotkeyRegistrationFailure>> _apply;
    private readonly UpdateService _update;

    // Hotkeys
    private readonly HotkeyBox _openPickerBox = new();
    private readonly HotkeyBox _nextBox = new();
    private readonly HotkeyBox _previousBox = new();
    private readonly HotkeyBox _togglePauseBox = new();

    // General
    private readonly CheckBox _startWithWindows = new() { Text = "Start with Windows", AutoSize = true };
    private readonly CheckBox _autoChangeEnabled = new() { Text = "Change wallpaper automatically", AutoSize = true };
    private readonly NumericUpDown _intervalBox = new() { Minimum = 1, Maximum = 1440, Width = 60 };
    private readonly ComboBox _nextModeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox _pauseOnFullscreen = new() { Text = "Pause auto-change while a fullscreen app or game runs", AutoSize = true };
    private readonly CheckBox _pauseOnBatterySaver = new() { Text = "Pause auto-change while battery saver is on", AutoSize = true };
    private readonly CheckBox _dailyWallpaper = new() { Text = "Wallpaper of the day from Bing", AutoSize = true };
    private readonly CheckBox _autoCheckUpdates = new() { Text = "Check for updates automatically", AutoSize = true };
    private readonly Button _checkNowButton = new() { Text = "Check now", AutoSize = true };
    private readonly Label _versionLabel = new() { AutoSize = true };

    // Folders
    private readonly TextBox _rootFolderBox = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _includeBuiltIn = new() { AutoSize = true };
    private readonly ListBox _extraFoldersList = new() { Dock = DockStyle.Fill, Height = 64, IntegralHeight = false, HorizontalScrollbar = true };
    private readonly CheckBox _dayNightEnabled = new() { Text = "Use different folders for day and night", AutoSize = true };
    private readonly TextBox _dayFolderBox = new() { Dock = DockStyle.Fill };
    private readonly TextBox _nightFolderBox = new() { Dock = DockStyle.Fill };
    private readonly DateTimePicker _nightStart = NewTimePicker();
    private readonly DateTimePicker _nightEnd = NewTimePicker();
    private readonly Label _hiddenLabel = new() { AutoSize = true, Margin = new Padding(0, 8, 6, 0) };
    private readonly Button _clearHiddenButton = new() { Text = "Restore all", AutoSize = true };
    private readonly List<Control> _dayNightControls = new();

    // Display
    private readonly RadioButton _sameOnAllScreens = new() { Text = "Same wallpaper on every screen", AutoSize = true };
    private readonly RadioButton _differentPerScreen = new() { Text = "A different wallpaper on each screen", AutoSize = true };
    private readonly ComboBox _fitModeBox = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly CheckBox _syncAccentColor = new() { Text = "Sync Windows accent color from the wallpaper", AutoSize = true };
    private readonly CheckBox _themeFollowsWallpaper = new() { Text = "Switch Windows light / dark mode to match the wallpaper", AutoSize = true };

    private readonly Button _saveButton = new() { Text = "Save", AutoSize = true };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true };

    public SettingsForm(AppConfig current, Func<AppConfig, IReadOnlyList<HotkeyRegistrationFailure>> apply, UpdateService update)
    {
        _draft = current.Clone(); // edits apply to a copy so Cancel discards them
        _apply = apply;
        _update = update;

        Text = "WallpaperSuite Settings";
        Font = SystemFonts.MessageBoxFont;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 470);

        // Window icon from the embedded ApplicationIcon in the exe.
        try
        {
            var exeLocation = System.Reflection.Assembly.GetEntryAssembly()!.Location;
            Icon = Icon.ExtractAssociatedIcon(exeLocation);
        }
        catch { }

        BuildLayout();
        LoadValues();

        _saveButton.Click += OnSave;
        _cancelButton.Click += (_, _) => Close();
        AcceptButton = _saveButton;
        CancelButton = _cancelButton;
    }

    private static DateTimePicker NewTimePicker() => new()
    {
        Format = DateTimePickerFormat.Custom,
        CustomFormat = "HH:mm",
        ShowUpDown = true,
        Width = 70,
    };

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildGeneralTab());
        tabs.TabPages.Add(BuildFoldersTab());
        tabs.TabPages.Add(BuildDisplayTab());
        tabs.TabPages.Add(BuildHotkeysTab());

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 8, 0, 0),
        };
        buttons.Controls.Add(_saveButton);
        buttons.Controls.Add(_cancelButton);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), ColumnCount = 1 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(tabs, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
    }

    private TabPage BuildGeneralTab()
    {
        var intervalRow = Row(
            new Label { Text = "Every", AutoSize = true, Margin = new Padding(20, 8, 6, 0) },
            _intervalBox,
            new Label { Text = "minutes,", AutoSize = true, Margin = new Padding(6, 8, 6, 0) },
            _nextModeBox,
            new Label { Text = "order", AutoSize = true, Margin = new Padding(6, 8, 0, 0) });

        var page = Stack(
            _startWithWindows,
            Spacer(),
            _autoChangeEnabled,
            intervalRow,
            _pauseOnFullscreen,
            _pauseOnBatterySaver,
            Spacer(),
            _dailyWallpaper,
            Hint("Downloads one photo a day from bing.com and applies it. This is one of two optional network features; the other is the update check."),
            Spacer(),
            _autoCheckUpdates,
            Row(_checkNowButton, _versionLabel),
            Hint("Checks GitHub for a new release at most once per 24 hours. Only the app version is sent; nothing about your machine."));

        _autoChangeEnabled.CheckedChanged += (_, _) => UpdateEnabledState();
        _checkNowButton.Click += (_, _) => _update.CheckForUpdatesNow();
        return Page("General", page);
    }

    private TabPage BuildFoldersTab()
    {
        var rootRow = FolderRow(_rootFolderBox, withClear: true);

        var extraButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        var addExtra = new Button { Text = "Add...", AutoSize = true };
        var removeExtra = new Button { Text = "Remove", AutoSize = true };
        addExtra.Click += (_, _) =>
        {
            string? folder = BrowseFolder("Add a wallpaper folder", null);
            if (folder is not null && !_extraFoldersList.Items.Cast<string>().Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                _extraFoldersList.Items.Add(folder);
            }
        };
        removeExtra.Click += (_, _) =>
        {
            if (_extraFoldersList.SelectedIndex >= 0)
            {
                _extraFoldersList.Items.RemoveAt(_extraFoldersList.SelectedIndex);
            }
        };
        extraButtons.Controls.Add(addExtra);
        extraButtons.Controls.Add(removeExtra);

        var extraRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = new Padding(0) };
        extraRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        extraRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        extraRow.Controls.Add(_extraFoldersList, 0, 0);
        extraRow.Controls.Add(extraButtons, 1, 0);

        var dayRow = LabeledFolderRow("Day:", _dayFolderBox);
        var nightRow = LabeledFolderRow("Night:", _nightFolderBox);
        var timesRow = Row(
            new Label { Text = "Night from", AutoSize = true, Margin = new Padding(20, 8, 6, 0) },
            _nightStart,
            new Label { Text = "to", AutoSize = true, Margin = new Padding(6, 8, 6, 0) },
            _nightEnd);
        _dayNightControls.AddRange(new Control[] { dayRow, nightRow, timesRow });
        _dayNightEnabled.CheckedChanged += (_, _) => UpdateEnabledState();

        _clearHiddenButton.Click += (_, _) =>
        {
            _draft.Excluded.Clear();
            UpdateHiddenLabel();
        };

        var page = Stack(
            Heading("Main folder"),
            rootRow,
            _includeBuiltIn,
            Heading("Extra folders"),
            extraRow,
            Hint("Folders dropped onto the picker also appear here."),
            Heading("Day and night"),
            _dayNightEnabled,
            dayRow,
            nightRow,
            timesRow,
            Hint("Auto-change picks from the day or night folder by time of day. An empty folder falls back to the whole library."),
            Row(_hiddenLabel, _clearHiddenButton));

        return Page("Folders", page);
    }

    private TabPage BuildDisplayTab()
    {
        int screens = MonitorWallpaper.GetMonitors().Count;
        string screenText = screens switch
        {
            0 => "Could not detect your screens.",
            1 => "1 screen detected. The per-screen option takes effect when you connect another screen.",
            _ => $"{screens} screens detected.",
        };

        var page = Stack(
            Heading("Multiple screens"),
            _sameOnAllScreens,
            _differentPerScreen,
            Hint(screenText + " In per-screen mode, Next and auto-change give every screen its own image, and the picker sets the image on the screen it is open on (Shift+Enter sets it on all screens)."),
            Heading("Wallpaper"),
            Row(new Label { Text = "Fit mode:", AutoSize = true, Margin = new Padding(0, 8, 6, 0) }, _fitModeBox),
            Heading("Windows colors"),
            _syncAccentColor,
            _themeFollowsWallpaper,
            Hint("Bright wallpapers switch Windows (apps and taskbar) to light mode, dark wallpapers to dark mode."));

        return Page("Display", page);
    }

    private TabPage BuildHotkeysTab()
    {
        // Hotkeys grid: label | HotkeyBox | Reset, one row per action.
        var hotkeysGrid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3 };
        hotkeysGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        hotkeysGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hotkeysGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        AddHotkeyRow(hotkeysGrid, "Open picker:", _openPickerBox, AppConfig.ActionOpenPicker);
        AddHotkeyRow(hotkeysGrid, "Next wallpaper:", _nextBox, AppConfig.ActionNextWallpaper);
        AddHotkeyRow(hotkeysGrid, "Previous wallpaper:", _previousBox, AppConfig.ActionPreviousWallpaper);
        AddHotkeyRow(hotkeysGrid, "Pause / resume:", _togglePauseBox, AppConfig.ActionTogglePause);

        var page = Stack(
            hotkeysGrid,
            Hint("Click a box and press the new key combination. If Alt+Shift switches your keyboard language, pick a different combination."));
        return Page("Hotkeys", page);
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

    // ------------------------------------------------------------ layout helpers

    private static TabPage Page(string title, Control content)
    {
        var page = new TabPage(title) { Padding = new Padding(10), AutoScroll = true, UseVisualStyleBackColor = true };
        page.Controls.Add(content);
        return page;
    }

    private static TableLayoutPanel Stack(params Control[] controls)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1 };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (Control control in controls)
        {
            panel.Controls.Add(control);
        }

        return panel;
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 2, 0, 2) };
        row.Controls.AddRange(controls);
        return row;
    }

    private static Label Heading(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font(SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont, FontStyle.Bold),
        Margin = new Padding(0, 10, 0, 2),
    };

    private static Label Hint(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(460, 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(20, 0, 0, 4),
    };

    private static Control Spacer() => new Panel { Height = 6, Width = 1, Margin = new Padding(0) };

    private TableLayoutPanel FolderRow(TextBox box, bool withClear)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = withClear ? 3 : 2, Margin = new Padding(0, 2, 0, 2) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            string? folder = BrowseFolder("Choose a wallpaper folder", box.Text);
            if (folder is not null)
            {
                box.Text = folder;
            }
        };
        row.Controls.Add(box, 0, 0);
        row.Controls.Add(browse, 1, 0);
        if (withClear)
        {
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            var clear = new Button { Text = "Clear", AutoSize = true };
            clear.Click += (_, _) => box.Text = "";
            row.Controls.Add(clear, 2, 0);
        }

        return row;
    }

    private TableLayoutPanel LabeledFolderRow(string label, TextBox box)
    {
        var row = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 4, Margin = new Padding(20, 2, 0, 2) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var browse = new Button { Text = "Browse...", AutoSize = true };
        browse.Click += (_, _) =>
        {
            string? folder = BrowseFolder($"Choose the {label.TrimEnd(':').ToLowerInvariant()} wallpaper folder", box.Text);
            if (folder is not null)
            {
                box.Text = folder;
            }
        };
        var clear = new Button { Text = "Clear", AutoSize = true };
        clear.Click += (_, _) => box.Text = "";
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        row.Controls.Add(box, 1, 0);
        row.Controls.Add(browse, 2, 0);
        row.Controls.Add(clear, 3, 0);
        return row;
    }

    private string? BrowseFolder(string description, string? start)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (!string.IsNullOrWhiteSpace(start) && Directory.Exists(start))
        {
            dialog.SelectedPath = start;
        }

        return dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath)
            ? dialog.SelectedPath
            : null;
    }

    private void UpdateEnabledState()
    {
        _intervalBox.Enabled = _nextModeBox.Enabled = _autoChangeEnabled.Checked;
        foreach (Control control in _dayNightControls)
        {
            control.Enabled = _dayNightEnabled.Checked;
        }
    }

    private void UpdateHiddenLabel()
    {
        int hidden = _draft.Excluded.Count;
        _hiddenLabel.Text = hidden == 0
            ? "No wallpapers hidden with \"Never show again\"."
            : $"{hidden} wallpaper(s) hidden with \"Never show again\".";
        _clearHiddenButton.Visible = hidden > 0;
    }

    // ------------------------------------------------------------- load / save

    private void LoadValues()
    {
        _openPickerBox.SetSpec(_draft.GetHotkey(AppConfig.ActionOpenPicker));
        _nextBox.SetSpec(_draft.GetHotkey(AppConfig.ActionNextWallpaper));
        _previousBox.SetSpec(_draft.GetHotkey(AppConfig.ActionPreviousWallpaper));
        _togglePauseBox.SetSpec(_draft.GetHotkey(AppConfig.ActionTogglePause));

        _startWithWindows.Checked = _draft.StartWithWindows;
        _autoChangeEnabled.Checked = _draft.AutoChangeEnabled;
        _intervalBox.Value = Math.Clamp(_draft.AutoChangeIntervalMinutes, 1, 1440);
        _nextModeBox.Items.AddRange(new object[] { "random", "sequential" });
        _nextModeBox.SelectedIndex = string.Equals(
            _draft.NextMode,
            AppConfig.NextModeSequential,
            StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        _pauseOnFullscreen.Checked = _draft.PauseOnFullscreen;
        _pauseOnBatterySaver.Checked = _draft.PauseOnBatterySaver;
        _dailyWallpaper.Checked = _draft.DailyWallpaperEnabled;
        _autoCheckUpdates.Checked = _draft.AutoCheckUpdates;
        var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName()?.Version;
        _versionLabel.Text = version is not null ? $"Version {version.Major}.{version.Minor}.{version.Build}" : "Version unknown";

        _rootFolderBox.Text = _draft.RootFolder;
        int builtInCount = WallpaperSources.CountBuiltIn();
        _includeBuiltIn.Text = builtInCount > 0
            ? $"Include the {builtInCount} built-in wallpapers"
            : "Include built-in wallpapers (none installed)";
        _includeBuiltIn.Checked = _draft.IncludeBuiltInWallpapers;
        foreach (string folder in _draft.ExtraFolders)
        {
            _extraFoldersList.Items.Add(folder);
        }

        _dayNightEnabled.Checked = _draft.DayNightEnabled;
        _dayFolderBox.Text = _draft.DayFolder;
        _nightFolderBox.Text = _draft.NightFolder;
        _nightStart.Value = DateTime.Today.AddHours(_draft.NightStartHour).AddMinutes(_draft.NightStartMinute);
        _nightEnd.Value = DateTime.Today.AddHours(_draft.NightEndHour).AddMinutes(_draft.NightEndMinute);
        UpdateHiddenLabel();

        bool perMonitor = string.Equals(_draft.MonitorMode, AppConfig.MonitorModePerMonitor, StringComparison.OrdinalIgnoreCase);
        _sameOnAllScreens.Checked = !perMonitor;
        _differentPerScreen.Checked = perMonitor;
        _fitModeBox.Items.AddRange(FitModes);
        _fitModeBox.SelectedIndex = Math.Max(0, Array.IndexOf(FitModes, _draft.FitMode));
        _syncAccentColor.Checked = _draft.SyncAccentColor;
        _themeFollowsWallpaper.Checked = _draft.ThemeFollowsWallpaper;

        UpdateEnabledState();
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var duplicates = FindDuplicateHotkeys();
        if (duplicates.Count > 0)
        {
            Warn("Two actions cannot use the same hotkey:\n\n" + string.Join("\n", duplicates));
            return;
        }

        string root = _rootFolderBox.Text.Trim();
        string day = _dayFolderBox.Text.Trim();
        string night = _nightFolderBox.Text.Trim();
        foreach ((string path, string label) in new[] { (root, "Main folder"), (day, "Day folder"), (night, "Night folder") })
        {
            if (path.Length > 0 && !Directory.Exists(path))
            {
                Warn($"{label} does not exist:\n\n{path}");
                return;
            }
        }

        if (_dayNightEnabled.Checked && day.Length == 0 && night.Length == 0)
        {
            Warn("Choose a day folder, a night folder, or both — or turn off day and night folders.");
            return;
        }

        _draft.StartWithWindows = _startWithWindows.Checked;
        _draft.AutoChangeEnabled = _autoChangeEnabled.Checked;
        _draft.AutoChangeIntervalMinutes = (int)_intervalBox.Value;
        _draft.NextMode = _nextModeBox.SelectedIndex == 1
            ? AppConfig.NextModeSequential
            : AppConfig.NextModeRandom;
        _draft.PauseOnFullscreen = _pauseOnFullscreen.Checked;
        _draft.PauseOnBatterySaver = _pauseOnBatterySaver.Checked;
        _draft.DailyWallpaperEnabled = _dailyWallpaper.Checked;
        _draft.AutoCheckUpdates = _autoCheckUpdates.Checked;

        _draft.RootFolder = root;
        _draft.IncludeBuiltInWallpapers = _includeBuiltIn.Checked;
        _draft.ExtraFolders = _extraFoldersList.Items.Cast<string>().ToList();
        _draft.DayNightEnabled = _dayNightEnabled.Checked;
        _draft.DayFolder = day;
        _draft.NightFolder = night;
        _draft.NightStartHour = _nightStart.Value.Hour;
        _draft.NightStartMinute = _nightStart.Value.Minute;
        _draft.NightEndHour = _nightEnd.Value.Hour;
        _draft.NightEndMinute = _nightEnd.Value.Minute;

        _draft.MonitorMode = _differentPerScreen.Checked ? AppConfig.MonitorModePerMonitor : AppConfig.MonitorModeSame;
        _draft.FitMode = FitModes[Math.Max(0, _fitModeBox.SelectedIndex)];
        _draft.SyncAccentColor = _syncAccentColor.Checked;
        _draft.ThemeFollowsWallpaper = _themeFollowsWallpaper.Checked;

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

    private void Warn(string text)
        => MessageBox.Show(this, text, "WallpaperSuite", MessageBoxButtons.OK, MessageBoxIcon.Warning);

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
