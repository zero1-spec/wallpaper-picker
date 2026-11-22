using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using WallpaperSuite.Core;

namespace Walld;

/// <summary>
/// Finds duplicate wallpaper images across configured folders and lists
/// missing/broken library entries. Scanning and hashing run on a background
/// thread; the UI stays responsive.
/// </summary>
internal sealed class DuplicateFinder : Form
{
    private readonly List<string> _folders;
    private readonly JsonStore<Dictionary<string, List<string>>> _collectionsStore = new(AppPaths.CollectionsFile);
    private readonly JsonStore<HistoryData> _historyStore = new(AppPaths.HistoryFile);

    private readonly ListView _list;
    private readonly Button _recycleBtn;
    private readonly Button _removeBtn;
    private readonly Button _closeBtn;
    private readonly ProgressBar _progress;
    private readonly Label _status;

    private List<ListViewItem> _allItems = new();
    private List<string> _missingEntries = new();

    public DuplicateFinder(List<string> folders)
    {
        _folders = folders;

        Text = "Find Duplicate Wallpapers";
        Size = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable
        };
        _list.Columns.Add("Type", 90, HorizontalAlignment.Left);
        _list.Columns.Add("Path", -2, HorizontalAlignment.Left);
        _list.Columns.Add("Size", 80, HorizontalAlignment.Right);
        _list.Columns.Add("Hash", 64, HorizontalAlignment.Left);

        _progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 18, Visible = false };

        _status = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 20,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft,
            Text = "Scanning..."
        };

        _recycleBtn = new Button { Text = "Send Checked to Recycle Bin", Dock = DockStyle.Bottom, Height = 30, Enabled = false };
        _removeBtn = new Button { Text = "Remove Checked Missing Entries", Dock = DockStyle.Bottom, Height = 30, Enabled = false };
        _closeBtn = new Button { Text = "Close", Dock = DockStyle.Bottom, Height = 30 };

        _recycleBtn.Click += OnRecycle;
        _removeBtn.Click += OnRemoveFromLibrary;
        _closeBtn.Click += (_, _) => Close();
        _list.ItemCheck += (_, _) => UpdateButtonStates();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 110, Padding = new Padding(8) };
        bottom.Controls.Add(_closeBtn);
        bottom.Controls.Add(_removeBtn);
        bottom.Controls.Add(_recycleBtn);
        bottom.Controls.Add(_progress);
        bottom.Controls.Add(_status);

        Controls.Add(_list);
        Controls.Add(bottom);

        Load += async (_, _) => await ScanAndFindDuplicatesAsync();
    }

    private async Task ScanAndFindDuplicatesAsync()
    {
        try
        {
            _progress.Visible = true;
            _progress.Style = ProgressBarStyle.Marquee;

            // 1. Scan all folders for images.
            var allFiles = new List<WallpaperFile>();
            foreach (string folder in _folders)
            {
                allFiles.AddRange(WallpaperLibrary.Scan(folder));
            }

            // 2. Group by size (skip zero-length).
            var bySize = allFiles
                .Where(f => f.Size > 0)
                .GroupBy(f => f.Size)
                .Where(g => g.Count() > 1)
                .ToList();

            // 3. Hash candidates and group by hash.
            var dupGroups = new List<List<WallpaperFile>>();
            int scanned = 0;
            foreach (var sizeGroup in bySize)
            {
                var candidates = sizeGroup.ToList();
                var byHash = new Dictionary<string, List<WallpaperFile>>(StringComparer.OrdinalIgnoreCase);

                foreach (var file in candidates)
                {
                    scanned++;
                    string hash = await Task.Run(() => ComputeSha256(file.Path));
                    if (!byHash.TryGetValue(hash, out var list))
                    {
                        list = [];
                        byHash[hash] = list;
                    }
                    list.Add(file);
                }

                foreach (var kvp in byHash)
                {
                    if (kvp.Value.Count > 1)
                        dupGroups.Add(kvp.Value);
                }
            }

            // 4. Find missing entries in collections and history.
            var missing = new List<string>();
            var collections = _collectionsStore.Load() ?? new Dictionary<string, List<string>>();
            foreach (var kvp in collections)
            {
                foreach (string path in kvp.Value)
                {
                    try { if (!File.Exists(path)) missing.Add(path); } catch { missing.Add(path); }
                }
            }
            try
            {
                HistoryData history = _historyStore.Load();
                foreach (string path in history.Paths ?? new List<string>())
                {
                    try { if (!File.Exists(path)) missing.Add(path); } catch { missing.Add(path); }
                }
            }
            catch { }
            _missingEntries = missing.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            // 5. Populate UI on the UI thread.
            Invoke(() =>
            {
                _progress.Visible = false;
                _status.Text = $"Found {dupGroups.Count} duplicate group(s) and {_missingEntries.Count} missing entr(y/ies).";

                foreach (var group in dupGroups)
                {
                    string hash = ComputeSha256(group[0].Path);
                    long size = group[0].Size;
                    foreach (var file in group)
                    {
                        var item = new ListViewItem(new[] { "Duplicate", file.Path, FormatSize(size), hash })
                        {
                            Tag = file.Path,
                            Checked = true
                        };
                        _list.Items.Add(item);
                        _allItems.Add(item);
                    }
                    _list.Items.Add(new ListViewItem(new string[] { "", "", "", "" })); // separator
                }

                foreach (string path in _missingEntries)
                {
                    var item = new ListViewItem(new[] { "Missing", path, "-", "-" })
                    {
                        Tag = path,
                        Checked = true
                    };
                    _list.Items.Add(item);
                    _allItems.Add(item);
                }

                UpdateButtonStates();
            });
        }
        catch (Exception ex)
        {
            Invoke(() =>
            {
                _progress.Visible = false;
                _status.Text = "Scan failed: " + ex.Message;
                SimpleLogger.Error("Duplicate finder scan failed", ex);
            });
        }
    }

    private static string ComputeSha256(string path)
    {
        using var sha = SHA256.Create();
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        byte[] hash = sha.ComputeHash(fs);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string FormatSize(long bytes)
    {
        string[] su = { "B", "KB", "MB", "GB", "TB" };
        double val = bytes;
        int i = 0;
        while (val >= 1024 && i < su.Length - 1)
        {
            val /= 1024;
            i++;
        }
        return $"{val:0.##} {su[i]}";
    }

    private void UpdateButtonStates()
    {
        int checkedCount = _list.Items.Cast<ListViewItem>().Count(i => i.Checked);
        _recycleBtn.Enabled = checkedCount > 0;
        _removeBtn.Enabled = checkedCount > 0 && _missingEntries.Count > 0;
    }

    private void OnRecycle(object? sender, EventArgs e)
    {
        var toRecycle = _list.Items.Cast<ListViewItem>()
            .Where(i => i.Checked && i.Text == "Duplicate")
            .Select(i => (string)i.Tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (toRecycle.Count == 0)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Send {toRecycle.Count} file(s) to the Recycle Bin?\n\nThis cannot be undone easily.",
            "WallpaperSuite",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        // Build multi-string: path1\0path2\0\0
        StringBuilder sb = new();
        foreach (string path in toRecycle)
        {
            sb.Append(path);
            sb.Append('\0');
        }
        sb.Append('\0');

        var op = new SHFILEOPSTRUCT
        {
            hwnd = Handle,
            wFunc = FO_DELETE,
            pFrom = Marshal.StringToHGlobalAuto(sb.ToString()),
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT,
        };

        try
        {
            int rc = SHFileOperation(ref op);
            if (rc != 0 && !op.fAnyOperationsAborted)
            {
                SimpleLogger.Warn($"SHFileOperation returned {rc}");
            }

            // Remove recycled items from the list.
            var recycled = new HashSet<string>(toRecycle, StringComparer.OrdinalIgnoreCase);
            for (int i = _list.Items.Count - 1; i >= 0; i--)
            {
                if (_list.Items[i] is ListViewItem item && recycled.Contains((string)item.Tag!))
                {
                    _list.Items.RemoveAt(i);
                }
            }
            _allItems.RemoveAll(a => recycled.Contains((string)a.Tag!));
            UpdateButtonStates();

            SimpleLogger.Info($"Recycled {toRecycle.Count} duplicate file(s)");
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("Failed to recycle files", ex);
            MessageBox.Show(this, "Failed to send files to Recycle Bin: " + ex.Message, "WallpaperSuite", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (op.pFrom != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(op.pFrom);
            }
        }
    }

    private void OnRemoveFromLibrary(object? sender, EventArgs e)
    {
        var toRemove = _list.Items.Cast<ListViewItem>()
            .Where(i => i.Checked && i.Text == "Missing")
            .Select(i => (string)i.Tag!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (toRemove.Count == 0)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Remove {toRemove.Count} missing entr(y/ies) from collections and history?\n\nThe files themselves are NOT deleted.",
            "WallpaperSuite",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            return;
        }

        try
        {
            // Remove from collections.
            var collections = _collectionsStore.Load() ?? new Dictionary<string, List<string>>();
            bool changed = false;
            foreach (var kvp in collections.ToList())
            {
                var original = kvp.Value;
                var filtered = original.Where(p => !toRemove.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
                if (filtered.Count != original.Count)
                {
                    if (filtered.Count == 0)
                        collections.Remove(kvp.Key);
                    else
                        collections[kvp.Key] = filtered;
                    changed = true;
                }
            }
            if (changed)
            {
                _collectionsStore.Save(collections);
            }

            // Remove from history.
            HistoryData history = _historyStore.Load();
            int histBefore = history.Paths?.Count ?? 0;
            history.Paths = (history.Paths ?? new List<string>())
                .Where(p => !toRemove.Contains(p, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (history.Index >= history.Paths.Count)
                history.Index = history.Paths.Count - 1;
            if (history.Paths.Count == 0)
                history.Index = -1;
            _historyStore.Save(history);
            bool histChanged = history.Paths.Count != histBefore;

            if (changed || histChanged)
            {
                SimpleLogger.Info($"Removed {toRemove.Count} missing entr(y/ies) from library");
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Error("Failed to remove missing entries", ex);
            MessageBox.Show(this, "Failed to remove entries: " + ex.Message, "WallpaperSuite", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Remove missing items from the list.
        var removeSet = new HashSet<string>(toRemove, StringComparer.OrdinalIgnoreCase);
        for (int i = _list.Items.Count - 1; i >= 0; i--)
        {
            if (_list.Items[i] is ListViewItem item && removeSet.Contains((string)item.Tag!))
            {
                _list.Items.RemoveAt(i);
            }
        }
        _allItems.RemoveAll(a => removeSet.Contains((string)a.Tag!));
        _missingEntries.RemoveAll(m => removeSet.Contains(m, StringComparer.OrdinalIgnoreCase));
        UpdateButtonStates();
    }

    // ---- Win32: SHFileOperation for Recycle Bin ----
    private const int FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public IntPtr pFrom;
        public IntPtr pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public IntPtr lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);
}
