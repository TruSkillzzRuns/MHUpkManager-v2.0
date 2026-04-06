using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;
using MHUpkManager.TexturePreview;
using MHUpkManager.UI;
using System.Diagnostics;

namespace MHUpkManager.MeshPreview;

internal sealed class MeshPreviewUI : UserControl
{
    private const int LeftPanelWidth = 320;
    private const int BottomLogHeight = 170;
    private const int DetailsPanelWidth = 340;

    private readonly SplitContainer _verticalSplit;
    private readonly Panel _contentPanel;
    private readonly Panel _leftPanelHost;
    private readonly SplitContainer _workspaceSplit;
    private readonly Panel _viewportPanel;
    private readonly TableLayoutPanel _leftLayout;
    private readonly RichTextBox _detailsTextBox;
    private readonly Button _loadFbxButton;
    private readonly Button _loadUe3Button;
    private readonly ComboBox _rendererComboBox;
    private readonly CheckBox _showFbxCheckBox;
    private readonly CheckBox _showUe3CheckBox;
    private readonly CheckBox _wireframeCheckBox;
    private readonly CheckBox _showBonesCheckBox;
    private readonly CheckBox _showWeightsCheckBox;
    private readonly CheckBox _showSectionsCheckBox;
    private readonly CheckBox _showNormalsCheckBox;
    private readonly CheckBox _showTangentsCheckBox;
    private readonly CheckBox _showUvSeamsCheckBox;
    private readonly Button _resetPreviewButton;
    private readonly Button _resetCameraButton;
    private readonly ComboBox _displayModeComboBox;
    private readonly ComboBox _shadingModeComboBox;
    private readonly ComboBox _backgroundStyleComboBox;
    private readonly ComboBox _lightingPresetComboBox;
    private readonly ComboBox _materialChannelComboBox;
    private readonly ComboBox _sectionFocusModeComboBox;
    private readonly ComboBox _sectionComboBox;
    private readonly ComboBox _weightViewComboBox;
    private readonly ComboBox _boneComboBox;
    private readonly TrackBar _ambientLightTrackBar;
    private readonly CheckBox _showGroundPlaneCheckBox;
    private readonly Label _displayModeLabel;
    private readonly Label _shadingModeLabel;
    private readonly Label _backgroundStyleLabel;
    private readonly Label _lightingPresetLabel;
    private readonly Label _materialChannelLabel;
    private readonly Label _sectionFocusModeLabel;
    private readonly Label _sectionLabel;
    private readonly Label _rendererLabel;
    private readonly Label _boneLabel;
    private readonly Label _weightViewLabel;
    private readonly Label _ambientLightLabel;
    private readonly TextBox _logTextBox;
    private readonly MeshPreviewControl _previewControl;
    private readonly MeshPreviewLogger _logger;
    private readonly UpkFileRepository _repository = new();
    private readonly FbxToPreviewMeshConverter _fbxConverter = new();
    private readonly UE3ToPreviewMeshConverter _ue3Converter = new();
    private readonly MeshPreviewGameMaterialResolver _gameMaterialResolver = new();
    private UnrealHeader _cachedPreviewHeader;
    private string _cachedPreviewUpkPath;
    private DateTime _cachedPreviewWriteTimeUtc;

    public MeshPreviewUI()
    {
        Dock = DockStyle.Fill;

        _loadFbxButton = CreateButton("Load FBX Mesh");
        _loadUe3Button = CreateButton("Load UE3 Mesh From UPK");
        _rendererLabel = CreateLabel("Renderer:");
        _rendererComboBox = CreateComboBox();
        _rendererComboBox.Items.AddRange(Enum.GetNames(typeof(MeshPreviewBackend)));
        _rendererComboBox.SelectedItem = nameof(MeshPreviewBackend.OpenTK);
        _showFbxCheckBox = CreateCheckBox("Show FBX Mesh", true);
        _showUe3CheckBox = CreateCheckBox("Show UE3 Mesh", true);
        _wireframeCheckBox = CreateCheckBox("Wireframe");
        _showBonesCheckBox = CreateCheckBox("Show Bones");
        _showWeightsCheckBox = CreateCheckBox("Show Weights");
        _showSectionsCheckBox = CreateCheckBox("Show Sections");
        _showNormalsCheckBox = CreateCheckBox("Show Normals");
        _showTangentsCheckBox = CreateCheckBox("Show Tangents");
        _showUvSeamsCheckBox = CreateCheckBox("Show UV Seams");
        _showGroundPlaneCheckBox = CreateCheckBox("Show Ground Plane", true);
        _resetPreviewButton = CreateButton("Reset Preview");
        _resetCameraButton = CreateButton("Reset Camera");

        _displayModeLabel = CreateLabel("Display Mode:");
        _displayModeComboBox = CreateComboBox();
        _displayModeComboBox.Items.AddRange(Enum.GetNames(typeof(MeshPreviewDisplayMode)));
        _displayModeComboBox.SelectedIndex = 0;

        _shadingModeLabel = CreateLabel("Shading Mode:");
        _shadingModeComboBox = CreateComboBox();
        _shadingModeComboBox.Items.AddRange(Enum.GetNames(typeof(MeshPreviewShadingMode)));
        _shadingModeComboBox.SelectedItem = nameof(MeshPreviewShadingMode.Lit);

        _backgroundStyleLabel = CreateLabel("Background:");
        _backgroundStyleComboBox = CreateComboBox();
        _backgroundStyleComboBox.Items.AddRange(Enum.GetNames(typeof(MeshPreviewBackgroundStyle)));
        _backgroundStyleComboBox.SelectedItem = nameof(MeshPreviewBackgroundStyle.DarkGradient);

        _lightingPresetLabel = CreateLabel("Lighting Preset:");
        _lightingPresetComboBox = CreateComboBox();
        _lightingPresetComboBox.Items.AddRange(Enum.GetNames(typeof(MeshPreviewLightingPreset)));
        _lightingPresetComboBox.SelectedItem = nameof(MeshPreviewLightingPreset.Neutral);

        _materialChannelLabel = CreateLabel("Material Channel:");
        _materialChannelComboBox = CreateComboBox();
        _materialChannelComboBox.Items.AddRange(Enum.GetNames(typeof(MeshPreviewMaterialChannel)));
        _materialChannelComboBox.SelectedItem = nameof(MeshPreviewMaterialChannel.FullMaterial);

        _sectionFocusModeLabel = CreateLabel("Section Focus:");
        _sectionFocusModeComboBox = CreateComboBox();
        _sectionFocusModeComboBox.Items.AddRange(Enum.GetNames(typeof(MeshPreviewSectionFocusMode)));
        _sectionFocusModeComboBox.SelectedItem = nameof(MeshPreviewSectionFocusMode.None);

        _sectionLabel = CreateLabel("Focused Section:");
        _sectionComboBox = CreateComboBox();
        _sectionComboBox.Items.Add("(All Sections)");

        _weightViewLabel = CreateLabel("Weight View:");
        _weightViewComboBox = CreateComboBox();
        _weightViewComboBox.Items.AddRange(Enum.GetNames(typeof(MeshPreviewWeightViewMode)));
        _weightViewComboBox.SelectedItem = nameof(MeshPreviewWeightViewMode.SelectedBoneHeatmap);

        _boneLabel = CreateLabel("Influence Bone:");
        _boneComboBox = CreateComboBox();

        _ambientLightLabel = CreateLabel("Ambient Light:");
        _ambientLightTrackBar = new TrackBar
        {
            Minimum = 0,
            Maximum = 100,
            TickFrequency = 10,
            Value = 30,
            AutoSize = false
        };

        _logTextBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Dock = DockStyle.Fill
        };
        _logger = new MeshPreviewLogger(_logTextBox);
        _previewControl = new MeshPreviewControl { Dock = DockStyle.Fill };
        _detailsTextBox = WorkspaceUiStyle.CreateReadOnlyDetailsTextBox(BuildWorkflowDetailsText());

        _leftPanelHost = new Panel
        {
            Dock = DockStyle.Left,
            AutoScroll = true,
            MinimumSize = new Size(320, 0),
            Width = LeftPanelWidth
        };

        _viewportPanel = new Panel
        {
            Dock = DockStyle.Fill
        };

        _leftLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Padding = new Padding(8),
            Margin = Padding.Empty
        };
        _leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(1, "Source"));
        AddRow(_loadFbxButton);
        AddRow(_loadUe3Button);
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(2, "Display"));
        AddRow(_rendererLabel);
        AddRow(_rendererComboBox);
        AddRow(_displayModeLabel);
        AddRow(_displayModeComboBox);
        AddRow(_shadingModeLabel);
        AddRow(_shadingModeComboBox);
        AddRow(_backgroundStyleLabel);
        AddRow(_backgroundStyleComboBox);
        AddRow(_lightingPresetLabel);
        AddRow(_lightingPresetComboBox);
        AddRow(_materialChannelLabel);
        AddRow(_materialChannelComboBox);
        AddRow(_showFbxCheckBox);
        AddRow(_showUe3CheckBox);
        AddRow(_wireframeCheckBox);
        AddRow(_showGroundPlaneCheckBox);
        AddRow(_showBonesCheckBox);
        AddRow(_showWeightsCheckBox);
        AddRow(_weightViewLabel);
        AddRow(_weightViewComboBox);
        AddRow(_showSectionsCheckBox);
        AddRow(_showNormalsCheckBox);
        AddRow(_showTangentsCheckBox);
        AddRow(_showUvSeamsCheckBox);
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(3, "Inspection"));
        AddRow(_sectionFocusModeLabel);
        AddRow(_sectionFocusModeComboBox);
        AddRow(_sectionLabel);
        AddRow(_sectionComboBox);
        AddRow(_boneLabel);
        AddRow(_boneComboBox);
        AddRow(_ambientLightLabel);
        AddRow(_ambientLightTrackBar);
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(4, "Actions"));
        AddRow(_resetPreviewButton);
        AddRow(_resetCameraButton, bottomSpacing: 0);

        _leftPanelHost.Controls.Add(_leftLayout);
        _viewportPanel.Controls.Add(_previewControl);

        _workspaceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            FixedPanel = FixedPanel.Panel2,
            IsSplitterFixed = true
        };
        _workspaceSplit.Panel1.Controls.Add(_viewportPanel);
        _workspaceSplit.Panel2.Controls.Add(_detailsTextBox);

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill
        };
        _contentPanel.Controls.Add(_workspaceSplit);
        _contentPanel.Controls.Add(_leftPanelHost);

        _verticalSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel2,
            SplitterDistance = Math.Max(400, Height - BottomLogHeight)
        };
        _verticalSplit.Panel1.Controls.Add(_contentPanel);
        _verticalSplit.Panel2.Controls.Add(_logTextBox);
        _verticalSplit.Panel2MinSize = 120;

        Controls.Add(_verticalSplit);

        WireEvents();
        ApplySceneState();
        Load += (_, _) => ApplyWorkspaceLayout();
        SizeChanged += (_, _) => ApplyWorkspaceLayout();
    }

    public void LoadFbxMeshFromPath(string fbxPath)
    {
        _logger.Log($"Loading FBX mesh: {fbxPath}");
        MeshPreviewMesh mesh = _fbxConverter.Convert(fbxPath, _logger.Log);
        _previewControl.Scene.SetFbxMesh(mesh);
        RefreshBoneList();
        RefreshSectionList();
        _previewControl.ResetCamera();
        _previewControl.RefreshPreview();
    }

    public async Task LoadUe3MeshFromUpkAsync(string upkPath, string exportPath, int lodIndex = 0)
    {
        // TODO: If you want tighter integration with MainForm later, call this with the currently opened UPK path
        // and selected SkeletalMesh export instead of using the file-selection dialog path.
        _logger.Log($"Loading UE3 SkeletalMesh preview: {exportPath} from {upkPath}");
        Stopwatch totalStopwatch = Stopwatch.StartNew();
        Stopwatch stageStopwatch = Stopwatch.StartNew();

        UnrealHeader header = await GetPreviewHeaderAsync(upkPath, tablesOnly: true).ConfigureAwait(true);
        _logger.Log($"Mesh Preview timing: UPK tables ready in {stageStopwatch.ElapsedMilliseconds} ms.");

        stageStopwatch.Restart();
        UnrealExportTableEntry export = header.ExportTable
            .FirstOrDefault(e => string.Equals(e.GetPathName(), exportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find SkeletalMesh export '{exportPath}'.");
        _logger.Log($"Mesh Preview timing: export lookup completed in {stageStopwatch.ElapsedMilliseconds} ms.");

        if (export.UnrealObject == null)
        {
            stageStopwatch.Restart();
            await header.ReadExportObjectAsync(export, null).ConfigureAwait(true);
            _logger.Log($"Mesh Preview timing: export object reader prepared in {stageStopwatch.ElapsedMilliseconds} ms.");

            stageStopwatch.Restart();
            await export.ParseUnrealObject(false, false).ConfigureAwait(true);
            _logger.Log($"Mesh Preview timing: SkeletalMesh parse completed in {stageStopwatch.ElapsedMilliseconds} ms.");
        }
        else
        {
            _logger.Log("Mesh Preview timing: SkeletalMesh parse skipped because the export was already parsed.");
        }

        if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh skeletalMesh)
            throw new InvalidOperationException($"Export '{exportPath}' is not a SkeletalMesh.");

        stageStopwatch.Restart();
        MeshPreviewMesh mesh = _ue3Converter.Convert(skeletalMesh, lodIndex, _logger.Log);
        await _gameMaterialResolver.ApplyToSectionsAsync(upkPath, skeletalMesh, mesh, _logger.Log).ConfigureAwait(true);
        _logger.Log($"Mesh Preview timing: preview mesh conversion completed in {stageStopwatch.ElapsedMilliseconds} ms.");

        stageStopwatch.Restart();
        _previewControl.Scene.SetUe3Mesh(mesh);
        _displayModeComboBox.SelectedItem = nameof(MeshPreviewDisplayMode.Ue3Only);
        _showUe3CheckBox.Checked = true;
        ResetPreviewMaterial();
        RefreshBoneList();
        RefreshSectionList();
        _previewControl.ResetCamera();
        _previewControl.RefreshPreview();
        _logger.Log($"Mesh Preview timing: scene update and refresh completed in {stageStopwatch.ElapsedMilliseconds} ms.");
        _logger.Log($"Mesh Preview timing: total load completed in {totalStopwatch.ElapsedMilliseconds} ms.");
    }

    public void SetUe3Mesh(USkeletalMesh skeletalMesh, int lodIndex = 0)
    {
        MeshPreviewMesh mesh = _ue3Converter.Convert(skeletalMesh, lodIndex, _logger.Log);
        _previewControl.Scene.SetUe3Mesh(mesh);
        _displayModeComboBox.SelectedItem = nameof(MeshPreviewDisplayMode.Ue3Only);
        _showUe3CheckBox.Checked = true;
        RefreshBoneList();
        RefreshSectionList();
        _previewControl.ResetCamera();
        _previewControl.RefreshPreview();
    }

    public void ClearLog()
    {
        _logger.Clear();
    }

    public void Log(string message)
    {
        _logger.Log(message);
    }

    public MeshPreviewBackend CurrentBackend => _previewControl.Backend;

    public void SetPreviewMaterialTexture(TexturePreviewMaterialSlot slot, TexturePreviewTexture texture)
    {
        _previewControl.Scene.MaterialSet.SetTexture(slot, texture);
    }

    public void SetUe3SectionPreviewTexture(int sectionIndex, TexturePreviewMaterialSlot slot, TexturePreviewTexture texture)
    {
        _previewControl.Scene.SetUe3SectionMaterialTexture(sectionIndex, slot, texture);
    }

    public void SetMaterialPreviewEnabled(bool enabled)
    {
        _previewControl.Scene.MaterialPreviewEnabled = enabled;
        _previewControl.Scene.MaterialSet.Enabled = enabled;
    }

    public void ResetPreviewMaterial()
    {
        _previewControl.Scene.MaterialSet.Clear();
        _previewControl.Scene.MaterialSet.Enabled = false;
        _previewControl.Scene.MaterialPreviewEnabled = false;
        _previewControl.Scene.ClearUe3SectionMaterialOverrides();
    }

    public void SetDisplayMode(MeshPreviewDisplayMode mode)
    {
        _displayModeComboBox.SelectedItem = nameof(mode);
        ApplySceneState();
    }

    public void SetShadingMode(MeshPreviewShadingMode mode)
    {
        _shadingModeComboBox.SelectedItem = nameof(mode);
        ApplySceneState();
    }

    public void FocusUe3Section(int sectionIndex, MeshPreviewSectionFocusMode focusMode = MeshPreviewSectionFocusMode.Highlight)
    {
        _sectionFocusModeComboBox.SelectedItem = nameof(focusMode);
        RefreshSectionList();
        PreviewSectionOption option = _sectionComboBox.Items
            .OfType<PreviewSectionOption>()
            .FirstOrDefault(item => item.Mesh == MeshPreviewSectionFocusMesh.Ue3 && item.SectionIndex == sectionIndex);
        _sectionComboBox.SelectedItem = option ?? _sectionComboBox.Items[0];
        ApplySceneState();
    }

    public void RefreshPreview()
    {
        _previewControl.RefreshPreview();
    }

    public void SetHiddenUe3Sections(IEnumerable<int> sectionIndices)
    {
        _previewControl.Scene.SetHiddenSections(ue3Mesh: true, sectionIndices);
        _previewControl.RefreshPreview();
    }

    public void ClearHiddenUe3Sections()
    {
        _previewControl.Scene.SetHiddenSections(ue3Mesh: true, Array.Empty<int>());
        _previewControl.RefreshPreview();
    }

    private void WireEvents()
    {
        _loadFbxButton.Click += (_, _) => LoadFbxViaDialog();
        _loadUe3Button.Click += async (_, _) => await LoadUe3ViaDialogAsync().ConfigureAwait(true);
        _resetPreviewButton.Click += (_, _) => ResetPreviewState();
        _resetCameraButton.Click += (_, _) => _previewControl.ResetCamera();
        _rendererComboBox.SelectedIndexChanged += (_, _) => ApplyRendererSelection();
        _displayModeComboBox.SelectedIndexChanged += (_, _) => ApplySceneState();
        _shadingModeComboBox.SelectedIndexChanged += (_, _) => ApplySceneState();
        _backgroundStyleComboBox.SelectedIndexChanged += (_, _) => ApplySceneState();
        _lightingPresetComboBox.SelectedIndexChanged += (_, _) => ApplySceneState();
        _materialChannelComboBox.SelectedIndexChanged += (_, _) => ApplySceneState();
        _showFbxCheckBox.CheckedChanged += (_, _) => ApplySceneState();
        _showUe3CheckBox.CheckedChanged += (_, _) => ApplySceneState();
        _wireframeCheckBox.CheckedChanged += (_, _) => ApplySceneState();
        _showGroundPlaneCheckBox.CheckedChanged += (_, _) => ApplySceneState();
        _showBonesCheckBox.CheckedChanged += (_, _) => ApplySceneState();
        _showWeightsCheckBox.CheckedChanged += (_, _) => ApplySceneState();
        _weightViewComboBox.SelectedIndexChanged += (_, _) => ApplySceneState();
        _showSectionsCheckBox.CheckedChanged += (_, _) => ApplySceneState();
        _showNormalsCheckBox.CheckedChanged += (_, _) => ApplySceneState();
        _showTangentsCheckBox.CheckedChanged += (_, _) => ApplySceneState();
        _showUvSeamsCheckBox.CheckedChanged += (_, _) => ApplySceneState();
        _sectionFocusModeComboBox.SelectedIndexChanged += (_, _) => ApplySceneState();
        _sectionComboBox.SelectedIndexChanged += (_, _) => ApplySceneState();
        _boneComboBox.SelectedIndexChanged += (_, _) => ApplySceneState();
        _ambientLightTrackBar.ValueChanged += (_, _) => ApplySceneState();
    }

    private void ApplySceneState()
    {
        MeshPreviewScene scene = _previewControl.Scene;
        scene.ShowFbxMesh = _showFbxCheckBox.Checked;
        scene.ShowUe3Mesh = _showUe3CheckBox.Checked;
        scene.Wireframe = _wireframeCheckBox.Checked;
        scene.ShowGroundPlane = _showGroundPlaneCheckBox.Checked;
        scene.ShowBones = _showBonesCheckBox.Checked;
        scene.ShowWeights = _showWeightsCheckBox.Checked;
        scene.WeightViewMode = Enum.TryParse<MeshPreviewWeightViewMode>(_weightViewComboBox.SelectedItem as string, out MeshPreviewWeightViewMode weightViewMode)
            ? weightViewMode
            : MeshPreviewWeightViewMode.SelectedBoneHeatmap;
        scene.ShowSections = _showSectionsCheckBox.Checked;
        scene.ShowNormals = _showNormalsCheckBox.Checked;
        scene.ShowTangents = _showTangentsCheckBox.Checked;
        scene.ShowUvSeams = _showUvSeamsCheckBox.Checked;
        scene.DisplayMode = Enum.TryParse<MeshPreviewDisplayMode>(_displayModeComboBox.SelectedItem as string, out MeshPreviewDisplayMode mode)
            ? mode
            : MeshPreviewDisplayMode.Overlay;
        scene.ShadingMode = Enum.TryParse<MeshPreviewShadingMode>(_shadingModeComboBox.SelectedItem as string, out MeshPreviewShadingMode shadingMode)
            ? shadingMode
            : MeshPreviewShadingMode.Lit;
        scene.BackgroundStyle = Enum.TryParse<MeshPreviewBackgroundStyle>(_backgroundStyleComboBox.SelectedItem as string, out MeshPreviewBackgroundStyle backgroundStyle)
            ? backgroundStyle
            : MeshPreviewBackgroundStyle.DarkGradient;
        scene.LightingPreset = Enum.TryParse<MeshPreviewLightingPreset>(_lightingPresetComboBox.SelectedItem as string, out MeshPreviewLightingPreset lightingPreset)
            ? lightingPreset
            : MeshPreviewLightingPreset.Neutral;
        scene.MaterialChannel = Enum.TryParse<MeshPreviewMaterialChannel>(_materialChannelComboBox.SelectedItem as string, out MeshPreviewMaterialChannel materialChannel)
            ? materialChannel
            : MeshPreviewMaterialChannel.FullMaterial;
        scene.SectionFocusMode = Enum.TryParse<MeshPreviewSectionFocusMode>(_sectionFocusModeComboBox.SelectedItem as string, out MeshPreviewSectionFocusMode sectionFocusMode)
            ? sectionFocusMode
            : MeshPreviewSectionFocusMode.None;
        if (_sectionComboBox.SelectedItem is PreviewSectionOption sectionOption)
        {
            scene.SectionFocusMesh = sectionOption.Mesh;
            scene.FocusedSectionIndex = sectionOption.SectionIndex;
        }
        else
        {
            scene.SectionFocusMesh = MeshPreviewSectionFocusMesh.None;
            scene.FocusedSectionIndex = -1;
        }
        scene.SelectedBoneName = _boneComboBox.SelectedIndex <= 0 ? string.Empty : _boneComboBox.SelectedItem as string ?? string.Empty;
        scene.AmbientLight = _ambientLightTrackBar.Value / 100.0f;
        _previewControl.RefreshPreview();
    }

    private void ApplyRendererSelection()
    {
        MeshPreviewBackend backend = Enum.TryParse<MeshPreviewBackend>(_rendererComboBox.SelectedItem as string, out MeshPreviewBackend parsed)
            ? parsed
            : MeshPreviewBackend.OpenTK;

        _previewControl.SetBackend(backend);
    }

    private void ResetPreviewState()
    {
        _rendererComboBox.SelectedItem = nameof(MeshPreviewBackend.OpenTK);
        _displayModeComboBox.SelectedItem = nameof(MeshPreviewDisplayMode.Overlay);
        _shadingModeComboBox.SelectedItem = nameof(MeshPreviewShadingMode.Lit);
        _backgroundStyleComboBox.SelectedItem = nameof(MeshPreviewBackgroundStyle.DarkGradient);
        _lightingPresetComboBox.SelectedItem = nameof(MeshPreviewLightingPreset.Neutral);
        _materialChannelComboBox.SelectedItem = nameof(MeshPreviewMaterialChannel.FullMaterial);
        _showFbxCheckBox.Checked = true;
        _showUe3CheckBox.Checked = true;
        _wireframeCheckBox.Checked = false;
        _showGroundPlaneCheckBox.Checked = true;
        _showBonesCheckBox.Checked = false;
        _showWeightsCheckBox.Checked = false;
        _weightViewComboBox.SelectedItem = nameof(MeshPreviewWeightViewMode.SelectedBoneHeatmap);
        _showSectionsCheckBox.Checked = false;
        _showNormalsCheckBox.Checked = false;
        _showTangentsCheckBox.Checked = false;
        _showUvSeamsCheckBox.Checked = false;
        _sectionFocusModeComboBox.SelectedItem = nameof(MeshPreviewSectionFocusMode.None);
        _sectionComboBox.SelectedIndex = 0;
        _boneComboBox.SelectedIndex = 0;
        _ambientLightTrackBar.Value = 30;
        _previewControl.ResetCamera();
        ApplyRendererSelection();
        ApplySceneState();
    }

    private void RefreshBoneList()
    {
        string selected = _boneComboBox.SelectedItem as string;
        _boneComboBox.Items.Clear();
        _boneComboBox.Items.Add("(Max Weight)");
        foreach (string boneName in _previewControl.Scene.BoneNames)
            _boneComboBox.Items.Add(boneName);

        if (!string.IsNullOrWhiteSpace(selected) && _boneComboBox.Items.Contains(selected))
            _boneComboBox.SelectedItem = selected;
        else
            _boneComboBox.SelectedIndex = 0;
    }

    private void RefreshSectionList()
    {
        object selected = _sectionComboBox.SelectedItem;
        _sectionComboBox.Items.Clear();
        _sectionComboBox.Items.Add("(All Sections)");

        foreach (PreviewSectionOption option in BuildSectionOptions())
            _sectionComboBox.Items.Add(option);

        if (selected is PreviewSectionOption selectedOption)
        {
            foreach (object item in _sectionComboBox.Items)
            {
                if (item is PreviewSectionOption option &&
                    option.Mesh == selectedOption.Mesh &&
                    option.SectionIndex == selectedOption.SectionIndex)
                {
                    _sectionComboBox.SelectedItem = item;
                    return;
                }
            }
        }

        _sectionComboBox.SelectedIndex = 0;
    }

    private async Task ApplyGameApproxMaterialPreviewAsync(string upkPath, USkeletalMesh skeletalMesh)
    {
        ResetPreviewMaterial();

        try
        {
            MeshPreviewGameMaterialResult result = await _gameMaterialResolver
                .BuildMaterialSetAsync(upkPath, skeletalMesh, _logger.Log)
                .ConfigureAwait(true);

            foreach ((TexturePreviewMaterialSlot slot, TexturePreviewTexture texture) in result.MaterialSet.Textures)
                _previewControl.Scene.MaterialSet.SetTexture(slot, texture);

            List<string> slotSummary = result.MaterialSet.Textures
                .Select(static pair => $"{pair.Key}={pair.Value.ExportPath}")
                .ToList();
            bool hasTextures = slotSummary.Count > 0;
            _previewControl.Scene.MaterialSet.Enabled = hasTextures;
            _previewControl.Scene.MaterialPreviewEnabled = hasTextures;

            if (hasTextures)
            {
                _logger.Log($"GameApprox material set resolved from UE3 material chain: {result.Summary}");
                _logger.Log($"GameApprox bound preview textures: {string.Join(", ", slotSummary)}");
            }
            else
                _logger.Log("GameApprox material set could not resolve any previewable UE3 textures for this mesh.");
        }
        catch (Exception ex)
        {
            _previewControl.Scene.MaterialSet.Enabled = false;
            _previewControl.Scene.MaterialPreviewEnabled = false;
            _logger.Log($"GameApprox material resolution failed: {ex.Message}");
        }
    }

    private IEnumerable<PreviewSectionOption> BuildSectionOptions()
    {
        MeshPreviewMesh fbxMesh = _previewControl.Scene.FbxMesh;
        if (fbxMesh != null)
        {
            foreach (MeshPreviewSection section in fbxMesh.Sections)
                yield return new PreviewSectionOption(MeshPreviewSectionFocusMesh.Fbx, section.Index, $"FBX: {section.Name}");
        }

        MeshPreviewMesh ue3Mesh = _previewControl.Scene.Ue3Mesh;
        if (ue3Mesh != null)
        {
            foreach (MeshPreviewSection section in ue3Mesh.Sections)
                yield return new PreviewSectionOption(MeshPreviewSectionFocusMesh.Ue3, section.Index, $"UE3: {section.Name}");
        }
    }

    private void LoadFbxViaDialog()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "FBX Files (*.fbx)|*.fbx",
            Title = "Select FBX Mesh"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            LoadFbxMeshFromPath(dialog.FileName);
        }
        catch (Exception ex)
        {
            _logger.Log($"FBX preview load failed: {ex.Message}");
        }
    }

    private async Task LoadUe3ViaDialogAsync()
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
            string exportPath = await PromptForSkeletalMeshExportAsync(dialog.FileName).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(exportPath))
                return;

            await LoadUe3MeshFromUpkAsync(dialog.FileName, exportPath).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.Log($"UE3 preview load failed: {ex.Message}");
        }
    }

    private async Task<string> PromptForSkeletalMeshExportAsync(string upkPath)
    {
        // TODO: Replace this picker with your existing object/export-selection UI if you want the preview tab
        // to bind directly to the app's current package browser rather than prompting for an export list.
        UnrealHeader header = await GetPreviewHeaderAsync(upkPath, tablesOnly: true).ConfigureAwait(true);

        List<string> exports = header.ExportTable
            .Where(export => string.Equals(export.ClassReferenceNameIndex?.Name, nameof(USkeletalMesh), StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(export.ClassReferenceNameIndex?.Name, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
            .Select(static export => export.GetPathName())
            .OrderBy(static name => name)
            .ToList();

        if (exports.Count == 0)
            throw new InvalidOperationException("The selected UPK did not contain any SkeletalMesh exports.");

        using SkeletalMeshSelectionForm selectionForm = new(exports);
        return selectionForm.ShowDialog(FindForm()) == DialogResult.OK ? selectionForm.SelectedExportPath : null;
    }

    private async Task<UnrealHeader> GetPreviewHeaderAsync(string upkPath, bool tablesOnly)
    {
        DateTime writeTimeUtc = File.GetLastWriteTimeUtc(upkPath);
        if (!string.Equals(_cachedPreviewUpkPath, upkPath, StringComparison.OrdinalIgnoreCase))
        {
            _cachedPreviewHeader = null;
            _cachedPreviewUpkPath = upkPath;
            _cachedPreviewWriteTimeUtc = default;
        }
        else if (_cachedPreviewHeader != null && _cachedPreviewWriteTimeUtc != writeTimeUtc)
        {
            _logger.Log($"Mesh Preview cache invalidated for updated UPK: {upkPath}");
            _cachedPreviewHeader = null;
        }

        _cachedPreviewHeader ??= await _repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        _cachedPreviewWriteTimeUtc = writeTimeUtc;

        if (tablesOnly)
            await _cachedPreviewHeader.ReadTablesAsync(null).ConfigureAwait(true);
        else
            await _cachedPreviewHeader.ReadHeaderAsync(null).ConfigureAwait(true);

        return _cachedPreviewHeader;
    }

    private static Button CreateButton(string text)
    {
        Button button = WorkspaceUiStyle.CreateActionButton(text);
        button.Margin = new Padding(0, 0, 0, 8);
        return button;
    }

    private static CheckBox CreateCheckBox(string text, bool isChecked = false)
    {
        return new CheckBox
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
            UseVisualStyleBackColor = true,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            TextAlign = ContentAlignment.BottomLeft,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 6)
        };
    }

    private static ComboBox CreateComboBox()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    private void AddRow(Control control, int bottomSpacing = 8)
    {
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        control.Margin = new Padding(0, 0, 0, bottomSpacing);

        int rowIndex = _leftLayout.RowCount++;
        _leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _leftLayout.Controls.Add(control, 0, rowIndex);
    }

    private void ApplyWorkspaceLayout()
    {
        if (_workspaceSplit.Width <= 0)
            return;

        _workspaceSplit.Panel2MinSize = 300;
        int desiredRight = Math.Min(DetailsPanelWidth, Math.Max(300, _workspaceSplit.Width / 3));
        int maxLeft = _workspaceSplit.Width - _workspaceSplit.Panel2MinSize - _workspaceSplit.SplitterWidth;
        _workspaceSplit.SplitterDistance = Math.Max(200, Math.Min(maxLeft, _workspaceSplit.Width - desiredRight - _workspaceSplit.SplitterWidth));
    }

    private static string BuildWorkflowDetailsText()
    {
        return string.Join(Environment.NewLine,
        [
            "Mesh Preview Workflow",
            string.Empty,
            "1. Load an FBX mesh when you want to inspect imported geometry before replacement.",
            "2. Load a UE3 SkeletalMesh from a UPK when you want to inspect the in-game asset. UE3 loads switch the preview to Ue3Only so you start from the real game mesh instead of a stacked comparison view.",
            "3. Use Display Mode to switch to SideBySide or Overlay only when you intentionally want to compare FBX and UE3 meshes, then choose Shading Mode between Lit, Studio, Clay, MatCap, and GameApprox.",
            "4. Use Background and Lighting Preset to tune the viewport presentation, then keep Show Ground Plane on when you want the mesh visually anchored.",
            "5. Switch Material Channel when you want to inspect base color, normal, specular, emissive, or mask contribution directly.",
            "6. Turn on Show Weights, then switch Weight View to inspect either a selected bone or each vertex's strongest influence.",
            "7. Use Section Focus to highlight or isolate one section while keeping the other viewport overlays available.",
            "8. Choose an influence bone when you want the weight overlay to focus on a specific joint.",
            "9. Reset Preview or Reset Camera when you want to clear the view and start a fresh comparison."
        ]);
    }

    private sealed record PreviewSectionOption(MeshPreviewSectionFocusMesh Mesh, int SectionIndex, string Label)
    {
        public override string ToString() => Label;
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
