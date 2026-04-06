namespace MHUpkManager.MeshPreview;

internal sealed class MeshPreviewLogger
{
    private readonly TextBox _textBox;

    public MeshPreviewLogger(TextBox textBox)
    {
        _textBox = textBox;
    }

    public void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        if (_textBox.TextLength > 0)
            _textBox.AppendText(Environment.NewLine);

        _textBox.AppendText(message);
    }

    public void Clear()
    {
        _textBox.Clear();
    }
}
