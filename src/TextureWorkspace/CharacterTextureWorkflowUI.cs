using MHUpkManager.TexturePreview;
using MHUpkManager.UI;
using UpkManager.Models.UpkFile.Engine.Material;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;
using System.Text;

namespace MHUpkManager.TextureWorkspace;

internal sealed class CharacterTextureWorkflowUI : UserControl
{
    private const int LeftPanelWidth = 430;
    private const int BottomLogHeight = 160;
    private const int ApplyButtonHeight = 44;

    private readonly Func<string> _currentUpkPathProvider;
    private readonly Func<string> _currentSkeletalMeshExportPathProvider;
    private readonly Action<TexturePreviewTexture> _openTextureInPreview;
    private readonly Func<string, string, IReadOnlyList<(int SectionIndex, TexturePreviewMaterialSlot Slot, string ReplacementFilePath)>, Task> _previewOnMeshAsync;
    private readonly Action _clearMeshPreview;
    private readonly UpkFileRepository _repository = new();
    private readonly UpkTextureLoader _upkTextureLoader = new();
    private readonly TextureLoader _textureLoader = new();
    private readonly TexturePreviewInjector _injector = new();
    private readonly SplitContainer _verticalSplit;
    private readonly SplitContainer _contentSplit;
    private readonly TableLayoutPanel _leftLayout;
    private readonly TextBox _upkPathTextBox;
    private readonly TextBox _meshPathTextBox;
    private readonly Label _summaryLabel;
    private readonly Button _useSelectedButton;
    private readonly Button _browseUpkButton;
    private readonly Button _detectTargetsButton;
    private readonly Button _chooseReplacementButton;
    private readonly Button _autoAssignButton;
    private readonly Button _clearReplacementButton;
    private readonly Button _openCurrentTargetButton;
    private readonly Button _sendReplacementToPreviewButton;
    private readonly Button _previewApplyPlanButton;
    private readonly Button _stageSelectedButton;
    private readonly Button _previewStagedOnMeshButton;
    private readonly Button _resetPreviewOnlyButton;
    private readonly Button _applySelectedStagedButton;
    private readonly Button _resetSelectedStagedButton;
    private readonly Button _saveStagedManifestButton;
    private readonly Button _loadStagedManifestButton;
    private readonly Button _applyReplacementSetButton;
    private readonly Button _rollbackFromManifestButton;
    private readonly ListView _workflowListView;
    private readonly RichTextBox _detailsTextBox;
    private readonly TextBox _logTextBox;
    private readonly ComboBox _filterComboBox;
    private List<WorkflowTextureSlotGroup> _targets = [];

    public CharacterTextureWorkflowUI(
        Func<string> currentUpkPathProvider,
        Func<string> currentSkeletalMeshExportPathProvider,
        Action<TexturePreviewTexture> openTextureInPreview,
        Func<string, string, IReadOnlyList<(int SectionIndex, TexturePreviewMaterialSlot Slot, string ReplacementFilePath)>, Task> previewOnMeshAsync,
        Action clearMeshPreview)
    {
        _currentUpkPathProvider = currentUpkPathProvider;
        _currentSkeletalMeshExportPathProvider = currentSkeletalMeshExportPathProvider;
        _openTextureInPreview = openTextureInPreview;
        _previewOnMeshAsync = previewOnMeshAsync;
        _clearMeshPreview = clearMeshPreview;
        Dock = DockStyle.Fill;

        _upkPathTextBox = CreatePathTextBox("No UPK selected.");
        _meshPathTextBox = CreatePathTextBox("No SkeletalMesh selected.");
        _summaryLabel = new Label
        {
            Text = "Pick a character mesh, detect targets, preview the plan, then apply or roll back.",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 60,
            ForeColor = Color.DimGray
        };

        _useSelectedButton = CreateButton("Use Selected Character");
        _browseUpkButton = CreateButton("Browse");
        _detectTargetsButton = CreateButton("Detect Texture Targets");
        _chooseReplacementButton = CreateButton("Choose Replacement File");
        _autoAssignButton = CreateButton("Auto Assign From Folder");
        _clearReplacementButton = CreateButton("Clear Replacement File");
        _openCurrentTargetButton = CreateButton("Open Current Target");
        _sendReplacementToPreviewButton = CreateButton("Send Replacement To Preview");
        _stageSelectedButton = CreateButton("Stage Selected");
        _previewStagedOnMeshButton = CreateButton("Preview Staged On Mesh");
        _resetPreviewOnlyButton = CreateButton("Reset Preview Only");
        _applySelectedStagedButton = CreateButton("Apply Selected Staged", ApplyButtonHeight);
        _resetSelectedStagedButton = CreateButton("Reset Selected Staged", ApplyButtonHeight);
        _saveStagedManifestButton = CreateButton("Save Staged Mod Set");
        _loadStagedManifestButton = CreateButton("Load Staged Mod Set");
        _previewApplyPlanButton = CreateButton("Preview Apply Plan", ApplyButtonHeight);
        _applyReplacementSetButton = CreateButton("Apply Replacement Set", ApplyButtonHeight);
        _rollbackFromManifestButton = CreateButton("Rollback From Manifest", ApplyButtonHeight);

        _workflowListView = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = true,
            View = View.Details,
            GridLines = true,
            ShowGroups = true,
            Font = new Font(Font.FontFamily, 10.0f)
        };
        _workflowListView.Columns.Add("Status", 105);
        _workflowListView.Columns.Add("Stage", 85);
        _workflowListView.Columns.Add("Section", 70);
        _workflowListView.Columns.Add("Slot Target", 180);
        _workflowListView.Columns.Add("Current Target", 220);
        _workflowListView.Columns.Add("Replacement", 260);

        _filterComboBox = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        _filterComboBox.Items.AddRange(["All Rows", "Staged", "Pending", "Applied", "Ready", "Risk"]);
        _filterComboBox.SelectedIndex = 0;

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
        AddRow(CreateLabel("Character SkeletalMesh:"));
        AddRow(_meshPathTextBox);
        AddRow(_summaryLabel);
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(2, "Actions"));
        AddRow(_detectTargetsButton);
        AddRow(_chooseReplacementButton);
        AddRow(_autoAssignButton);
        AddRow(_clearReplacementButton);
        AddRow(_openCurrentTargetButton);
        AddRow(_sendReplacementToPreviewButton);
        AddRow(_stageSelectedButton);
        AddRow(_previewStagedOnMeshButton);
        AddRow(_resetPreviewOnlyButton);
        AddRow(_previewApplyPlanButton);
        AddRow(_applySelectedStagedButton);
        AddRow(_resetSelectedStagedButton);
        AddRow(_saveStagedManifestButton);
        AddRow(_loadStagedManifestButton);
        AddRow(_applyReplacementSetButton);
        AddRow(_rollbackFromManifestButton, 0);

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
            Orientation = Orientation.Vertical,
            SplitterDistance = 860
        };
        Panel centerPanel = new() { Dock = DockStyle.Fill };
        centerPanel.Controls.Add(_workflowListView);
        centerPanel.Controls.Add(_filterComboBox);
        _contentSplit.Panel1.Controls.Add(centerPanel);
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

        _useSelectedButton.Click += (_, _) => UseSelectedCharacter();
        _browseUpkButton.Click += async (_, _) => await BrowseCharacterAsync().ConfigureAwait(true);
        _detectTargetsButton.Click += async (_, _) => await DetectTargetsAsync().ConfigureAwait(true);
        _chooseReplacementButton.Click += (_, _) => ChooseReplacementFile();
        _autoAssignButton.Click += (_, _) => AutoAssignFromFolder();
        _clearReplacementButton.Click += (_, _) => ClearReplacementFile();
        _openCurrentTargetButton.Click += async (_, _) => await OpenCurrentTargetAsync().ConfigureAwait(true);
        _sendReplacementToPreviewButton.Click += (_, _) => SendReplacementToPreview();
        _stageSelectedButton.Click += (_, _) => StageSelectedTargets();
        _previewStagedOnMeshButton.Click += async (_, _) => await PreviewStagedOnMeshAsync().ConfigureAwait(true);
        _resetPreviewOnlyButton.Click += (_, _) => ResetPreviewOnly();
        _previewApplyPlanButton.Click += async (_, _) => await PreviewApplyPlanAsync().ConfigureAwait(true);
        _applySelectedStagedButton.Click += async (_, _) => await ApplySelectedStagedAsync().ConfigureAwait(true);
        _resetSelectedStagedButton.Click += (_, _) => ResetSelectedStaged();
        _saveStagedManifestButton.Click += (_, _) => SaveStagedModSet();
        _loadStagedManifestButton.Click += async (_, _) => await LoadStagedModSetAsync().ConfigureAwait(true);
        _applyReplacementSetButton.Click += async (_, _) => await ApplyReplacementSetAsync().ConfigureAwait(true);
        _rollbackFromManifestButton.Click += (_, _) => RollbackFromManifest();
        _workflowListView.SelectedIndexChanged += (_, _) => ShowSelectedDetails();
        _filterComboBox.SelectedIndexChanged += (_, _) => BindTargets();
    }

    private void UseSelectedCharacter()
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
            Log("Select a SkeletalMesh export first.");
            return;
        }

        _upkPathTextBox.Text = upkPath;
        _meshPathTextBox.Text = meshPath;
        Log($"Selected character mesh: {meshPath}");
    }

    private async Task BrowseCharacterAsync()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Unreal Package Files (*.upk)|*.upk",
            Title = "Select UPK Containing a Character SkeletalMesh"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            SetBusy(true);
            List<string> exports = await GetSkeletalMeshExportsAsync(dialog.FileName).ConfigureAwait(true);
            if (exports.Count == 0)
                throw new InvalidOperationException("The selected UPK did not contain any SkeletalMesh exports.");

            using SkeletalMeshSelectionForm selectionForm = new(exports);
            if (selectionForm.ShowDialog(FindForm()) != DialogResult.OK)
                return;

            _upkPathTextBox.Text = dialog.FileName;
            _meshPathTextBox.Text = selectionForm.SelectedExportPath;
            Log($"Selected character mesh: {selectionForm.SelectedExportPath}");
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

    private async Task<List<string>> GetSkeletalMeshExportsAsync(string upkPath)
    {
        var header = await _repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadTablesAsync(null).ConfigureAwait(true);
        return header.ExportTable
            .Where(static export =>
                string.Equals(export.ClassReferenceNameIndex?.Name, nameof(USkeletalMesh), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(export.ClassReferenceNameIndex?.Name, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
            .Select(static export => export.GetPathName())
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task DetectTargetsAsync()
    {
        if (string.IsNullOrWhiteSpace(_upkPathTextBox.Text) || !File.Exists(_upkPathTextBox.Text))
        {
            Log("Select a valid UPK first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_meshPathTextBox.Text))
        {
            Log("Select a character SkeletalMesh first.");
            return;
        }

        try
        {
            SetBusy(true);
            _targets = await LoadWorkflowTargetsAsync(_upkPathTextBox.Text, _meshPathTextBox.Text).ConfigureAwait(true);
            BindTargets();
            Log($"Detected {_targets.Count} likely workflow target(s).");
        }
        catch (Exception ex)
        {
            Log($"Target detection failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<List<WorkflowTextureSlotGroup>> LoadWorkflowTargetsAsync(string upkPath, string skeletalMeshExportPath)
    {
        var header = await _repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        UnrealExportTableEntry export = header.ExportTable
            .FirstOrDefault(entry => string.Equals(entry.GetPathName(), skeletalMeshExportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find SkeletalMesh export '{skeletalMeshExportPath}'.");

        if (export.UnrealObject == null)
            await export.ParseUnrealObject(false, false).ConfigureAwait(true);

        if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh skeletalMesh)
            throw new InvalidOperationException($"Export '{skeletalMeshExportPath}' is not a SkeletalMesh.");

        List<WorkflowTextureTarget> rawTargets = [];
        if (skeletalMesh.LODModels.Count == 0)
            return [];

        FStaticLODModel lod = skeletalMesh.LODModels[0];
        for (int sectionIndex = 0; sectionIndex < lod.Sections.Count; sectionIndex++)
        {
            FSkelMeshSection section = lod.Sections[sectionIndex];
            FObject materialObject = section.MaterialIndex >= 0 && section.MaterialIndex < skeletalMesh.Materials.Count
                ? skeletalMesh.Materials[section.MaterialIndex]
                : null;

            UMaterialInstanceConstant material = materialObject?.LoadObject<UMaterialInstanceConstant>();
            if (material?.TextureParameterValues == null)
                continue;

            foreach (FTextureParameterValue parameter in material.TextureParameterValues)
            {
                TexturePreviewMaterialSlot slot = ResolveSlot(parameter.ParameterName?.Name);
                rawTargets.Add(new WorkflowTextureTarget
                {
                    SectionIndex = sectionIndex,
                    MaterialIndex = section.MaterialIndex,
                    MaterialPath = materialObject?.GetPathName() ?? "<missing>",
                    ParameterName = parameter.ParameterName?.Name ?? "<unnamed>",
                    Slot = slot,
                    CurrentTexturePath = parameter.ParameterValue?.GetPathName() ?? "<null>"
                });
            }
        }

        return [.. rawTargets
            .GroupBy(static target => new { target.SectionIndex, target.MaterialIndex, target.MaterialPath, target.Slot })
            .Select(group => new WorkflowTextureSlotGroup
            {
                SectionIndex = group.Key.SectionIndex,
                MaterialIndex = group.Key.MaterialIndex,
                MaterialPath = group.Key.MaterialPath,
                Slot = group.Key.Slot,
                Targets = [.. group.OrderBy(static target => target.ParameterName, StringComparer.OrdinalIgnoreCase)]
            })
            .OrderBy(static group => group.Slot)
            .ThenBy(static group => group.SectionIndex)];
    }

    private void BindTargets()
    {
        _workflowListView.BeginUpdate();
        try
        {
            _workflowListView.Items.Clear();
            _workflowListView.Groups.Clear();
            Dictionary<TexturePreviewMaterialSlot, ListViewGroup> groups = Enum
                .GetValues<TexturePreviewMaterialSlot>()
                .ToDictionary(
                    static slot => slot,
                    static slot => new ListViewGroup(slot.ToString(), HorizontalAlignment.Left));
            foreach (ListViewGroup group in groups.Values)
                _workflowListView.Groups.Add(group);

            foreach (WorkflowTextureSlotGroup target in GetFilteredTargets())
            {
                string status = target.GetGridStatusText();
                ListViewItem item = new(status)
                {
                    Group = groups[target.Slot]
                };
                item.SubItems.Add(target.GetStageText());
                item.SubItems.Add(target.SectionIndex.ToString());
                item.SubItems.Add($"Section {target.SectionIndex} {target.Slot}");
                item.SubItems.Add(ShortenPathLabel(target.CurrentTexturePath));
                item.SubItems.Add(string.IsNullOrWhiteSpace(target.ReplacementFilePath) ? "-" : Path.GetFileName(target.ReplacementFilePath));
                item.Tag = target;
                item.ForeColor = target.IsApplied
                    ? Color.FromArgb(0, 75, 140)
                    : !target.HasReplacementFile
                        ? Color.FromArgb(140, 70, 0)
                        : target.HasCurrentTextureTarget
                            ? Color.FromArgb(0, 90, 35)
                            : Color.FromArgb(150, 60, 60);
                _workflowListView.Items.Add(item);
            }
        }
        finally
        {
            _workflowListView.EndUpdate();
        }

        UpdateSummary();
        if (_workflowListView.Items.Count > 0)
            _workflowListView.Items[0].Selected = true;
    }

    private void UpdateSummary()
    {
        int assignedCount = _targets.Count(static target => !string.IsNullOrWhiteSpace(target.ReplacementFilePath));
        int readyCount = _targets.Count(static target => target.IsReadyToApply);
        int stagedCount = _targets.Count(static target => target.IsStaged);
        int appliedCount = _targets.Count(static target => target.IsApplied);
        string slotSummary = _targets.Count == 0
            ? "No texture targets detected yet."
            : string.Join(
                " | ",
                _targets
                    .GroupBy(static target => target.Slot)
                    .OrderBy(static group => group.Key)
                    .Select(group =>
                    {
                        int slotAssigned = group.Count(static target => !string.IsNullOrWhiteSpace(target.ReplacementFilePath));
                        return $"{group.Key}: {slotAssigned}/{group.Count()} ready";
                    }));

        _summaryLabel.Text =
            $"Detected {_targets.Count} likely texture target(s). Assigned files: {assignedCount}/{_targets.Count}. Ready to apply: {readyCount}/{_targets.Count}. Staged: {stagedCount}. Applied: {appliedCount}.{Environment.NewLine}" +
            $"{slotSummary}";
    }

    private List<WorkflowTextureSlotGroup> GetSelectedTargets()
    {
        return [.. _workflowListView.SelectedItems
            .Cast<ListViewItem>()
            .Select(static item => item.Tag)
            .OfType<WorkflowTextureSlotGroup>()];
    }

    private List<WorkflowTextureSlotGroup> GetStagedTargets()
    {
        List<WorkflowTextureSlotGroup> selected = GetSelectedTargets()
            .Where(static target => target.IsStaged)
            .ToList();
        return selected.Count > 0
            ? selected
            : _targets.Where(static target => target.IsStaged).ToList();
    }

    private void ChooseReplacementFile()
    {
        List<WorkflowTextureSlotGroup> selected = GetSelectedTargets();
        if (selected.Count == 0)
        {
            Log("Select a workflow target first.");
            return;
        }

        WorkflowTextureSlotGroup target = selected[0];

        using OpenFileDialog dialog = new()
        {
            Filter = "Texture Files|*.png;*.jpg;*.jpeg;*.dds;*.tga",
            Title = $"Select Replacement For {target.Slot}"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        foreach (WorkflowTextureSlotGroup row in selected)
            row.ReplacementFilePath = dialog.FileName;
        BindTargets();
        SelectTarget(target);
        Log($"Assigned replacement file to {selected.Count} workflow row(s): {dialog.FileName}");
    }

    private void AutoAssignFromFolder()
    {
        if (_targets.Count == 0)
        {
            Log("Detect texture targets first.");
            return;
        }

        using FolderBrowserDialog dialog = new()
        {
            Description = "Select a folder containing replacement textures"
        };

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;

        Dictionary<string, string> files = Directory
            .EnumerateFiles(dialog.SelectedPath)
            .Where(static path => IsSupportedTextureFile(path))
            .GroupBy(static path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        int assigned = 0;
        foreach (WorkflowTextureSlotGroup target in _targets)
        {
            string match = FindBestReplacementMatch(target, files);
            if (string.IsNullOrWhiteSpace(match))
                continue;

            target.ReplacementFilePath = match;
            assigned++;
        }

        BindTargets();
        Log($"Auto-assign complete. Matched {assigned} workflow row(s) from {dialog.SelectedPath}.");
    }

    private void ClearReplacementFile()
    {
        if (_workflowListView.SelectedItems.Count == 0 || _workflowListView.SelectedItems[0].Tag is not WorkflowTextureSlotGroup target)
        {
            Log("Select a workflow target first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(target.ReplacementFilePath))
        {
            Log("The selected workflow target does not currently have a replacement file.");
            return;
        }

        string cleared = target.ReplacementFilePath;
        target.ReplacementFilePath = string.Empty;
        BindTargets();
        SelectTarget(target);
        Log($"Cleared replacement file: {cleared}");
    }

    private void StageSelectedTargets()
    {
        List<WorkflowTextureSlotGroup> selected = GetSelectedTargets();
        if (selected.Count == 0)
        {
            Log("Select one or more workflow rows first.");
            return;
        }

        int stagedCount = 0;
        foreach (WorkflowTextureSlotGroup target in selected)
        {
            target.IsStaged = true;
            stagedCount++;
        }

        BindTargets();
        Log($"Staged {stagedCount} workflow target(s) as pending mods.");
    }

    private async Task PreviewStagedOnMeshAsync()
    {
        if (_previewOnMeshAsync == null)
        {
            Log("Mesh preview bridge is not available.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_upkPathTextBox.Text) || !File.Exists(_upkPathTextBox.Text) || string.IsNullOrWhiteSpace(_meshPathTextBox.Text))
        {
            Log("Select a valid UPK and character SkeletalMesh first.");
            return;
        }

        List<WorkflowTextureSlotGroup> staged = GetStagedTargets();
        if (staged.Count == 0)
        {
            Log("Stage one or more workflow rows first.");
            return;
        }

        List<(int SectionIndex, TexturePreviewMaterialSlot Slot, string ReplacementFilePath)> previewEntries = [.. staged
            .Where(static target => target.HasReplacementFile)
            .Select(static target => (target.SectionIndex, target.Slot, target.ReplacementFilePath))];

        if (previewEntries.Count == 0)
        {
            Log("The staged rows do not currently have valid replacement files.");
            return;
        }

        try
        {
            SetBusy(true);
            await _previewOnMeshAsync(_upkPathTextBox.Text, _meshPathTextBox.Text, previewEntries).ConfigureAwait(true);
            Log($"Previewed {previewEntries.Count} staged replacement(s) on the mesh.");
        }
        catch (Exception ex)
        {
            Log($"Mesh preview staging failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ResetPreviewOnly()
    {
        _clearMeshPreview?.Invoke();
        Log("Cleared staged mesh preview overrides. Staged rows were left unchanged.");
    }

    private async Task OpenCurrentTargetAsync()
    {
        if (_workflowListView.SelectedItems.Count == 0 || _workflowListView.SelectedItems[0].Tag is not WorkflowTextureSlotGroup target)
        {
            Log("Select a workflow target first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(target.CurrentTexturePath) || target.CurrentTexturePath == "<null>")
        {
            Log("The selected workflow target does not have a current texture export.");
            return;
        }

        try
        {
            SetBusy(true);
            TexturePreviewTexture texture = await _upkTextureLoader
                .LoadFromUpkAsync(_upkPathTextBox.Text, target.CurrentTexturePath, target.Slot, Log)
                .ConfigureAwait(true);
            _openTextureInPreview?.Invoke(texture);
            Log($"Opened current target in Texture Preview: {target.CurrentTexturePath}");
        }
        catch (Exception ex)
        {
            Log($"Failed to open current target: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SendReplacementToPreview()
    {
        if (_workflowListView.SelectedItems.Count == 0 || _workflowListView.SelectedItems[0].Tag is not WorkflowTextureSlotGroup target)
        {
            Log("Select a workflow target first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(target.ReplacementFilePath) || !File.Exists(target.ReplacementFilePath))
        {
            Log("Assign a replacement file first.");
            return;
        }

        try
        {
            TexturePreviewTexture texture = _textureLoader.LoadFromFile(target.ReplacementFilePath, target.Slot);
            texture.Slot = target.Slot;
            _openTextureInPreview?.Invoke(texture);
            Log($"Sent replacement file to Texture Preview: {target.ReplacementFilePath}");
        }
        catch (Exception ex)
        {
            Log($"Failed to send replacement to preview: {ex.Message}");
        }
    }

    private async Task ApplyReplacementSetAsync()
    {
        await ApplyTargetsAsync(_targets, "Apply Replacement Set").ConfigureAwait(true);
    }

    private async Task ApplySelectedStagedAsync()
    {
        List<WorkflowTextureSlotGroup> staged = GetStagedTargets();
        if (staged.Count == 0)
        {
            Log("Stage one or more workflow rows first.");
            return;
        }

        await ApplyTargetsAsync(staged, "Apply Selected Staged").ConfigureAwait(true);
    }

    private async Task ApplyTargetsAsync(IReadOnlyList<WorkflowTextureSlotGroup> targetsToApply, string operationName)
    {
        if (string.IsNullOrWhiteSpace(_upkPathTextBox.Text) || !File.Exists(_upkPathTextBox.Text))
        {
            Log("Select a valid UPK first.");
            return;
        }

        if (_targets.Count == 0)
        {
            Log("Detect texture targets first.");
            return;
        }

        if (targetsToApply == null || targetsToApply.Count == 0)
        {
            Log("No workflow rows were selected for apply.");
            return;
        }

        CharacterApplyPlan plan;
        try
        {
            SetBusy(true);
            plan = await BuildApplyPlanAsync(_upkPathTextBox.Text, targetsToApply).ConfigureAwait(true);
            _detailsTextBox.Text = plan.ToDisplayText();
        }
        catch (Exception ex)
        {
            Log($"Apply plan preview failed: {ex.Message}");
            return;
        }
        finally
        {
            SetBusy(false);
        }

        List<CharacterApplyPlanEntry> applicableEntries = plan.Entries
            .Where(static entry => string.Equals(entry.Action, "Apply", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (applicableEntries.Count == 0)
        {
            Log("No workflow rows are ready to apply. Check Preview Apply Plan for skips and risks.");
            return;
        }

        string confirmationText =
            $"Apply {applicableEntries.Count} replacement(s)?{Environment.NewLine}{Environment.NewLine}" +
            $"Apply: {plan.ApplyCount}{Environment.NewLine}" +
            $"Skip: {plan.SkipCount}{Environment.NewLine}" +
            $"Risk: {plan.RiskCount}{Environment.NewLine}{Environment.NewLine}" +
            "Only rows marked Apply will be injected.";

            if (MessageBox.Show(
                FindForm(),
                confirmationText,
                $"Confirm {operationName}",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            Log($"{operationName} was canceled.");
            return;
        }

        int successCount = 0;
        int failedCount = 0;
        List<CharacterApplyManifestEntry> manifestEntries = [];

        try
        {
            SetBusy(true);
            Log($"{operationName}: applying {applicableEntries.Count} workflow target(s).");

            foreach (CharacterApplyPlanEntry entry in applicableEntries)
            {
                TexturePreviewTexture texture = null;
                try
                {
                    TextureInjectionTargetInfo targetInfo = await _injector
                        .ResolveTargetInfoAsync(_upkPathTextBox.Text, entry.CurrentTexturePath)
                        .ConfigureAwait(true);
                    Log($"Applying Section {entry.SectionIndex} {entry.Slot} -> {entry.CurrentTexturePath}");
                    texture = _textureLoader.LoadFromFile(entry.ReplacementFilePath, entry.Slot);
                    texture.Slot = entry.Slot;
                    await _injector
                        .InjectAsync(_upkPathTextBox.Text, entry.CurrentTexturePath, texture, Log)
                        .ConfigureAwait(true);
                    successCount++;
                    manifestEntries.Add(CharacterApplyManifestEntry.Success(entry, targetInfo));
                    WorkflowTextureSlotGroup target = _targets.FirstOrDefault(candidate =>
                        candidate.SectionIndex == entry.SectionIndex &&
                        candidate.Slot == entry.Slot &&
                        string.Equals(candidate.CurrentTexturePath, entry.CurrentTexturePath, StringComparison.OrdinalIgnoreCase));
                    if (target != null)
                    {
                        target.IsApplied = true;
                        target.IsStaged = true;
                        target.LastAppliedManifestFilePath = targetInfo.ManifestFilePath;
                        target.LastAppliedSourceCachePath = targetInfo.SourceTextureCachePath;
                        target.LastAppliedDestinationCachePath = targetInfo.DestinationTextureCachePath;
                    }
                }
                catch (Exception ex)
                {
                    failedCount++;
                    Log($"Apply failed for Section {entry.SectionIndex} {entry.Slot}: {ex.Message}");
                    manifestEntries.Add(CharacterApplyManifestEntry.Failure(entry, ex.Message));
                }
                finally
                {
                    texture?.Dispose();
                }
            }
        }
        finally
        {
            SetBusy(false);
        }

        Log($"{operationName} complete. Succeeded: {successCount}. Failed: {failedCount}.");
        string manifestPath = WriteApplyManifest(_upkPathTextBox.Text, plan, manifestEntries);
        Log($"Saved apply manifest: {manifestPath}");
        BindTargets();
        ShowSelectedDetails();
    }

    private void RollbackFromManifest()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Apply Manifest Files (*.txt)|*.txt",
            Title = "Select Character Texture Apply Manifest"
        };

        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string defaultDirectory = Path.Combine(desktopPath, "MHUpkManager_ApplyManifests");
        if (Directory.Exists(defaultDirectory))
            dialog.InitialDirectory = defaultDirectory;

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            CharacterApplyManifest manifest = CharacterApplyManifest.Load(dialog.FileName);
            HashSet<string> restoredPaths = [];

            foreach (string path in manifest.BackupPaths.Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string backupPath = path + ".bak";
                if (!File.Exists(backupPath))
                    continue;

                File.Copy(backupPath, path, overwrite: true);
                restoredPaths.Add(path);
            }

            Log($"Rollback complete. Restored {restoredPaths.Count} file(s) from manifest: {dialog.FileName}");
            _detailsTextBox.Text = manifest.ToRollbackDisplayText(restoredPaths.Count);
        }
        catch (Exception ex)
        {
            Log($"Rollback failed: {ex.Message}");
        }
    }

    private void ResetSelectedStaged()
    {
        List<WorkflowTextureSlotGroup> staged = GetStagedTargets();
        if (staged.Count == 0)
        {
            Log("Stage one or more workflow rows first.");
            return;
        }

        int restored = 0;
        foreach (WorkflowTextureSlotGroup target in staged)
        {
            foreach (string path in target.GetRestorePaths())
            {
                string backupPath = path + ".bak";
                if (!File.Exists(backupPath))
                    continue;

                File.Copy(backupPath, path, overwrite: true);
                restored++;
            }

            target.IsApplied = false;
            target.IsStaged = true;
            target.LastAppliedManifestFilePath = string.Empty;
            target.LastAppliedSourceCachePath = string.Empty;
            target.LastAppliedDestinationCachePath = string.Empty;
        }

        _clearMeshPreview?.Invoke();
        BindTargets();
        Log($"Reset {staged.Count} staged workflow target(s). Restored {restored} backup file copy operation(s).");
        ShowSelectedDetails();
    }

    private void SaveStagedModSet()
    {
        if (_targets.Count == 0)
        {
            Log("Detect texture targets first.");
            return;
        }

        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string manifestDirectory = Path.Combine(desktopPath, "MHUpkManager_StagedModSets");
        Directory.CreateDirectory(manifestDirectory);

        string safeUpkName = string.IsNullOrWhiteSpace(_upkPathTextBox.Text)
            ? "unknown_upk"
            : Path.GetFileNameWithoutExtension(_upkPathTextBox.Text);
        string manifestPath = Path.Combine(manifestDirectory, $"{safeUpkName}_staged_mods_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        File.WriteAllText(manifestPath, CharacterStagedModManifest.Save(_upkPathTextBox.Text, _meshPathTextBox.Text, _targets));
        Log($"Saved staged mod set: {manifestPath}");
        _detailsTextBox.Text = BuildManifestSummaryText(
            "Saved Staged Mod Set",
            _targets.Count,
            _targets.Count(static target => target.IsStaged),
            _targets.Count(static target => target.IsApplied));
    }

    private async Task LoadStagedModSetAsync()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Staged Mod Set Files (*.txt)|*.txt",
            Title = "Select Character Texture Staged Mod Set"
        };

        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string defaultDirectory = Path.Combine(desktopPath, "MHUpkManager_StagedModSets");
        if (Directory.Exists(defaultDirectory))
            dialog.InitialDirectory = defaultDirectory;

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            CharacterStagedModManifest manifest = CharacterStagedModManifest.Load(dialog.FileName);
            _upkPathTextBox.Text = manifest.UpkPath;
            _meshPathTextBox.Text = manifest.SkeletalMeshPath;
            _targets = await LoadWorkflowTargetsAsync(_upkPathTextBox.Text, _meshPathTextBox.Text).ConfigureAwait(true);

            foreach (CharacterStagedModManifestEntry saved in manifest.Entries)
            {
                WorkflowTextureSlotGroup target = _targets.FirstOrDefault(candidate =>
                    candidate.SectionIndex == saved.SectionIndex &&
                    candidate.Slot == saved.Slot &&
                    string.Equals(candidate.CurrentTexturePath, saved.CurrentTexturePath, StringComparison.OrdinalIgnoreCase));
                if (target == null)
                    continue;

                target.ReplacementFilePath = saved.ReplacementFilePath;
                target.IsStaged = saved.IsStaged;
                target.IsApplied = saved.IsApplied;
            }

            BindTargets();
            Log($"Loaded staged mod set: {dialog.FileName}");
            _detailsTextBox.Text = BuildManifestSummaryText(
                "Loaded Staged Mod Set",
                manifest.Entries.Count,
                _targets.Count(static target => target.IsStaged),
                _targets.Count(static target => target.IsApplied));
        }
        catch (Exception ex)
        {
            Log($"Load staged mod set failed: {ex.Message}");
        }
    }

    private async Task PreviewApplyPlanAsync()
    {
        if (string.IsNullOrWhiteSpace(_upkPathTextBox.Text) || !File.Exists(_upkPathTextBox.Text))
        {
            Log("Select a valid UPK first.");
            return;
        }

        if (_targets.Count == 0)
        {
            Log("Detect texture targets first.");
            return;
        }

        try
        {
            SetBusy(true);
            List<WorkflowTextureSlotGroup> staged = GetStagedTargets();
            IReadOnlyList<WorkflowTextureSlotGroup> source = staged.Count > 0 ? staged : _targets;
            CharacterApplyPlan plan = await BuildApplyPlanAsync(_upkPathTextBox.Text, source).ConfigureAwait(true);
            _detailsTextBox.Text = plan.ToDisplayText();
            Log($"Previewed apply plan. Apply={plan.ApplyCount}, Skip={plan.SkipCount}, Risk={plan.RiskCount}.");
        }
        catch (Exception ex)
        {
            Log($"Apply plan preview failed: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<CharacterApplyPlan> BuildApplyPlanAsync(string upkPath, IReadOnlyList<WorkflowTextureSlotGroup> sourceTargets)
    {
        var header = await _repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        List<CharacterApplyPlanEntry> entries = [];
        foreach (WorkflowTextureSlotGroup target in sourceTargets)
            entries.Add(await BuildPlanEntryAsync(header, target).ConfigureAwait(true));

        return new CharacterApplyPlan(entries);
    }

    private async Task<CharacterApplyPlanEntry> BuildPlanEntryAsync(UnrealHeader header, WorkflowTextureSlotGroup target)
    {
        string action;
        string reason;
        string targetSizeText = "<unknown>";
        string replacementSizeText = "<none>";
        bool dimensionsMatch = false;
        bool likelyNormalTarget = target.Slot == TexturePreviewMaterialSlot.Normal;

        if (!target.HasReplacementFile)
        {
            action = "Skip";
            reason = "No replacement file assigned.";
        }
        else if (!target.HasCurrentTextureTarget)
        {
            action = "Skip";
            reason = "No resolved current texture target.";
        }
        else
        {
            TexturePreviewTexture replacementTexture = null;
            try
            {
                replacementTexture = _textureLoader.LoadFromFile(target.ReplacementFilePath, target.Slot);
                replacementSizeText = replacementTexture.ResolutionText;

                UnrealExportTableEntry export = header.ExportTable
                    .FirstOrDefault(entry => string.Equals(entry.GetPathName(), target.CurrentTexturePath, StringComparison.OrdinalIgnoreCase));

                if (export == null)
                {
                    action = "Skip";
                    reason = "Target Texture2D export was not found.";
                }
                else
                {
                    if (export.UnrealObject == null)
                        await export.ParseUnrealObject(false, false).ConfigureAwait(true);

                    if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not UTexture2D texture2D)
                    {
                        action = "Skip";
                        reason = "Resolved target is not a Texture2D.";
                    }
                    else
                    {
                        targetSizeText = $"{texture2D.SizeX} x {texture2D.SizeY}";
                        dimensionsMatch = replacementTexture.Width == texture2D.SizeX && replacementTexture.Height == texture2D.SizeY;
                        likelyNormalTarget = likelyNormalTarget || IsLikelyNormalTarget(texture2D, target.CurrentTexturePath);

                        if (!dimensionsMatch)
                        {
                            action = "Risk";
                            reason = "Replacement dimensions do not match the target texture.";
                        }
                        else
                        {
                            action = "Apply";
                            reason = "Replacement file and target texture are ready for injection.";
                        }
                    }
                }
            }
            finally
            {
                replacementTexture?.Dispose();
            }
        }

        return new CharacterApplyPlanEntry
        {
            SectionIndex = target.SectionIndex,
            Slot = target.Slot,
            CurrentTexturePath = target.CurrentTexturePath,
            ReplacementFilePath = target.ReplacementFilePath,
            Action = action,
            Reason = reason,
            TargetSizeText = targetSizeText,
            ReplacementSizeText = replacementSizeText,
            DimensionsMatch = dimensionsMatch,
            LikelyNormalTarget = likelyNormalTarget
        };
    }

    private void ShowSelectedDetails()
    {
        if (_workflowListView.SelectedItems.Count == 0 || _workflowListView.SelectedItems[0].Tag is not WorkflowTextureSlotGroup target)
        {
            _detailsTextBox.Text = BuildWorkflowDetailsText();
            return;
        }

        _detailsTextBox.Text = string.Join(Environment.NewLine,
        [
            BuildWorkflowDetailsText(),
            string.Empty,
            "Current Workflow Target",
            $"Status: {target.GetStatusText()}",
            $"StageState: {target.GetStageText()}",
            $"SectionIndex: {target.SectionIndex}",
            $"MaterialIndex: {target.MaterialIndex}",
            $"MaterialPath: {target.MaterialPath}",
            $"TextureSlot: {target.Slot}",
            $"CurrentTexturePath: {target.CurrentTexturePath}",
            $"ReplacementFile: {(string.IsNullOrWhiteSpace(target.ReplacementFilePath) ? "<none>" : target.ReplacementFilePath)}",
            $"PreviewFocus: Section {target.SectionIndex}",
            $"Parameters: {(target.Targets.Count == 0 ? "<none>" : string.Join(", ", target.Targets.Select(static x => x.ParameterName)))}",
            string.Empty,
            !target.HasReplacementFile
                ? "Choose a replacement file for this slot, then stage it as a pending mod."
                : !target.IsStaged
                    ? "Stage this row when you want it included in mesh preview, save/load mod sets, or staged apply."
                    : !target.HasCurrentTextureTarget
                        ? "This staged row has no resolved current texture target, so it cannot be applied yet."
                        : target.IsApplied
                            ? "This staged row was applied. Reset Selected Staged will try to restore its backed-up files."
                            : "Preview the staged rows on the mesh, review the apply plan, or apply the selected staged mods."
        ]);
    }

    private static string BuildWorkflowDetailsText()
    {
        return WorkspaceUiStyle.BuildWorkflowText(
            "Character Workflow",
            "Select the character SkeletalMesh from the current UPK or Browse.",
            "Detect Texture Targets to build the section and slot plan, then choose replacement files manually or use Auto Assign From Folder.",
            "Stage the rows you want to treat as pending mods, then Preview Staged On Mesh to push section-aware texture overrides into Mesh Preview before writing anything.",
            "Use the row filter to review Staged, Pending, Applied, Ready, or Risk targets, then Preview Apply Plan to verify what will Apply, Skip, or Risk.",
            "Apply Selected Staged when you want selective mod application, or use Apply Replacement Set when you want one full pass over every detected row.",
            "Save or Load Staged Mod Set to manage pending character texture packs, use Reset Preview Only to clear mesh-only overrides, and use Reset Selected Staged or Rollback From Manifest when you need to revert.");
    }

    private static string BuildManifestSummaryText(string title, int entryCount, int stagedCount, int appliedCount)
    {
        return WorkspaceUiStyle.BuildSelectionText(
        [
            $"Summary: {title}",
            $"ManifestEntries: {entryCount}",
            $"StagedRows: {stagedCount}",
            $"AppliedRows: {appliedCount}"
        ],
        "Review the loaded rows, preview them on the mesh, or apply only the staged subset you want.");
    }

    private void SelectTarget(WorkflowTextureSlotGroup target)
    {
        foreach (ListViewItem item in _workflowListView.Items)
        {
            if (ReferenceEquals(item.Tag, target))
            {
                item.Selected = true;
                item.Focused = true;
                item.EnsureVisible();
                return;
            }
        }
    }

    private static TexturePreviewMaterialSlot ResolveSlot(string parameterName)
    {
        string name = parameterName ?? string.Empty;
        if (name.Contains("norm", StringComparison.OrdinalIgnoreCase))
            return TexturePreviewMaterialSlot.Normal;
        if (name.Contains("spec", StringComparison.OrdinalIgnoreCase))
            return TexturePreviewMaterialSlot.Specular;
        if (name.Contains("emis", StringComparison.OrdinalIgnoreCase))
            return TexturePreviewMaterialSlot.Emissive;
        if (name.Contains("mask", StringComparison.OrdinalIgnoreCase))
            return TexturePreviewMaterialSlot.Mask;
        return TexturePreviewMaterialSlot.Diffuse;
    }

    private static bool IsLikelyNormalTarget(UTexture2D texture, string exportPath)
    {
        string exportName = exportPath ?? string.Empty;
        return texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_WorldNormalMap
            or UTexture.TextureGroup.TEXTUREGROUP_CharacterNormalMap
            or UTexture.TextureGroup.TEXTUREGROUP_WeaponNormalMap
            or UTexture.TextureGroup.TEXTUREGROUP_VehicleNormalMap
            || exportName.Contains("normal", StringComparison.OrdinalIgnoreCase)
            || exportName.Contains("_n", StringComparison.OrdinalIgnoreCase)
            || exportName.Contains("_nm", StringComparison.OrdinalIgnoreCase)
            || exportName.Contains("norm", StringComparison.OrdinalIgnoreCase);
    }

    private static string WriteApplyManifest(string upkPath, CharacterApplyPlan plan, IReadOnlyList<CharacterApplyManifestEntry> entries)
    {
        string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string manifestDirectory = Path.Combine(desktopPath, "MHUpkManager_ApplyManifests");
        Directory.CreateDirectory(manifestDirectory);

        string safeUpkName = string.IsNullOrWhiteSpace(upkPath)
            ? "unknown_upk"
            : Path.GetFileNameWithoutExtension(upkPath);
        string manifestPath = Path.Combine(
            manifestDirectory,
            $"{safeUpkName}_texture_apply_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        StringBuilder builder = new();
        builder.AppendLine("MHUpkManager Character Texture Apply Manifest");
        builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"UPK: {upkPath}");
        builder.AppendLine($"Planned Apply: {plan.ApplyCount}");
        builder.AppendLine($"Planned Skip: {plan.SkipCount}");
        builder.AppendLine($"Planned Risk: {plan.RiskCount}");
        builder.AppendLine();

        foreach (CharacterApplyManifestEntry entry in entries.OrderBy(static item => item.SectionIndex).ThenBy(static item => item.Slot))
        {
            builder.AppendLine($"Section {entry.SectionIndex} / {entry.Slot}");
            builder.AppendLine($"Result: {entry.Result}");
            builder.AppendLine($"Target: {entry.CurrentTexturePath}");
            builder.AppendLine($"Replacement: {entry.ReplacementFilePath}");
            builder.AppendLine($"Reason: {entry.Reason}");
            builder.AppendLine($"ManifestFile: {entry.ManifestFilePath}");
            builder.AppendLine($"SourceCache: {entry.SourceTextureCachePath}");
            builder.AppendLine($"DestinationCache: {entry.DestinationTextureCachePath}");
            builder.AppendLine();
        }

        File.WriteAllText(manifestPath, builder.ToString());
        return manifestPath;
    }

    private void Log(string message)
    {
        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetBusy(bool busy)
    {
        _useSelectedButton.Enabled = !busy;
        _browseUpkButton.Enabled = !busy;
        _detectTargetsButton.Enabled = !busy;
        _chooseReplacementButton.Enabled = !busy;
        _autoAssignButton.Enabled = !busy;
        _clearReplacementButton.Enabled = !busy;
        _openCurrentTargetButton.Enabled = !busy;
        _sendReplacementToPreviewButton.Enabled = !busy;
        _stageSelectedButton.Enabled = !busy;
        _previewStagedOnMeshButton.Enabled = !busy;
        _resetPreviewOnlyButton.Enabled = !busy;
        _previewApplyPlanButton.Enabled = !busy;
        _applySelectedStagedButton.Enabled = !busy;
        _resetSelectedStagedButton.Enabled = !busy;
        _saveStagedManifestButton.Enabled = !busy;
        _loadStagedManifestButton.Enabled = !busy;
        _applyReplacementSetButton.Enabled = !busy;
        _rollbackFromManifestButton.Enabled = !busy;
        UseWaitCursor = busy;
        Form form = FindForm();
        if (form != null)
            form.UseWaitCursor = busy;
    }

    private static Button CreateButton(string text, int minimumHeight = 38)
    {
        return new Button
        {
            Text = text,
            UseVisualStyleBackColor = true,
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(0, minimumHeight)
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

    private static string ShortenPathLabel(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "<null>")
            return path;

        int split = path.LastIndexOf('.');
        if (split <= 0 || split >= path.Length - 1)
            return path;

        return path[(split + 1)..];
    }

    private IEnumerable<WorkflowTextureSlotGroup> GetFilteredTargets()
    {
        string filter = _filterComboBox.SelectedItem as string ?? "All Rows";
        IEnumerable<WorkflowTextureSlotGroup> query = _targets;
        return filter switch
        {
            "Staged" => query.Where(static target => target.IsStaged),
            "Pending" => query.Where(static target => target.IsStaged && !target.IsApplied),
            "Applied" => query.Where(static target => target.IsApplied),
            "Ready" => query.Where(static target => target.IsReadyToApply),
            "Risk" => query.Where(static target => target.HasReplacementFile && !target.IsReadyToApply),
            _ => query
        };
    }

    private static bool IsSupportedTextureFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".dds", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindBestReplacementMatch(WorkflowTextureSlotGroup target, IReadOnlyDictionary<string, string> files)
    {
        string currentBase = Path.GetFileNameWithoutExtension(target.CurrentTexturePath ?? string.Empty);
        string materialBase = Path.GetFileNameWithoutExtension(target.MaterialPath ?? string.Empty);
        string slotKeyword = GetSlotKeyword(target.Slot);

        List<string> exactKeys =
        [
            currentBase,
            $"{currentBase}_{slotKeyword}",
            $"{materialBase}_{slotKeyword}",
            $"section{target.SectionIndex}_{slotKeyword}"
        ];

        foreach (string key in exactKeys.Where(static key => !string.IsNullOrWhiteSpace(key)))
        {
            if (files.TryGetValue(key, out string match))
                return match;
        }

        foreach ((string key, string path) in files)
        {
            if ((!string.IsNullOrWhiteSpace(currentBase) && key.Contains(currentBase, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(materialBase) && key.Contains(materialBase, StringComparison.OrdinalIgnoreCase))
                || key.Contains(slotKeyword, StringComparison.OrdinalIgnoreCase)
                || key.Contains($"section{target.SectionIndex}", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }
        }

        return string.Empty;
    }

    private static string GetSlotKeyword(TexturePreviewMaterialSlot slot)
    {
        return slot switch
        {
            TexturePreviewMaterialSlot.Normal => "norm",
            TexturePreviewMaterialSlot.Specular => "spec",
            TexturePreviewMaterialSlot.Emissive => "emissive",
            TexturePreviewMaterialSlot.Mask => "mask",
            _ => "diff"
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

    private sealed class SkeletalMeshSelectionForm : Form
    {
        private readonly ListBox _listBox;

        public SkeletalMeshSelectionForm(IEnumerable<string> exportPaths)
        {
            Text = "Select Character SkeletalMesh";
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

    private sealed class WorkflowTextureTarget
    {
        public int SectionIndex { get; init; }
        public int MaterialIndex { get; init; }
        public string MaterialPath { get; init; } = string.Empty;
        public string ParameterName { get; init; } = string.Empty;
        public TexturePreviewMaterialSlot Slot { get; init; }
        public string CurrentTexturePath { get; init; } = string.Empty;
    }

    private sealed class WorkflowTextureSlotGroup
    {
        public int SectionIndex { get; init; }
        public int MaterialIndex { get; init; }
        public string MaterialPath { get; init; } = string.Empty;
        public TexturePreviewMaterialSlot Slot { get; init; }
        public IReadOnlyList<WorkflowTextureTarget> Targets { get; init; } = Array.Empty<WorkflowTextureTarget>();
        public string CurrentTexturePath => Targets.FirstOrDefault(static target => !string.IsNullOrWhiteSpace(target.CurrentTexturePath) && target.CurrentTexturePath != "<null>")?.CurrentTexturePath
            ?? Targets.FirstOrDefault()?.CurrentTexturePath
            ?? string.Empty;
        public string ReplacementFilePath { get; set; } = string.Empty;
        public bool IsStaged { get; set; }
        public bool IsApplied { get; set; }
        public string LastAppliedManifestFilePath { get; set; } = string.Empty;
        public string LastAppliedSourceCachePath { get; set; } = string.Empty;
        public string LastAppliedDestinationCachePath { get; set; } = string.Empty;
        public bool HasReplacementFile => !string.IsNullOrWhiteSpace(ReplacementFilePath) && File.Exists(ReplacementFilePath);
        public bool HasCurrentTextureTarget => !string.IsNullOrWhiteSpace(CurrentTexturePath) && CurrentTexturePath != "<null>";
        public bool IsReadyToApply => HasReplacementFile && HasCurrentTextureTarget;

        public string GetStatusText()
        {
            if (IsApplied)
                return "Applied and available for staged reset";

            if (IsStaged && IsReadyToApply)
                return "Pending staged mod";

            if (!HasReplacementFile)
                return "Needs replacement file";

            return HasCurrentTextureTarget
                ? "Ready to apply"
                : "Missing current texture target";
        }

        public string GetGridStatusText()
        {
            if (IsApplied)
                return "Applied";

            if (IsStaged && IsReadyToApply)
                return "Pending";

            if (!HasReplacementFile)
                return "Needs File";

            return HasCurrentTextureTarget ? "Ready" : "No Target";
        }

        public string GetStageText()
        {
            if (IsApplied)
                return "Applied";

            return IsStaged ? "Pending" : "-";
        }

        public IEnumerable<string> GetRestorePaths()
        {
            return new[]
            {
                LastAppliedManifestFilePath,
                LastAppliedSourceCachePath,
                LastAppliedDestinationCachePath
            }.Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }

    private sealed class CharacterApplyPlan
    {
        public CharacterApplyPlan(IReadOnlyList<CharacterApplyPlanEntry> entries)
        {
            Entries = entries;
        }

        public IReadOnlyList<CharacterApplyPlanEntry> Entries { get; }
        public int ApplyCount => Entries.Count(static entry => string.Equals(entry.Action, "Apply", StringComparison.OrdinalIgnoreCase));
        public int SkipCount => Entries.Count(static entry => string.Equals(entry.Action, "Skip", StringComparison.OrdinalIgnoreCase));
        public int RiskCount => Entries.Count(static entry => string.Equals(entry.Action, "Risk", StringComparison.OrdinalIgnoreCase));

        public string ToDisplayText()
        {
            List<string> lines =
            [
                "Character Apply Plan",
                $"Apply: {ApplyCount}",
                $"Skip: {SkipCount}",
                $"Risk: {RiskCount}",
                string.Empty
            ];

            foreach (CharacterApplyPlanEntry entry in Entries.OrderBy(static item => item.SectionIndex).ThenBy(static item => item.Slot))
            {
                lines.Add($"Section {entry.SectionIndex} / {entry.Slot}");
                lines.Add($"Action: {entry.Action}");
                lines.Add($"Target: {entry.CurrentTexturePath}");
                lines.Add($"Replacement: {(string.IsNullOrWhiteSpace(entry.ReplacementFilePath) ? "<none>" : entry.ReplacementFilePath)}");
                lines.Add($"TargetSize: {entry.TargetSizeText}");
                lines.Add($"ReplacementSize: {entry.ReplacementSizeText}");
                lines.Add($"DimensionsMatch: {entry.DimensionsMatch}");
                lines.Add($"LikelyNormalTarget: {entry.LikelyNormalTarget}");
                lines.Add($"Reason: {entry.Reason}");
                lines.Add(string.Empty);
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    private sealed class CharacterApplyPlanEntry
    {
        public int SectionIndex { get; init; }
        public TexturePreviewMaterialSlot Slot { get; init; }
        public string CurrentTexturePath { get; init; } = string.Empty;
        public string ReplacementFilePath { get; init; } = string.Empty;
        public string Action { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public string TargetSizeText { get; init; } = string.Empty;
        public string ReplacementSizeText { get; init; } = string.Empty;
        public bool DimensionsMatch { get; init; }
        public bool LikelyNormalTarget { get; init; }
    }

    private sealed class CharacterApplyManifestEntry
    {
        public int SectionIndex { get; init; }
        public TexturePreviewMaterialSlot Slot { get; init; }
        public string Result { get; init; } = string.Empty;
        public string CurrentTexturePath { get; init; } = string.Empty;
        public string ReplacementFilePath { get; init; } = string.Empty;
        public string Reason { get; init; } = string.Empty;
        public string ManifestFilePath { get; init; } = string.Empty;
        public string SourceTextureCachePath { get; init; } = string.Empty;
        public string DestinationTextureCachePath { get; init; } = string.Empty;

        public static CharacterApplyManifestEntry Success(CharacterApplyPlanEntry entry, TextureInjectionTargetInfo targetInfo)
        {
            return new CharacterApplyManifestEntry
            {
                SectionIndex = entry.SectionIndex,
                Slot = entry.Slot,
                Result = "Applied",
                CurrentTexturePath = entry.CurrentTexturePath,
                ReplacementFilePath = entry.ReplacementFilePath,
                Reason = "Injected successfully.",
                ManifestFilePath = targetInfo.ManifestFilePath,
                SourceTextureCachePath = targetInfo.SourceTextureCachePath,
                DestinationTextureCachePath = targetInfo.DestinationTextureCachePath
            };
        }

        public static CharacterApplyManifestEntry Failure(CharacterApplyPlanEntry entry, string reason)
        {
            return new CharacterApplyManifestEntry
            {
                SectionIndex = entry.SectionIndex,
                Slot = entry.Slot,
                Result = "Failed",
                CurrentTexturePath = entry.CurrentTexturePath,
                ReplacementFilePath = entry.ReplacementFilePath,
                Reason = reason
            };
        }
    }

    private sealed class CharacterApplyManifest
    {
        public string ManifestPath { get; init; } = string.Empty;
        public IReadOnlyList<CharacterApplyManifestEntry> Entries { get; init; } = Array.Empty<CharacterApplyManifestEntry>();

        public IEnumerable<string> BackupPaths =>
            Entries.SelectMany(static entry => new[]
            {
                entry.ManifestFilePath,
                entry.SourceTextureCachePath,
                entry.DestinationTextureCachePath
            });

        public static CharacterApplyManifest Load(string manifestPath)
        {
            string[] lines = File.ReadAllLines(manifestPath);
            List<CharacterApplyManifestEntry> entries = [];

            int index = 0;
            while (index < lines.Length)
            {
                if (!lines[index].StartsWith("Section ", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    continue;
                }

                string sectionLine = lines[index++];
                string[] sectionParts = sectionLine["Section ".Length..].Split(" / ", 2, StringSplitOptions.TrimEntries);
                int sectionIndex = sectionParts.Length > 0 && int.TryParse(sectionParts[0], out int parsedSection) ? parsedSection : -1;
                TexturePreviewMaterialSlot slot = sectionParts.Length > 1 && Enum.TryParse(sectionParts[1], out TexturePreviewMaterialSlot parsedSlot)
                    ? parsedSlot
                    : TexturePreviewMaterialSlot.Diffuse;

                string result = ReadManifestValue(lines, ref index, "Result:");
                string target = ReadManifestValue(lines, ref index, "Target:");
                string replacement = ReadManifestValue(lines, ref index, "Replacement:");
                string reason = ReadManifestValue(lines, ref index, "Reason:");
                string manifestFile = ReadManifestValue(lines, ref index, "ManifestFile:");
                string sourceCache = ReadManifestValue(lines, ref index, "SourceCache:");
                string destinationCache = ReadManifestValue(lines, ref index, "DestinationCache:");

                while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
                    index++;

                entries.Add(new CharacterApplyManifestEntry
                {
                    SectionIndex = sectionIndex,
                    Slot = slot,
                    Result = result,
                    CurrentTexturePath = target,
                    ReplacementFilePath = replacement,
                    Reason = reason,
                    ManifestFilePath = manifestFile,
                    SourceTextureCachePath = sourceCache,
                    DestinationTextureCachePath = destinationCache
                });
            }

            return new CharacterApplyManifest
            {
                ManifestPath = manifestPath,
                Entries = entries
            };
        }

        public string ToRollbackDisplayText(int restoredCount)
        {
            return
                $"Rollback Manifest{Environment.NewLine}" +
                $"ManifestPath: {ManifestPath}{Environment.NewLine}" +
                $"AppliedEntries: {Entries.Count(static entry => string.Equals(entry.Result, "Applied", StringComparison.OrdinalIgnoreCase))}{Environment.NewLine}" +
                $"RestoredFiles: {restoredCount}{Environment.NewLine}{Environment.NewLine}" +
                "Rollback restored any available .bak files listed in the manifest.";
        }

        private static string ReadManifestValue(string[] lines, ref int index, string prefix)
        {
            if (index >= lines.Length)
                return string.Empty;

            string line = lines[index++];
            return line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? line[prefix.Length..].Trim()
                : string.Empty;
        }
    }

    private sealed class CharacterStagedModManifest
    {
        public string UpkPath { get; init; } = string.Empty;
        public string SkeletalMeshPath { get; init; } = string.Empty;
        public IReadOnlyList<CharacterStagedModManifestEntry> Entries { get; init; } = Array.Empty<CharacterStagedModManifestEntry>();

        public static string Save(string upkPath, string skeletalMeshPath, IReadOnlyList<WorkflowTextureSlotGroup> targets)
        {
            StringBuilder builder = new();
            builder.AppendLine("MHUpkManager Character Texture Staged Mod Set");
            builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            builder.AppendLine($"UPK: {upkPath}");
            builder.AppendLine($"SkeletalMesh: {skeletalMeshPath}");
            builder.AppendLine();

            foreach (WorkflowTextureSlotGroup target in targets.OrderBy(static item => item.SectionIndex).ThenBy(static item => item.Slot))
            {
                builder.AppendLine($"Section {target.SectionIndex} / {target.Slot}");
                builder.AppendLine($"CurrentTexturePath: {target.CurrentTexturePath}");
                builder.AppendLine($"ReplacementFile: {target.ReplacementFilePath}");
                builder.AppendLine($"IsStaged: {target.IsStaged}");
                builder.AppendLine($"IsApplied: {target.IsApplied}");
                builder.AppendLine();
            }

            return builder.ToString();
        }

        public static CharacterStagedModManifest Load(string manifestPath)
        {
            string[] lines = File.ReadAllLines(manifestPath);
            string upkPath = string.Empty;
            string skeletalMeshPath = string.Empty;
            List<CharacterStagedModManifestEntry> entries = [];
            int index = 0;

            while (index < lines.Length)
            {
                string line = lines[index];
                if (line.StartsWith("UPK:", StringComparison.OrdinalIgnoreCase))
                {
                    upkPath = line["UPK:".Length..].Trim();
                    index++;
                    continue;
                }

                if (line.StartsWith("SkeletalMesh:", StringComparison.OrdinalIgnoreCase))
                {
                    skeletalMeshPath = line["SkeletalMesh:".Length..].Trim();
                    index++;
                    continue;
                }

                if (!line.StartsWith("Section ", StringComparison.OrdinalIgnoreCase))
                {
                    index++;
                    continue;
                }

                string[] sectionParts = line["Section ".Length..].Split(" / ", 2, StringSplitOptions.TrimEntries);
                int sectionIndex = sectionParts.Length > 0 && int.TryParse(sectionParts[0], out int parsedSection) ? parsedSection : -1;
                TexturePreviewMaterialSlot slot = sectionParts.Length > 1 && Enum.TryParse(sectionParts[1], out TexturePreviewMaterialSlot parsedSlot)
                    ? parsedSlot
                    : TexturePreviewMaterialSlot.Diffuse;
                index++;

                string currentTexturePath = ReadManifestValue(lines, ref index, "CurrentTexturePath:");
                string replacementFilePath = ReadManifestValue(lines, ref index, "ReplacementFile:");
                bool isStaged = bool.TryParse(ReadManifestValue(lines, ref index, "IsStaged:"), out bool parsedStaged) && parsedStaged;
                bool isApplied = bool.TryParse(ReadManifestValue(lines, ref index, "IsApplied:"), out bool parsedApplied) && parsedApplied;

                while (index < lines.Length && string.IsNullOrWhiteSpace(lines[index]))
                    index++;

                entries.Add(new CharacterStagedModManifestEntry
                {
                    SectionIndex = sectionIndex,
                    Slot = slot,
                    CurrentTexturePath = currentTexturePath,
                    ReplacementFilePath = replacementFilePath,
                    IsStaged = isStaged,
                    IsApplied = isApplied
                });
            }

            return new CharacterStagedModManifest
            {
                UpkPath = upkPath,
                SkeletalMeshPath = skeletalMeshPath,
                Entries = entries
            };
        }

        private static string ReadManifestValue(string[] lines, ref int index, string prefix)
        {
            if (index >= lines.Length)
                return string.Empty;

            string line = lines[index++];
            return line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? line[prefix.Length..].Trim()
                : string.Empty;
        }
    }

    private sealed class CharacterStagedModManifestEntry
    {
        public int SectionIndex { get; init; }
        public TexturePreviewMaterialSlot Slot { get; init; }
        public string CurrentTexturePath { get; init; } = string.Empty;
        public string ReplacementFilePath { get; init; } = string.Empty;
        public bool IsStaged { get; init; }
        public bool IsApplied { get; init; }
    }
}
