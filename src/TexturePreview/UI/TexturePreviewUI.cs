using MHUpkManager.MeshPreview;
using MHUpkManager.UI;

namespace MHUpkManager.TexturePreview;

internal sealed class TexturePreviewUI : UserControl
{
    private const int LeftPanelWidth = 430;
    private const int DetailsPanelWidth = 300;

    private readonly MeshPreviewUI _meshPreviewUi;
    private readonly Func<string> _currentUpkPathProvider;
    private readonly Func<string> _currentTextureExportPathProvider;
    private readonly SplitContainer _verticalSplit;
    private readonly SplitContainer _workspaceSplit;
    private readonly Panel _contentPanel;
    private readonly Panel _leftPanelHost;
    private readonly Panel _viewportPanel;
    private readonly TableLayoutPanel _leftLayout;
    private readonly Button _loadFileButton;
    private readonly Button _loadUpkButton;
    private readonly Button _loadSelectedButton;
    private readonly ComboBox _textureTypeComboBox;
    private readonly CheckBox _applyToMeshPreviewCheckBox;
    private readonly CheckBox _autoInjectIntoUpkCheckBox;
    private readonly Button _injectTextureButton;
    private readonly Button _exportTextureButton;
    private readonly ComboBox _mipComboBox;
    private readonly Label _resolutionValueLabel;
    private readonly Label _formatValueLabel;
    private readonly Label _mipCountValueLabel;
    private readonly Label _compressionValueLabel;
    private readonly Label _mipSourceValueLabel;
    private readonly Button _resetMaterialButton;
    private readonly RichTextBox _detailsTextBox;
    private readonly TextBox _logTextBox;
    private readonly TexturePreviewControl _previewControl;
    private readonly TexturePreviewLogger _logger;
    private readonly ToolTip _toolTip;
    private readonly TextureLoader _textureLoader = new();
    private readonly UpkTextureLoader _upkTextureLoader = new();
    private readonly TexturePreviewInjector _injector = new();
    private readonly TextureToMaterialConverter _converter = new();
    private readonly MaterialPreviewBinder _materialBinder;
    private readonly List<TexturePreviewTexture> _loadedTextures = [];
    private TexturePreviewTexture _currentTexture;
    private bool _injectTextureBusy;
    private bool _updatingMipSelection;
    private bool _workspaceSplitInitialized;

    public TexturePreviewUI(
        MeshPreviewUI meshPreviewUi,
        Func<string> currentUpkPathProvider = null,
        Func<string> currentTextureExportPathProvider = null)
    {
        _meshPreviewUi = meshPreviewUi;
        _currentUpkPathProvider = currentUpkPathProvider;
        _currentTextureExportPathProvider = currentTextureExportPathProvider;
        Dock = DockStyle.Fill;

        _loadFileButton = CreateButton("Load From Disk");
        _loadUpkButton = CreateButton("Load From UPK");
        _loadSelectedButton = CreateButton("Load Selected Texture");
        _textureTypeComboBox = CreateComboBox();
        _textureTypeComboBox.Items.AddRange(Enum.GetNames(typeof(TexturePreviewMaterialSlot)));
        _textureTypeComboBox.SelectedItem = nameof(TexturePreviewMaterialSlot.Diffuse);
        _applyToMeshPreviewCheckBox = CreateCheckBox("Apply To Preview Mesh");
        _autoInjectIntoUpkCheckBox = CreateCheckBox("Auto Inject UPK");
        _injectTextureButton = CreateButton("Inject Texture");
        _exportTextureButton = CreateButton("Export Texture");
        _mipComboBox = CreateComboBox();
        _resetMaterialButton = CreateButton("Clear Loaded Textures");

        _logTextBox = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill
        };
        _toolTip = new ToolTip();
        _logger = new TexturePreviewLogger(_logTextBox);
        _materialBinder = new MaterialPreviewBinder(_meshPreviewUi, _logger, _converter);

        _previewControl = new TexturePreviewControl { Dock = DockStyle.Fill, AllowDrop = true };
        _detailsTextBox = WorkspaceUiStyle.CreateReadOnlyDetailsTextBox(BuildWorkflowDetailsText());

        _leftPanelHost = new Panel
        {
            Dock = DockStyle.Left,
            AutoScroll = true,
            Width = LeftPanelWidth,
            MinimumSize = new Size(LeftPanelWidth, 0)
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
            Padding = new Padding(8)
        };
        _leftLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _leftLayout.RowCount = 0;

        _resolutionValueLabel = CreateValueLabel();
        _formatValueLabel = CreateValueLabel();
        _mipCountValueLabel = CreateValueLabel();
        _compressionValueLabel = CreateValueLabel();
        _mipSourceValueLabel = CreateValueLabel();

        _toolTip.SetToolTip(_loadFileButton, "Select a texture from disk (PNG, DDS, TGA, JPG).");
        _toolTip.SetToolTip(_loadUpkButton, "Select one or more Texture2D exports from the current UPK, or browse for a UPK if none is open.");
        _toolTip.SetToolTip(_loadSelectedButton, "Load the currently selected Texture2D export from the object tree.");

        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(1, "Source"));
        AddRow(_loadFileButton);
        AddRow(_loadUpkButton);
        AddRow(_loadSelectedButton);
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(2, "Actions"));
        AddRow(CreateLabel("Texture Type:"));
        AddRow(_textureTypeComboBox);
        AddRow(_applyToMeshPreviewCheckBox);
        AddRow(_autoInjectIntoUpkCheckBox);
        AddRow(_injectTextureButton);
        AddRow(_exportTextureButton);
        AddRow(WorkspaceUiStyle.CreateWorkflowSectionHeader(3, "Details"));
        AddRow(CreateLabel("Mip Level:"));
        AddRow(_mipComboBox);
        AddRow(CreateLabel("Resolution:"));
        AddRow(_resolutionValueLabel);
        AddRow(CreateLabel("Format:"));
        AddRow(_formatValueLabel);
        AddRow(CreateLabel("Mip Count:"));
        AddRow(_mipCountValueLabel);
        AddRow(CreateLabel("Mip Source:"));
        AddRow(_mipSourceValueLabel);
        AddRow(CreateLabel("Compression:"));
        AddRow(_compressionValueLabel);
        AddRow(_resetMaterialButton, 0);

        _leftPanelHost.Controls.Add(_leftLayout);
        _viewportPanel.Controls.Add(_previewControl);
        _workspaceSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
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
            SplitterDistance = Math.Max(400, Height - 170)
        };
        _verticalSplit.Panel1.Controls.Add(_contentPanel);
        _verticalSplit.Panel2.Controls.Add(_logTextBox);
        _verticalSplit.Panel2MinSize = 120;

        Controls.Add(_verticalSplit);

        WireEvents();
        Resize += (_, _) =>
        {
            if (_workspaceSplitInitialized)
                UpdateWorkspaceSplit();
        };
        Load += (_, _) =>
        {
            _workspaceSplitInitialized = true;
            BeginInvoke(new Action(UpdateWorkspaceSplit));
        };
    }

    private void WireEvents()
    {
        _loadFileButton.Click += (_, _) => LoadTextureFromFile();
        _loadUpkButton.Click += async (_, _) => await LoadTextureFromUpkAsync().ConfigureAwait(true);
        _loadSelectedButton.Click += async (_, _) => await LoadSelectedTextureAsync().ConfigureAwait(true);
        _textureTypeComboBox.SelectedIndexChanged += (_, _) => UpdateCurrentSlot();
        _applyToMeshPreviewCheckBox.CheckedChanged += (_, _) => _materialBinder.SetEnabled(_applyToMeshPreviewCheckBox.Checked);
        _mipComboBox.SelectedIndexChanged += async (_, _) => await ReloadSelectedMipAsync().ConfigureAwait(true);
        _injectTextureButton.Click += (_, _) => InjectTexture();
        _exportTextureButton.Click += (_, _) => ExportTexture();
        _resetMaterialButton.Click += (_, _) => ResetMaterial();
        _previewControl.TextureFileDropped += (_, filePath) => HandleDroppedTexture(filePath);
    }

    private void HandleDroppedTexture(string filePath)
    {
        try
        {
            LoadTexture(filePath);
            _logger.Log($"Loaded dropped texture {filePath}.");
        }
        catch (Exception ex)
        {
            _logger.Log($"Texture drop failed: {ex.Message}");
        }
    }

    private void LoadTextureFromFile()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Texture Files|*.png;*.jpg;*.jpeg;*.dds;*.tga",
            Title = "Select Texture File(s)",
            Multiselect = true
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        try
        {
            if (dialog.FileNames.Length == 1)
            {
                LoadTexture(dialog.FileName);
                return;
            }

            TexturePreviewMaterialSlot fallbackSlot = SelectedSlot;
            List<TexturePreviewTexture> textures = [];
            foreach (string filePath in dialog.FileNames)
            {
                TexturePreviewTexture texture = _textureLoader.LoadFromFile(filePath, fallbackSlot);
                texture.Slot = _converter.ResolveSlot(Path.GetFileNameWithoutExtension(filePath), fallbackSlot);
                textures.Add(texture);
            }

            SetCurrentTextures(textures);
            _logger.Log($"Loaded {textures.Count} texture(s) from disk.");
            foreach (TexturePreviewTexture texture in textures)
                _logger.Log($"Loaded texture file: {texture.Name}");
        }
        catch (Exception ex)
        {
            _logger.Log($"Texture load failed: {ex.Message}");
        }
    }

    private void LoadTexture(string filePath)
    {
        TexturePreviewMaterialSlot fallbackSlot = SelectedSlot;
        TexturePreviewTexture texture = _textureLoader.LoadFromFile(filePath, fallbackSlot);
        texture.Slot = _converter.ResolveSlot(Path.GetFileNameWithoutExtension(filePath), fallbackSlot);
        _textureTypeComboBox.SelectedItem = texture.Slot.ToString();
        SetCurrentTexture(texture);
    }

    private async Task LoadTextureFromUpkAsync()
    {
        string upkPath = _currentUpkPathProvider?.Invoke();
        if (string.IsNullOrWhiteSpace(upkPath) || !File.Exists(upkPath))
        {
            using OpenFileDialog dialog = new()
            {
                Filter = "Unreal Package Files (*.upk)|*.upk",
                Title = "Select UPK Containing a Texture2D"
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            upkPath = dialog.FileName;
        }

        try
        {
            List<string> exports = await _upkTextureLoader.GetTextureExportsAsync(upkPath).ConfigureAwait(true);
            if (exports.Count == 0)
                throw new InvalidOperationException("The selected UPK did not contain any Texture2D exports.");

            using TextureSelectionForm picker = new(exports);
            if (picker.ShowDialog(FindForm()) != DialogResult.OK)
                return;

            List<string> selectedExportPaths = picker.SelectedExportPaths;
            if (selectedExportPaths.Count == 0)
                return;

            TexturePreviewMaterialSlot fallbackSlot = SelectedSlot;
            List<TexturePreviewTexture> textures = [];
            foreach (string exportPath in selectedExportPaths)
            {
                TexturePreviewTexture texture = await _upkTextureLoader.LoadFromUpkAsync(upkPath, exportPath, fallbackSlot, _logger.Log).ConfigureAwait(true);
                texture.Slot = _converter.ResolveSlot(exportPath, fallbackSlot);
                textures.Add(texture);
            }

            SetCurrentTextures(textures);
            _logger.Log($"Loaded {textures.Count} texture(s) from UPK.");
            foreach (TexturePreviewTexture texture in textures)
                _logger.Log($"Loaded texture export: {texture.Name}");
        }
        catch (Exception ex)
        {
            _logger.Log($"UPK texture load failed: {ex.Message}");
        }
    }

    private async Task LoadSelectedTextureAsync()
    {
        string upkPath = _currentUpkPathProvider?.Invoke();
        string exportPath = _currentTextureExportPathProvider?.Invoke();

        if (string.IsNullOrWhiteSpace(upkPath) || !File.Exists(upkPath))
        {
            _logger.Log("Open a UPK in the main window first, or use Load From UPK.");
            return;
        }

        if (string.IsNullOrWhiteSpace(exportPath))
        {
            _logger.Log("Select a Texture2D export in the object tree first.");
            return;
        }

        try
        {
            TexturePreviewMaterialSlot fallbackSlot = SelectedSlot;
            TexturePreviewTexture texture = await _upkTextureLoader.LoadFromUpkAsync(upkPath, exportPath, fallbackSlot, _logger.Log).ConfigureAwait(true);
            texture.Slot = _converter.ResolveSlot(exportPath, fallbackSlot);
            _textureTypeComboBox.SelectedItem = texture.Slot.ToString();
            SetCurrentTexture(texture);
        }
        catch (Exception ex)
        {
            _logger.Log($"Selected texture load failed: {ex.Message}");
        }
    }

    private void SetCurrentTexture(TexturePreviewTexture texture)
    {
        _loadedTextures.Clear();
        if (texture != null)
            _loadedTextures.Add(texture);

        _currentTexture = texture;
        _previewControl.SetTexture(texture);
        PopulateMipLevels(texture);
        UpdateMetadata(texture);
        UpdateDetailsText(texture);
        _logger.Log($"Loaded texture {texture.Name} [{texture.ResolutionText}] from {texture.SourceDescription}.");

        if (_applyToMeshPreviewCheckBox.Checked)
            ApplyLoadedTexturesToMaterial(true);
    }

    private void SetCurrentTextures(IReadOnlyList<TexturePreviewTexture> textures)
    {
        _loadedTextures.Clear();
        if (textures != null)
            _loadedTextures.AddRange(textures.Where(static texture => texture != null));

        _currentTexture = _loadedTextures.FirstOrDefault();
        _previewControl.SetTextures(_loadedTextures);
        PopulateMipLevels(_currentTexture);
        UpdateMetadata(_currentTexture);
        UpdateDetailsText(_currentTexture);

        if (_currentTexture == null)
            return;

        _textureTypeComboBox.SelectedItem = _currentTexture.Slot.ToString();

        if (_applyToMeshPreviewCheckBox.Checked)
            ApplyLoadedTexturesToMaterial(true);
    }

    private void UpdateCurrentSlot()
    {
        if (_currentTexture == null)
            return;

        _currentTexture.Slot = SelectedSlot;
        if (_applyToMeshPreviewCheckBox.Checked)
            ApplyLoadedTexturesToMaterial(true);
    }

    private void UpdateMetadata(TexturePreviewTexture texture)
    {
        _resolutionValueLabel.Text = texture?.ResolutionText ?? "-";
        _formatValueLabel.Text = texture?.Format ?? "-";
        _mipCountValueLabel.Text = texture == null ? "-" : texture.MipCount.ToString();
        _mipSourceValueLabel.Text = texture?.MipSource ?? "-";
        _compressionValueLabel.Text = texture?.Compression ?? "-";
    }

    private void UpdateDetailsText(TexturePreviewTexture texture)
    {
        _detailsTextBox.Text = BuildWorkflowDetailsText(texture);
    }

    private void InjectTexture()
    {
        if (_currentTexture == null)
        {
            _logger.Log("Load a texture before injecting.");
            return;
        }

        if (_injectTextureBusy)
        {
            _logger.Log("Texture injection is already running.");
            return;
        }

        _ = InjectTextureAsync();
    }

    private async Task InjectTextureAsync()
    {
        using OpenFileDialog dialog = new()
        {
            Filter = "Unreal Package Files (*.upk)|*.upk",
            Title = "Select UPK Containing the Target Texture2D"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        string logFilePath = BeginTextureInjectionLog();
        Action<string> compositeLog = message =>
        {
            _logger.Log(message);
            AppendTextureInjectionLog(logFilePath, message);
        };

        try
        {
            SetInjectionBusy(true);
            compositeLog("Enumerating texture exports from selected UPK.");
            List<string> exports = await _upkTextureLoader.GetTextureExportsAsync(dialog.FileName).ConfigureAwait(true);
            if (exports.Count == 0)
                throw new InvalidOperationException("The selected UPK did not contain any Texture2D exports.");

            using TextureSelectionForm picker = new(exports);
            if (picker.ShowDialog(FindForm()) != DialogResult.OK)
                return;

            string exportPath = picker.SelectedExportPaths.FirstOrDefault();
            if (string.IsNullOrWhiteSpace(exportPath))
                return;

            compositeLog($"Starting texture injection into {exportPath}.");
            await _injector.InjectAsync(dialog.FileName, exportPath, _currentTexture, compositeLog).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            compositeLog($"Texture injection failed: {ex.Message}");
        }
        finally
        {
            compositeLog($"Texture injection log: {logFilePath}");
            SetInjectionBusy(false);
        }
    }

    private static string BeginTextureInjectionLog()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "MHUpkManager_TextureLogs");
        Directory.CreateDirectory(directory);

        string path = Path.Combine(
            directory,
            $"texture-inject-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        File.WriteAllText(path, "Texture Injection Diagnostics" + Environment.NewLine);
        return path;
    }

    private static void AppendTextureInjectionLog(string path, string message)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        File.AppendAllText(path, line);
    }

    private void ExportTexture()
    {
        if (_currentTexture == null)
        {
            _logger.Log("Load a texture before exporting.");
            return;
        }

        using SaveFileDialog dialog = new()
        {
            FileName = _currentTexture.Name,
            Filter = "PNG Files (*.png)|*.png|DDS Files (*.dds)|*.dds",
            Title = "Export Preview Texture"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (extension == ".dds" && _currentTexture.ContainerBytes != null && string.Equals(_currentTexture.ContainerType, "DDS", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllBytes(dialog.FileName, _currentTexture.ContainerBytes);
            _logger.Log($"Exported DDS texture to {dialog.FileName}.");
            return;
        }

        _currentTexture.Bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);
        _logger.Log($"Exported preview bitmap to {dialog.FileName}.");
    }

    private void ResetMaterial()
    {
        _loadedTextures.Clear();
        _currentTexture = null;
        _previewControl.SetTextures(Array.Empty<TexturePreviewTexture>());
        PopulateMipLevels(null);
        UpdateMetadata(null);
        UpdateDetailsText(null);
        _textureTypeComboBox.SelectedItem = nameof(TexturePreviewMaterialSlot.Diffuse);
        _materialBinder.ResetMaterial();
        _applyToMeshPreviewCheckBox.Checked = false;
        _logger.Log("Cleared loaded textures and preview selection.");
    }

    public void LoadExternalTexture(TexturePreviewTexture texture)
    {
        if (texture == null)
            return;

        _textureTypeComboBox.SelectedItem = texture.Slot.ToString();
        SetCurrentTexture(texture);
        _logger.Log($"Loaded texture from Texture Viewer: {texture.Name}");
    }

    private void UpdateWorkspaceSplit()
    {
        if (!_workspaceSplitInitialized || _workspaceSplit.Width <= 0)
            return;

        FixedDetailsSplitLayout.Apply(_workspaceSplit, DetailsPanelWidth);
    }

    private static string BuildWorkflowDetailsText(TexturePreviewTexture texture = null)
    {
        List<string> lines =
        [
            "Texture Preview Workflow",
            string.Empty,
            "1. Load a texture from disk, UPK, or the current object-tree selection.",
            "2. Set the texture slot if you need to drive Mesh Preview materials.",
            "3. Inspect resolution, format, mip count, mip source, and compression.",
            "4. Export the texture or inject it into a Texture2D target when you are ready.",
            "5. Use this as the low-level texture workbench underneath Material Swap and Character Workflow."
        ];

        if (texture != null)
        {
            lines.Add(string.Empty);
            lines.Add("Current Texture");
            lines.Add($"Name: {texture.Name}");
            lines.Add($"Slot: {texture.Slot}");
            lines.Add($"Resolution: {texture.ResolutionText}");
            lines.Add($"Format: {texture.Format}");
            lines.Add($"MipCount: {texture.MipCount}");
            lines.Add($"Source: {texture.SourceDescription}");
            lines.Add($"ExportPath: {texture.ExportPath ?? "<none>"}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private TexturePreviewMaterialSlot SelectedSlot =>
        Enum.TryParse<TexturePreviewMaterialSlot>(_textureTypeComboBox.SelectedItem as string, out TexturePreviewMaterialSlot slot)
            ? slot
            : TexturePreviewMaterialSlot.Diffuse;

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

    private static CheckBox CreateCheckBox(string text)
    {
        return new CheckBox
        {
            Text = text,
            AutoSize = true,
            Dock = DockStyle.Top,
            UseVisualStyleBackColor = true,
            Padding = new Padding(0, 2, 0, 2)
        };
    }

    private static ComboBox CreateComboBox()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Dock = DockStyle.Top
        };
    }

    private void ApplyLoadedTexturesToMaterial(bool applyToMeshPreview)
    {
        if (_loadedTextures.Count == 0)
            return;

        _materialBinder.ApplyTextures(_loadedTextures, applyToMeshPreview);
    }

    private void PopulateMipLevels(TexturePreviewTexture texture)
    {
        _updatingMipSelection = true;
        try
        {
            _mipComboBox.Items.Clear();
            if (texture?.AvailableMipLevels == null || texture.AvailableMipLevels.Count == 0)
            {
                _mipComboBox.Enabled = false;
                return;
            }

            _mipComboBox.Enabled = texture.AvailableMipLevels.Count > 1;
            foreach (TexturePreviewMipLevel mip in texture.AvailableMipLevels)
                _mipComboBox.Items.Add(mip);

            TexturePreviewMipLevel selectedMip = texture.AvailableMipLevels
                .FirstOrDefault(mip => mip.AbsoluteIndex == texture.SelectedMipIndex)
                ?? texture.AvailableMipLevels.First();
            _mipComboBox.SelectedItem = selectedMip;
        }
        finally
        {
            _updatingMipSelection = false;
        }
    }

    private async Task ReloadSelectedMipAsync()
    {
        if (_updatingMipSelection || _currentTexture == null || !_currentTexture.IsUpkTexture)
            return;

        if (_mipComboBox.SelectedItem is not TexturePreviewMipLevel mip || mip.AbsoluteIndex == _currentTexture.SelectedMipIndex)
            return;

        try
        {
            TexturePreviewTexture reloaded = await _upkTextureLoader
                .LoadFromUpkAsync(_currentTexture.SourcePath, _currentTexture.ExportPath, _currentTexture.Slot, _logger.Log, mip.AbsoluteIndex)
                .ConfigureAwait(true);
            reloaded.Slot = _currentTexture.Slot;
            SetCurrentTexture(reloaded);
        }
        catch (Exception ex)
        {
            _logger.Log($"Mip load failed: {ex.Message}");
        }
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

    private static Label CreateValueLabel()
    {
        return new Label
        {
            Text = "-",
            AutoSize = true,
            Dock = DockStyle.Top,
            ForeColor = Color.DimGray
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

    private sealed class TextureSelectionForm : Form
    {
        private readonly List<string> _exports;
        private readonly TextBox _filterTextBox;
        private readonly Label _countLabel;
        private readonly ListBox _listBox;
        private readonly Button _okButton;
        private readonly Button _cancelButton;
        private readonly Panel _buttonPanel;

        public TextureSelectionForm(IEnumerable<string> exports)
        {
            _exports = exports.OrderBy(static exportPath => exportPath).ToList();
            Text = "Select Texture2D Export";
            Width = 640;
            Height = 480;
            MinimumSize = new Size(420, 320);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            MinimizeBox = false;

            _filterTextBox = new TextBox
            {
                Dock = DockStyle.Top,
                PlaceholderText = "Filter exports..."
            };
            _filterTextBox.TextChanged += (_, _) => ApplyFilter();

            _countLabel = new Label
            {
                Dock = DockStyle.Top,
                AutoSize = false,
                Height = 20,
                TextAlign = ContentAlignment.MiddleLeft
            };

            _listBox = new ListBox
            {
                Dock = DockStyle.Fill,
                SelectionMode = SelectionMode.MultiExtended
            };
            _listBox.DoubleClick += (_, _) => ConfirmSelection();

            _okButton = new Button
            {
                Text = "Load",
                DialogResult = DialogResult.OK,
                Width = 90,
                Height = 32,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom
            };
            _okButton.Click += (_, _) => ConfirmSelection();

            _cancelButton = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Width = 90,
                Height = 32,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom
            };

            FlowLayoutPanel buttonFlow = new()
            {
                Dock = DockStyle.Right,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Padding = new Padding(0)
            };
            buttonFlow.Controls.Add(_cancelButton);
            buttonFlow.Controls.Add(_okButton);

            _buttonPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                Padding = new Padding(8)
            };
            _buttonPanel.Controls.Add(buttonFlow);

            AcceptButton = _okButton;
            CancelButton = _cancelButton;

            Controls.Add(_listBox);
            Controls.Add(_countLabel);
            Controls.Add(_filterTextBox);
            Controls.Add(_buttonPanel);

            ApplyFilter();
        }

        public List<string> SelectedExportPaths => _listBox.SelectedItems.Cast<string>().ToList();

        private void ConfirmSelection()
        {
            if (_listBox.SelectedItems.Count == 0)
                return;

            DialogResult = DialogResult.OK;
            Close();
        }

        private void ApplyFilter()
        {
            string filter = _filterTextBox.Text?.Trim() ?? string.Empty;
            IEnumerable<string> filtered = string.IsNullOrWhiteSpace(filter)
                ? _exports
                : _exports.Where(exportPath => exportPath.Contains(filter, StringComparison.OrdinalIgnoreCase));

            string previouslySelected = _listBox.SelectedItem as string;
            _listBox.BeginUpdate();
            try
            {
                _listBox.Items.Clear();
                foreach (string exportPath in filtered)
                    _listBox.Items.Add(exportPath);
            }
            finally
            {
                _listBox.EndUpdate();
            }

            if (!string.IsNullOrWhiteSpace(previouslySelected))
            {
                int previousIndex = _listBox.Items.IndexOf(previouslySelected);
                if (previousIndex >= 0)
                    _listBox.SelectedIndex = previousIndex;
            }

            if (_listBox.SelectedIndex < 0 && _listBox.Items.Count > 0)
                _listBox.SelectedIndex = 0;

            _countLabel.Text = $"{_listBox.Items.Count} export(s)";
        }
    }

    private void SetInjectionBusy(bool busy)
    {
        _injectTextureBusy = busy;
        _injectTextureButton.Enabled = !busy;
        _loadFileButton.Enabled = !busy;
        _loadUpkButton.Enabled = !busy;
        UseWaitCursor = busy;

        Form form = FindForm();
        if (form != null)
            form.UseWaitCursor = busy;
    }
}
