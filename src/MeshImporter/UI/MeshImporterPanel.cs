using MHUpkManager.UI;

namespace MHUpkManager.MeshImporter;

internal sealed class MeshImporterPanel : UserControl
{
    private const int OuterPadding = 16;
    private const int RowGap = 34;
    private const int LabelHeight = 40;
    private const int FieldHeight = 48;
    private const int CheckBoxHeight = 40;
    private const int ProgressHeight = 36;
    private const int ButtonWidth = 132;
    private const int SideGap = 12;
    private const int PercentWidth = 44;
    private const int MinimumLogHeight = 320;
    private const int SectionGap = 18;
    private const int DetailsPanelWidth = 320;

    private readonly Control _sourceSectionLabel;
    private readonly Control _importSectionLabel;
    private readonly Control _outputSectionLabel;
    private readonly Label _upkLabel;
    private readonly TextBox _upkPathTextBox;
    private readonly Button _browseUpkButton;
    private readonly Label _skeletalMeshLabel;
    private readonly ComboBox _skeletalMeshComboBox;
    private readonly Button _refreshMeshesButton;
    private readonly Label _fbxLabel;
    private readonly TextBox _fbxPathTextBox;
    private readonly Button _browseFbxButton;
    private readonly Label _lodLabel;
    private readonly ComboBox _lodComboBox;
    private readonly CheckBox _replaceAllLodsCheckBox;
    private readonly Label _progressLabel;
    private readonly ProgressBar _progressBar;
    private readonly Label _progressPercentLabel;
    private readonly Button _importButton;
    private readonly RichTextBox _detailsTextBox;
    private readonly TextBox _logTextBox;

    public MeshImporterPanel()
    {
        SuspendLayout();

        Dock = DockStyle.Fill;
        AutoScroll = false;
        BackColor = SystemColors.Control;

        _sourceSectionLabel = WorkspaceUiStyle.CreateWorkflowSectionHeader(1, "Source");
        _upkLabel = CreateLabel("UPK File:");
        _upkPathTextBox = CreateTextBox();
        _browseUpkButton = CreateButton("Browse");
        _browseUpkButton.Click += (_, _) => BrowseUpkRequested?.Invoke(this, EventArgs.Empty);

        _skeletalMeshLabel = CreateLabel("SkeletalMesh Export:");
        _skeletalMeshComboBox = CreateComboBox();
        _skeletalMeshComboBox.SelectedIndexChanged += (_, _) => SkeletalMeshChanged?.Invoke(this, EventArgs.Empty);
        _refreshMeshesButton = CreateButton("Browse");
        _refreshMeshesButton.Click += (_, _) => BrowseUpkRequested?.Invoke(this, EventArgs.Empty);

        _importSectionLabel = WorkspaceUiStyle.CreateWorkflowSectionHeader(2, "Import");
        _fbxLabel = CreateLabel("FBX File:");
        _fbxPathTextBox = CreateTextBox();
        _browseFbxButton = CreateButton("Browse");
        _browseFbxButton.Click += (_, _) => BrowseFbxRequested?.Invoke(this, EventArgs.Empty);

        _lodLabel = CreateLabel("LOD Selection:");
        _lodComboBox = CreateComboBox();

        _replaceAllLodsCheckBox = new CheckBox
        {
            Text = "Replace all LODs",
            AutoSize = false,
            UseVisualStyleBackColor = true
        };

        _outputSectionLabel = WorkspaceUiStyle.CreateWorkflowSectionHeader(4, "Output");
        _progressLabel = CreateLabel("Progress:");
        _progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100
        };
        _progressPercentLabel = new Label
        {
            Text = "0%",
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false
        };
        _importButton = CreateButton("Import Mesh");
        _importButton.Click += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty);
        _detailsTextBox = WorkspaceUiStyle.CreateReadOnlyDetailsTextBox(BuildWorkflowDetailsText());

        _logTextBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Text = "Missing bones, dropped influences, section rebuild info, LOD replacement info, and UPK injection info appear below."
        };

        Controls.AddRange(
        [
            _sourceSectionLabel,
            _upkLabel,
            _upkPathTextBox,
            _browseUpkButton,
            _skeletalMeshLabel,
            _skeletalMeshComboBox,
            _refreshMeshesButton,
            _importSectionLabel,
            _fbxLabel,
            _fbxPathTextBox,
            _browseFbxButton,
            _lodLabel,
            _lodComboBox,
            _replaceAllLodsCheckBox,
            _outputSectionLabel,
            _progressLabel,
            _progressBar,
            _progressPercentLabel,
            _importButton,
            _detailsTextBox,
            _logTextBox
        ]);

        ResumeLayout(true);
    }

    public event EventHandler BrowseUpkRequested;
    public event EventHandler BrowseFbxRequested;
    public event EventHandler SkeletalMeshChanged;
    public event EventHandler ImportRequested;

    public string UpkPath
    {
        get => InvokeIfRequired(() => _upkPathTextBox.Text);
        set => InvokeIfRequired(() => _upkPathTextBox.Text = value);
    }

    public string FbxPath
    {
        get => InvokeIfRequired(() => _fbxPathTextBox.Text);
        set => InvokeIfRequired(() => _fbxPathTextBox.Text = value);
    }

    public string SelectedMeshName => InvokeIfRequired(() => _skeletalMeshComboBox.SelectedItem as string);
    public int SelectedLodIndex => InvokeIfRequired(() => _lodComboBox.SelectedIndex < 0 ? 0 : _lodComboBox.SelectedIndex);
    public bool ReplaceAllLods => InvokeIfRequired(() => _replaceAllLodsCheckBox.Checked);

    public void SetMeshOptions(IEnumerable<string> meshNames)
    {
        InvokeIfRequired(() =>
        {
            _skeletalMeshComboBox.Items.Clear();
            foreach (string meshName in meshNames.OrderBy(static name => name))
                _skeletalMeshComboBox.Items.Add(meshName);

            if (_skeletalMeshComboBox.Items.Count > 0)
                _skeletalMeshComboBox.SelectedIndex = 0;
        });
    }

    public void SetLodOptions(int lodCount)
    {
        InvokeIfRequired(() =>
        {
            _lodComboBox.Items.Clear();
            for (int i = 0; i < Math.Max(1, lodCount); i++)
                _lodComboBox.Items.Add($"LOD {i}");

            _lodComboBox.SelectedIndex = 0;
        });
    }

    public void SetBusy(bool isBusy)
    {
        if (InvokeRequired)
        {
            Invoke(() => SetBusy(isBusy));
            return;
        }

        _browseUpkButton.Enabled = !isBusy;
        _browseFbxButton.Enabled = !isBusy;
        _refreshMeshesButton.Enabled = !isBusy;
        _skeletalMeshComboBox.Enabled = !isBusy;
        _lodComboBox.Enabled = !isBusy;
        _replaceAllLodsCheckBox.Enabled = !isBusy;
        _importButton.Enabled = !isBusy;
    }

    public void ReportProgress(int value, int maximum, string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => ReportProgress(value, maximum, message));
            return;
        }

        _progressBar.Maximum = Math.Max(1, maximum);
        _progressBar.Value = Math.Clamp(value, 0, _progressBar.Maximum);
        _progressPercentLabel.Text = $"{(int)Math.Round((_progressBar.Value / (double)_progressBar.Maximum) * 100)}%";
        AppendLog(message);
    }

    public void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            Invoke(() => AppendLog(message));
            return;
        }

        if (string.IsNullOrWhiteSpace(message))
            return;

        if (!string.IsNullOrWhiteSpace(_logTextBox.Text))
            _logTextBox.AppendText(Environment.NewLine);

        _logTextBox.AppendText(message);
    }

    public void ClearLog()
    {
        InvokeIfRequired(() => _logTextBox.Clear());
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        if (_upkLabel is null)
            return;

        int contentWidth = Math.Max(250, ClientSize.Width - (OuterPadding * 2) - DetailsPanelWidth - SideGap);
        int fieldWidth = Math.Max(120, contentWidth - ButtonWidth - SideGap);
        int y = OuterPadding;
        int detailsX = OuterPadding + contentWidth + SideGap;
        int detailsHeight = Math.Max(240, ClientSize.Height - (OuterPadding * 2));

        Place(_sourceSectionLabel, OuterPadding, y, contentWidth, WorkspaceUiStyle.SectionHeaderHeight);
        y += WorkspaceUiStyle.SectionHeaderHeight + 4;
        LayoutFieldRow(_upkLabel, _upkPathTextBox, _browseUpkButton, ref y, fieldWidth, contentWidth);
        LayoutFieldRow(_skeletalMeshLabel, _skeletalMeshComboBox, _refreshMeshesButton, ref y, fieldWidth, contentWidth);
        y += SectionGap;

        Place(_importSectionLabel, OuterPadding, y, contentWidth, WorkspaceUiStyle.SectionHeaderHeight);
        y += WorkspaceUiStyle.SectionHeaderHeight + 4;
        LayoutFieldRow(_fbxLabel, _fbxPathTextBox, _browseFbxButton, ref y, fieldWidth, contentWidth);

        Place(_lodLabel, OuterPadding, y, contentWidth, LabelHeight);
        y += LabelHeight;
        Place(_lodComboBox, OuterPadding + 104, y, contentWidth - 104, FieldHeight);
        y += FieldHeight + RowGap;

        Place(_replaceAllLodsCheckBox, OuterPadding, y, contentWidth, CheckBoxHeight);
        y += CheckBoxHeight + SectionGap;

        Place(_outputSectionLabel, OuterPadding, y, contentWidth, WorkspaceUiStyle.SectionHeaderHeight);
        y += WorkspaceUiStyle.SectionHeaderHeight + 4;

        Place(_progressLabel, OuterPadding, y, 96, LabelHeight);
        Place(_progressBar, OuterPadding + 96, y + 2, contentWidth - 96 - PercentWidth - 8, ProgressHeight);
        Place(_progressPercentLabel, OuterPadding + contentWidth - PercentWidth, y, PercentWidth, LabelHeight);
        y += ProgressHeight + RowGap;

        Place(_importButton, OuterPadding, y, contentWidth, FieldHeight);
        y += FieldHeight + RowGap;

        int remainingHeight = ClientSize.Height - y - OuterPadding;
        int logHeight = Math.Max(MinimumLogHeight, remainingHeight);
        Place(_logTextBox, OuterPadding, y, contentWidth, logHeight);
        Place(_detailsTextBox, detailsX, OuterPadding, DetailsPanelWidth, detailsHeight);
    }

    private void LayoutFieldRow(Label label, Control field, Control button, ref int y, int fieldWidth, int contentWidth)
    {
        Place(label, OuterPadding, y, contentWidth, LabelHeight);
        y += LabelHeight;
        Place(field, OuterPadding, y, fieldWidth, FieldHeight);
        Place(button, OuterPadding + fieldWidth + SideGap, y, ButtonWidth, FieldHeight);
        y += FieldHeight + RowGap;
    }

    private static Label CreateLabel(string text)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            TextAlign = ContentAlignment.BottomLeft
        };
    }

    private static TextBox CreateTextBox()
    {
        return new TextBox();
    }

    private static ComboBox CreateComboBox()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList
        };
    }

    private static Button CreateButton(string text)
    {
        Button button = WorkspaceUiStyle.CreateActionButton(text);
        button.Dock = DockStyle.None;
        button.AutoSize = false;
        button.AutoSizeMode = AutoSizeMode.GrowOnly;
        return button;
    }

    private static void Place(Control control, int x, int y, int width, int height)
    {
        control.SetBounds(x, y, Math.Max(1, width), Math.Max(1, height));
    }

    private static string BuildWorkflowDetailsText()
    {
        return string.Join(Environment.NewLine,
        [
            "Mesh Importer Workflow",
            string.Empty,
            "1. Choose the target UPK and SkeletalMesh export you want to replace.",
            "2. Choose the prepared FBX you want to import into that target mesh.",
            "3. Pick a specific LOD or enable Replace all LODs when you want the same mesh written across every LOD.",
            "4. Run Import Mesh and review the log for dropped influences, section rebuilds, topology warnings, and replacement status.",
            "5. Use this when the replacement FBX is already ready and you want to write it back into the UPK."
        ]);
    }

    private void InvokeIfRequired(Action action)
    {
        if (InvokeRequired)
        {
            Invoke(action);
            return;
        }

        action();
    }

    private T InvokeIfRequired<T>(Func<T> func)
    {
        if (InvokeRequired)
            return (T)Invoke(func);

        return func();
    }
}
