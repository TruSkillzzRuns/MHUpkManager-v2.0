using MHUpkManager.TexturePreview;

namespace MHUpkManager.UiEditor;

internal sealed class UiImagePreviewForm : Form
{
    private readonly PictureBox _pictureBox;
    private readonly Label _summaryLabel;

    public UiImagePreviewForm(TexturePreviewTexture texture)
    {
        ArgumentNullException.ThrowIfNull(texture);

        Text = $"UI Image Preview - {texture.ExportPath}";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(700, 500);
        Size = new Size(1100, 800);

        _summaryLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(12, 10, 12, 10),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = BuildSummary(texture)
        };

        _pictureBox = new PictureBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 20),
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = (Image)texture.Bitmap.Clone()
        };

        Controls.Add(_pictureBox);
        Controls.Add(_summaryLabel);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _pictureBox.Image?.Dispose();

        base.Dispose(disposing);
    }

    private static string BuildSummary(TexturePreviewTexture texture)
    {
        return $"{texture.ExportPath}    |    {texture.Width}x{texture.Height}    |    {texture.Format}    |    Mip {texture.SelectedMipIndex}";
    }
}
