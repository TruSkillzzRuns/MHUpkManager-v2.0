namespace MHUpkManager.TexturePreview;

internal sealed class TexturePreviewLogger
{
    private readonly TextBox _target;

    public TexturePreviewLogger(TextBox target)
    {
        _target = target;
    }

    public void Clear()
    {
        _target.Clear();
    }

    public void Log(string message)
    {
        string line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        if (_target.InvokeRequired)
        {
            _target.BeginInvoke(new Action(() => AppendLine(line)));
            return;
        }

        AppendLine(line);
    }

    private void AppendLine(string line)
    {
        _target.AppendText(line + Environment.NewLine);
    }
}
