namespace MHUpkManager.MeshPreview;

internal sealed class MeshPreviewControl : UserControl
{
    private readonly Panel _viewportHost;
    private IMeshPreviewViewportBackend _backend;
    private bool _handlingBackendFailure;

    public MeshPreviewControl()
    {
        Dock = DockStyle.Fill;
        _viewportHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Black
        };

        Controls.Add(_viewportHost);
        SwitchBackend(MeshPreviewBackend.OpenTK);
    }

    public MeshPreviewScene Scene { get; } = new();

    public MeshPreviewBackend Backend { get; private set; }

    public void SetBackend(MeshPreviewBackend backend)
    {
        if (Backend == backend && _backend != null)
            return;

        MeshPreviewDiagnostics.Log($"MeshPreviewControl.SetBackend requested: current={Backend}, requested={backend}");
        SwitchBackend(backend);
        ResetCamera();
        RefreshPreview();
    }

    public void ResetCamera()
    {
        _backend?.ResetCamera();
    }

    public void RefreshPreview()
    {
        _backend?.RefreshPreview();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Scene.Clear();
            _backend?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void SwitchBackend(MeshPreviewBackend backend)
    {
        MeshPreviewDiagnostics.Log($"MeshPreviewControl.SwitchBackend start: requested={backend}, previous={Backend}, activeBackendType={_backend?.GetType().Name ?? "<none>"}");
        Control oldView = _backend?.View;
        if (oldView != null)
            _viewportHost.Controls.Remove(oldView);

        IMeshPreviewViewportBackend previousBackend = _backend;
        _backend = null;
        previousBackend?.Dispose();

        try
        {
            _backend = backend switch
            {
                MeshPreviewBackend.VorticeDirect3D11 => CreateVorticeBackend(),
                _ => new OpenTkMeshPreviewViewport(Scene)
            };
            MeshPreviewDiagnostics.Log($"MeshPreviewControl.SwitchBackend created backend: {_backend.GetType().Name}");
        }
        catch (Exception ex)
        {
            MeshPreviewDiagnostics.LogException($"MeshPreviewControl.SwitchBackend failed creating {backend}", ex);
            _backend?.Dispose();
            _backend = new OpenTkMeshPreviewViewport(Scene);
            backend = MeshPreviewBackend.OpenTK;
            MeshPreviewDiagnostics.Log("MeshPreviewControl.SwitchBackend fell back to OpenTK after backend creation failure.");
        }

        Backend = backend;
        Control view = _backend.View;
        view.Dock = DockStyle.Fill;
        _viewportHost.Controls.Add(view);
        MeshPreviewDiagnostics.Log($"MeshPreviewControl.SwitchBackend complete: active={Backend}, view={view.GetType().Name}");
    }

    private IMeshPreviewViewportBackend CreateVorticeBackend()
    {
        VorticeMeshPreviewViewport viewport = new(Scene);
        viewport.BackendFailed += HandleVorticeBackendFailure;
        return viewport;
    }

    private void HandleVorticeBackendFailure(Exception exception)
    {
        MeshPreviewDiagnostics.LogException("MeshPreviewControl.HandleVorticeBackendFailure", exception);
        if (_handlingBackendFailure)
            return;

        _handlingBackendFailure = true;
        Action fallback = () =>
        {
            try
            {
                if (Backend == MeshPreviewBackend.VorticeDirect3D11)
                {
                    SwitchBackend(MeshPreviewBackend.OpenTK);
                    ResetCamera();
                    RefreshPreview();
                }
            }
            finally
            {
                _handlingBackendFailure = false;
            }
        };

        if (IsHandleCreated)
            BeginInvoke(fallback);
        else
            fallback();
    }
}
