namespace MHUpkManager.MeshWorkspace;

internal sealed class MeshWorkspaceUI : UserControl
{
    private const int NavWidth = 280;

    private readonly Panel _navPanel;
    private readonly Panel _contentPanel;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly Button _previewButton;
    private readonly Button _exporterButton;
    private readonly Button _importerButton;
    private readonly Button _sectionsButton;
    private readonly Dictionary<string, Control> _views = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.OrdinalIgnoreCase);
    private bool _darkModeEnabled;

    public MeshWorkspaceUI(
        Control meshPreviewView,
        Control meshExporterView,
        Control meshImporterView,
        Control meshSectionsView)
    {
        Dock = DockStyle.Fill;

        _titleLabel = new Label
        {
            Text = "Mesh Workspace",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 34,
            Font = new Font(Font.FontFamily, 12.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        };

        _subtitleLabel = new Label
        {
            Text = "Preview meshes, export FBX, and import replacements from one workspace.",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 56,
            ForeColor = Color.DimGray
        };

        _previewButton = CreateNavButton("Preview");
        _exporterButton = CreateNavButton("Exporter");
        _importerButton = CreateNavButton("Importer");
        _sectionsButton = CreateNavButton("Sections");

        _navPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = NavWidth,
            Padding = new Padding(12)
        };

        FlowLayoutPanel navFlow = new()
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true
        };
        navFlow.Controls.Add(_titleLabel);
        navFlow.Controls.Add(_subtitleLabel);
        navFlow.Controls.Add(_previewButton);
        navFlow.Controls.Add(_exporterButton);
        navFlow.Controls.Add(_importerButton);
        navFlow.Controls.Add(_sectionsButton);
        _navPanel.Controls.Add(navFlow);

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill
        };

        Controls.Add(_contentPanel);
        Controls.Add(_navPanel);

        RegisterView("preview", _previewButton, meshPreviewView);
        RegisterView("exporter", _exporterButton, meshExporterView);
        RegisterView("importer", _importerButton, meshImporterView);
        RegisterView("sections", _sectionsButton, meshSectionsView);

        ShowView("preview");
    }

    public void SetDarkMode(bool enabled)
    {
        _darkModeEnabled = enabled;
        BackColor = enabled ? Color.FromArgb(18, 18, 18) : SystemColors.Control;
        _navPanel.BackColor = enabled ? Color.FromArgb(24, 24, 24) : SystemColors.Control;
        _contentPanel.BackColor = enabled ? Color.FromArgb(18, 18, 18) : SystemColors.Control;
        _titleLabel.ForeColor = enabled ? Color.FromArgb(232, 232, 232) : SystemColors.ControlText;
        _subtitleLabel.ForeColor = enabled ? Color.FromArgb(232, 232, 232) : Color.DimGray;

        foreach ((string key, Button button) in _buttons)
            ApplyButtonState(button, _views.TryGetValue(key, out Control view) && view.Visible, _darkModeEnabled);
    }

    private void RegisterView(string key, Button button, Control view)
    {
        view.Dock = DockStyle.Fill;
        view.Visible = false;
        _views[key] = view;
        _buttons[key] = button;
        _contentPanel.Controls.Add(view);
        button.Click += (_, _) => ShowView(key);
    }

    private void ShowView(string key)
    {
        if (!_views.ContainsKey(key))
            return;

        foreach ((string viewKey, Control view) in _views)
            view.Visible = string.Equals(viewKey, key, StringComparison.OrdinalIgnoreCase);

        foreach ((string buttonKey, Button button) in _buttons)
            ApplyButtonState(button, string.Equals(buttonKey, key, StringComparison.OrdinalIgnoreCase), _darkModeEnabled);
    }

    public void ShowPreviewView()
    {
        ShowView("preview");
    }

    private static Button CreateNavButton(string text)
    {
        return new Button
        {
            Text = text,
            Width = 232,
            Height = 42,
            TextAlign = ContentAlignment.MiddleLeft,
            UseVisualStyleBackColor = true,
            Margin = new Padding(0, 0, 0, 8)
        };
    }

    private static void ApplyButtonState(Button button, bool active, bool darkModeEnabled)
    {
        button.UseVisualStyleBackColor = false;
        button.ForeColor = darkModeEnabled ? Color.FromArgb(232, 232, 232) : SystemColors.ControlText;
        button.BackColor = darkModeEnabled
            ? active ? Color.FromArgb(52, 95, 160) : Color.FromArgb(36, 36, 36)
            : active ? Color.FromArgb(225, 235, 250) : SystemColors.Control;
        button.Font = new Font(button.Font, active ? FontStyle.Bold : FontStyle.Regular);
    }
}
