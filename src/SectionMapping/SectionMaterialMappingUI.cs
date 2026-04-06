using MHUpkManager.UI;

namespace MHUpkManager.SectionMapping;

internal sealed class SectionMaterialMappingUI : UserControl
{
    private const int LeftPanelWidth = 430;
    private const int BottomLogHeight = 160;
    private const int DetailsPanelWidth = 260;
    private bool _contentSplitInitialized;

    private readonly Func<string> _currentUpkPathProvider;
    private readonly Func<string> _currentSkeletalMeshExportPathProvider;
    private readonly SectionMaterialMappingService _service = new();
    private readonly SplitContainer _verticalSplit;
    private readonly SplitContainer _contentSplit;
    private readonly TableLayoutPanel _leftLayout;
    private readonly TextBox _upkPathTextBox;
    private readonly TextBox _meshPathTextBox;
    private readonly TextBox _fbxPathTextBox;
    private readonly NumericUpDown _lodNumeric;
    private readonly CheckBox _allowTopologyChangeCheckBox;
    private readonly Button _useSelectedButton;
    private readonly Button _browseUpkButton;
    private readonly Button _browseFbxButton;
    private readonly Button _analyzeButton;
    private readonly ListView _mappingListView;
    private readonly RichTextBox _detailsTextBox;
    private readonly TextBox _logTextBox;
    private SectionMaterialMappingResult _currentResult;

    public SectionMaterialMappingUI(
        Func<string> currentUpkPathProvider = null,
        Func<string> currentSkeletalMeshExportPathProvider = null)
    {
        _currentUpkPathProvider = currentUpkPathProvider;
        _currentSkeletalMeshExportPathProvider = currentSkeletalMeshExportPathProvider;
        Dock = DockStyle.Fill;

        _upkPathTextBox = CreatePathTextBox("No UPK selected.");
        _meshPathTextBox = CreatePathTextBox("No SkeletalMesh selected.");
        _fbxPathTextBox = CreatePathTextBox("No FBX selected.");
        _lodNumeric = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 99,
            Dock = DockStyle.Top
        };
        _allowTopologyChangeCheckBox = new CheckBox
        {
            Text = "Allow Topology Change",
            Dock = DockStyle.Top,
            AutoSize = true
        };

        _useSelectedButton = CreateButton("Use Selected SkeletalMesh");
        _browseUpkButton = CreateButton("Browse");
        _browseFbxButton = CreateButton("Browse");
        _analyzeButton = CreateButton("Analyze Mapping");

        _mappingListView = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details,
            GridLines = true,
            Font = new Font(Font.FontFamily, 10.0f)
        };
        _mappingListView.Columns.Add("Original", 95);
        _mappingListView.Columns.Add("Imported", 120);
        _mappingListView.Columns.Add("Material", 110);
        _mappingListView.Columns.Add("Behavior", 520);

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
        AddRow(_useSelectedButton);
        AddRow(_browseUpkButton);
        AddRow(CreateLabel("UPK:"));
        AddRow(_upkPathTextBox);
        AddRow(CreateLabel("SkeletalMesh:"));
        AddRow(_meshPathTextBox);
        AddRow(_browseFbxButton);
        AddRow(CreateLabel("FBX:"));
        AddRow(_fbxPathTextBox);
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(3, "Actions"));
        AddRow(CreateLabel("LOD Index:"));
        AddRow(_lodNumeric);
        AddRow(_allowTopologyChangeCheckBox);
        AddRow(_analyzeButton, 0);

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
        _contentSplit.Panel1.Controls.Add(_mappingListView);
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

        _useSelectedButton.Click += (_, _) => UseSelectedSkeletalMesh();
        _browseUpkButton.Click += async (_, _) => await BrowseUpkAsync().ConfigureAwait(true);
        _browseFbxButton.Click += (_, _) => BrowseFbx();
        _analyzeButton.Click += async (_, _) => await AnalyzeAsync().ConfigureAwait(true);
        _mappingListView.SelectedIndexChanged += (_, _) => ShowSelectedDetails();
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

    private void UseSelectedSkeletalMesh()
    {
        string upkPath = _currentUpkPathProvider?.Invoke();
        string meshPath = _currentSkeletalMeshExportPathProvider?.Invoke();

        if (string.IsNullOrWhiteSpace(upkPath) || !File.Exists(upkPath))
        {
            Log("Open a UPK first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(meshPath))
        {
            Log("Select a SkeletalMesh export in the object tree first.");
            return;
        }

        _upkPathTextBox.Text = upkPath;
        _meshPathTextBox.Text = meshPath;
        Log($"Selected SkeletalMesh: {meshPath}");
    }

    private async Task BrowseUpkAsync()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Unreal Package Files (*.upk)|*.upk",
            Title = "Select UPK Containing a SkeletalMesh"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            SetBusy(true);
            List<string> exports = await _service.GetSkeletalMeshExportsAsync(dialog.FileName).ConfigureAwait(true);
            if (exports.Count == 0)
                throw new InvalidOperationException("The selected UPK did not contain any SkeletalMesh exports.");

            using SkeletalMeshSelectionForm selectionForm = new(exports);
            if (selectionForm.ShowDialog(FindForm()) != DialogResult.OK)
                return;

            _upkPathTextBox.Text = dialog.FileName;
            _meshPathTextBox.Text = selectionForm.SelectedExportPath;
            Log($"Selected SkeletalMesh: {selectionForm.SelectedExportPath}");
        }
        catch (Exception ex)
        {
            Log($"Browse failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BrowseFbx()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "FBX Files (*.fbx)|*.fbx",
            Title = "Select FBX To Analyze"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        _fbxPathTextBox.Text = dialog.FileName;
        Log($"Selected FBX: {dialog.FileName}");
    }

    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(_upkPathTextBox.Text) || !File.Exists(_upkPathTextBox.Text))
        {
            Log("Select a valid UPK first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_meshPathTextBox.Text))
        {
            Log("Select a SkeletalMesh export first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_fbxPathTextBox.Text) || !File.Exists(_fbxPathTextBox.Text))
        {
            Log("Select a valid FBX first.");
            return;
        }

        try
        {
            SetBusy(true);
            Log("Running mapping analysis.");
            _currentResult = await _service.AnalyzeAsync(
                _upkPathTextBox.Text,
                _meshPathTextBox.Text,
                _fbxPathTextBox.Text,
                Decimal.ToInt32(_lodNumeric.Value),
                _allowTopologyChangeCheckBox.Checked).ConfigureAwait(true);
            BindResult(_currentResult);
            Log($"Mapping analysis complete. Layout={_currentResult.LayoutStrategy}, importedSections={_currentResult.ImportedSectionCount}.");
        }
        catch (Exception ex)
        {
            Log($"Mapping analysis failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BindResult(SectionMaterialMappingResult result)
    {
        _mappingListView.BeginUpdate();
        try
        {
            _mappingListView.Items.Clear();
            foreach (SectionMaterialMappingEntry entry in result.Entries)
            {
                string importedLabel = entry.SourceImportedSectionIndices.Count == 0
                    ? "-"
                    : string.Join(",", entry.SourceImportedSectionIndices);

                ListViewItem item = new(entry.OriginalSectionIndex.ToString());
                item.SubItems.Add(importedLabel);
                item.SubItems.Add(entry.FinalMaterialIndex.ToString());
                item.SubItems.Add(entry.Behavior);
                item.Tag = entry;
                _mappingListView.Items.Add(item);
            }
        }
        finally
        {
            _mappingListView.EndUpdate();
        }

        if (_mappingListView.Items.Count > 0)
            _mappingListView.Items[0].Selected = true;
        else
            _detailsTextBox.Text = BuildWorkflowDetailsText() + Environment.NewLine + Environment.NewLine + "No mapping data available.";
    }

    private void ShowSelectedDetails()
    {
        if (_mappingListView.SelectedItems.Count == 0 || _mappingListView.SelectedItems[0].Tag is not SectionMaterialMappingEntry entry)
        {
            _detailsTextBox.Text = BuildWorkflowDetailsText();
            return;
        }

        List<string> lines = [BuildWorkflowDetailsText(), string.Empty, "Current Mapping Entry"];
        lines.Add($"OriginalSectionIndex: {entry.OriginalSectionIndex}");
        lines.Add($"OriginalTriangleCount: {entry.OriginalTriangleCount}");
        lines.Add($"FinalMaterialIndex: {entry.FinalMaterialIndex}");
        lines.Add($"Behavior: {entry.Behavior}");
        lines.Add($"PreserveOriginal: {entry.PreserveOriginal}");
        lines.Add($"ImportedVertexCount: {entry.ImportedVertexCount}");
        lines.Add($"ImportedTriangleCount: {entry.ImportedTriangleCount}");
        lines.Add($"SourceImportedSectionIndices: {(entry.SourceImportedSectionIndices.Count == 0 ? "-" : string.Join(", ", entry.SourceImportedSectionIndices))}");
        lines.Add($"SourceImportedSectionNames: {(entry.SourceImportedSectionNames.Count == 0 ? "-" : string.Join(", ", entry.SourceImportedSectionNames))}");
        lines.Add($"SourceImportedMaterialNames: {(entry.SourceImportedMaterialNames.Count == 0 ? "-" : string.Join(", ", entry.SourceImportedMaterialNames))}");

        if (_currentResult != null)
        {
            lines.Add(string.Empty);
            lines.Add($"LayoutStrategy: {_currentResult.LayoutStrategy}");
            lines.Add($"ImportedSectionCount: {_currentResult.ImportedSectionCount}");
            lines.Add($"UsedSingleSectionSplit: {_currentResult.UsedSingleSectionSplit}");
            lines.Add($"SplitStrategy: {_currentResult.SplitStrategy}");
        }

        lines.Add(string.Empty);
        lines.Add("Use this to confirm preserve, merge, or split behavior before import. If the final material hookup still looks wrong, compare the result with Material Inspector.");
        _detailsTextBox.Text = string.Join(Environment.NewLine, lines);
    }

    private static string BuildWorkflowDetailsText()
    {
        return WorkspaceUiStyle.BuildWorkflowText(
            "Section Mapping Workflow",
            "Select the target SkeletalMesh from the current UPK or Browse.",
            "Select the FBX you plan to import or retarget from. Unrigged replacement meshes are allowed here because this tool is analyzing section layout, not final skinning.",
            "Set the LOD and whether topology changes are allowed.",
            "Run Analyze Mapping to preview section preservation, merging, splitting, and final material indices.",
            "Use the result to confirm the importer layout before replacing the mesh.");
    }

    private void Log(string message)
    {
        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetBusy(bool busy)
    {
        _useSelectedButton.Enabled = !busy;
        _browseUpkButton.Enabled = !busy;
        _browseFbxButton.Enabled = !busy;
        _analyzeButton.Enabled = !busy;
        UseWaitCursor = busy;
        Form form = FindForm();
        if (form != null)
            form.UseWaitCursor = busy;
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
            Height = 64,
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

    private static Label CreateSectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            Height = 40,
            Dock = DockStyle.Top,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            ForeColor = Color.FromArgb(60, 60, 60),
            Padding = new Padding(0, 6, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft
        };
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

    private sealed class SkeletalMeshSelectionForm : Form
    {
        private readonly ListBox _listBox;

        public SkeletalMeshSelectionForm(IEnumerable<string> exportPaths)
        {
            Text = "Select SkeletalMesh Export";
            Width = 640;
            Height = 480;
            StartPosition = FormStartPosition.CenterParent;

            _listBox = new ListBox { Dock = DockStyle.Fill };
            _listBox.Items.AddRange(exportPaths.ToArray());
            if (_listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;
            _listBox.DoubleClick += (_, _) => ConfirmSelection();

            Button okButton = new() { Text = "Select", Dock = DockStyle.Bottom, Height = 48 };
            okButton.Click += (_, _) => ConfirmSelection();

            Controls.Add(_listBox);
            Controls.Add(okButton);
        }

        public string SelectedExportPath => _listBox.SelectedItem as string;

        private void ConfirmSelection()
        {
            if (_listBox.SelectedItem == null)
                return;

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
