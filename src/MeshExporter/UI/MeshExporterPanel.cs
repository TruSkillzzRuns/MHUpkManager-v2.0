using MHUpkManager.UI;

namespace MHUpkManager.MeshExporter;

internal sealed class MeshExporterPanel : UserControl
{
    private const int OuterPadding = 16;
    private const int RowGap = 34;
    private const int LabelHeight = 40;
    private const int FieldHeight = 48;
    private const int ProgressHeight = 36;
    private const int ButtonWidth = 132;
    private const int SideGap = 12;
    private const int PercentWidth = 44;
    private const int MinimumLogHeight = 320;
    private const int SectionGap = 18;
    private const int DetailsPanelWidth = 320;

    private readonly Control _sourceSectionLabel;
    private readonly Control _exportSectionLabel;
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
    private readonly Label _progressLabel;
    private readonly ProgressBar _progressBar;
    private readonly Label _progressPercentLabel;
    private readonly Button _exportButton;
    private readonly RichTextBox _detailsTextBox;
    private readonly TextBox _logTextBox;

    public MeshExporterPanel()
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

        _exportSectionLabel = WorkspaceUiStyle.CreateWorkflowSectionHeader(2, "Export");
        _fbxLabel = CreateLabel("FBX File:");
        _fbxPathTextBox = CreateTextBox();
        _browseFbxButton = CreateButton("Browse");
        _browseFbxButton.Click += (_, _) => BrowseFbxRequested?.Invoke(this, EventArgs.Empty);

        _lodLabel = CreateLabel("LOD Selection:");
        _lodComboBox = CreateComboBox();

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

        _outputSectionLabel = WorkspaceUiStyle.CreateWorkflowSectionHeader(4, "Output");
        _exportButton = CreateButton("Export FBX");
        _exportButton.Click += (_, _) => ExportRequested?.Invoke(this, EventArgs.Empty);
        _detailsTextBox = WorkspaceUiStyle.CreateReadOnlyDetailsTextBox(BuildWorkflowDetailsText());

        _logTextBox = new TextBox
        {
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            ReadOnly = true,
            Text = "SkeletalMesh export discovery, LOD inspection, and FBX export steps appear below."
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
            _exportSectionLabel,
            _fbxLabel,
            _fbxPathTextBox,
            _browseFbxButton,
            _lodLabel,
            _lodComboBox,
            _outputSectionLabel,
            _progressLabel,
            _progressBar,
            _progressPercentLabel,
            _exportButton,
            _detailsTextBox,
            _logTextBox
        ]);

        ResumeLayout(true);
    }

    public event EventHandler BrowseUpkRequested;
    public event EventHandler BrowseFbxRequested;
    public event EventHandler SkeletalMeshChanged;
    public event EventHandler ExportRequested;

    public string UpkPath
    {
        get => _upkPathTextBox.Text;
        set => _upkPathTextBox.Text = value;
    }

    public string FbxPath
    {
        get => _fbxPathTextBox.Text;
        set => _fbxPathTextBox.Text = value;
    }

    public string SelectedMeshName => _skeletalMeshComboBox.SelectedItem as string;
    public int SelectedLodIndex => _lodComboBox.SelectedIndex < 0 ? 0 : _lodComboBox.SelectedIndex;

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

    public void SetBusy(bool isBusy)
    {
        _browseUpkButton.Enabled = !isBusy;
        _browseFbxButton.Enabled = !isBusy;
        _refreshMeshesButton.Enabled = !isBusy;
        _skeletalMeshComboBox.Enabled = !isBusy;
        _lodComboBox.Enabled = !isBusy;
        _exportButton.Enabled = !isBusy;
    }

    public void ReportProgress(int value, int maximum, string message)
    {
        _progressBar.Maximum = Math.Max(1, maximum);
        _progressBar.Value = Math.Clamp(value, 0, _progressBar.Maximum);
        _progressPercentLabel.Text = $"{(int)Math.Round((_progressBar.Value / (double)_progressBar.Maximum) * 100)}%";
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

        Place(_exportSectionLabel, OuterPadding, y, contentWidth, WorkspaceUiStyle.SectionHeaderHeight);
        y += WorkspaceUiStyle.SectionHeaderHeight + 4;
        LayoutFieldRow(_fbxLabel, _fbxPathTextBox, _browseFbxButton, ref y, fieldWidth, contentWidth);

        Place(_lodLabel, OuterPadding, y, contentWidth, LabelHeight);
        y += LabelHeight;
        Place(_lodComboBox, OuterPadding + 104, y, contentWidth - 104, FieldHeight);
        y += FieldHeight + SectionGap;

        Place(_outputSectionLabel, OuterPadding, y, contentWidth, WorkspaceUiStyle.SectionHeaderHeight);
        y += WorkspaceUiStyle.SectionHeaderHeight + 4;

        Place(_progressLabel, OuterPadding, y, 96, LabelHeight);
        Place(_progressBar, OuterPadding + 96, y + 2, contentWidth - 96 - PercentWidth - 8, ProgressHeight);
        Place(_progressPercentLabel, OuterPadding + contentWidth - PercentWidth, y, PercentWidth, LabelHeight);
        y += ProgressHeight + RowGap;

        Place(_exportButton, OuterPadding, y, contentWidth, FieldHeight);
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
            "Mesh Exporter Workflow",
            string.Empty,
            "1. Choose the source UPK and SkeletalMesh export you want to inspect.",
            "2. Pick the LOD you want to export from that game mesh.",
            "3. Choose the FBX output path for the exported file.",
            "4. Run Export FBX and review the log for mesh discovery, LOD inspection, and export status.",
            "5. Use this when you want to inspect, edit, or retarget the current game mesh in external tools."
        ]);
    }
}
