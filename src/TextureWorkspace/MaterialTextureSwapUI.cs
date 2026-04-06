using MHUpkManager.TexturePreview;
using MHUpkManager.UI;
using UpkManager.Models.UpkFile.Engine.Material;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace MHUpkManager.TextureWorkspace;

internal sealed class MaterialTextureSwapUI : UserControl
{
    private const int LeftPanelWidth = 430;
    private const int BottomLogHeight = 160;
    private const int DetailsPanelWidth = 260;
    private bool _contentSplitInitialized;

    private readonly Func<string> _currentUpkPathProvider;
    private readonly Func<string> _currentSkeletalMeshExportPathProvider;
    private readonly Action<TexturePreviewTexture> _openTextureInPreview;
    private readonly UpkFileRepository _repository = new();
    private readonly UpkTextureLoader _upkTextureLoader = new();
    private readonly SplitContainer _verticalSplit;
    private readonly SplitContainer _contentSplit;
    private readonly TableLayoutPanel _leftLayout;
    private readonly TextBox _upkPathTextBox;
    private readonly TextBox _meshPathTextBox;
    private readonly Button _useSelectedButton;
    private readonly Button _browseUpkButton;
    private readonly Button _refreshButton;
    private readonly Button _openCurrentTextureButton;
    private readonly Button _loadReplacementToPreviewButton;
    private readonly ListView _sectionListView;
    private readonly ListView _textureParamListView;
    private readonly RichTextBox _detailsTextBox;
    private readonly TextBox _logTextBox;
    private readonly TextureLoader _textureLoader = new();
    private List<SwapSectionInfo> _sections = [];

    public MaterialTextureSwapUI(
        Func<string> currentUpkPathProvider,
        Func<string> currentSkeletalMeshExportPathProvider,
        Action<TexturePreviewTexture> openTextureInPreview)
    {
        _currentUpkPathProvider = currentUpkPathProvider;
        _currentSkeletalMeshExportPathProvider = currentSkeletalMeshExportPathProvider;
        _openTextureInPreview = openTextureInPreview;
        Dock = DockStyle.Fill;

        _upkPathTextBox = CreatePathTextBox("No UPK selected.");
        _meshPathTextBox = CreatePathTextBox("No SkeletalMesh selected.");
        _useSelectedButton = CreateButton("Use Selected SkeletalMesh");
        _browseUpkButton = CreateButton("Browse");
        _refreshButton = CreateButton("Refresh Targets");
        _openCurrentTextureButton = CreateButton("Open Current Target");
        _loadReplacementToPreviewButton = CreateButton("Load Replacement File");

        _sectionListView = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details,
            GridLines = true,
            Font = new Font(Font.FontFamily, 10.0f)
        };
        _sectionListView.Columns.Add("Section", 95);
        _sectionListView.Columns.Add("Material", 110);
        _sectionListView.Columns.Add("Path", 620);

        _textureParamListView = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            HideSelection = false,
            MultiSelect = false,
            View = View.Details,
            GridLines = true,
            Font = new Font(Font.FontFamily, 10.0f)
        };
        _textureParamListView.Columns.Add("Param", 210);
        _textureParamListView.Columns.Add("Slot", 120);
        _textureParamListView.Columns.Add("Texture", 495);

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
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(2, "Actions"));
        AddRow(_refreshButton);
        AddRow(_openCurrentTextureButton);
        AddRow(_loadReplacementToPreviewButton, 0);

        Panel leftPanel = new()
        {
            Dock = DockStyle.Left,
            Width = LeftPanelWidth,
            MinimumSize = new Size(LeftPanelWidth, 0),
            AutoScroll = true
        };
        leftPanel.Controls.Add(_leftLayout);

        SplitContainer innerSplit = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            FixedPanel = FixedPanel.Panel1,
            SplitterDistance = 220
        };
        innerSplit.Panel1.Controls.Add(_sectionListView);
        innerSplit.Panel2.Controls.Add(_textureParamListView);

        _contentSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        _contentSplit.Panel1.Controls.Add(innerSplit);
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

        _useSelectedButton.Click += async (_, _) =>
        {
            UseSelected();
            await RefreshTargetsAsync().ConfigureAwait(true);
        };
        _browseUpkButton.Click += async (_, _) => await BrowseUpkAsync().ConfigureAwait(true);
        _refreshButton.Click += async (_, _) => await RefreshTargetsAsync().ConfigureAwait(true);
        _sectionListView.SelectedIndexChanged += (_, _) => BindTextureParams();
        _textureParamListView.SelectedIndexChanged += (_, _) => ShowSelectionDetails();
        _openCurrentTextureButton.Click += async (_, _) => await OpenCurrentTextureInPreviewAsync().ConfigureAwait(true);
        _loadReplacementToPreviewButton.Click += (_, _) => LoadReplacementFileToPreview();
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

    private void UseSelected()
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
            List<string> exports = await GetSkeletalMeshExportsAsync(dialog.FileName).ConfigureAwait(true);
            if (exports.Count == 0)
                throw new InvalidOperationException("The selected UPK did not contain any SkeletalMesh exports.");

            using SkeletalMeshSelectionForm selectionForm = new(exports);
            if (selectionForm.ShowDialog(FindForm()) != DialogResult.OK)
                return;

            _upkPathTextBox.Text = dialog.FileName;
            _meshPathTextBox.Text = selectionForm.SelectedExportPath;
            await RefreshTargetsAsync().ConfigureAwait(true);
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

    private async Task RefreshTargetsAsync()
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

        try
        {
            SetBusy(true);
            _sections = await LoadSectionsAsync(_upkPathTextBox.Text, _meshPathTextBox.Text).ConfigureAwait(true);
            BindSections();
            Log($"Loaded {_sections.Count} section material target(s).");
        }
        catch (Exception ex)
        {
            Log($"Failed to load material texture targets: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task<List<SwapSectionInfo>> LoadSectionsAsync(string upkPath, string skeletalMeshExportPath)
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

        List<SwapSectionInfo> sections = [];
        if (skeletalMesh.LODModels.Count == 0)
            return sections;

        FStaticLODModel lod = skeletalMesh.LODModels[0];
        for (int sectionIndex = 0; sectionIndex < lod.Sections.Count; sectionIndex++)
        {
            FSkelMeshSection section = lod.Sections[sectionIndex];
            FObject materialObject = section.MaterialIndex >= 0 && section.MaterialIndex < skeletalMesh.Materials.Count
                ? skeletalMesh.Materials[section.MaterialIndex]
                : null;

            UMaterialInstanceConstant material = materialObject?.LoadObject<UMaterialInstanceConstant>();
            List<SwapTextureTarget> targets = [];
            if (material?.TextureParameterValues != null)
            {
                foreach (FTextureParameterValue parameter in material.TextureParameterValues)
                {
                    targets.Add(new SwapTextureTarget
                    {
                        ParameterName = parameter.ParameterName?.Name ?? "<unnamed>",
                        TexturePath = parameter.ParameterValue?.GetPathName() ?? "<null>",
                        Slot = ResolveSlot(parameter.ParameterName?.Name)
                    });
                }
            }

            sections.Add(new SwapSectionInfo
            {
                SectionIndex = sectionIndex,
                MaterialIndex = section.MaterialIndex,
                MaterialPath = materialObject?.GetPathName() ?? "<missing>",
                Targets = [.. targets.OrderBy(static target => target.ParameterName, StringComparer.OrdinalIgnoreCase)]
            });
        }

        return sections;
    }

    private void BindSections()
    {
        _sectionListView.BeginUpdate();
        try
        {
            _sectionListView.Items.Clear();
            foreach (SwapSectionInfo section in _sections)
            {
                ListViewItem item = new(section.SectionIndex.ToString());
                item.SubItems.Add(section.MaterialIndex.ToString());
                item.SubItems.Add(section.MaterialPath);
                item.Tag = section;
                _sectionListView.Items.Add(item);
            }
        }
        finally
        {
            _sectionListView.EndUpdate();
        }

        _textureParamListView.Items.Clear();
        if (_sectionListView.Items.Count > 0)
            _sectionListView.Items[0].Selected = true;
    }

    private void BindTextureParams()
    {
        _textureParamListView.BeginUpdate();
        try
        {
            _textureParamListView.Items.Clear();
            if (_sectionListView.SelectedItems.Count == 0 || _sectionListView.SelectedItems[0].Tag is not SwapSectionInfo section)
                return;

            foreach (SwapTextureTarget target in section.Targets)
            {
                ListViewItem item = new(target.ParameterName);
                item.SubItems.Add(target.Slot.ToString());
                item.SubItems.Add(target.TexturePath);
                item.Tag = (section, target);
                _textureParamListView.Items.Add(item);
            }
        }
        finally
        {
            _textureParamListView.EndUpdate();
        }

        if (_textureParamListView.Items.Count > 0)
            _textureParamListView.Items[0].Selected = true;
        else
            _detailsTextBox.Text = BuildWorkflowDetailsText() + Environment.NewLine + Environment.NewLine + "The selected material did not expose any texture parameters.";
    }

    private void ShowSelectionDetails()
    {
        if (_textureParamListView.SelectedItems.Count == 0 || _textureParamListView.SelectedItems[0].Tag is not ValueTuple<SwapSectionInfo, SwapTextureTarget> tagged)
        {
            _detailsTextBox.Text = BuildWorkflowDetailsText();
            return;
        }

        SwapSectionInfo section = tagged.Item1;
        SwapTextureTarget target = tagged.Item2;
        _detailsTextBox.Text = string.Join(Environment.NewLine,
        [
            BuildWorkflowDetailsText(),
            string.Empty,
            "Current Texture Target",
            $"SectionIndex: {section.SectionIndex}",
            $"MaterialIndex: {section.MaterialIndex}",
            $"MaterialPath: {section.MaterialPath}",
            $"TextureParameter: {target.ParameterName}",
            $"TargetSlot: {target.Slot}",
            $"CurrentTexturePath: {target.TexturePath}",
            string.Empty,
            "Open the current target in Texture Preview to confirm the real texture, or load a replacement file into Texture Preview using the inferred slot."
        ]);
    }

    private static string BuildWorkflowDetailsText()
    {
        return WorkspaceUiStyle.BuildWorkflowText(
            "Material Swap Workflow",
            "Select the target SkeletalMesh from the current UPK or Browse.",
            "Refresh Targets to resolve section materials and texture parameters.",
            "Pick a section, then pick a texture parameter from the lower list.",
            "Open the current target in Texture Preview or load a replacement file into Texture Preview.",
            "Use this view to bridge from mesh sections and material params to the real texture exports.");
    }

    private async Task OpenCurrentTextureInPreviewAsync()
    {
        if (_textureParamListView.SelectedItems.Count == 0 || _textureParamListView.SelectedItems[0].Tag is not ValueTuple<SwapSectionInfo, SwapTextureTarget> tagged)
        {
            Log("Select a texture parameter first.");
            return;
        }

        if (string.IsNullOrWhiteSpace(tagged.Item2.TexturePath) || tagged.Item2.TexturePath == "<null>")
        {
            Log("The selected texture parameter does not point at a texture export.");
            return;
        }

        try
        {
            SetBusy(true);
            TexturePreviewTexture texture = await _upkTextureLoader
                .LoadFromUpkAsync(_upkPathTextBox.Text, tagged.Item2.TexturePath, tagged.Item2.Slot, Log)
                .ConfigureAwait(true);
            _openTextureInPreview?.Invoke(texture);
            Log($"Opened {tagged.Item2.TexturePath} in Texture Preview.");
        }
        catch (Exception ex)
        {
            Log($"Failed to open current texture target: {ex.Message}");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void LoadReplacementFileToPreview()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Texture Files|*.png;*.jpg;*.jpeg;*.dds;*.tga",
            Title = "Select Replacement Texture"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            TexturePreviewMaterialSlot slot = TexturePreviewMaterialSlot.Diffuse;
            if (_textureParamListView.SelectedItems.Count > 0 && _textureParamListView.SelectedItems[0].Tag is ValueTuple<SwapSectionInfo, SwapTextureTarget> tagged)
                slot = tagged.Item2.Slot;

            TexturePreviewTexture texture = _textureLoader.LoadFromFile(dialog.FileName, slot);
            texture.Slot = slot;
            _openTextureInPreview?.Invoke(texture);
            Log($"Loaded replacement texture into Texture Preview: {dialog.FileName}");
        }
        catch (Exception ex)
        {
            Log($"Failed to load replacement texture: {ex.Message}");
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

    private void Log(string message)
    {
        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void SetBusy(bool busy)
    {
        _useSelectedButton.Enabled = !busy;
        _browseUpkButton.Enabled = !busy;
        _refreshButton.Enabled = !busy;
        _openCurrentTextureButton.Enabled = !busy;
        _loadReplacementToPreviewButton.Enabled = !busy;
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

    private sealed class SwapSectionInfo
    {
        public int SectionIndex { get; init; }
        public int MaterialIndex { get; init; }
        public string MaterialPath { get; init; } = string.Empty;
        public IReadOnlyList<SwapTextureTarget> Targets { get; init; } = Array.Empty<SwapTextureTarget>();
    }

    private sealed class SwapTextureTarget
    {
        public string ParameterName { get; init; } = string.Empty;
        public string TexturePath { get; init; } = string.Empty;
        public TexturePreviewMaterialSlot Slot { get; init; }
    }
}
