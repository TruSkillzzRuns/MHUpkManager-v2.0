using OpenTK.GLControl;
using System.Numerics;

namespace MHUpkManager.TexturePreview;

internal sealed class TexturePreviewControl : UserControl
{
    private readonly GLControl _glControl;
    private readonly TexturePreviewRenderer _renderer = new();
    private readonly List<TexturePreviewTexture> _loadedTextures = [];
    private Point _lastMousePosition;
    private bool _isPanning;
    private float _zoom = 1.0f;
    private Vector2 _pan = Vector2.Zero;

    public TexturePreviewControl()
    {
        Dock = DockStyle.Fill;
        AllowDrop = true;

        _glControl = new GLControl(new GLControlSettings
        {
            API = OpenTK.Windowing.Common.ContextAPI.OpenGL,
            APIVersion = new Version(3, 3),
            Profile = OpenTK.Windowing.Common.ContextProfile.Core,
            Flags = OpenTK.Windowing.Common.ContextFlags.Default
        })
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black
        };

        Controls.Add(_glControl);

        _glControl.Load += (_, _) => _renderer.Initialize();
        _glControl.Paint += (_, _) => Draw();
        _glControl.Resize += (_, _) => _glControl.Invalidate();
        _glControl.MouseWheel += OnMouseWheel;
        _glControl.MouseDown += OnMouseDown;
        _glControl.MouseUp += (_, _) => _isPanning = false;
        _glControl.MouseMove += OnMouseMove;
        _glControl.DragEnter += OnDragEnter;
        _glControl.DragDrop += OnDragDrop;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;
    }

    public event EventHandler<string> TextureFileDropped;

    public TexturePreviewTexture CurrentTexture { get; private set; }

    public void SetTexture(TexturePreviewTexture texture)
    {
        _loadedTextures.Clear();
        if (texture != null)
            _loadedTextures.Add(texture);

        CurrentTexture = texture;
        _renderer.SetTexture(texture);
        ResetView();
        _glControl.Invalidate();
    }

    public void SetTextures(IReadOnlyList<TexturePreviewTexture> textures)
    {
        _loadedTextures.Clear();
        if (textures != null)
            _loadedTextures.AddRange(textures.Where(static texture => texture != null));

        // TODO: Add thumbnail strip or cycling controls when multi-texture browsing is needed.
        CurrentTexture = _loadedTextures.FirstOrDefault();
        _renderer.SetTexture(CurrentTexture);
        ResetView();
        _glControl.Invalidate();
    }

    public void ResetView()
    {
        _zoom = 1.0f;
        _pan = Vector2.Zero;
        _glControl.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderer.Dispose();
            _glControl.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Draw()
    {
        _glControl.MakeCurrent();
        _renderer.Render(_glControl.ClientSize.Width, _glControl.ClientSize.Height, _zoom, _pan);
        _glControl.SwapBuffers();
    }

    private void OnMouseWheel(object sender, MouseEventArgs e)
    {
        float multiplier = e.Delta > 0 ? 1.15f : 1.0f / 1.15f;
        _zoom = Math.Clamp(_zoom * multiplier, 0.1f, 64.0f);
        _glControl.Invalidate();
    }

    private void OnMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left && e.Button != MouseButtons.Middle)
            return;

        _isPanning = true;
        _lastMousePosition = e.Location;
        _glControl.Focus();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPanning)
            return;

        _pan += new Vector2(e.X - _lastMousePosition.X, e.Y - _lastMousePosition.Y);
        _lastMousePosition = e.Location;
        _glControl.Invalidate();
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object sender, DragEventArgs e)
    {
        string[] files = e.Data?.GetData(DataFormats.FileDrop) as string[];
        string file = files?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(file))
            TextureFileDropped?.Invoke(this, file);
    }
}
