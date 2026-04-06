using MHUpkManager.UI;

namespace MHUpkManager.BackupManager;

internal sealed class BackupManagerPanel : UserControl
{
    private const int LeftPanelWidth = 430;
    private const int BottomLogHeight = 160;
    private const int DetailsPanelWidth = 300;

    private readonly Func<string> _currentUpkPathProvider;
    private readonly BackupManagerService _service = new();
    private readonly SplitContainer _verticalSplit;
    private readonly SplitContainer _contentSplit;
    private readonly TableLayoutPanel _leftLayout;
    private readonly TextBox _folderPathTextBox;
    private readonly CheckBox _recursiveCheckBox;
    private readonly Label _summaryLabel;
    private readonly Button _useCurrentUpkFolderButton;
    private readonly Button _browseFolderButton;
    private readonly Button _scanBackupsButton;
    private readonly Button _refreshButton;
    private readonly Button _restoreSelectedButton;
    private readonly ListView _backupListView;
    private readonly RichTextBox _detailsTextBox;
    private readonly TextBox _logTextBox;
    private List<BackupEntry> _entries = [];
    private bool _contentSplitInitialized;
    private bool _busy;

    public BackupManagerPanel(Func<string> currentUpkPathProvider = null)
    {
        _currentUpkPathProvider = currentUpkPathProvider;
        Dock = DockStyle.Fill;

        _folderPathTextBox = CreatePathTextBox("No backup folder selected.");
        _recursiveCheckBox = new CheckBox
        {
            Text = "Scan subfolders",
            Dock = DockStyle.Top,
            AutoSize = true
        };
        _summaryLabel = WorkspaceUiStyle.CreateValueLabel("Select a folder, scan for .bak files, then restore the one you need.");

        _useCurrentUpkFolderButton = CreateButton("Use Current UPK Folder");
        _browseFolderButton = CreateButton("Browse");
        _scanBackupsButton = CreateButton("Scan Backups");
        _refreshButton = CreateButton("Refresh Results");
        _restoreSelectedButton = CreateButton("Restore Selected Backup");

        _backupListView = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details,
            GridLines = true
        };
        _backupListView.Font = new Font(Font.FontFamily, 10.0f);
        _backupListView.Columns.Add("File", 220);
        _backupListView.Columns.Add("Original Exists", 120);
        _backupListView.Columns.Add("Modified", 160);
        _backupListView.Columns.Add("Backup Path", 520);

        _detailsTextBox = WorkspaceUiStyle.CreateReadOnlyDetailsTextBox(BuildWorkflowDetailsText());
        _logTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical
        };

        _leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        _leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(1, "Source"));
        AddRow(_useCurrentUpkFolderButton);
        AddRow(_browseFolderButton);
        AddRow(CreateLabel("Folder:"));
        AddRow(_folderPathTextBox);
        AddRow(_recursiveCheckBox);
        AddRow(_summaryLabel);
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(2, "Actions"));
        AddRow(_scanBackupsButton);
        AddRow(_refreshButton);
        AddRow(_restoreSelectedButton, 0);

        Panel leftPanel = new()
        {
            Dock = DockStyle.Left,
            Width = LeftPanelWidth,
            MinimumSize = new Size(LeftPanelWidth, 0),
            AutoScroll = true
        };
        leftPanel.Controls.Add(_leftLayout);

        _contentSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        _contentSplit.Panel1.Controls.Add(_backupListView);
        _contentSplit.Panel2.Controls.Add(_detailsTextBox);

        Panel contentPanel = new() { Dock = DockStyle.Fill };
        contentPanel.Controls.Add(_contentSplit);
        contentPanel.Controls.Add(leftPanel);

        _verticalSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2,
            SplitterDistance = Math.Max(420, Height - BottomLogHeight)
        };
        _verticalSplit.Panel1.Controls.Add(contentPanel);
        _verticalSplit.Panel2.Controls.Add(_logTextBox);
        _verticalSplit.Panel2MinSize = 120;

        Controls.Add(_verticalSplit);

        _useCurrentUpkFolderButton.Click += (_, _) => UseCurrentUpkFolder();
        _browseFolderButton.Click += (_, _) => BrowseFolder();
        _scanBackupsButton.Click += (_, _) => ScanBackups();
        _refreshButton.Click += (_, _) => ScanBackups();
        _restoreSelectedButton.Click += (_, _) => RestoreSelectedBackup();
        _backupListView.SelectedIndexChanged += (_, _) => ShowSelectedDetails();
        Resize += (_, _) =>
        {
            if (_contentSplitInitialized)
                UpdateContentSplit();
        };
        Load += (_, _) =>
        {
            _contentSplitInitialized = true;
            BeginInvoke(new Action(UpdateContentSplit));
        };
    }

    private void UseCurrentUpkFolder()
    {
        string upkPath = _currentUpkPathProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(upkPath))
        {
            Log("Open a UPK first, or browse for a folder manually.");
            return;
        }

        string folder = Path.GetDirectoryName(upkPath);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
        {
            Log("The current UPK folder could not be resolved.");
            return;
        }

        _folderPathTextBox.Text = folder;
        Log($"Using current UPK folder: {folder}");
    }

    private void BrowseFolder()
    {
        using FolderBrowserDialog dialog = new()
        {
            Description = "Select Folder To Scan For .bak Files",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK)
            return;

        _folderPathTextBox.Text = dialog.SelectedPath;
        Log($"Selected folder: {dialog.SelectedPath}");
    }

    private void ScanBackups()
    {
        if (string.IsNullOrWhiteSpace(_folderPathTextBox.Text) || !Directory.Exists(_folderPathTextBox.Text))
        {
            Log("Select a valid folder first.");
            return;
        }

        try
        {
            SetBusy(true);
            _entries = _service.ScanBackupFiles(_folderPathTextBox.Text, _recursiveCheckBox.Checked);
            BindEntries();
            Log($"Found {_entries.Count} backup file(s).");
        }
        catch (Exception ex)
        {
            Log($"Backup scan failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BindEntries()
    {
        _backupListView.BeginUpdate();
        try
        {
            _backupListView.Items.Clear();
            foreach (BackupEntry entry in _entries)
            {
                ListViewItem item = new(entry.FileName);
                item.SubItems.Add(entry.OriginalExists ? "Yes" : "No");
                item.SubItems.Add(entry.LastWriteTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
                item.SubItems.Add(entry.BackupPath);
                item.Tag = entry;
                _backupListView.Items.Add(item);
            }
        }
        finally
        {
            _backupListView.EndUpdate();
        }

        _summaryLabel.Text = _entries.Count == 0
            ? "No .bak files found in the selected folder."
            : $"Found {_entries.Count} backup file(s). Select one to inspect or restore.";

        if (_backupListView.Items.Count > 0)
            _backupListView.Items[0].Selected = true;
        else
            _detailsTextBox.Text = BuildWorkflowDetailsText() + Environment.NewLine + Environment.NewLine + "No backup files found.";
    }

    private void ShowSelectedDetails()
    {
        if (_backupListView.SelectedItems.Count == 0 || _backupListView.SelectedItems[0].Tag is not BackupEntry entry)
        {
            _detailsTextBox.Text = BuildWorkflowDetailsText();
            return;
        }

        _detailsTextBox.Text = string.Join(Environment.NewLine,
        [
            BuildWorkflowDetailsText(),
            string.Empty,
            "Current Backup",
            $"File: {entry.FileName}",
            $"BackupPath: {entry.BackupPath}",
            $"OriginalPath: {entry.OriginalPath}",
            $"OriginalExists: {entry.OriginalExists}",
            $"BackupSize: {FormatSize(entry.BackupSizeBytes)}",
            $"OriginalSize: {(entry.OriginalSizeBytes.HasValue ? FormatSize(entry.OriginalSizeBytes.Value) : "<missing>")}",
            $"BackupModified: {entry.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
            string.Empty,
            "Restore Selected Backup will copy the .bak file back over the original path."
        ]);
    }

    private void RestoreSelectedBackup()
    {
        if (_backupListView.SelectedItems.Count == 0 || _backupListView.SelectedItems[0].Tag is not BackupEntry entry)
        {
            Log("Select a backup entry first.");
            return;
        }

        DialogResult confirm = MessageBox.Show(
            $"Restore this backup?\n\nBackup:\n{entry.BackupPath}\n\nOriginal target:\n{entry.OriginalPath}",
            "Restore Backup",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning);

        if (confirm != DialogResult.OK)
            return;

        try
        {
            SetBusy(true);
            _service.RestoreBackup(entry);
            Log($"Restored backup to: {entry.OriginalPath}");
            _entries = _service.ScanBackupFiles(_folderPathTextBox.Text, _recursiveCheckBox.Checked);
            BindEntries();
        }
        catch (Exception ex)
        {
            Log($"Backup restore failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string BuildWorkflowDetailsText()
    {
        return WorkspaceUiStyle.BuildWorkflowText(
            "Backup Manager Workflow",
            "Choose a folder manually or use the current UPK folder.",
            "Scan the folder for .bak files created by mesh, retarget, or texture operations.",
            "Select a backup in the center list to inspect its original target path.",
            "Restore Selected Backup when you want to copy the .bak file back over the original.",
            "Use this as the lightweight safety and recovery tool for modified packages and cache files.");
    }

    private void Log(string message)
    {
        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _useCurrentUpkFolderButton.Enabled = !busy;
        _browseFolderButton.Enabled = !busy;
        _scanBackupsButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _restoreSelectedButton.Enabled = !busy;
        UseWaitCursor = busy;
        Form form = FindForm();
        if (form != null)
            form.UseWaitCursor = busy;
    }

    private void AddRow(Control control, int bottomSpacing = 8)
    {
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        control.Margin = new Padding(0, 0, 0, bottomSpacing);
        int row = _leftLayout.RowCount++;
        _leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _leftLayout.Controls.Add(control, 0, row);
    }

    private void UpdateContentSplit()
    {
        if (!_contentSplitInitialized || _contentSplit.Width <= 0)
            return;

        FixedDetailsSplitLayout.Apply(_contentSplit, DetailsPanelWidth);
    }

    private static Button CreateButton(string text)
    {
        return new Button
        {
            Text = text,
            UseVisualStyleBackColor = true,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, 46)
        };
    }

    private static TextBox CreatePathTextBox(string text)
    {
        return new TextBox
        {
            Text = text,
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Top,
            Height = 72,
            ScrollBars = ScrollBars.Vertical,
            BackColor = SystemColors.Window
        };
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Top
        };
    }

    private static string FormatSize(long bytes)
    {
        double value = bytes;
        string[] units = ["B", "KB", "MB", "GB"];
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
