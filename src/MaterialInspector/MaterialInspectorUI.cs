using MHUpkManager.UI;

namespace MHUpkManager.MaterialInspector;

internal sealed class MaterialInspectorUI : UserControl
{
    private const int LeftPanelWidth = 430;
    private const int BottomLogHeight = 160;
    private const int DetailsPanelWidth = 260;

    private readonly Func<string> _currentUpkPathProvider;
    private readonly Func<string> _currentSkeletalMeshExportPathProvider;
    private readonly MaterialInspectorService _service = new();
    private readonly SplitContainer _verticalSplit;
    private readonly SplitContainer _contentSplit;
    private readonly Panel _leftPanelHost;
    private readonly TableLayoutPanel _leftLayout;
    private readonly Button _loadSelectedButton;
    private readonly Button _browseUpkButton;
    private readonly Button _refreshButton;
    private readonly TextBox _sourceTextBox;
    private readonly ListView _sectionListView;
    private readonly RichTextBox _detailsTextBox;
    private readonly TextBox _logTextBox;
    private MaterialInspectorResult _currentResult;
    private bool _busy;
    private bool _contentSplitInitialized;

    public MaterialInspectorUI(
        Func<string> currentUpkPathProvider = null,
        Func<string> currentSkeletalMeshExportPathProvider = null)
    {
        _currentUpkPathProvider = currentUpkPathProvider;
        _currentSkeletalMeshExportPathProvider = currentSkeletalMeshExportPathProvider;
        Dock = DockStyle.Fill;

        _loadSelectedButton = CreateButton("Inspect Selected SkeletalMesh");
        _browseUpkButton = CreateButton("Browse");
        _refreshButton = CreateButton("Refresh Current Result");
        _sourceTextBox = new TextBox
        {
            Text = "No SkeletalMesh inspected.",
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Top,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.FixedSingle,
            Height = 88,
            BackColor = SystemColors.Window,
            ForeColor = Color.DimGray,
            WordWrap = true
        };

        _sectionListView = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details,
            GridLines = true
        };
        _sectionListView.Font = new Font(Font.FontFamily, 10.0f);
        _sectionListView.Columns.Add("Section", 90);
        _sectionListView.Columns.Add("Material", 90);
        _sectionListView.Columns.Add("Type", 220);

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
        AddRow(_loadSelectedButton);
        AddRow(_browseUpkButton);
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(2, "Actions"));
        AddRow(_refreshButton);
        AddRow(_sourceTextBox, 0);

        _leftPanelHost = new Panel
        {
            Dock = DockStyle.Left,
            Width = LeftPanelWidth,
            MinimumSize = new Size(LeftPanelWidth, 0),
            AutoScroll = true
        };
        _leftPanelHost.Controls.Add(_leftLayout);

        _contentSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        _contentSplit.Panel1.Controls.Add(_sectionListView);
        _contentSplit.Panel2.Controls.Add(_detailsTextBox);

        Panel contentPanel = new() { Dock = DockStyle.Fill };
        contentPanel.Controls.Add(_contentSplit);
        contentPanel.Controls.Add(_leftPanelHost);

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

        _loadSelectedButton.Click += async (_, _) => await InspectSelectedAsync().ConfigureAwait(true);
        _browseUpkButton.Click += async (_, _) => await BrowseUpkAsync().ConfigureAwait(true);
        _refreshButton.Click += async (_, _) => await RefreshCurrentAsync().ConfigureAwait(true);
        _sectionListView.SelectedIndexChanged += (_, _) => ShowSelectedSectionDetails();
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

    private async Task InspectSelectedAsync()
    {
        string upkPath = _currentUpkPathProvider?.Invoke();
        string exportPath = _currentSkeletalMeshExportPathProvider?.Invoke();

        if (string.IsNullOrWhiteSpace(upkPath) || !File.Exists(upkPath))
        {
            Log("Open a UPK and select a SkeletalMesh first, or use Browse UPK.");
            return;
        }

        if (string.IsNullOrWhiteSpace(exportPath))
        {
            Log("Select a SkeletalMesh export in the object tree first.");
            return;
        }

        await LoadResultAsync(upkPath, exportPath).ConfigureAwait(true);
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
            Log($"Reading SkeletalMesh exports from {dialog.FileName}");
            List<string> exports = await _service.GetSkeletalMeshExportsAsync(dialog.FileName).ConfigureAwait(true);
            if (exports.Count == 0)
                throw new InvalidOperationException("The selected UPK did not contain any SkeletalMesh exports.");

            using SkeletalMeshSelectionForm selectionForm = new(exports);
            if (selectionForm.ShowDialog(FindForm()) != DialogResult.OK)
                return;

            await LoadResultAsync(dialog.FileName, selectionForm.SelectedExportPath).ConfigureAwait(true);
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

    private async Task RefreshCurrentAsync()
    {
        if (_currentResult == null)
        {
            Log("No current inspection result to refresh.");
            return;
        }

        await LoadResultAsync(_currentResult.UpkPath, _currentResult.SkeletalMeshExportPath).ConfigureAwait(true);
    }

    private async Task LoadResultAsync(string upkPath, string exportPath)
    {
        try
        {
            SetBusy(true);
            Log($"Inspecting {exportPath}");
            _currentResult = await _service.InspectAsync(upkPath, exportPath).ConfigureAwait(true);
            BindResult(_currentResult);
            Log($"Loaded {_currentResult.Sections.Count} section material mappings.");
        }
        catch (Exception ex)
        {
            Log($"Material inspection failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void BindResult(MaterialInspectorResult result)
    {
        _sourceTextBox.Text = $"{Path.GetFileName(result.UpkPath)}{Environment.NewLine}{result.SkeletalMeshExportPath}";
        _sectionListView.BeginUpdate();
        try
        {
            _sectionListView.Items.Clear();
            foreach (MaterialInspectorSectionInfo section in result.Sections)
            {
                ListViewItem item = new(section.SectionIndex.ToString());
                item.SubItems.Add(section.MaterialIndex.ToString());
                item.SubItems.Add(section.MaterialType);
                item.Tag = section;
                _sectionListView.Items.Add(item);
            }
        }
        finally
        {
            _sectionListView.EndUpdate();
        }

        if (_sectionListView.Items.Count > 0)
            _sectionListView.Items[0].Selected = true;
        else
            _detailsTextBox.Text = BuildWorkflowDetailsText() + Environment.NewLine + Environment.NewLine + "No section data available.";
    }

    private void ShowSelectedSectionDetails()
    {
        if (_sectionListView.SelectedItems.Count == 0)
        {
            _detailsTextBox.Text = BuildWorkflowDetailsText();
            return;
        }

        if (_sectionListView.SelectedItems[0].Tag is not MaterialInspectorSectionInfo section)
            return;

        List<string> lines = [BuildWorkflowDetailsText(), string.Empty, "Current Section"];
        lines.Add($"SectionIndex: {section.SectionIndex}");
        lines.Add($"MaterialIndex: {section.MaterialIndex}");
        lines.Add($"MaterialPath: {section.MaterialPath}");
        lines.Add($"MaterialType: {section.MaterialType}");
        lines.Add(string.Empty);

        for (int i = 0; i < section.MaterialChain.Count; i++)
        {
            MaterialInspectorMaterialNode node = section.MaterialChain[i];
            lines.Add($"{node.TypeName}");
            lines.Add($"Path: {node.Path}");
            if (node.BlendMode.HasValue)
                lines.Add($"BlendMode: {node.BlendMode.Value}");
            if (node.TwoSided.HasValue)
                lines.Add($"TwoSided: {node.TwoSided.Value}");

            lines.Add("TextureParameters:");
            if (node.TextureParameters.Count == 0)
            {
                lines.Add("  <none>");
            }
            else
            {
                foreach (MaterialInspectorTextureParameter parameter in node.TextureParameters)
                    lines.Add($"{parameter.Name}: {parameter.TexturePath}");
            }

            lines.Add("ScalarParameters:");
            if (node.ScalarParameters.Count == 0)
            {
                lines.Add("  <none>");
            }
            else
            {
                foreach (MaterialInspectorScalarParameter parameter in node.ScalarParameters)
                    lines.Add($"{parameter.Name}: {parameter.Value:0.###}");
            }

            lines.Add("VectorParameters:");
            if (node.VectorParameters.Count == 0)
            {
                lines.Add("  <none>");
            }
            else
            {
                foreach (MaterialInspectorVectorParameter parameter in node.VectorParameters)
                    lines.Add($"{parameter.Name}: ({parameter.Value.X:0.###}, {parameter.Value.Y:0.###}, {parameter.Value.Z:0.###})");
            }

            lines.Add(string.Empty);
        }

        lines.Add("Use this to compare the real material chain against the expected character setup before moving to Material Swap or Character Workflow.");
        _detailsTextBox.Text = string.Join(Environment.NewLine, lines);
    }

    private static string BuildWorkflowDetailsText()
    {
        return WorkspaceUiStyle.BuildWorkflowText(
            "Material Inspector Workflow",
            "Load a SkeletalMesh from the current selection or Browse.",
            "Select a section from the middle panel.",
            "Read the resolved material path and parent chain.",
            "Check texture parameters like diffuse, normal, specular, emissive, and masks.",
            "Use this to confirm what the game mesh is actually rendering with before you swap or inject anything.");
    }

    private void Log(string message)
    {
        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _loadSelectedButton.Enabled = !busy;
        _browseUpkButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
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

    private void AddRow(Control control, int bottomSpacing = 8)
    {
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        control.Margin = new Padding(0, 0, 0, bottomSpacing);
        int row = _leftLayout.RowCount++;
        _leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _leftLayout.Controls.Add(control, 0, row);
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

            Button okButton = new() { Text = "Inspect", Dock = DockStyle.Bottom, Height = 48 };
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
