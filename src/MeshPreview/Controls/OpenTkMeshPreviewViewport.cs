using System.Numerics;
using OpenTK.GLControl;

namespace MHUpkManager.MeshPreview;

internal sealed class OpenTkMeshPreviewViewport : UserControl, IMeshPreviewViewportBackend
{
    private readonly GLControl _glControl;
    private readonly MeshPreviewRenderer _renderer = new();
    private readonly MeshPreviewCamera _camera = new();
    private readonly MeshPreviewScene _scene;
    private Point _lastMousePosition;
    private MouseButtons _activeButton = MouseButtons.None;
    private bool _disposed;

    public OpenTkMeshPreviewViewport(MeshPreviewScene scene)
    {
        _scene = scene;
        Dock = DockStyle.Fill;
        MeshPreviewDiagnostics.Log("OpenTkMeshPreviewViewport created.");

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
        _glControl.MouseDown += OnMouseDown;
        _glControl.MouseUp += OnMouseUp;
        _glControl.MouseMove += OnMouseMove;
        _glControl.MouseWheel += OnMouseWheel;
    }

    public Control View => this;

    public void ResetCamera()
    {
        MeshPreviewMesh focusMesh = _scene.Ue3Mesh ?? _scene.FbxMesh;
        if (focusMesh == null)
            _camera.Reset(Vector3.Zero, 10.0f);
        else
            _camera.Reset(focusMesh.Center, MathF.Max(1.0f, focusMesh.Radius));

        _glControl.Invalidate();
    }

    public void RefreshPreview()
    {
        _glControl.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            MeshPreviewDiagnostics.Log("OpenTkMeshPreviewViewport disposing.");
            ReleaseSceneMeshGlResources();
            _renderer.Dispose();
            _glControl.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Draw()
    {
        if (_disposed || !IsHandleCreated || _glControl.IsDisposed || _glControl.ClientSize.Width <= 0 || _glControl.ClientSize.Height <= 0)
            return;

        try
        {
            _glControl.MakeCurrent();
            _renderer.Render(_scene, _camera, _glControl.ClientSize.Width, _glControl.ClientSize.Height);
            _glControl.SwapBuffers();
        }
        catch (Exception ex)
        {
            MeshPreviewDiagnostics.LogException("OpenTkMeshPreviewViewport.Draw", ex);
            throw;
        }
    }

    private void ReleaseSceneMeshGlResources()
    {
        if (_glControl.IsDisposed || !_glControl.IsHandleCreated)
            return;

        try
        {
            _glControl.MakeCurrent();
            _scene.FbxMesh?.Dispose();
            _scene.Ue3Mesh?.Dispose();
        }
        catch
        {
            // Ignore GL teardown failures during backend switches; the goal is to clear stale upload state when possible.
        }
    }

    private void OnMouseDown(object sender, MouseEventArgs e)
    {
        _activeButton = e.Button;
        _lastMousePosition = e.Location;
        _glControl.Focus();
    }

    private void OnMouseUp(object sender, MouseEventArgs e)
    {
        _activeButton = MouseButtons.None;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_activeButton == MouseButtons.None)
            return;

        float dx = e.X - _lastMousePosition.X;
        float dy = e.Y - _lastMousePosition.Y;
        _lastMousePosition = e.Location;

        if (_activeButton == MouseButtons.Left)
            _camera.Orbit(dx, dy);
        else if (_activeButton == MouseButtons.Middle)
            _camera.Pan(dx, -dy);

        _glControl.Invalidate();
    }

    private void OnMouseWheel(object sender, MouseEventArgs e)
    {
        _camera.Zoom(e.Delta / 120.0f);
        _glControl.Invalidate();
    }
}
