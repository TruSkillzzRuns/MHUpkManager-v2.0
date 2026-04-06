namespace MHUpkManager.TextureWorkspace;

internal sealed class TextureWorkspaceUI : UserControl
{
    private const int NavWidth = 300;

    private readonly Panel _navPanel;
    private readonly Panel _contentPanel;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly Button _previewButton;
    private readonly Button _materialsButton;
    private readonly Button _mappingButton;
    private readonly Button _swapButton;
    private readonly Button _workflowButton;
    private readonly Dictionary<string, Control> _views = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Button> _buttons = new(StringComparer.OrdinalIgnoreCase);
    private string _activeKey = string.Empty;
    private bool _darkModeEnabled;

    public TextureWorkspaceUI(
        Control texturePreviewView,
        Control materialInspectorView,
        Control sectionMappingView,
        Control materialTextureSwapView,
        Control characterTextureWorkflowView)
    {
        Dock = DockStyle.Fill;

        _titleLabel = new Label
        {
            Text = "Texture Workspace",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 34,
            Font = new Font(Font.FontFamily, 12.5f, FontStyle.Bold),
            TextAlign = ContentAlignment.BottomLeft
        };

        _subtitleLabel = new Label
        {
            Text = "Preview, inspect, and analyze texture and material workflows from one place.",
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 56,
            ForeColor = Color.DimGray
        };

        _previewButton = CreateNavButton("Texture Preview");
        _materialsButton = CreateNavButton("Material Inspector");
        _mappingButton = CreateNavButton("Section Mapping");
        _swapButton = CreateNavButton("Material Swap");
        _workflowButton = CreateNavButton("Character Workflow");

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
        navFlow.Controls.Add(_materialsButton);
        navFlow.Controls.Add(_mappingButton);
        navFlow.Controls.Add(_swapButton);
        navFlow.Controls.Add(_workflowButton);
        _navPanel.Controls.Add(navFlow);

        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill
        };

        Controls.Add(_contentPanel);
        Controls.Add(_navPanel);

        RegisterView("preview", _previewButton, texturePreviewView);
        RegisterView("materials", _materialsButton, materialInspectorView);
        RegisterView("mapping", _mappingButton, sectionMappingView);
        RegisterView("swap", _swapButton, materialTextureSwapView);
        RegisterView("workflow", _workflowButton, characterTextureWorkflowView);

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
            ApplyButtonState(button, string.Equals(key, _activeKey, StringComparison.OrdinalIgnoreCase), _darkModeEnabled);
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

        _activeKey = key;
    }

    private static Button CreateNavButton(string text)
    {
        return new Button
        {
            Text = text,
            Width = 256,
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
