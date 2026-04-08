namespace MHUpkManager.UiEditor;

internal sealed class UiPackageSelectionForm : Form
{
    private readonly ListBox _listBox;

    public UiPackageSelectionForm(IEnumerable<string> packagePaths, string initialFolder)
    {
        Text = "Select UI Package";
        Width = 820;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;

        Label headerLabel = new()
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(12, 10, 12, 8),
            Text = $"UI-related UPKs in:{Environment.NewLine}{initialFolder}",
            AutoEllipsis = true
        };

        _listBox = new ListBox
        {
            Dock = DockStyle.Fill
        };
        _listBox.Items.AddRange(packagePaths.ToArray());
        if (_listBox.Items.Count > 0)
            _listBox.SelectedIndex = 0;
        _listBox.DoubleClick += (_, _) => ConfirmSelection();

        FlowLayoutPanel buttonPanel = new()
        {
            Dock = DockStyle.Bottom,
            Height = 56,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };

        Button openButton = new()
        {
            Text = "Use Selected",
            Width = 120,
            Height = 36
        };
        openButton.Click += (_, _) => ConfirmSelection();

        Button browseButton = new()
        {
            Text = "Browse...",
            Width = 120,
            Height = 36
        };
        browseButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Retry;
            Close();
        };

        Button cancelButton = new()
        {
            Text = "Cancel",
            Width = 120,
            Height = 36
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        buttonPanel.Controls.Add(openButton);
        buttonPanel.Controls.Add(browseButton);
        buttonPanel.Controls.Add(cancelButton);

        Controls.Add(_listBox);
        Controls.Add(buttonPanel);
        Controls.Add(headerLabel);
    }

    public string SelectedPackagePath => _listBox.SelectedItem as string;

    private void ConfirmSelection()
    {
        if (_listBox.SelectedItem == null)
            return;

        DialogResult = DialogResult.OK;
        Close();
    }
}
