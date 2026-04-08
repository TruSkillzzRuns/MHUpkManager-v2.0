using MHUpkManager.EnemyConverter;
using MHUpkManager.TexturePreview;
using MHUpkManager.UI;

namespace MHUpkManager.UiEditor;

internal sealed class UiEditorPanel : UserControl
{
    private readonly TextBox _packagePathTextBox;
    private readonly TextBox _subjectTextBox;
    private readonly Label _summaryLabel;
    private readonly DataGridView _targetsGrid;
    private readonly Label _textureSummaryLabel;
    private readonly DataGridView _textureAssetsGrid;
    private readonly TextBox _replacementPathTextBox;
    private readonly TextBox _swfImportPathTextBox;
    private readonly RichTextBox _swfPreviewTextBox;
    private readonly RichTextBox _detailsTextBox;
    private readonly TextBox _logTextBox;
    private readonly Button _browsePackageButton;
    private readonly Button _useCurrentPackageButton;
    private readonly Button _scanButton;
    private readonly Button _dryRunPatchButton;
    private readonly Button _scanTextureAssetsButton;
    private readonly Button _previewTextureButton;
    private readonly Button _exportTextureButton;
    private readonly Button _chooseReplacementButton;
    private readonly Button _applyTextureReplacementButton;
    private readonly Button _previewSwfButton;
    private readonly Button _exportSwfRawButton;
    private readonly Button _exportSwfEmbeddedButton;
    private readonly Button _chooseSwfImportButton;
    private readonly Button _importSwfButton;
    private readonly List<EnemyClientUiTarget> _targets = [];
    private readonly List<UiEditorTextureAsset> _textureAssets = [];
    private bool _suppressSelectionEvents;

    public UiEditorPanel()
    {
        Dock = DockStyle.Fill;

        _packagePathTextBox = CreateReadOnlyField();
        _subjectTextBox = new TextBox
        {
            Dock = DockStyle.Top,
            BorderStyle = BorderStyle.FixedSingle
        };

        _browsePackageButton = WorkspaceUiStyle.CreateActionButton("Browse UI Package...");
        _browsePackageButton.Click += (_, _) => BrowsePackageRequested?.Invoke(this, EventArgs.Empty);

        _useCurrentPackageButton = WorkspaceUiStyle.CreateActionButton("Use Current UPK");
        _useCurrentPackageButton.Click += (_, _) => UseCurrentPackageRequested?.Invoke(this, EventArgs.Empty);

        _scanButton = WorkspaceUiStyle.CreateActionButton("Scan UI Targets");
        _scanButton.Click += (_, _) => ScanRequested?.Invoke(this, EventArgs.Empty);

        _dryRunPatchButton = WorkspaceUiStyle.CreateActionButton("Create Dry-Run Patched Copy");
        _dryRunPatchButton.Click += (_, _) => DryRunPatchRequested?.Invoke(this, EventArgs.Empty);

        _scanTextureAssetsButton = WorkspaceUiStyle.CreateActionButton("Scan Base Image Assets");
        _scanTextureAssetsButton.Click += (_, _) => ScanTextureAssetsRequested?.Invoke(this, EventArgs.Empty);

        _previewTextureButton = WorkspaceUiStyle.CreateActionButton("Preview Selected Image");
        _previewTextureButton.Click += (_, _) => PreviewTextureRequested?.Invoke(this, EventArgs.Empty);

        _exportTextureButton = WorkspaceUiStyle.CreateActionButton("Export Selected Image");
        _exportTextureButton.Click += (_, _) => ExportTextureRequested?.Invoke(this, EventArgs.Empty);

        _chooseReplacementButton = WorkspaceUiStyle.CreateActionButton("Choose Replacement Image");
        _chooseReplacementButton.Click += (_, _) => ChooseReplacementRequested?.Invoke(this, EventArgs.Empty);

        _applyTextureReplacementButton = WorkspaceUiStyle.CreateActionButton("Apply Image Replacement");
        _applyTextureReplacementButton.Click += (_, _) => ApplyTextureReplacementRequested?.Invoke(this, EventArgs.Empty);

        _previewSwfButton = WorkspaceUiStyle.CreateActionButton("Preview Selected SWF Export");
        _previewSwfButton.Click += (_, _) => PreviewSwfRequested?.Invoke(this, EventArgs.Empty);

        _exportSwfRawButton = WorkspaceUiStyle.CreateActionButton("Export Raw SWF Export");
        _exportSwfRawButton.Click += (_, _) => ExportSwfRawRequested?.Invoke(this, EventArgs.Empty);

        _exportSwfEmbeddedButton = WorkspaceUiStyle.CreateActionButton("Export Embedded SWF/GFX");
        _exportSwfEmbeddedButton.Click += (_, _) => ExportSwfEmbeddedRequested?.Invoke(this, EventArgs.Empty);

        _chooseSwfImportButton = WorkspaceUiStyle.CreateActionButton("Choose SWF/GFX/Raw Import");
        _chooseSwfImportButton.Click += (_, _) => ChooseSwfImportRequested?.Invoke(this, EventArgs.Empty);

        _importSwfButton = WorkspaceUiStyle.CreateActionButton("Re-Import Selected SWF Export");
        _importSwfButton.Click += (_, _) => ImportSwfRequested?.Invoke(this, EventArgs.Empty);

        _summaryLabel = WorkspaceUiStyle.CreateValueLabel("Pick a cooked UI package, then scan for exports and icon/object hits.");
        _textureSummaryLabel = WorkspaceUiStyle.CreateValueLabel("Scan Texture2D exports here when you want to replace icon/base-image assets.");

        _targetsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = false,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        WorkspaceUiStyle.StyleGrid(_targetsGrid);
        _targetsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "scoreColumn", HeaderText = "Score", FillWeight = 10f });
        _targetsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "exportColumn", HeaderText = "Export", FillWeight = 42f });
        _targetsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "classColumn", HeaderText = "Class", FillWeight = 12f });
        _targetsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "hitColumn", HeaderText = "Hits", FillWeight = 36f });
        _targetsGrid.SelectionChanged += (_, _) =>
        {
            if (_suppressSelectionEvents)
                return;

            RefreshDetails();
            SelectedTargetChanged?.Invoke(this, EventArgs.Empty);
        };

        _textureAssetsGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            MultiSelect = false,
            RowHeadersVisible = false,
            AutoGenerateColumns = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        WorkspaceUiStyle.StyleGrid(_textureAssetsGrid);
        _textureAssetsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "textureExportColumn", HeaderText = "Texture Export", FillWeight = 58f });
        _textureAssetsGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "replacementColumn", HeaderText = "Replacement", FillWeight = 42f });
        _textureAssetsGrid.SelectionChanged += (_, _) =>
        {
            if (_suppressSelectionEvents)
                return;

            RefreshTextureSelectionState();
            SelectedTextureAssetChanged?.Invoke(this, EventArgs.Empty);
        };

        _replacementPathTextBox = CreateReadOnlyField();
        _swfImportPathTextBox = CreateReadOnlyField();

        _swfPreviewTextBox = WorkspaceUiStyle.CreateReadOnlyDetailsTextBox(
            "Select a swfmovie target, then preview it here to inspect the export size, embedded payload, and string hints.");

        _detailsTextBox = WorkspaceUiStyle.CreateReadOnlyDetailsTextBox(BuildDefaultDetailsText());
        _logTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = "UI Editor scan results, dry-run patch output, and package notes appear here."
        };

        TableLayoutPanel leftLayout = new()
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        AddRow(leftLayout, WorkspaceUiStyle.CreateWorkflowSectionHeader(1, "Package"));
        AddRow(leftLayout, CreateLabel("Cooked UI Package:"));
        AddRow(leftLayout, _packagePathTextBox);
        AddRow(leftLayout, _browsePackageButton);
        AddRow(leftLayout, _useCurrentPackageButton);
        AddRow(leftLayout, WorkspaceUiStyle.CreateWorkflowSectionHeader(2, "Scan Filter"));
        AddRow(leftLayout, CreateLabel("Hero / Term (optional):"));
        AddRow(leftLayout, _subjectTextBox);
        AddRow(leftLayout, _scanButton);
        AddRow(leftLayout, _summaryLabel);
        AddRow(leftLayout, CreateTargetsGroup());
        AddRow(leftLayout, WorkspaceUiStyle.CreateWorkflowSectionHeader(3, "Dry-Run Patch"));
        AddRow(leftLayout, _dryRunPatchButton);
        AddRow(leftLayout, WorkspaceUiStyle.CreateWorkflowSectionHeader(4, "Base Image Assets"));
        AddRow(leftLayout, _scanTextureAssetsButton);
        AddRow(leftLayout, _textureSummaryLabel);
        AddRow(leftLayout, CreateTextureAssetsGroup());
        AddRow(leftLayout, _previewTextureButton);
        AddRow(leftLayout, _exportTextureButton);
        AddRow(leftLayout, CreateLabel("Replacement File:"));
        AddRow(leftLayout, _replacementPathTextBox);
        AddRow(leftLayout, _chooseReplacementButton);
        AddRow(leftLayout, _applyTextureReplacementButton);
        AddRow(leftLayout, WorkspaceUiStyle.CreateWorkflowSectionHeader(5, "SWF Workspace"));
        AddRow(leftLayout, _previewSwfButton);
        AddRow(leftLayout, _exportSwfRawButton);
        AddRow(leftLayout, _exportSwfEmbeddedButton);
        AddRow(leftLayout, CreateLabel("Import File:"));
        AddRow(leftLayout, _swfImportPathTextBox);
        AddRow(leftLayout, _chooseSwfImportButton);
        AddRow(leftLayout, _importSwfButton);
        AddRow(leftLayout, CreateSwfPreviewGroup());

        GroupBox detailsGroup = new()
        {
            Text = "Details",
            Dock = DockStyle.Right,
            Width = 420
        };
        Panel detailsPanel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        detailsPanel.Controls.Add(_detailsTextBox);
        detailsGroup.Controls.Add(detailsPanel);

        Panel topPanel = new()
        {
            Dock = DockStyle.Fill
        };
        topPanel.Controls.Add(leftLayout);
        topPanel.Controls.Add(detailsGroup);

        GroupBox logGroup = new()
        {
            Text = "Log",
            Dock = DockStyle.Bottom,
            Height = 220
        };
        logGroup.Controls.Add(_logTextBox);

        Controls.Add(topPanel);
        Controls.Add(logGroup);
    }

    public event EventHandler BrowsePackageRequested;
    public event EventHandler UseCurrentPackageRequested;
    public event EventHandler ScanRequested;
    public event EventHandler DryRunPatchRequested;
    public event EventHandler SelectedTargetChanged;
    public event EventHandler ScanTextureAssetsRequested;
    public event EventHandler SelectedTextureAssetChanged;
    public event EventHandler PreviewTextureRequested;
    public event EventHandler ExportTextureRequested;
    public event EventHandler ChooseReplacementRequested;
    public event EventHandler ApplyTextureReplacementRequested;
    public event EventHandler PreviewSwfRequested;
    public event EventHandler ExportSwfRawRequested;
    public event EventHandler ExportSwfEmbeddedRequested;
    public event EventHandler ChooseSwfImportRequested;
    public event EventHandler ImportSwfRequested;

    public string PackagePath => _packagePathTextBox.Text.Trim();
    public string SubjectName => _subjectTextBox.Text.Trim();
    public string SwfImportFilePath => _swfImportPathTextBox.Text.Trim();

    public EnemyClientUiTarget SelectedTarget => _targetsGrid.SelectedRows.Count == 0
        ? null
        : _targetsGrid.SelectedRows[0].Tag as EnemyClientUiTarget;

    public UiEditorTextureAsset SelectedTextureAsset => _textureAssetsGrid.SelectedRows.Count == 0
        ? null
        : _textureAssetsGrid.SelectedRows[0].Tag as UiEditorTextureAsset;

    public void SetPackagePath(string packagePath)
    {
        _packagePathTextBox.Text = packagePath ?? string.Empty;
    }

    public void SetBusy(bool busy)
    {
        _browsePackageButton.Enabled = !busy;
        _useCurrentPackageButton.Enabled = !busy;
        _scanButton.Enabled = !busy;
        _dryRunPatchButton.Enabled = !busy;
        _scanTextureAssetsButton.Enabled = !busy;
        _previewTextureButton.Enabled = !busy;
        _exportTextureButton.Enabled = !busy;
        _chooseReplacementButton.Enabled = !busy;
        _applyTextureReplacementButton.Enabled = !busy;
        _previewSwfButton.Enabled = !busy;
        _exportSwfRawButton.Enabled = !busy;
        _exportSwfEmbeddedButton.Enabled = !busy;
        _chooseSwfImportButton.Enabled = !busy;
        _importSwfButton.Enabled = !busy;
        _subjectTextBox.Enabled = !busy;
        _targetsGrid.Enabled = !busy;
        _textureAssetsGrid.Enabled = !busy;
    }

    public void SetTargets(IReadOnlyList<EnemyClientUiTarget> targets)
    {
        _targets.Clear();
        if (targets != null)
            _targets.AddRange(targets);

        _summaryLabel.Text = _targets.Count == 0
            ? "No matching exports were found in this package."
            : $"Found {_targets.Count} matching exports in {Path.GetFileName(PackagePath)}.";

        _suppressSelectionEvents = true;
        try
        {
            _targetsGrid.Rows.Clear();
            foreach (EnemyClientUiTarget target in _targets)
            {
                string hitSummary = string.Join(", ", target.ContractHints.Concat(target.RawStringHits).Concat(target.FieldHits).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
                int rowIndex = _targetsGrid.Rows.Add(
                    target.RelevanceScore.ToString(),
                    target.ExportPath,
                    target.ClassName,
                    hitSummary);
                _targetsGrid.Rows[rowIndex].Tag = target;
            }

            if (_targetsGrid.Rows.Count > 0)
                _targetsGrid.Rows[0].Selected = true;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        RefreshDetails();
    }

    public void SetTextureAssets(IReadOnlyList<UiEditorTextureAsset> assets)
    {
        _textureAssets.Clear();
        if (assets != null)
            _textureAssets.AddRange(assets);

        _textureSummaryLabel.Text = _textureAssets.Count == 0
            ? "No matching Texture2D exports were found in this package."
            : $"Found {_textureAssets.Count} matching Texture2D exports in {Path.GetFileName(PackagePath)}.";

        _suppressSelectionEvents = true;
        try
        {
            _textureAssetsGrid.Rows.Clear();
            foreach (UiEditorTextureAsset asset in _textureAssets)
            {
                int rowIndex = _textureAssetsGrid.Rows.Add(
                    asset.ExportPath,
                    string.IsNullOrWhiteSpace(asset.ReplacementFilePath) ? "-" : Path.GetFileName(asset.ReplacementFilePath));
                _textureAssetsGrid.Rows[rowIndex].Tag = asset;
            }

            if (_textureAssetsGrid.Rows.Count > 0)
                _textureAssetsGrid.Rows[0].Selected = true;
        }
        finally
        {
            _suppressSelectionEvents = false;
        }

        RefreshTextureSelectionState();
    }

    public void SetTextureReplacementFile(string replacementFilePath)
    {
        UiEditorTextureAsset asset = SelectedTextureAsset;
        if (asset != null)
            asset.ReplacementFilePath = replacementFilePath ?? string.Empty;

        _replacementPathTextBox.Text = replacementFilePath ?? string.Empty;
        RefreshTextureRows();
    }

    public void SetSwfImportFile(string importFilePath)
    {
        _swfImportPathTextBox.Text = importFilePath ?? string.Empty;
    }

    public void SetSwfPreview(string previewText)
    {
        _swfPreviewTextBox.Text = string.IsNullOrWhiteSpace(previewText)
            ? "Select a swfmovie target, then preview it here to inspect the export size, embedded payload, and string hints."
            : previewText;
    }

    public void SetLog(IEnumerable<string> lines)
    {
        _logTextBox.Lines = lines?.ToArray() ?? [];
        if (_logTextBox.TextLength > 0)
        {
            _logTextBox.SelectionStart = _logTextBox.TextLength;
            _logTextBox.ScrollToCaret();
        }
    }

    public void AppendLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (_logTextBox.TextLength > 0)
            _logTextBox.AppendText(Environment.NewLine);

        _logTextBox.AppendText(line);
        _logTextBox.SelectionStart = _logTextBox.TextLength;
        _logTextBox.ScrollToCaret();
    }

    private GroupBox CreateTargetsGroup()
    {
        GroupBox group = new()
        {
            Text = "Candidate Exports",
            Dock = DockStyle.Top,
            Height = 220
        };
        group.Controls.Add(_targetsGrid);
        return group;
    }

    private GroupBox CreateTextureAssetsGroup()
    {
        GroupBox group = new()
        {
            Text = "Texture2D Assets",
            Dock = DockStyle.Top,
            Height = 280
        };
        group.Controls.Add(_textureAssetsGrid);
        return group;
    }

    private GroupBox CreateSwfPreviewGroup()
    {
        GroupBox group = new()
        {
            Text = "SWF Preview / Summary",
            Dock = DockStyle.Top,
            Height = 260
        };
        group.Controls.Add(_swfPreviewTextBox);
        return group;
    }

    private void RefreshDetails()
    {
        EnemyClientUiTarget target = SelectedTarget;
        _detailsTextBox.Text = target == null
            ? BuildDefaultDetailsText()
            : BuildTargetDetailsText(target);
    }

    private void RefreshTextureSelectionState()
    {
        UiEditorTextureAsset asset = SelectedTextureAsset;
        _replacementPathTextBox.Text = asset?.ReplacementFilePath ?? string.Empty;
    }

    private void RefreshTextureRows()
    {
        foreach (DataGridViewRow row in _textureAssetsGrid.Rows)
        {
            if (row.Tag is not UiEditorTextureAsset asset)
                continue;

            row.Cells["replacementColumn"].Value = string.IsNullOrWhiteSpace(asset.ReplacementFilePath)
                ? "-"
                : Path.GetFileName(asset.ReplacementFilePath);
        }
    }

    private static string BuildDefaultDetailsText()
    {
        return "UI Editor Workflow" + Environment.NewLine + Environment.NewLine +
               "1. Pick a cooked UI package such as MarvelHUD_SF.upk, MarvelFrontEnd_SF.upk, or ICO__MarvelUIIcons_Store_SF.upk." + Environment.NewLine +
               "2. Optionally enter a hero, team-up, or search term to rank relevant exports higher." + Environment.NewLine +
               "3. Scan the package to list likely SWF/UI exports, icon object names, and binding surfaces." + Environment.NewLine +
               "4. Preview and export Texture2D base image assets directly inside this tool." + Environment.NewLine +
               "5. Preview, export, and re-import swfmovie exports from the selected target." + Environment.NewLine +
               "6. Create a dry-run patched copy to safely test string/object edits without touching the live package.";
    }

    private static string BuildTargetDetailsText(EnemyClientUiTarget target)
    {
        List<string> lines =
        [
            "Selected Target",
            string.Empty,
            $"Package: {Path.GetFileName(target.PackagePath)}",
            $"Export: {target.ExportPath}",
            $"Class: {target.ClassName}",
            $"Export Index: {target.ExportIndex}",
            $"Serial Offset: 0x{target.SerialOffset:X}",
            $"Serial Size: {target.SerialSize}",
            $"Relevance Score: {target.RelevanceScore}",
            string.Empty,
            "Contract Hints"
        ];

        if (target.ContractHints.Count == 0)
            lines.Add("None");
        else
            lines.AddRange(target.ContractHints);

        lines.Add(string.Empty);
        lines.Add("Raw String Hits");
        if (target.RawStringHits.Count == 0)
            lines.Add("None");
        else
            lines.AddRange(target.RawStringHits);

        lines.Add(string.Empty);
        lines.Add("Field Hits");
        if (target.FieldHits.Count == 0)
            lines.Add("None");
        else
            lines.AddRange(target.FieldHits);

        return string.Join(Environment.NewLine, lines);
    }

    private static TextBox CreateReadOnlyField()
    {
        return new TextBox
        {
            Dock = DockStyle.Top,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Text = text
        };
    }

    private static void AddRow(TableLayoutPanel layout, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(control, 0, layout.RowCount++);
    }
}
