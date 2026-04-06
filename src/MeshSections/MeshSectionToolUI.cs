using MHUpkManager.MeshPreview;
using MHUpkManager.UI;

namespace MHUpkManager.MeshSections;

internal sealed class MeshSectionToolUI : UserControl
{
    private const int LeftPanelWidth = 430;
    private const int BottomLogHeight = 160;
    private const int DetailsPanelWidth = 300;

    private readonly Func<string> _currentUpkPathProvider;
    private readonly Func<string> _currentSkeletalMeshExportPathProvider;
    private readonly MeshPreviewUI _meshPreviewUi;
    private readonly MeshSectionToolService _service = new();
    private readonly SplitContainer _verticalSplit;
    private readonly SplitContainer _contentSplit;
    private readonly TableLayoutPanel _leftLayout;
    private readonly TextBox _upkPathTextBox;
    private readonly TextBox _meshPathTextBox;
    private readonly NumericUpDown _lodNumeric;
    private readonly Button _useSelectedButton;
    private readonly Button _browseUpkButton;
    private readonly Button _analyzeButton;
    private readonly Button _loadPreviewButton;
    private readonly Button _applyPreviewHideButton;
    private readonly Button _clearPreviewHideButton;
    private readonly Button _buildStripPlanButton;
    private readonly DataGridView _sectionGrid;
    private readonly RichTextBox _detailsTextBox;
    private readonly TextBox _logTextBox;
    private MeshSectionToolResult _currentResult;
    private bool _contentSplitInitialized;

    public MeshSectionToolUI(
        MeshPreviewUI meshPreviewUi,
        Func<string> currentUpkPathProvider = null,
        Func<string> currentSkeletalMeshExportPathProvider = null)
    {
        _meshPreviewUi = meshPreviewUi;
        _currentUpkPathProvider = currentUpkPathProvider;
        _currentSkeletalMeshExportPathProvider = currentSkeletalMeshExportPathProvider;
        Dock = DockStyle.Fill;

        _upkPathTextBox = CreatePathTextBox("No UPK selected.");
        _meshPathTextBox = CreatePathTextBox("No SkeletalMesh selected.");
        _lodNumeric = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 99,
            Dock = DockStyle.Top
        };

        _useSelectedButton = CreateButton("Use Selected SkeletalMesh");
        _browseUpkButton = CreateButton("Browse");
        _analyzeButton = CreateButton("Analyze Sections");
        _loadPreviewButton = CreateButton("Load In Preview");
        _applyPreviewHideButton = CreateButton("Hide In Preview");
        _clearPreviewHideButton = CreateButton("Clear Preview Hide");
        _buildStripPlanButton = CreateButton("Build Strip Plan");

        _sectionGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        };
        WorkspaceUiStyle.StyleGrid(_sectionGrid);
        _sectionGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "visibleColumn", HeaderText = "Visible" });
        _sectionGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "stripColumn", HeaderText = "Strip" });
        _sectionGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "sectionColumn", HeaderText = "Section", ReadOnly = true });
        _sectionGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "nameColumn", HeaderText = "Name", ReadOnly = true });
        _sectionGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "materialColumn", HeaderText = "Material", ReadOnly = true });
        _sectionGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "trianglesColumn", HeaderText = "Triangles", ReadOnly = true });
        _sectionGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = "verticesColumn", HeaderText = "Vertices", ReadOnly = true });

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
        AddRow(CreateLabel("LOD Index:"));
        AddRow(_lodNumeric);
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(2, "Actions"));
        AddRow(_analyzeButton);
        AddRow(_loadPreviewButton);
        AddRow(_applyPreviewHideButton);
        AddRow(_clearPreviewHideButton);
        AddRow(_buildStripPlanButton, 0);

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
        _contentSplit.Panel1.Controls.Add(_sectionGrid);
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
        _analyzeButton.Click += async (_, _) => await AnalyzeAsync().ConfigureAwait(true);
        _loadPreviewButton.Click += async (_, _) => await LoadPreviewAsync().ConfigureAwait(true);
        _applyPreviewHideButton.Click += (_, _) => ApplyPreviewHide();
        _clearPreviewHideButton.Click += (_, _) => ClearPreviewHide();
        _buildStripPlanButton.Click += (_, _) => BuildStripPlan();
        _sectionGrid.SelectionChanged += (_, _) => ShowSelectedSectionDetails();
        _sectionGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_sectionGrid.IsCurrentCellDirty)
                _sectionGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _sectionGrid.CellValueChanged += (_, _) => ShowSelectedSectionDetails();
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

    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(_upkPathTextBox.Text) || !File.Exists(_upkPathTextBox.Text))
        {
            Log("Select a valid UPK first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_meshPathTextBox.Text))
        {
            Log("Select a SkeletalMesh first.");
            return;
        }

        try
        {
            SetBusy(true);
            _currentResult = await _service.AnalyzeAsync(_upkPathTextBox.Text, _meshPathTextBox.Text, (int)_lodNumeric.Value).ConfigureAwait(true);
            BindResult(_currentResult);
            Log($"Analyzed {_currentResult.Sections.Count} sections for LOD {_currentResult.LodIndex}.");
        }
        catch (Exception ex)
        {
            Log($"Section analysis failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task LoadPreviewAsync()
    {
        if (_currentResult == null)
        {
            await AnalyzeAsync().ConfigureAwait(true);
            if (_currentResult == null)
                return;
        }

        try
        {
            SetBusy(true);
            await _meshPreviewUi.LoadUe3MeshFromUpkAsync(_currentResult.UpkPath, _currentResult.SkeletalMeshExportPath, _currentResult.LodIndex).ConfigureAwait(true);
            ApplyPreviewHide();
            Log("Loaded selected SkeletalMesh into Mesh Preview.");
        }
        catch (Exception ex)
        {
            Log($"Preview load failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyPreviewHide()
    {
        IReadOnlyList<int> hiddenSections = GetHiddenSectionIndices();
        _meshPreviewUi.SetHiddenUe3Sections(hiddenSections);
        Log(hiddenSections.Count == 0
            ? "Preview hide cleared. All sections are visible."
            : $"Preview hiding applied to {hiddenSections.Count} section(s): {string.Join(", ", hiddenSections)}");
    }

    private void ClearPreviewHide()
    {
        foreach (DataGridViewRow row in _sectionGrid.Rows)
            row.Cells["visibleColumn"].Value = true;

        _meshPreviewUi.ClearHiddenUe3Sections();
        Log("Preview hide cleared.");
        ShowSelectedSectionDetails();
    }

    private void BuildStripPlan()
    {
        if (_currentResult == null)
        {
            Log("Analyze sections first.");
            return;
        }

        IReadOnlyList<int> stripSections = GetStripSectionIndices();
        MeshSectionStripPlan plan = _service.BuildStripPlan(_currentResult, stripSections);
        _detailsTextBox.Text = BuildStripPlanDetails(plan);
        Log($"Strip plan built. Keep {plan.KeepSectionCount}, strip {plan.StripSectionCount}, remove {plan.RemovedTriangleCount} triangle(s).");
    }

    private void BindResult(MeshSectionToolResult result)
    {
        _sectionGrid.Rows.Clear();
        foreach (MeshSectionInfo section in result.Sections)
        {
            int rowIndex = _sectionGrid.Rows.Add(
                true,
                false,
                section.SectionIndex,
                section.SectionName,
                section.MaterialIndex,
                section.TriangleCount,
                section.EstimatedVertexCount);
            _sectionGrid.Rows[rowIndex].Tag = section;
        }

        if (_sectionGrid.Rows.Count > 0)
            _sectionGrid.Rows[0].Selected = true;
        else
            _detailsTextBox.Text = BuildWorkflowDetailsText() + Environment.NewLine + Environment.NewLine + "No section data available.";
    }

    private void ShowSelectedSectionDetails()
    {
        if (_sectionGrid.SelectedRows.Count == 0 || _sectionGrid.SelectedRows[0].Tag is not MeshSectionInfo section)
        {
            _detailsTextBox.Text = BuildWorkflowDetailsText();
            return;
        }

        bool visible = Convert.ToBoolean(_sectionGrid.SelectedRows[0].Cells["visibleColumn"].Value ?? true);
        bool strip = Convert.ToBoolean(_sectionGrid.SelectedRows[0].Cells["stripColumn"].Value ?? false);
        _detailsTextBox.Text = string.Join(Environment.NewLine,
        [
            BuildWorkflowDetailsText(),
            string.Empty,
            "Current Section",
            $"SectionIndex: {section.SectionIndex}",
            $"SectionName: {section.SectionName}",
            $"MaterialIndex: {section.MaterialIndex}",
            $"TriangleCount: {section.TriangleCount}",
            $"EstimatedVertexCount: {section.EstimatedVertexCount}",
            $"ChunkIndex: {section.ChunkIndex}",
            $"MaterialPath: {section.MaterialPath}",
            $"Preview: {(visible ? "Visible" : "Hidden")}",
            $"Strip Plan: {(strip ? "Marked For Strip" : "Keep")}",
            string.Empty,
            $"Next: {(strip ? "Build Strip Plan to review what this removal would change." : visible ? "Uncheck Visible to hide this section in Mesh Preview." : "Use Hide In Preview to isolate the remaining mesh sections.")}"
        ]);
    }

    private IReadOnlyList<int> GetHiddenSectionIndices()
    {
        List<int> hidden = [];
        foreach (DataGridViewRow row in _sectionGrid.Rows)
        {
            if (row.Tag is not MeshSectionInfo section)
                continue;

            bool visible = Convert.ToBoolean(row.Cells["visibleColumn"].Value ?? true);
            if (!visible)
                hidden.Add(section.SectionIndex);
        }

        return hidden;
    }

    private IReadOnlyList<int> GetStripSectionIndices()
    {
        List<int> strip = [];
        foreach (DataGridViewRow row in _sectionGrid.Rows)
        {
            if (row.Tag is not MeshSectionInfo section)
                continue;

            bool marked = Convert.ToBoolean(row.Cells["stripColumn"].Value ?? false);
            if (marked)
                strip.Add(section.SectionIndex);
        }

        return strip;
    }

    private void SetBusy(bool busy)
    {
        _useSelectedButton.Enabled = !busy;
        _browseUpkButton.Enabled = !busy;
        _analyzeButton.Enabled = !busy;
        _loadPreviewButton.Enabled = !busy;
        _applyPreviewHideButton.Enabled = !busy;
        _clearPreviewHideButton.Enabled = !busy;
        _buildStripPlanButton.Enabled = !busy;
        _lodNumeric.Enabled = !busy;
        _sectionGrid.Enabled = !busy;
    }

    private void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (_logTextBox.TextLength > 0)
            _logTextBox.AppendText(Environment.NewLine);
        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private void AddRow(Control control, int bottomSpacing = 8)
    {
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        control.Margin = new Padding(0, 0, 0, bottomSpacing);
        int rowIndex = _leftLayout.RowCount++;
        _leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _leftLayout.Controls.Add(control, 0, rowIndex);
    }

    private void UpdateContentSplit()
    {
        if (_contentSplit.Width <= 0)
            return;

        FixedDetailsSplitLayout.Apply(_contentSplit, DetailsPanelWidth, detailsPanelMinSize: 280, contentPanelMinSize: 420);
    }

    private static string BuildWorkflowDetailsText()
    {
        return string.Join(Environment.NewLine,
        [
            "Mesh Sections Workflow",
            string.Empty,
            "1. Choose a SkeletalMesh and LOD, then run Analyze Sections to inspect the current section layout.",
            "2. Use the Visible column and Hide In Preview to isolate body parts, accessories, or duplicate geometry in Mesh Preview.",
            "3. Use Load In Preview when you want the selected SkeletalMesh loaded into the Mesh workspace preview first.",
            "4. Mark Strip on the sections you want to remove from a future replacement build.",
            "5. Build Strip Plan to review the keep versus strip result before any destructive mesh editing is added."
        ]);
    }

    private static string BuildStripPlanDetails(MeshSectionStripPlan plan)
    {
        List<string> lines =
        [
            BuildWorkflowDetailsText(),
            string.Empty,
            "Strip Plan",
            $"KeepSectionCount: {plan.KeepSectionCount}",
            $"StripSectionCount: {plan.StripSectionCount}",
            $"RemovedTriangleCount: {plan.RemovedTriangleCount}",
            $"RemainingTriangleCount: {plan.RemainingTriangleCount}"
        ];

        foreach (MeshSectionStripPlanEntry entry in plan.Entries)
            lines.Add($"Section {entry.SectionIndex}: {entry.Action}, MaterialIndex {entry.MaterialIndex}, Triangles {entry.TriangleCount}, Material {entry.MaterialPath}");

        lines.Add(string.Empty);
        lines.Add("Next: This is a planning tool in the current build. Use it to decide which sections should eventually be stripped before wiring destructive mesh edits.");
        return string.Join(Environment.NewLine, lines);
    }

    private static Button CreateButton(string text)
    {
        return WorkspaceUiStyle.CreateActionButton(text);
    }

    private static TextBox CreatePathTextBox(string text)
    {
        return new TextBox
        {
            Text = text,
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Top,
            Height = 62,
            BackColor = SystemColors.Window,
            ForeColor = Color.DimGray,
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

            Button okButton = new() { Text = "Load", Dock = DockStyle.Bottom, Height = 48 };
            okButton.Click += (_, _) => DialogResult = DialogResult.OK;

            Controls.Add(_listBox);
            Controls.Add(okButton);
        }

        public string SelectedExportPath => _listBox.SelectedItem as string;
    }
}
