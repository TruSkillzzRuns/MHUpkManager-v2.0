using MHUpkManager.MeshPreview;
using MHUpkManager.UI;

namespace MHUpkManager.Retargeting;

internal sealed class SkeletalMeshRetargeterPanel : UserControl
{
    private const int LeftPanelWidth = 430;
    private const int BottomLogHeight = 160;
    private const int WorkflowPanelWidth = 460;

    private readonly Panel _contentPanel;
    private readonly Panel _leftPanelHost;
    private readonly TableLayoutPanel _leftLayout;
    private readonly Panel _rightPanel;
    private readonly Panel _logPanel;
    private readonly TextBox _upkPathTextBox;
    private readonly Button _browseUpkButton;
    private readonly Button _useCurrentUpkButton;
    private readonly ComboBox _skeletalMeshComboBox;
    private readonly ComboBox _lodComboBox;
    private readonly Button _importMeshButton;
    private readonly TextBox _meshPathTextBox;
    private readonly Button _importAnimSetButton;
    private readonly TextBox _animSetTextBox;
    private readonly Button _importTexturesButton;
    private readonly TextBox _texturesTextBox;
    private readonly Button _importSkeletonButton;
    private readonly TextBox _skeletonPathTextBox;
    private readonly Button _autoMapButton;
    private readonly Label _autoOrientationSummaryLabel;
    private readonly Button _autoOrientationButton;
    private readonly FlowLayoutPanel _orientationAdjustPanel;
    private readonly Button _rotateLeftButton;
    private readonly Button _rotateRightButton;
    private readonly Button _rotate180Button;
    private readonly Button _pitchForwardButton;
    private readonly Button _pitchBackwardButton;
    private readonly Button _rollLeftButton;
    private readonly Button _rollRightButton;
    private readonly Label _autoScaleSummaryLabel;
    private readonly Button _autoScaleButton;
    private readonly DataGridView _boneMappingGrid;
    private readonly Label _weightTransferSummaryLabel;
    private readonly Button _weightTransferButton;
    private readonly Label _autoRigSummaryLabel;
    private readonly Button _autoRigButton;
    private readonly Label _posePreviewSummaryLabel;
    private readonly ComboBox _posePresetComboBox;
    private readonly Button _applyPosePreviewButton;
    private readonly Button _resetPosePreviewButton;
    private readonly CheckedListBox _compatibilityChecklist;
    private readonly Button _compatibilityButton;
    private readonly ProgressBar _progressBar;
    private readonly Label _progressLabel;
    private readonly Button _exportFbxButton;
    private readonly Button _replaceMeshButton;
    private readonly Label _mappingHelpLabel;
    private readonly RichTextBox _workflowDetailsTextBox;
    private readonly TextBox _logTextBox;
    private readonly MeshPreviewControl _posePreviewControl;

    public SkeletalMeshRetargeterPanel()
    {
        Dock = DockStyle.Fill;

        _upkPathTextBox = CreateTextBox();
        _browseUpkButton = CreateButton("Browse UPK");
        _browseUpkButton.Click += (_, _) => BrowseUpkRequested?.Invoke(this, EventArgs.Empty);
        _useCurrentUpkButton = CreateButton("Use Current UPK");
        _useCurrentUpkButton.Click += (_, _) => UseCurrentUpkRequested?.Invoke(this, EventArgs.Empty);

        _skeletalMeshComboBox = CreateComboBox();
        _skeletalMeshComboBox.SelectedIndexChanged += (_, _) => SkeletalMeshChanged?.Invoke(this, EventArgs.Empty);
        _lodComboBox = CreateComboBox();

        _meshPathTextBox = CreateTextBox();
        _importMeshButton = CreateButton("Import Source Mesh");
        _importMeshButton.Click += (_, _) => ImportMeshRequested?.Invoke(this, EventArgs.Empty);

        _animSetTextBox = CreateTextBox();
        _importAnimSetButton = CreateButton("Import AnimSet");
        _importAnimSetButton.Click += (_, _) => ImportAnimSetRequested?.Invoke(this, EventArgs.Empty);

        _texturesTextBox = CreateTextBox();
        _importTexturesButton = CreateButton("Import Textures");
        _importTexturesButton.Click += (_, _) => ImportTexturesRequested?.Invoke(this, EventArgs.Empty);

        _skeletonPathTextBox = CreateTextBox();
        _importSkeletonButton = CreateButton("Import Target Skeleton");
        _importSkeletonButton.Click += (_, _) => ImportSkeletonRequested?.Invoke(this, EventArgs.Empty);

        _autoMapButton = CreateButton("Auto Map Bones");
        _autoMapButton.Click += (_, _) => AutoBoneMapRequested?.Invoke(this, EventArgs.Empty);

        _autoOrientationSummaryLabel = WorkspaceUiStyle.CreateValueLabel("No automatic orientation has been applied.");
        _autoOrientationButton = CreateButton("Auto Orient To Target");
        _autoOrientationButton.Click += (_, _) => AutoOrientationRequested?.Invoke(this, EventArgs.Empty);
        _rotateLeftButton = CreateButton("Rotate -90");
        _rotateLeftButton.Click += (_, _) => RotateSourceLeftRequested?.Invoke(this, EventArgs.Empty);
        _rotateRightButton = CreateButton("Rotate +90");
        _rotateRightButton.Click += (_, _) => RotateSourceRightRequested?.Invoke(this, EventArgs.Empty);
        _rotate180Button = CreateButton("Rotate 180");
        _rotate180Button.Click += (_, _) => RotateSourceFlipRequested?.Invoke(this, EventArgs.Empty);
        _pitchForwardButton = CreateButton("Pitch -90");
        _pitchForwardButton.Click += (_, _) => RotateSourcePitchForwardRequested?.Invoke(this, EventArgs.Empty);
        _pitchBackwardButton = CreateButton("Pitch +90");
        _pitchBackwardButton.Click += (_, _) => RotateSourcePitchBackwardRequested?.Invoke(this, EventArgs.Empty);
        _rollLeftButton = CreateButton("Roll -90");
        _rollLeftButton.Click += (_, _) => RotateSourceRollLeftRequested?.Invoke(this, EventArgs.Empty);
        _rollRightButton = CreateButton("Roll +90");
        _rollRightButton.Click += (_, _) => RotateSourceRollRightRequested?.Invoke(this, EventArgs.Empty);
        _orientationAdjustPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true
        };
        _orientationAdjustPanel.Controls.Add(_rotateLeftButton);
        _orientationAdjustPanel.Controls.Add(_rotateRightButton);
        _orientationAdjustPanel.Controls.Add(_rotate180Button);
        _orientationAdjustPanel.Controls.Add(_pitchForwardButton);
        _orientationAdjustPanel.Controls.Add(_pitchBackwardButton);
        _orientationAdjustPanel.Controls.Add(_rollLeftButton);
        _orientationAdjustPanel.Controls.Add(_rollRightButton);

        _autoScaleSummaryLabel = WorkspaceUiStyle.CreateValueLabel("No automatic scale has been applied.");
        _autoScaleButton = CreateButton("Auto Scale To Target");
        _autoScaleButton.Click += (_, _) => AutoScaleRequested?.Invoke(this, EventArgs.Empty);

        _weightTransferSummaryLabel = WorkspaceUiStyle.CreateValueLabel("No weight transfer has been run yet.");
        _weightTransferButton = CreateButton("Apply Weight Transfer");
        _weightTransferButton.Click += (_, _) => WeightTransferRequested?.Invoke(this, EventArgs.Empty);

        _autoRigSummaryLabel = WorkspaceUiStyle.CreateValueLabel("One-click bind to the original MHO skeleton has not been run.");
        _autoRigButton = CreateButton("One-Click Bind To Skeleton");
        _autoRigButton.Click += (_, _) => AutoRigRequested?.Invoke(this, EventArgs.Empty);

        _posePreviewSummaryLabel = WorkspaceUiStyle.CreateValueLabel("Pose preview is ready after weight transfer or one-click bind.");
        _posePresetComboBox = CreateComboBox();
        _posePresetComboBox.Items.AddRange(Enum.GetNames(typeof(RetargetPosePreset)));
        _posePresetComboBox.SelectedItem = nameof(RetargetPosePreset.BindPose);
        _applyPosePreviewButton = CreateButton("Apply Pose Preview");
        _applyPosePreviewButton.Click += (_, _) => ApplyPosePreviewRequested?.Invoke(this, EventArgs.Empty);
        _resetPosePreviewButton = CreateButton("Reset Pose Preview");
        _resetPosePreviewButton.Click += (_, _) => ResetPosePreviewRequested?.Invoke(this, EventArgs.Empty);

        _compatibilityChecklist = new CheckedListBox
        {
            Dock = DockStyle.Top,
            Height = 96,
            CheckOnClick = false
        };
        _compatibilityChecklist.Items.AddRange(
        [
            "Strip UE4/UE5 metadata",
            "Collapse to LOD0",
            "Ensure UE3 bone ordering",
            "Preserve vertex colors / UVs / smoothing"
        ]);
        for (int i = 0; i < _compatibilityChecklist.Items.Count; i++)
            _compatibilityChecklist.SetItemChecked(i, true);

        _compatibilityButton = CreateButton("Apply UE3 Fixes");
        _compatibilityButton.Click += (_, _) => CompatibilityFixRequested?.Invoke(this, EventArgs.Empty);

        _progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Minimum = 0,
            Maximum = 100,
            Height = 24
        };
        _progressLabel = WorkspaceUiStyle.CreateValueLabel("Idle");

        _exportFbxButton = CreateButton("Export FBX");
        _exportFbxButton.Click += (_, _) => ExportFbxRequested?.Invoke(this, EventArgs.Empty);

        _replaceMeshButton = CreateButton("Replace Mesh In UPK");
        _replaceMeshButton.Click += (_, _) => ReplaceMeshRequested?.Invoke(this, EventArgs.Empty);

        _boneMappingGrid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeight = 48,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ReadOnly = true,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect
        };
        _boneMappingGrid.Columns.Add("upkBoneColumn", "UPK Bone");
        _boneMappingGrid.Columns.Add("playerBoneColumn", "Player Bone");
        WorkspaceUiStyle.StyleGrid(_boneMappingGrid);
        _boneMappingGrid.Columns["upkBoneColumn"].MinimumWidth = 180;
        _boneMappingGrid.Columns["upkBoneColumn"].FillWeight = 50;
        _boneMappingGrid.Columns["playerBoneColumn"].MinimumWidth = 180;
        _boneMappingGrid.Columns["playerBoneColumn"].FillWeight = 50;

        _mappingHelpLabel = WorkspaceUiStyle.CreateValueLabel(
            "UPK Bone is the destination bone from the selected game SkeletalMesh. Player Bone is the imported source bone mapped onto it.");
        _mappingHelpLabel.Dock = DockStyle.Top;
        _mappingHelpLabel.Height = 52;
        _mappingHelpLabel.Padding = new Padding(0, 4, 0, 8);

        _workflowDetailsTextBox = WorkspaceUiStyle.CreateReadOnlyDetailsTextBox(
            "SkeletalMesh Retargeter Workflow" + Environment.NewLine + Environment.NewLine +
            "1. Choose the target character." + Environment.NewLine +
            "Browse the UPK, then select the game SkeletalMesh export and LOD you want to replace." + Environment.NewLine + Environment.NewLine +
            "2. Import the source assets." + Environment.NewLine +
            "Import Source Mesh is required. Import Target Skeleton is useful when the source mesh came from a different rig. Import AnimSet and Import Textures are optional support assets." + Environment.NewLine + Environment.NewLine +
            "3. Generate the first bone map." + Environment.NewLine +
            "Run Auto Map Bones, then review the Bone Mapping table. UPK Bone is the destination game bone. Player Bone is the imported source bone that will drive it." + Environment.NewLine + Environment.NewLine +
            "4. Align the source mesh." + Environment.NewLine +
            "Run Auto Orient To Target to correct forward and up axes. Use the manual rotate, pitch, and roll buttons only when the source mesh still faces the wrong way. Run Auto Scale To Target when the mesh is too large or too small." + Environment.NewLine + Environment.NewLine +
            "5. Transfer weights and bind." + Environment.NewLine +
            "Apply Weight Transfer to move skin weights into the target skeleton context. Then run One-Click Bind To Skeleton after mapping, orientation, and scale look correct." + Environment.NewLine + Environment.NewLine +
            "6. Preview diagnostic poses." + Environment.NewLine +
            "Use Apply Pose Preview after weight transfer or one-click bind to spot shoulder collapse, elbow pinching, wrist twist, and leg deformation before export." + Environment.NewLine + Environment.NewLine +
            "7. Normalize for UE3." + Environment.NewLine +
            "Apply UE3 Fixes to normalize the result for Marvel Heroes package requirements. Keep the checklist enabled unless you have a specific reason to preserve a different layout." + Environment.NewLine + Environment.NewLine +
            "8. Export or replace." + Environment.NewLine +
            "Export FBX writes the current retargeted result for inspection in external tools. Replace Mesh In UPK writes the final mesh back into the selected package." + Environment.NewLine + Environment.NewLine +
            "Recommended order" + Environment.NewLine +
            "Browse UPK -> select SkeletalMesh -> Import Source Mesh -> Auto Map Bones -> Auto Orient To Target -> Auto Scale To Target -> Apply Weight Transfer -> One-Click Bind To Skeleton -> Apply Pose Preview -> Apply UE3 Fixes -> Export FBX or Replace Mesh In UPK.");

        _posePreviewControl = new MeshPreviewControl
        {
            Dock = DockStyle.Fill
        };
        _posePreviewControl.SetBackend(MeshPreviewBackend.VorticeDirect3D11);
        _posePreviewControl.Scene.DisableBackfaceCullingForFbx = true;

        _logTextBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = "Retargeting progress, mapping decisions, compatibility fixes, and UPK replacement steps appear below."
        };

        _leftPanelHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true
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

        AddLeftRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(1, "Target"));
        AddLeftRow(CreateLabel("UPK File:"));
        AddLeftRow(_upkPathTextBox);
        AddLeftRow(_browseUpkButton);
        AddLeftRow(_useCurrentUpkButton);
        AddLeftRow(CreateLabel("SkeletalMesh Export:"));
        AddLeftRow(_skeletalMeshComboBox);
        AddLeftRow(CreateLabel("LOD Selection:"));
        AddLeftRow(_lodComboBox);
        AddLeftRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(2, "Source Assets"));
        AddLeftRow(CreateLabel("Imported Mesh:"));
        AddLeftRow(_meshPathTextBox);
        AddLeftRow(_importMeshButton);
        AddLeftRow(CreateLabel("Imported AnimSet:"));
        AddLeftRow(_animSetTextBox);
        AddLeftRow(_importAnimSetButton);
        AddLeftRow(CreateLabel("Imported Textures:"));
        AddLeftRow(_texturesTextBox);
        AddLeftRow(_importTexturesButton);
        AddLeftRow(CreateLabel("Player Skeleton FBX:"));
        AddLeftRow(_skeletonPathTextBox);
        AddLeftRow(_importSkeletonButton);
        AddLeftRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(3, "Retarget Steps"));
        AddLeftRow(_autoMapButton);
        AddLeftRow(CreateLabel("Orientation:"));
        AddLeftRow(_autoOrientationSummaryLabel);
        AddLeftRow(_autoOrientationButton);
        AddLeftRow(_orientationAdjustPanel);
        AddLeftRow(CreateLabel("Scale:"));
        AddLeftRow(_autoScaleSummaryLabel);
        AddLeftRow(_autoScaleButton);
        AddLeftRow(CreateLabel("Weight Transfer:"));
        AddLeftRow(_weightTransferSummaryLabel);
        AddLeftRow(_weightTransferButton);
        AddLeftRow(CreateLabel("Bind To Skeleton:"));
        AddLeftRow(_autoRigSummaryLabel);
        AddLeftRow(_autoRigButton);
        AddLeftRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(6, "Pose Preview"));
        AddLeftRow(CreateLabel("Pose Preset:"));
        AddLeftRow(_posePresetComboBox);
        AddLeftRow(_posePreviewSummaryLabel);
        AddLeftRow(_applyPosePreviewButton);
        AddLeftRow(_resetPosePreviewButton);
        AddLeftRow(CreateLabel("UE3 Compatibility:"));
        AddLeftRow(_compatibilityChecklist);
        AddLeftRow(_compatibilityButton);
        AddLeftRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(8, "Output"));
        AddLeftRow(CreateLabel("Progress:"));
        AddLeftRow(_progressBar);
        AddLeftRow(_progressLabel);
        AddLeftRow(_exportFbxButton);
        AddLeftRow(_replaceMeshButton, 0);
        _leftPanelHost.Controls.Add(_leftLayout);

        _rightPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        GroupBox mappingGroup = new()
        {
            Text = "Bone Mapping",
            Dock = DockStyle.Fill
        };
        Panel mappingContentPanel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(8)
        };
        GroupBox posePreviewGroup = new()
        {
            Text = "Pose Preview",
            Dock = DockStyle.Top,
            Height = 440
        };
        posePreviewGroup.Controls.Add(_posePreviewControl);
        Panel mappingPanel = new()
        {
            Dock = DockStyle.Fill
        };
        mappingPanel.Controls.Add(_boneMappingGrid);
        mappingPanel.Controls.Add(_mappingHelpLabel);
        mappingContentPanel.Controls.Add(mappingPanel);
        mappingContentPanel.Controls.Add(posePreviewGroup);
        mappingGroup.Controls.Add(mappingContentPanel);
        GroupBox workflowDetailsGroup = new()
        {
            Text = "Workflow Details",
            Dock = DockStyle.Right,
            Width = WorkflowPanelWidth
        };
        Panel workflowDetailsPanel = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 10, 12, 12)
        };
        workflowDetailsPanel.Controls.Add(_workflowDetailsTextBox);
        workflowDetailsGroup.Controls.Add(workflowDetailsPanel);
        _rightPanel.Controls.Add(workflowDetailsGroup);
        _rightPanel.Controls.Add(mappingGroup);

        _logPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = BottomLogHeight
        };
        _logPanel.Controls.Add(_logTextBox);

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill
        };
        _leftPanelHost.Dock = DockStyle.Left;
        _leftPanelHost.Width = LeftPanelWidth;
        _rightPanel.Dock = DockStyle.Fill;
        _contentPanel.Controls.Add(_rightPanel);
        _contentPanel.Controls.Add(_leftPanelHost);

        Controls.Add(_contentPanel);
        Controls.Add(_logPanel);
    }

    public event EventHandler BrowseUpkRequested;
    public event EventHandler UseCurrentUpkRequested;
    public event EventHandler SkeletalMeshChanged;
    public event EventHandler ImportMeshRequested;
    public event EventHandler ImportAnimSetRequested;
    public event EventHandler ImportTexturesRequested;
    public event EventHandler ImportSkeletonRequested;
    public event EventHandler AutoBoneMapRequested;
    public event EventHandler AutoOrientationRequested;
    public event EventHandler RotateSourceLeftRequested;
    public event EventHandler RotateSourceRightRequested;
    public event EventHandler RotateSourceFlipRequested;
    public event EventHandler RotateSourcePitchForwardRequested;
    public event EventHandler RotateSourcePitchBackwardRequested;
    public event EventHandler RotateSourceRollLeftRequested;
    public event EventHandler RotateSourceRollRightRequested;
    public event EventHandler AutoScaleRequested;
    public event EventHandler WeightTransferRequested;
    public event EventHandler AutoRigRequested;
    public event EventHandler ApplyPosePreviewRequested;
    public event EventHandler ResetPosePreviewRequested;
    public event EventHandler CompatibilityFixRequested;
    public event EventHandler ExportFbxRequested;
    public event EventHandler ReplaceMeshRequested;

    public string UpkPath
    {
        get => _upkPathTextBox.Text;
        set => _upkPathTextBox.Text = value;
    }

    public string MeshPath
    {
        get => _meshPathTextBox.Text;
        set => _meshPathTextBox.Text = value;
    }

    public string AnimSetDisplay
    {
        get => _animSetTextBox.Text;
        set => _animSetTextBox.Text = value;
    }

    public string TexturesDisplay
    {
        get => _texturesTextBox.Text;
        set => _texturesTextBox.Text = value;
    }

    public string PlayerSkeletonPath
    {
        get => _skeletonPathTextBox.Text;
        set => _skeletonPathTextBox.Text = value;
    }

    public string SelectedMeshName => _skeletalMeshComboBox.SelectedItem as string;
    public int SelectedLodIndex => _lodComboBox.SelectedIndex < 0 ? 0 : _lodComboBox.SelectedIndex;
    public RetargetPosePreset SelectedPosePreset => Enum.TryParse<RetargetPosePreset>(_posePresetComboBox.SelectedItem as string, out RetargetPosePreset preset)
        ? preset
        : RetargetPosePreset.BindPose;

    public void SetMeshOptions(IEnumerable<string> meshNames)
    {
        _skeletalMeshComboBox.Items.Clear();
        foreach (string meshName in meshNames.OrderBy(static name => name))
            _skeletalMeshComboBox.Items.Add(meshName);

        if (_skeletalMeshComboBox.Items.Count > 0)
            _skeletalMeshComboBox.SelectedIndex = 0;
    }

    public void SetLodOptions(int lodCount)
    {
        _lodComboBox.Items.Clear();
        for (int i = 0; i < Math.Max(1, lodCount); i++)
            _lodComboBox.Items.Add($"LOD {i}");

        _lodComboBox.SelectedIndex = 0;
    }

    public void SetBusy(bool busy)
    {
        foreach (Control control in new Control[]
        {
            _browseUpkButton,
            _useCurrentUpkButton,
            _skeletalMeshComboBox,
            _lodComboBox,
            _importMeshButton,
            _importAnimSetButton,
            _importTexturesButton,
            _importSkeletonButton,
            _autoMapButton,
            _autoOrientationButton,
            _rotateLeftButton,
            _rotateRightButton,
            _rotate180Button,
            _pitchForwardButton,
            _pitchBackwardButton,
            _rollLeftButton,
            _rollRightButton,
            _autoScaleButton,
            _weightTransferButton,
            _autoRigButton,
            _posePresetComboBox,
            _applyPosePreviewButton,
            _resetPosePreviewButton,
            _compatibilityButton,
            _exportFbxButton,
            _replaceMeshButton
        })
        {
            control.Enabled = !busy;
        }
    }

    public void SetMapping(IEnumerable<KeyValuePair<string, string>> mappings)
    {
        _boneMappingGrid.Rows.Clear();
        foreach ((string source, string target) in mappings.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            _boneMappingGrid.Rows.Add(source, target);
    }

    public void SetWeightTransferSummary(string summary)
    {
        _weightTransferSummaryLabel.Text = string.IsNullOrWhiteSpace(summary) ? "No weight transfer has been run yet." : summary;
    }

    public void SetAutoRigSummary(string summary)
    {
        _autoRigSummaryLabel.Text = string.IsNullOrWhiteSpace(summary)
            ? "One-click bind to the original MHO skeleton has not been run."
            : summary;
    }

    public void SetAutoScaleSummary(string summary)
    {
        _autoScaleSummaryLabel.Text = string.IsNullOrWhiteSpace(summary) ? "No automatic scale has been applied." : summary;
    }

    public void SetAutoOrientationSummary(string summary)
    {
        _autoOrientationSummaryLabel.Text = string.IsNullOrWhiteSpace(summary) ? "No automatic orientation has been applied." : summary;
    }

    public void SetPosePreviewSummary(string summary)
    {
        _posePreviewSummaryLabel.Text = string.IsNullOrWhiteSpace(summary)
            ? "Pose preview is ready after weight transfer or one-click bind."
            : summary;
    }

    public void SetPosePreviewMesh(MeshPreviewMesh mesh)
    {
        _posePreviewControl.Scene.SetFbxMesh(mesh);
        _posePreviewControl.RefreshPreview();
        _posePreviewControl.ResetCamera();
    }

    public void ClearPosePreview()
    {
        _posePreviewControl.Scene.SetFbxMesh(null);
        _posePreviewControl.RefreshPreview();
    }

    public void ReportProgress(int value, int maximum, string message)
    {
        _progressBar.Maximum = Math.Max(1, maximum);
        _progressBar.Value = Math.Clamp(value, 0, _progressBar.Maximum);
        _progressLabel.Text = $"{(int)Math.Round((_progressBar.Value / (double)_progressBar.Maximum) * 100)}% - {message}";
        AppendLog(message);
    }

    public void AppendLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (!string.IsNullOrWhiteSpace(_logTextBox.Text))
            _logTextBox.AppendText(Environment.NewLine);
        _logTextBox.AppendText(message);
    }

    public void ClearLog()
    {
        _logTextBox.Clear();
    }

    private void AddLeftRow(Control control, int bottomSpacing = 8)
    {
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        control.Margin = new Padding(0, 0, 0, bottomSpacing);
        int row = _leftLayout.RowCount++;
        _leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _leftLayout.Controls.Add(control, 0, row);
    }

    private static TextBox CreateTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Top
        };
    }

    private static Button CreateButton(string text)
    {
        return WorkspaceUiStyle.CreateActionButton(text);
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

    private static ComboBox CreateComboBox()
    {
        return new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
    }
}
