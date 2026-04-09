using System.Numerics;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.D3DCompiler;
using Vortice.DXGI;
using MHUpkManager.TexturePreview;
using static Vortice.Direct3D11.D3D11;
using static Vortice.DXGI.DXGI;

namespace MHUpkManager.MeshPreview;

internal sealed class VorticeMeshPreviewViewport : UserControl, IMeshPreviewViewportBackend
{
    private readonly MeshPreviewScene _scene;
    private readonly MeshPreviewCamera _camera = new();
    private readonly VorticeMeshPreviewRenderer _renderer = new();
    private Point _lastMousePosition;
    private MouseButtons _activeButton = MouseButtons.None;
    private bool _disposed;
    private bool _faulted;

    public event Action<Exception> BackendFailed;

    public VorticeMeshPreviewViewport(MeshPreviewScene scene)
    {
        _scene = scene;
        Dock = DockStyle.Fill;
        BackColor = System.Drawing.Color.Black;
        MeshPreviewDiagnostics.Log("VorticeMeshPreviewViewport created.");
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        MouseDown += OnMouseDown;
        MouseUp += OnMouseUp;
        MouseMove += OnMouseMove;
        MouseWheel += OnMouseWheel;
        Resize += (_, _) => ResizeRenderer();
    }

    public Control View => this;

    public void ResetCamera()
    {
        MeshPreviewMesh focusMesh = _scene.Ue3Mesh ?? _scene.FbxMesh;
        if (focusMesh == null)
            _camera.Reset(Vector3.Zero, 10.0f);
        else
            _camera.Reset(focusMesh.Center, MathF.Max(1.0f, focusMesh.Radius));

        Invalidate();
    }

    public void RefreshPreview()
    {
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        MeshPreviewDiagnostics.Log($"VorticeMeshPreviewViewport handle created: {Handle}");
        ResizeRenderer();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        MeshPreviewDiagnostics.Log("VorticeMeshPreviewViewport handle destroyed.");
        DisposeRenderer();
        base.OnHandleDestroyed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_disposed || _faulted || !IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        try
        {
            _renderer.Render(Handle, _scene, _camera, ClientSize.Width, ClientSize.Height);
        }
        catch (Exception ex)
        {
            HandleRendererFailure(ex);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            MeshPreviewDiagnostics.Log("VorticeMeshPreviewViewport disposing.");
            DisposeRenderer();
        }

        base.Dispose(disposing);
    }

    private void DisposeRenderer()
    {
        if (_disposed)
            return;

        _disposed = true;
        _renderer.Dispose();
    }

    private void ResizeRenderer()
    {
        if (_disposed || _faulted || !IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0)
            return;

        try
        {
            MeshPreviewDiagnostics.Log($"VorticeMeshPreviewViewport.ResizeRenderer width={ClientSize.Width} height={ClientSize.Height}");
            _renderer.Resize(Handle, ClientSize.Width, ClientSize.Height);
            Invalidate();
        }
        catch (Exception ex)
        {
            HandleRendererFailure(ex);
        }
    }

    private void HandleRendererFailure(Exception exception)
    {
        if (_faulted)
            return;

        MeshPreviewDiagnostics.LogException("VorticeMeshPreviewViewport.HandleRendererFailure", exception);
        _faulted = true;
        DisposeRenderer();
        BackendFailed?.Invoke(exception);
    }

    private void OnMouseDown(object sender, MouseEventArgs e)
    {
        _activeButton = e.Button;
        _lastMousePosition = e.Location;
        Focus();
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

        Invalidate();
    }

    private void OnMouseWheel(object sender, MouseEventArgs e)
    {
        _camera.Zoom(e.Delta / 120.0f);
        Invalidate();
    }
}

internal sealed class VorticeMeshPreviewRenderer : IDisposable
{
    private ID3D11Device _device;
    private ID3D11DeviceContext _context;
    private IDXGISwapChain1 _swapChain;
    private ID3D11RenderTargetView _renderTargetView;
    private ID3D11Texture2D _depthTexture;
    private ID3D11DepthStencilView _depthStencilView;
    private ID3D11VertexShader _meshVertexShader;
    private ID3D11PixelShader _meshPixelShader;
    private ID3D11InputLayout _meshInputLayout;
    private ID3D11VertexShader _backgroundVertexShader;
    private ID3D11PixelShader _backgroundPixelShader;
    private ID3D11InputLayout _backgroundInputLayout;
    private ID3D11Buffer _backgroundVertexBuffer;
    private ID3D11VertexShader _lineVertexShader;
    private ID3D11PixelShader _linePixelShader;
    private ID3D11InputLayout _lineInputLayout;
    private ID3D11Buffer _backgroundConstantBuffer;
    private ID3D11Buffer _meshConstantBuffer;
    private ID3D11Buffer _lineConstantBuffer;
    private ID3D11RasterizerState _solidRasterizerState;
    private ID3D11RasterizerState _wireframeRasterizerState;
    private ID3D11RasterizerState _solidRasterizerStateNoCull;
    private ID3D11RasterizerState _wireframeRasterizerStateNoCull;
    private ID3D11SamplerState _textureSampler;
    private int _materialRevision = -1;
    private readonly Dictionary<TexturePreviewMaterialSlot, ID3D11ShaderResourceView> _materialTextures = [];
    private readonly Dictionary<string, ID3D11ShaderResourceView> _gameMaterialTextures = [];
    private readonly Dictionary<MeshPreviewMesh, CachedMeshBuffers> _meshBuffers = [];
    private IntPtr _hwnd;
    private int _width;
    private int _height;
    private bool _disposed;

    public void Resize(IntPtr hwnd, int width, int height)
    {
        if (_disposed)
            return;

        if (width <= 0 || height <= 0)
            return;

        EnsureDevice(hwnd, width, height);
        if (_swapChain == null || (_width == width && _height == height && _hwnd == hwnd))
            return;

        _width = width;
        _height = height;
        _hwnd = hwnd;
        ReleaseRenderTargets();
        _swapChain.ResizeBuffers(0, (uint)width, (uint)height, Format.Unknown, SwapChainFlags.None);
        CreateRenderTargets(width, height);
    }

    public void Render(IntPtr hwnd, MeshPreviewScene scene, MeshPreviewCamera camera, int width, int height)
    {
        if (_disposed)
            return;

        if (width <= 0 || height <= 0)
            return;

        EnsureDevice(hwnd, width, height);
        _context.OMSetRenderTargets(_renderTargetView, _depthStencilView);
        _context.ClearRenderTargetView(_renderTargetView, ResolveClearColor(scene.BackgroundStyle));
        _context.ClearDepthStencilView(_depthStencilView, DepthStencilClearFlags.Depth, 1.0f, 0);
        if (scene.BackgroundStyle == MeshPreviewBackgroundStyle.Checker)
            DrawCheckerOverlay();

        float aspect = Math.Max(1, width) / (float)Math.Max(1, height);
        Matrix4x4 projection = camera.GetProjectionMatrix(aspect);
        Matrix4x4 view = camera.GetViewMatrix();
        if (scene.ShowGroundPlane)
            DrawGrounding(scene, projection, view);

        if (scene.DisplayMode == MeshPreviewDisplayMode.SideBySide)
        {
            int leftWidth = Math.Max(1, width / 2);
            int rightWidth = Math.Max(1, width - leftWidth);
            DrawViewport(scene, camera, new Vortice.Mathematics.Viewport(0, 0, leftWidth, height), scene.FbxMesh, scene.ShowFbxMesh, false, ResolveBaseColor(scene, false, sideBySide: true));
            DrawViewport(scene, camera, new Vortice.Mathematics.Viewport(leftWidth, 0, rightWidth, height), scene.Ue3Mesh, scene.ShowUe3Mesh, true, ResolveBaseColor(scene, true, sideBySide: true));
        }
        else
        {
            Vortice.Mathematics.Viewport viewport = new(0, 0, width, height);
            DrawViewport(scene, camera, viewport, scene.FbxMesh, scene.ShowFbxMesh && scene.DisplayMode != MeshPreviewDisplayMode.Ue3Only, false, ResolveBaseColor(scene, false, sideBySide: false));
            DrawViewport(scene, camera, viewport, scene.Ue3Mesh, scene.ShowUe3Mesh && scene.DisplayMode != MeshPreviewDisplayMode.FbxOnly, true, ResolveBaseColor(scene, true, sideBySide: false));
        }

        _swapChain.Present(1, PresentFlags.None);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        foreach ((_, CachedMeshBuffers buffers) in _meshBuffers)
            buffers.Dispose();

        _meshBuffers.Clear();
        ReleaseRenderTargets();
        _backgroundVertexBuffer?.Dispose();
        _backgroundInputLayout?.Dispose();
        _backgroundConstantBuffer?.Dispose();
        _meshConstantBuffer?.Dispose();
        _lineConstantBuffer?.Dispose();
        _backgroundVertexShader?.Dispose();
        _backgroundPixelShader?.Dispose();
        _meshInputLayout?.Dispose();
        _lineInputLayout?.Dispose();
        _meshVertexShader?.Dispose();
        _meshPixelShader?.Dispose();
        _lineVertexShader?.Dispose();
        _linePixelShader?.Dispose();
        _solidRasterizerState?.Dispose();
        _wireframeRasterizerState?.Dispose();
        _solidRasterizerStateNoCull?.Dispose();
        _wireframeRasterizerStateNoCull?.Dispose();
        _textureSampler?.Dispose();
        foreach ((_, ID3D11ShaderResourceView texture) in _materialTextures)
            texture?.Dispose();
        _materialTextures.Clear();
        foreach ((_, ID3D11ShaderResourceView texture) in _gameMaterialTextures)
            texture?.Dispose();
        _gameMaterialTextures.Clear();
        _swapChain?.Dispose();
        _context?.Dispose();
        _device?.Dispose();
        _swapChain = null;
        _context = null;
        _device = null;
        _renderTargetView = null;
        _depthTexture = null;
        _depthStencilView = null;
        _backgroundVertexShader = null;
        _backgroundPixelShader = null;
        _backgroundInputLayout = null;
        _backgroundVertexBuffer = null;
        _backgroundConstantBuffer = null;
        _meshVertexShader = null;
        _meshPixelShader = null;
        _meshInputLayout = null;
        _lineVertexShader = null;
        _linePixelShader = null;
        _lineInputLayout = null;
        _meshConstantBuffer = null;
        _lineConstantBuffer = null;
        _solidRasterizerState = null;
        _wireframeRasterizerState = null;
        _solidRasterizerStateNoCull = null;
        _wireframeRasterizerStateNoCull = null;
        _textureSampler = null;
    }

    private void EnsureDevice(IntPtr hwnd, int width, int height)
    {
        if (_device != null)
        {
            if (_swapChain == null || _hwnd != hwnd)
                CreateSwapChain(hwnd, width, height);

            return;
        }

        DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
        flags |= DeviceCreationFlags.Debug;
#endif
        FeatureLevel[] levels =
        [
            FeatureLevel.Level_11_1,
            FeatureLevel.Level_11_0,
            FeatureLevel.Level_10_1,
            FeatureLevel.Level_10_0
        ];

        D3D11CreateDevice(
            null,
            DriverType.Hardware,
            flags,
            levels,
            out _device,
            out _context).CheckError();

        CreateDeviceResources();
        CreateSwapChain(hwnd, width, height);
    }

    private void CreateDeviceResources()
    {
        using Blob backgroundVsBlob = CompileShader(BackgroundVertexShaderSource, "VSMain", "vs_5_0");
        using Blob backgroundPsBlob = CompileShader(BackgroundPixelShaderSource, "PSMain", "ps_5_0");
        using Blob meshVsBlob = CompileShader(MeshVertexShaderSource, "VSMain", "vs_5_0");
        using Blob meshPsBlob = CompileShader(MeshPixelShaderSource, "PSMain", "ps_5_0");
        using Blob lineVsBlob = CompileShader(LineVertexShaderSource, "VSMain", "vs_5_0");
        using Blob linePsBlob = CompileShader(LinePixelShaderSource, "PSMain", "ps_5_0");

        _backgroundVertexShader = _device.CreateVertexShader(backgroundVsBlob);
        _backgroundPixelShader = _device.CreatePixelShader(backgroundPsBlob);
        _meshVertexShader = _device.CreateVertexShader(meshVsBlob);
        _meshPixelShader = _device.CreatePixelShader(meshPsBlob);
        _lineVertexShader = _device.CreateVertexShader(lineVsBlob);
        _linePixelShader = _device.CreatePixelShader(linePsBlob);

        _backgroundInputLayout = _device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32_Float, 0, 0)
        ], backgroundVsBlob);

        _backgroundVertexBuffer = _device.CreateBuffer(new[]
        {
            new Vector2(-1.0f, -1.0f),
            new Vector2( 1.0f, -1.0f),
            new Vector2(-1.0f,  1.0f),
            new Vector2( 1.0f,  1.0f)
        }, BindFlags.VertexBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);

        _meshInputLayout = _device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
            new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
            new InputElementDescription("TANGENT", 0, Format.R32G32B32_Float, 24, 0),
            new InputElementDescription("BINORMAL", 0, Format.R32G32B32_Float, 36, 0),
            new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 48, 0),
            new InputElementDescription("BONEINDICES", 0, Format.R32G32B32A32_SInt, 56, 0),
            new InputElementDescription("BONEWEIGHTS", 0, Format.R32G32B32A32_Float, 72, 0),
            new InputElementDescription("SECTIONINDEX", 0, Format.R32_SInt, 88, 0)
        ], meshVsBlob);

        _lineInputLayout = _device.CreateInputLayout(
        [
            new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0)
        ], lineVsBlob);

        _backgroundConstantBuffer = _device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<BackgroundConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _meshConstantBuffer = _device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<MeshConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));
        _lineConstantBuffer = _device.CreateBuffer(new BufferDescription((uint)Marshal.SizeOf<LineConstants>(), BindFlags.ConstantBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0));

        _solidRasterizerState = _device.CreateRasterizerState(new RasterizerDescription(CullMode.Back, FillMode.Solid));
        _wireframeRasterizerState = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Wireframe));
        _solidRasterizerStateNoCull = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Solid));
        _wireframeRasterizerStateNoCull = _device.CreateRasterizerState(new RasterizerDescription(CullMode.None, FillMode.Wireframe));
        _textureSampler = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Wrap,
            AddressV = TextureAddressMode.Wrap,
            AddressW = TextureAddressMode.Wrap,
            MipLODBias = 0.0f,
            MaxAnisotropy = 1,
            ComparisonFunc = ComparisonFunction.Never,
            BorderColor = new Vortice.Mathematics.Color4(0f, 0f, 0f, 0f),
            MinLOD = 0.0f,
            MaxLOD = float.MaxValue
        });
    }

    private void CreateSwapChain(IntPtr hwnd, int width, int height)
    {
        ReleaseRenderTargets();
        _swapChain?.Dispose();

        using IDXGIDevice dxgiDevice = _device.QueryInterface<IDXGIDevice>();
        using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
        using IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>();

        SwapChainDescription1 description = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = Format.R8G8B8A8_UNorm,
            Stereo = false,
            SampleDescription = new SampleDescription(1, 0),
            BufferUsage = Vortice.DXGI.Usage.RenderTargetOutput,
            BufferCount = 2,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Ignore
        };

        _swapChain = factory.CreateSwapChainForHwnd(_device, hwnd, description);
        factory.MakeWindowAssociation(hwnd, WindowAssociationFlags.IgnoreAltEnter);

        _hwnd = hwnd;
        _width = width;
        _height = height;
        CreateRenderTargets(width, height);
    }

    private void CreateRenderTargets(int width, int height)
    {
        using ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTargetView = _device.CreateRenderTargetView(backBuffer);

        Texture2DDescription depthDescription = new()
        {
            Width = (uint)width,
            Height = (uint)height,
            ArraySize = 1,
            MipLevels = 1,
            Format = Format.D24_UNorm_S8_UInt,
            BindFlags = BindFlags.DepthStencil,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default
        };

        _depthTexture = _device.CreateTexture2D(depthDescription);
        _depthStencilView = _device.CreateDepthStencilView(_depthTexture);
    }

    private void ReleaseRenderTargets()
    {
        _depthStencilView?.Dispose();
        _depthStencilView = null;
        _depthTexture?.Dispose();
        _depthTexture = null;
        _renderTargetView?.Dispose();
        _renderTargetView = null;
    }

    private void DrawBackground(MeshPreviewScene scene)
    {
        BackgroundConstants constants = new()
        {
            BackgroundStyle = (int)scene.BackgroundStyle
        };

        _context.RSSetViewport(new Vortice.Mathematics.Viewport(0, 0, _width, _height));
        _context.IASetInputLayout(_backgroundInputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        _context.IASetVertexBuffer(0, _backgroundVertexBuffer, (uint)Marshal.SizeOf<Vector2>());
        _context.VSSetShader(_backgroundVertexShader);
        _context.PSSetShader(_backgroundPixelShader);
        _context.VSSetConstantBuffer(0, _backgroundConstantBuffer);
        _context.PSSetConstantBuffer(0, _backgroundConstantBuffer);
        _context.UpdateSubresource(in constants, _backgroundConstantBuffer);
        _context.Draw(4, 0);
    }

    private void DrawCheckerOverlay()
    {
        const int verticalDivisions = 14;
        const int horizontalDivisions = 9;
        List<Vector3> lines = [];
        const float backgroundDepth = 1.0f;

        for (int i = 0; i <= verticalDivisions; i++)
        {
            float x = -1.0f + (2.0f * i / verticalDivisions);
            lines.Add(new Vector3(x, -1.0f, backgroundDepth));
            lines.Add(new Vector3(x, 0.15f, backgroundDepth));
        }

        for (int i = 0; i <= horizontalDivisions; i++)
        {
            float y = -1.0f + (1.15f * i / horizontalDivisions);
            lines.Add(new Vector3(-1.0f, y, backgroundDepth));
            lines.Add(new Vector3(1.0f, y, backgroundDepth));
        }

        DrawDynamicLines(lines, Matrix4x4.Identity, Matrix4x4.Identity, new Vector4(0.24f, 0.25f, 0.27f, 1.0f));
    }

    private void DrawGrounding(MeshPreviewScene scene, Matrix4x4 projection, Matrix4x4 view)
    {
        if (!TryGetGroundPlane(scene, out Vector3 center, out float radius, out float z))
            return;

        float halfExtent = MathF.Max(1.5f, radius * 1.8f);
        float gridStep = MathF.Max(radius / 4.0f, 0.5f);
        List<Vector3> gridLines = [];
        for (float x = center.X - halfExtent; x <= center.X + halfExtent + 0.001f; x += gridStep)
        {
            gridLines.Add(new Vector3(x, center.Y - halfExtent, z));
            gridLines.Add(new Vector3(x, center.Y + halfExtent, z));
        }

        for (float y = center.Y - halfExtent; y <= center.Y + halfExtent + 0.001f; y += gridStep)
        {
            gridLines.Add(new Vector3(center.X - halfExtent, y, z));
            gridLines.Add(new Vector3(center.X + halfExtent, y, z));
        }

        DrawDynamicLines(gridLines, projection, view, new Vector4(0.22f, 0.23f, 0.25f, 1f));

        List<Vector3> shadowLines = [];
        float shadowRadius = MathF.Max(radius * 0.65f, 0.75f);
        AddCircleLines(shadowLines, center with { Z = z + 0.01f }, shadowRadius, 32);
        AddCircleLines(shadowLines, center with { Z = z + 0.01f }, shadowRadius * 0.72f, 32);
        AddCircleLines(shadowLines, center with { Z = z + 0.01f }, shadowRadius * 0.46f, 32);
        DrawDynamicLines(shadowLines, projection, view, new Vector4(0.08f, 0.09f, 0.10f, 1f));
    }

    private void DrawViewport(MeshPreviewScene scene, MeshPreviewCamera camera, Vortice.Mathematics.Viewport viewport, MeshPreviewMesh mesh, bool visible, bool ue3Mesh, Vector3 baseColor)
    {
        _context.RSSetViewport(viewport);
        if (!visible || mesh == null || mesh.Vertices.Count == 0)
            return;

        CachedMeshBuffers buffers = GetMeshBuffers(mesh);
        ID3D11RasterizerState rasterizerState = ResolveRasterizerState(scene, ue3Mesh);
        Matrix4x4 projection = camera.GetProjectionMatrix(Math.Max(1.0f, viewport.Width) / Math.Max(1.0f, viewport.Height));
        Matrix4x4 view = camera.GetViewMatrix();

        MeshConstants constants = new()
        {
            Projection = projection,
            View = view,
            Model = Matrix4x4.Identity,
            CameraPosition = camera.GetPosition(),
            LightDirection = Vector3.Normalize(new Vector3(-0.45f, -0.7f, -0.4f)),
            AmbientLight = scene.AmbientLight,
            BaseColor = baseColor,
            LightingPreset = (int)scene.LightingPreset,
            MaterialChannel = (int)scene.MaterialChannel,
            WeightMode = scene.ShowWeights ? 1 : 0,
            WeightViewMode = (int)scene.WeightViewMode,
            ShadingMode = (int)scene.ShadingMode,
            ShowSections = scene.ShowSections ? 1 : 0,
            HighlightSection = 0,
            SectionColor = Vector4.One,
            SelectedBone = ResolveBoneIndex(mesh, scene.SelectedBoneName)
        };

        _context.IASetInputLayout(_meshInputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetVertexBuffer(0, buffers.VertexBuffer, (uint)Marshal.SizeOf<D3DMeshVertex>());
        _context.IASetIndexBuffer(buffers.IndexBuffer, Format.R32_UInt, 0);
        _context.VSSetShader(_meshVertexShader);
        _context.PSSetShader(_meshPixelShader);
        _context.VSSetConstantBuffer(0, _meshConstantBuffer);
        _context.PSSetConstantBuffer(0, _meshConstantBuffer);
        _context.PSSetSampler(0, _textureSampler);
        _context.RSSetState(rasterizerState);

        foreach (MeshPreviewSection section in mesh.Sections)
        {
            if (!scene.IsSectionVisible(ue3Mesh, section.Index))
                continue;

            ApplySectionMaterialState(scene, section, ue3Mesh, ref constants);
            constants.SectionColor = section.Color;
            constants.HighlightSection = scene.IsSectionHighlighted(ue3Mesh, section.Index) ? 1 : 0;
            _context.UpdateSubresource(in constants, _meshConstantBuffer);
            _context.DrawIndexed((uint)section.IndexCount, (uint)section.BaseIndex, 0);
        }

        ResetSectionMaterialState(ref constants);

        if (scene.ShowNormals)
            DrawLines(buffers.NormalLines, buffers.NormalLineCount, projection, view, Matrix4x4.Identity, new Vector4(1f, 0.1f, 0.9f, 1f));
        if (scene.ShowTangents)
            DrawLines(buffers.TangentLines, buffers.TangentLineCount, projection, view, Matrix4x4.Identity, new Vector4(0.1f, 1f, 1f, 1f));
        if (scene.ShowUvSeams)
            DrawLines(buffers.UvSeamLines, buffers.UvSeamLineCount, projection, view, Matrix4x4.Identity, new Vector4(1f, 0.8f, 0.2f, 1f));
        if (scene.ShowBones)
            DrawBoneOverlay(mesh, projection, view);
    }

    private void DrawBoneOverlay(MeshPreviewMesh mesh, Matrix4x4 projection, Matrix4x4 view)
    {
        List<Vector3> boneLines = [];
        List<Vector3> jointLines = [];
        float jointSize = MathF.Max(0.03f, mesh.Radius * 0.015f);

        foreach (MeshPreviewBone bone in mesh.Bones)
        {
            Vector3 center = bone.GlobalTransform.Translation;
            if (bone.ParentIndex >= 0 && bone.ParentIndex < mesh.Bones.Count)
            {
                boneLines.Add(mesh.Bones[bone.ParentIndex].GlobalTransform.Translation);
                boneLines.Add(center);
            }

            jointLines.Add(center + new Vector3(-jointSize, 0f, 0f));
            jointLines.Add(center + new Vector3(jointSize, 0f, 0f));
            jointLines.Add(center + new Vector3(0f, -jointSize, 0f));
            jointLines.Add(center + new Vector3(0f, jointSize, 0f));
            jointLines.Add(center + new Vector3(0f, 0f, -jointSize));
            jointLines.Add(center + new Vector3(0f, 0f, jointSize));
        }

        if (boneLines.Count > 0)
            DrawDynamicLines(boneLines, projection, view, new Vector4(0.8f, 0.8f, 0.8f, 1f));

        if (jointLines.Count > 0)
            DrawDynamicLines(jointLines, projection, view, new Vector4(1f, 0.9f, 0.2f, 1f));
    }

    private void DrawDynamicLines(List<Vector3> lines, Matrix4x4 projection, Matrix4x4 view, Vector4 color)
    {
        using ID3D11Buffer buffer = _device.CreateBuffer(lines.ToArray(), BindFlags.VertexBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
        DrawLines(buffer, lines.Count, projection, view, Matrix4x4.Identity, color);
    }

    private void DrawLines(ID3D11Buffer buffer, int vertexCount, Matrix4x4 projection, Matrix4x4 view, Matrix4x4 model, Vector4 color)
    {
        if (buffer == null || vertexCount <= 0)
            return;

        LineConstants constants = new()
        {
            Projection = projection,
            View = view,
            Model = model,
            Color = color
        };

        _context.UpdateSubresource(in constants, _lineConstantBuffer);
        _context.IASetInputLayout(_lineInputLayout);
        _context.IASetPrimitiveTopology(PrimitiveTopology.LineList);
        _context.IASetVertexBuffer(0, buffer, (uint)Marshal.SizeOf<Vector3>());
        _context.VSSetShader(_lineVertexShader);
        _context.PSSetShader(_linePixelShader);
        _context.VSSetConstantBuffer(0, _lineConstantBuffer);
        _context.PSSetConstantBuffer(0, _lineConstantBuffer);
        _context.RSSetState(_solidRasterizerState);
        _context.Draw((uint)vertexCount, 0);
    }

    private static Blob CompileShader(string source, string entryPoint, string profile)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"mhupkmanager-meshpreview-{Guid.NewGuid():N}.hlsl");
        try
        {
            File.WriteAllText(tempPath, source);
            Compiler.CompileFromFile(
                tempPath,
                null,
                null,
                entryPoint,
                profile,
                ShaderFlags.OptimizationLevel3,
                EffectFlags.None,
                out Blob code,
                out Blob error);

            using (error)
            {
                if (code == null)
                    throw new InvalidOperationException(error?.AsString() ?? $"Failed to compile shader '{entryPoint}' ({profile}).");
            }

            return code;
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private void ApplyMaterialState(MeshPreviewScene scene, ref MeshConstants constants)
    {
        ApplyMaterialState(scene, scene.MaterialSet, ref constants);
    }

    private void ApplyMaterialState(MeshPreviewScene scene, TexturePreviewMaterialSet materialSet, ref MeshConstants constants)
    {
        if (materialSet.Revision != _materialRevision)
        {
            RefreshMaterialTextures(materialSet);
            _materialRevision = materialSet.Revision;
        }

        bool enabled = scene.MaterialPreviewEnabled && materialSet.Enabled;
        TexturePreviewMaterialMode mode = materialSet.ResolveMode();

        constants.UseDiffuseMap = enabled && materialSet.HasTexture(TexturePreviewMaterialSlot.Diffuse) ? 1 : 0;
        constants.UseNormalMap = enabled && mode >= TexturePreviewMaterialMode.DiffuseAndNormal && materialSet.HasTexture(TexturePreviewMaterialSlot.Normal) ? 1 : 0;
        constants.UseSpecularMap = enabled && mode == TexturePreviewMaterialMode.FullMaterial && materialSet.HasTexture(TexturePreviewMaterialSlot.Specular) ? 1 : 0;
        constants.UseEmissiveMap = enabled && mode == TexturePreviewMaterialMode.FullMaterial && materialSet.HasTexture(TexturePreviewMaterialSlot.Emissive) ? 1 : 0;
        constants.UseMaskMap = enabled && mode == TexturePreviewMaterialMode.FullMaterial && materialSet.HasTexture(TexturePreviewMaterialSlot.Mask) ? 1 : 0;

        BindMaterialTexture(0, TexturePreviewMaterialSlot.Diffuse, constants.UseDiffuseMap == 1);
        BindMaterialTexture(1, TexturePreviewMaterialSlot.Normal, constants.UseNormalMap == 1);
        BindMaterialTexture(2, TexturePreviewMaterialSlot.Specular, constants.UseSpecularMap == 1);
        BindMaterialTexture(3, TexturePreviewMaterialSlot.Emissive, constants.UseEmissiveMap == 1);
        BindMaterialTexture(4, TexturePreviewMaterialSlot.Mask, constants.UseMaskMap == 1);
    }

    private void ApplySectionMaterialState(MeshPreviewScene scene, MeshPreviewSection section, bool ue3Mesh, ref MeshConstants constants)
    {
        ID3D11RasterizerState rasterizerState = ResolveRasterizerState(scene, ue3Mesh);
        bool gameApprox = ue3Mesh && scene.ShadingMode == MeshPreviewShadingMode.GameApprox;
        bool useGameMaterial = gameApprox && section.GameMaterial?.Enabled == true;

        if (!useGameMaterial)
        {
            TexturePreviewMaterialSet sectionMaterialSet = null;
            bool hasSectionOverride = scene.MaterialPreviewEnabled &&
                (ue3Mesh
                    ? scene.TryGetUe3SectionMaterialSet(section.Index, out sectionMaterialSet)
                    : scene.TryGetFbxSectionMaterialSet(section.Index, out sectionMaterialSet));

            if (hasSectionOverride)
            {
                ApplyMaterialState(scene, sectionMaterialSet, ref constants);
                constants.UseGameMaterial = 0;
                constants.AlphaTest = 0.0f;
                _context.RSSetState(rasterizerState);
                return;
            }

            if (gameApprox)
            {
                ApplyDisabledMaterialState(ref constants);
                return;
            }

            ApplyMaterialState(scene, ref constants);
            constants.UseGameMaterial = 0;
            _context.RSSetState(rasterizerState);
            return;
        }

        MeshPreviewGameMaterial material = section.GameMaterial;
        constants.UseGameMaterial = 0;
        constants.ShadingMode = (int)MeshPreviewShadingMode.Lit;
        constants.MaterialChannel = (int)MeshPreviewMaterialChannel.BaseColor;
        constants.UseDiffuseMap = material.HasTexture(MeshPreviewGameTextureSlot.Diffuse) ? 1 : 0;
        constants.UseNormalMap = 0;
        constants.UseSpecularMap = 0;
        constants.UseEmissiveMap = 0;
        constants.UseMaskMap = 0;
        constants.HasDiffuseMap = 0.0f;
        constants.HasNormalMap = 0.0f;
        constants.HasSmspsk = 0.0f;
        constants.HasEspa = 0.0f;
        constants.HasSmrr = 0.0f;
        constants.HasSpecColorMap = 0.0f;
        constants.AlphaTest = material.BlendMode == UpkManager.Models.UpkFile.Engine.Material.EBlendMode.BLEND_Masked ? 1.0f : 0.0f;
        constants.LambertDiffusePower = material.LambertDiffusePower;
        constants.PhongDiffusePower = material.PhongDiffusePower;
        constants.LightingAmbient = material.LightingAmbient;
        constants.ShadowAmbientMult = material.ShadowAmbientMult;
        constants.NormalStrength = material.NormalStrength;
        constants.ReflectionMult = material.ReflectionMult;
        constants.RimColorMult = material.RimColorMult;
        constants.RimFalloff = material.RimFalloff;
        constants.ScreenLightAmount = material.ScreenLightAmount;
        constants.ScreenLightMult = material.ScreenLightMult;
        constants.ScreenLightPower = material.ScreenLightPower;
        constants.SpecMult = material.SpecMult;
        constants.SpecMultLq = material.SpecMultLq;
        constants.SpecularPower = material.SpecularPower;
        constants.SpecularPowerMask = material.SpecularPowerMask;
        constants.LambertAmbient = material.LambertAmbient;
        constants.SkinScatterStrength = material.SkinScatterStrength;
        constants.ShadowAmbientColor = material.ShadowAmbientColor;
        constants.TwoSidedLighting = material.TwoSidedLighting;
        constants.FillLightColor = material.FillLightColor;
        constants.DiffuseColor = material.DiffuseColor;
        constants.SpecularColor = material.SpecularColor;
        constants.SubsurfaceInscatteringColor = material.SubsurfaceInscatteringColor;
        constants.SubsurfaceAbsorptionColor = material.SubsurfaceAbsorptionColor;
        constants.ImageReflectionNormalDampening = material.ImageReflectionNormalDampening;

        BindGameMaterialTexture(0, material, MeshPreviewGameTextureSlot.Diffuse, ref constants.HasDiffuseMap);
        _context.PSSetShaderResource(1, null);
        constants.HasNormalMap = 0.0f;
        BindGameMaterialTexture(2, material, MeshPreviewGameTextureSlot.Smspsk, ref constants.HasSmspsk);
        BindGameMaterialTexture(3, material, MeshPreviewGameTextureSlot.Espa, ref constants.HasEspa);
        BindGameMaterialTexture(4, material, MeshPreviewGameTextureSlot.Smrr, ref constants.HasSmrr);
        BindGameMaterialTexture(5, material, MeshPreviewGameTextureSlot.SpecColor, ref constants.HasSpecColorMap);

        _context.PSSetShaderResource(2, null);
        _context.PSSetShaderResource(3, null);
        _context.PSSetShaderResource(4, null);
        _context.PSSetShaderResource(5, null);
        constants.HasSmspsk = 0.0f;
        constants.HasEspa = 0.0f;
        constants.HasSmrr = 0.0f;
        constants.HasSpecColorMap = 0.0f;

        _context.RSSetState(rasterizerState);
    }

    private void ResetSectionMaterialState(ref MeshConstants constants)
    {
        ApplyDisabledMaterialState(ref constants);
        _context.RSSetState(_solidRasterizerState);
    }

    private void ApplyDisabledMaterialState(ref MeshConstants constants)
    {
        constants.UseGameMaterial = 0;
        constants.UseDiffuseMap = 0;
        constants.UseNormalMap = 0;
        constants.UseSpecularMap = 0;
        constants.UseEmissiveMap = 0;
        constants.UseMaskMap = 0;
        constants.HasDiffuseMap = 0.0f;
        constants.HasNormalMap = 0.0f;
        constants.HasSmspsk = 0.0f;
        constants.HasEspa = 0.0f;
        constants.HasSmrr = 0.0f;
        constants.HasSpecColorMap = 0.0f;
        constants.AlphaTest = 0.0f;

        for (uint slot = 0; slot <= 5; slot++)
            _context.PSSetShaderResource(slot, null);
    }

    private ID3D11RasterizerState ResolveRasterizerState(MeshPreviewScene scene, bool ue3Mesh)
    {
        bool disableBackfaceCulling = ue3Mesh ? scene.DisableBackfaceCullingForUe3 : scene.DisableBackfaceCullingForFbx;
        if (scene.Wireframe)
            return disableBackfaceCulling ? _wireframeRasterizerStateNoCull : _wireframeRasterizerState;

        return disableBackfaceCulling ? _solidRasterizerStateNoCull : _solidRasterizerState;
    }

    private void BindGameMaterialTexture(int slot, MeshPreviewGameMaterial material, MeshPreviewGameTextureSlot gameSlot, ref float hasTexture)
    {
        if (!material.HasTexture(gameSlot))
        {
            hasTexture = 0.0f;
            _context.PSSetShaderResource((uint)slot, null);
            return;
        }

        ID3D11ShaderResourceView resource = GetOrCreateGameMaterialTextureView(material.GetTexture(gameSlot));
        _context.PSSetShaderResource((uint)slot, resource);
        hasTexture = resource != null ? 1.0f : 0.0f;
    }

    private ID3D11ShaderResourceView GetOrCreateGameMaterialTextureView(TexturePreviewTexture texture)
    {
        string key = $"{texture.SourcePath}|{texture.ExportPath}|{texture.SelectedMipIndex}|{texture.Width}x{texture.Height}";
        if (_gameMaterialTextures.TryGetValue(key, out ID3D11ShaderResourceView resource))
            return resource;

        resource = CreateTextureView(texture);
        _gameMaterialTextures[key] = resource;
        return resource;
    }

    private void BindMaterialTexture(int slot, TexturePreviewMaterialSlot materialSlot, bool useSlotTexture)
    {
        ID3D11ShaderResourceView resource = useSlotTexture && _materialTextures.TryGetValue(materialSlot, out ID3D11ShaderResourceView materialTexture)
            ? materialTexture
            : null;
        _context.PSSetShaderResource((uint)slot, resource);
    }

    private void RefreshMaterialTextures(TexturePreviewMaterialSet materialSet)
    {
        foreach ((_, ID3D11ShaderResourceView texture) in _materialTextures)
            texture?.Dispose();

        _materialTextures.Clear();
        foreach ((TexturePreviewMaterialSlot slot, TexturePreviewTexture texture) in materialSet.Textures)
            _materialTextures[slot] = CreateTextureView(texture);
    }

    private ID3D11ShaderResourceView CreateTextureView(TexturePreviewTexture texture)
    {
        Texture2DDescription description = new()
        {
            Width = (uint)texture.Width,
            Height = (uint)texture.Height,
            ArraySize = 1,
            MipLevels = 1,
            Format = Format.R8G8B8A8_UNorm,
            BindFlags = BindFlags.ShaderResource,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable
        };

        return CreateTextureView(description, texture.RgbaPixels, texture.Width * 4);
    }

    private ID3D11ShaderResourceView CreateTextureView(Texture2DDescription description, byte[] pixelData, int rowPitch)
    {
        GCHandle handle = GCHandle.Alloc(pixelData, GCHandleType.Pinned);
        try
        {
            SubresourceData subresource = new(handle.AddrOfPinnedObject(), (uint)rowPitch, 0);
            using ID3D11Texture2D texture = _device.CreateTexture2D(description, subresource);
            return _device.CreateShaderResourceView(texture);
        }
        finally
        {
            handle.Free();
        }
    }

    private CachedMeshBuffers GetMeshBuffers(MeshPreviewMesh mesh)
    {
        if (_meshBuffers.TryGetValue(mesh, out CachedMeshBuffers buffers))
            return buffers;

        buffers = new CachedMeshBuffers(_device, mesh);
        _meshBuffers[mesh] = buffers;
        return buffers;
    }

    private static int ResolveBoneIndex(MeshPreviewMesh mesh, string boneName)
    {
        if (string.IsNullOrWhiteSpace(boneName))
            return -1;

        for (int i = 0; i < mesh.Bones.Count; i++)
        {
            if (string.Equals(mesh.Bones[i].Name, boneName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static Vector3 ResolveBaseColor(MeshPreviewScene scene, bool ue3Mesh, bool sideBySide)
    {
        if (sideBySide)
            return ue3Mesh ? new Vector3(0.62f, 0.68f, 0.74f) : new Vector3(0.74f, 0.70f, 0.66f);

        if (scene.ShadingMode == MeshPreviewShadingMode.GameApprox)
            return ue3Mesh ? new Vector3(0.68f, 0.69f, 0.71f) : new Vector3(0.70f, 0.70f, 0.70f);

        return scene.ShadingMode == MeshPreviewShadingMode.Studio
            ? (ue3Mesh ? new Vector3(0.73f, 0.75f, 0.78f) : new Vector3(0.76f, 0.75f, 0.73f))
            : (ue3Mesh ? new Vector3(0.72f, 0.73f, 0.75f) : new Vector3(0.74f, 0.74f, 0.74f));
    }

    private static bool TryGetGroundPlane(MeshPreviewScene scene, out Vector3 center, out float radius, out float z)
    {
        List<MeshPreviewMesh> meshes = [];
        if (scene.ShowFbxMesh && scene.FbxMesh != null)
            meshes.Add(scene.FbxMesh);
        if (scene.ShowUe3Mesh && scene.Ue3Mesh != null)
            meshes.Add(scene.Ue3Mesh);

        if (meshes.Count == 0)
        {
            center = Vector3.Zero;
            radius = 1.0f;
            z = -0.5f;
            return false;
        }

        center = Vector3.Zero;
        radius = 1.0f;
        z = float.MaxValue;
        foreach (MeshPreviewMesh mesh in meshes)
        {
            center += mesh.Center;
            radius = MathF.Max(radius, mesh.Radius);
            foreach (MeshPreviewVertex vertex in mesh.Vertices)
                z = MathF.Min(z, vertex.Position.Z);
        }

        center /= meshes.Count;
        z -= MathF.Max(0.02f, radius * 0.04f);
        center = new Vector3(center.X, center.Y, z);
        return true;
    }

    private static void AddCircleLines(List<Vector3> lines, Vector3 center, float radius, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            float a0 = (MathF.PI * 2.0f * i) / segments;
            float a1 = (MathF.PI * 2.0f * (i + 1)) / segments;
            lines.Add(center + new Vector3(MathF.Cos(a0) * radius, MathF.Sin(a0) * radius, 0.0f));
            lines.Add(center + new Vector3(MathF.Cos(a1) * radius, MathF.Sin(a1) * radius, 0.0f));
        }
    }

    private static Vortice.Mathematics.Color4 ResolveClearColor(MeshPreviewBackgroundStyle style)
    {
        return style switch
        {
            MeshPreviewBackgroundStyle.StudioGray => new Vortice.Mathematics.Color4(0.27f, 0.28f, 0.29f, 1.0f),
            MeshPreviewBackgroundStyle.FlatBlack => new Vortice.Mathematics.Color4(0.02f, 0.02f, 0.025f, 1.0f),
            MeshPreviewBackgroundStyle.Checker => new Vortice.Mathematics.Color4(0.16f, 0.17f, 0.18f, 1.0f),
            _ => new Vortice.Mathematics.Color4(0.11f, 0.12f, 0.14f, 1.0f)
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct D3DMeshVertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector3 Tangent;
        public Vector3 Bitangent;
        public Vector2 Uv;
        public Int4 BoneIndices;
        public Vector4 BoneWeights;
        public int SectionIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Int4
    {
        public int X;
        public int Y;
        public int Z;
        public int W;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MeshConstants
    {
        public Matrix4x4 Projection;
        public Matrix4x4 View;
        public Matrix4x4 Model;
        public Vector3 CameraPosition;
        private readonly float _paddingAfterCameraPosition;
        public Vector3 LightDirection;
        public float AmbientLight;
        public Vector3 BaseColor;
        public int LightingPreset;
        public int MaterialChannel;
        public int WeightMode;
        public int WeightViewMode;
        public int ShadingMode;
        public int SelectedBone;
        public int ShowSections;
        public int HighlightSection;
        public int UseGameMaterial;
        public Vector4 SectionColor;
        public int UseDiffuseMap;
        public int UseNormalMap;
        public int UseSpecularMap;
        public int UseEmissiveMap;
        public int UseMaskMap;
        public float HasDiffuseMap;
        public float HasNormalMap;
        public float HasSmspsk;
        public float HasEspa;
        public float HasSmrr;
        public float HasSpecColorMap;
        public float AlphaTest;
        public float LambertDiffusePower;
        public float PhongDiffusePower;
        public float LightingAmbient;
        public float ShadowAmbientMult;
        public float NormalStrength;
        public float ReflectionMult;
        public float RimColorMult;
        public float RimFalloff;
        public float ScreenLightAmount;
        public float ScreenLightMult;
        public float ScreenLightPower;
        public float SpecMult;
        public float SpecMultLq;
        public float SpecularPower;
        public float SpecularPowerMask;
        public float ImageReflectionNormalDampening;
        public Vector3 LambertAmbient;
        public float SkinScatterStrength;
        public Vector3 ShadowAmbientColor;
        public float TwoSidedLighting;
        public Vector3 FillLightColor;
        private readonly float _paddingMaterial0;
        public Vector3 DiffuseColor;
        private readonly float _paddingMaterial1;
        public Vector3 SpecularColor;
        private readonly float _paddingMaterial2;
        public Vector3 SubsurfaceInscatteringColor;
        private readonly float _paddingMaterial3;
        public Vector3 SubsurfaceAbsorptionColor;
        private readonly float _paddingMaterial4;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BackgroundConstants
    {
        public int BackgroundStyle;
        private readonly int _padding0;
        private readonly int _padding1;
        private readonly int _padding2;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LineConstants
    {
        public Matrix4x4 Projection;
        public Matrix4x4 View;
        public Matrix4x4 Model;
        public Vector4 Color;
    }

    private sealed class CachedMeshBuffers : IDisposable
    {
        public CachedMeshBuffers(ID3D11Device device, MeshPreviewMesh mesh)
        {
            D3DMeshVertex[] vertices = new D3DMeshVertex[mesh.Vertices.Count];
            for (int i = 0; i < mesh.Vertices.Count; i++)
            {
                MeshPreviewVertex source = mesh.Vertices[i];
                vertices[i] = new D3DMeshVertex
                {
                    Position = source.Position,
                    Normal = source.Normal,
                    Tangent = source.Tangent,
                    Bitangent = source.Bitangent,
                    Uv = source.Uv,
                    BoneIndices = new Int4 { X = source.Bone0, Y = source.Bone1, Z = source.Bone2, W = source.Bone3 },
                    BoneWeights = new Vector4(source.Weight0, source.Weight1, source.Weight2, source.Weight3),
                    SectionIndex = source.SectionIndex
                };
            }

            VertexBuffer = device.CreateBuffer(vertices, BindFlags.VertexBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
            IndexBuffer = device.CreateBuffer(mesh.Indices.ToArray(), BindFlags.IndexBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0);
            NormalLines = CreateLineBuffer(device, BuildDirectionLines(mesh.Vertices, static v => v.Normal, mesh.Radius * 0.05f), out int normalCount);
            NormalLineCount = normalCount;
            TangentLines = CreateLineBuffer(device, BuildDirectionLines(mesh.Vertices, static v => v.Tangent, mesh.Radius * 0.05f), out int tangentCount);
            TangentLineCount = tangentCount;
            UvSeamLines = CreateLineBuffer(device, mesh.UvSeamLines, out int uvCount);
            UvSeamLineCount = uvCount;
        }

        public ID3D11Buffer VertexBuffer { get; }
        public ID3D11Buffer IndexBuffer { get; }
        public ID3D11Buffer NormalLines { get; }
        public int NormalLineCount { get; }
        public ID3D11Buffer TangentLines { get; }
        public int TangentLineCount { get; }
        public ID3D11Buffer UvSeamLines { get; }
        public int UvSeamLineCount { get; }

        public void Dispose()
        {
            VertexBuffer?.Dispose();
            IndexBuffer?.Dispose();
            NormalLines?.Dispose();
            TangentLines?.Dispose();
            UvSeamLines?.Dispose();
        }

        private static List<Vector3> BuildDirectionLines(IReadOnlyList<MeshPreviewVertex> vertices, Func<MeshPreviewVertex, Vector3> selector, float scale)
        {
            List<Vector3> lines = new(vertices.Count * 2);
            foreach (MeshPreviewVertex vertex in vertices)
            {
                Vector3 direction = selector(vertex);
                if (direction.LengthSquared() <= 1e-6f)
                    continue;

                lines.Add(vertex.Position);
                lines.Add(vertex.Position + (Vector3.Normalize(direction) * scale));
            }

            return lines;
        }

        private static ID3D11Buffer CreateLineBuffer(ID3D11Device device, IReadOnlyList<Vector3> lines, out int count)
        {
            count = lines.Count;
            return count > 0 ? device.CreateBuffer(lines.ToArray(), BindFlags.VertexBuffer, ResourceUsage.Default, CpuAccessFlags.None, ResourceOptionFlags.None, 0, 0) : null;
        }
    }

    private const string MeshVertexShaderSource = """
        cbuffer MeshConstants : register(b0)
        {
            row_major float4x4 uProjection;
            row_major float4x4 uView;
            row_major float4x4 uModel;
            float3 uCameraPos;
            float _paddingAfterCameraPos;
            float3 uLightDirection;
            float uAmbientLight;
            float3 uBaseColor;
            int uLightingPreset;
            int uMaterialChannel;
            int uWeightMode;
            int uWeightViewMode;
            int uShadingMode;
            int uSelectedBone;
            int uShowSections;
            int uHighlightSection;
            int uUseGameMaterial;
            float4 uSectionColor;
        };

        struct VSInput
        {
            float3 Position : POSITION;
            float3 Normal : NORMAL;
            float3 Tangent : TANGENT;
            float3 Bitangent : BINORMAL;
            float2 Uv : TEXCOORD0;
            int4 BoneIndices : BONEINDICES;
            float4 BoneWeights : BONEWEIGHTS;
            int SectionIndex : SECTIONINDEX;
        };

        struct PSInput
        {
            float4 Position : SV_POSITION;
            float3 Normal : NORMAL0;
            float3 WorldPosition : TEXCOORD0;
            float3 Tangent : TEXCOORD1;
            float3 Bitangent : TEXCOORD2;
            float2 Uv : TEXCOORD3;
            nointerpolation int4 BoneIndices : BONEINDICES;
            float4 BoneWeights : BONEWEIGHTS;
        };

        PSInput VSMain(VSInput input)
        {
            PSInput output;
            float4 worldPosition = mul(float4(input.Position, 1.0), uModel);
            output.Position = mul(mul(worldPosition, uView), uProjection);
            output.Normal = mul(float4(input.Normal, 0.0), uModel).xyz;
            output.WorldPosition = worldPosition.xyz;
            output.Tangent = mul(float4(input.Tangent, 0.0), uModel).xyz;
            output.Bitangent = mul(float4(input.Bitangent, 0.0), uModel).xyz;
            output.Uv = input.Uv;
            output.BoneIndices = input.BoneIndices;
            output.BoneWeights = input.BoneWeights;
            return output;
        }
        """;

    private const string BackgroundVertexShaderSource = """
        cbuffer BackgroundConstants : register(b0)
        {
            int uBackgroundStyle;
            int _padding0;
            int _padding1;
            int _padding2;
        };

        struct VSOutput
        {
            float4 Position : SV_POSITION;
            float2 Uv : TEXCOORD0;
        };

        struct VSInput
        {
            float2 Position : POSITION;
        };

        VSOutput VSMain(VSInput input)
        {
            VSOutput output;
            float2 position = input.Position;
            output.Position = float4(position, 0.0, 1.0);
            output.Uv = position * 0.5 + 0.5;
            return output;
        }
        """;

    private const string BackgroundPixelShaderSource = """
        cbuffer BackgroundConstants : register(b0)
        {
            int uBackgroundStyle;
            int _padding0;
            int _padding1;
            int _padding2;
        };

        struct PSInput
        {
            float4 Position : SV_POSITION;
            float2 Uv : TEXCOORD0;
        };

        float3 DarkGradient(float2 uv)
        {
            float3 top = float3(0.07, 0.08, 0.10);
            float3 bottom = float3(0.14, 0.15, 0.18);
            float3 baseColor = lerp(bottom, top, smoothstep(0.0, 1.0, uv.y));
            float floorFade = smoothstep(0.0, 0.40, uv.y);
            baseColor += float3(0.03, 0.03, 0.035) * (1.0 - floorFade);
            return baseColor;
        }

        float3 StudioGray(float2 uv)
        {
            float3 top = float3(0.22, 0.23, 0.24);
            float3 bottom = float3(0.30, 0.30, 0.31);
            float3 baseColor = lerp(bottom, top, smoothstep(0.0, 1.0, uv.y));
            float floorFade = smoothstep(0.0, 0.36, uv.y);
            return baseColor + float3(0.025, 0.025, 0.025) * (1.0 - floorFade);
        }

        float3 Checker(float2 uv)
        {
            float3 top = float3(0.12, 0.13, 0.15);
            float3 bottom = float3(0.18, 0.18, 0.20);
            float3 baseColor = lerp(bottom, top, smoothstep(0.0, 1.0, uv.y));
            float2 gridUv = float2(uv.x * 14.0, uv.y * 9.0);
            float check = fmod(floor(gridUv.x) + floor(gridUv.y), 2.0);
            float checkerMask = 1.0 - smoothstep(0.42, 0.85, uv.y);
            baseColor = lerp(baseColor, baseColor + float3((check * 2.0 - 1.0) * 0.025, (check * 2.0 - 1.0) * 0.025, (check * 2.0 - 1.0) * 0.025), checkerMask * 0.65);
            return baseColor;
        }

        float4 PSMain(PSInput input) : SV_TARGET
        {
            float3 color;
            if (uBackgroundStyle == 1)
                color = StudioGray(input.Uv);
            else if (uBackgroundStyle == 2)
                color = float3(0.02, 0.02, 0.025);
            else if (uBackgroundStyle == 3)
                color = Checker(input.Uv);
            else
                color = DarkGradient(input.Uv);

            float vignette = smoothstep(1.15, 0.25, distance(input.Uv, float2(0.5, 0.52)));
            color *= lerp(0.86, 1.0, vignette);
            return float4(color, 1.0);
        }
        """;

    private const string MeshPixelShaderSource = """
        cbuffer MeshConstants : register(b0)
        {
            row_major float4x4 uProjection;
            row_major float4x4 uView;
            row_major float4x4 uModel;
            float3 uCameraPos;
            float _paddingAfterCameraPos;
            float3 uLightDirection;
            float uAmbientLight;
            float3 uBaseColor;
            int uLightingPreset;
            int uMaterialChannel;
            int uWeightMode;
            int uWeightViewMode;
            int uShadingMode;
            int uSelectedBone;
            int uShowSections;
            int uHighlightSection;
            int uUseGameMaterial;
            float4 uSectionColor;
            int uUseDiffuseMap;
            int uUseNormalMap;
            int uUseSpecularMap;
            int uUseEmissiveMap;
            int uUseMaskMap;
            float uHasDiffuseMap;
            float uHasNormalMap;
            float uHasSMSPSK;
            float uHasESPA;
            float uHasSMRR;
            float uHasSpecColorMap;
            float uAlphaTest;
            float uLambertDiffusePower;
            float uPhongDiffusePower;
            float uLightingAmbient;
            float uShadowAmbientMult;
            float uNormalStrength;
            float uReflectionMult;
            float uRimColorMult;
            float uRimFalloff;
            float uScreenLightAmount;
            float uScreenLightMult;
            float uScreenLightPower;
            float uSpecMult;
            float uSpecMultLQ;
            float uSpecularPower;
            float uSpecularPowerMask;
            float uImageReflectionNormalDampening;
            float3 uLambertAmbient;
            float uSkinScatterStrength;
            float3 uShadowAmbientColor;
            float uTwoSidedLighting;
            float3 uFillLightColor;
            float _paddingMaterial0;
            float3 uDiffuseColor;
            float _paddingMaterial1;
            float3 uSpecularColor;
            float _paddingMaterial2;
            float3 uSubsurfaceInscatteringColor;
            float _paddingMaterial3;
            float3 uSubsurfaceAbsorptionColor;
            float _paddingMaterial4;
        };

        Texture2D uDiffuseMap : register(t0);
        Texture2D uNormalMap : register(t1);
        Texture2D uSpecularMap : register(t2);
        Texture2D uEmissiveMap : register(t3);
        Texture2D uMaskMap : register(t4);
        Texture2D uSMSPSKMap : register(t2);
        Texture2D uESPAMap : register(t3);
        Texture2D uSMRRMap : register(t4);
        Texture2D uSpecColorMap : register(t5);
        SamplerState uSampler : register(s0);

        struct PSInput
        {
            float4 Position : SV_POSITION;
            float3 Normal : NORMAL0;
            float3 WorldPosition : TEXCOORD0;
            float3 Tangent : TEXCOORD1;
            float3 Bitangent : TEXCOORD2;
            float2 Uv : TEXCOORD3;
            nointerpolation int4 BoneIndices : BONEINDICES;
            float4 BoneWeights : BONEWEIGHTS;
        };

        float SelectedWeight(PSInput input)
        {
            if (uSelectedBone < 0)
                return max(max(input.BoneWeights.x, input.BoneWeights.y), max(input.BoneWeights.z, input.BoneWeights.w));

            float value = 0.0;
            if (input.BoneIndices.x == uSelectedBone) value = max(value, input.BoneWeights.x);
            if (input.BoneIndices.y == uSelectedBone) value = max(value, input.BoneWeights.y);
            if (input.BoneIndices.z == uSelectedBone) value = max(value, input.BoneWeights.z);
            if (input.BoneIndices.w == uSelectedBone) value = max(value, input.BoneWeights.w);
            return value;
        }

        float MaxInfluenceWeight(PSInput input)
        {
            return max(max(input.BoneWeights.x, input.BoneWeights.y), max(input.BoneWeights.z, input.BoneWeights.w));
        }

        struct MaterialMasks
        {
            float specMult;
            float specPower;
            float skinMask;
            float reflectivity;
            float emissive;
            float ambientOcclusion;
            float rimMask;
        };

        MaterialMasks GetMaterialMasks(PSInput input)
        {
            MaterialMasks masks;
            if (uHasSMSPSK > 0.5)
            {
                float4 smspsk = uSMSPSKMap.Sample(uSampler, input.Uv);
                masks.specMult = smspsk.r;
                masks.specPower = smspsk.g;
                masks.skinMask = smspsk.b;
                masks.reflectivity = smspsk.a;
                masks.emissive = 0.0;
                masks.ambientOcclusion = 1.0;
                masks.rimMask = 1.0;
            }
            else if (uHasESPA > 0.5 && uHasSMRR > 0.5)
            {
                float4 espa = uESPAMap.Sample(uSampler, input.Uv);
                float4 smrr = uSMRRMap.Sample(uSampler, input.Uv);
                masks.specMult = smrr.r;
                masks.rimMask = smrr.g;
                masks.reflectivity = smrr.b;
                masks.skinMask = espa.b;
                masks.specPower = espa.g;
                masks.emissive = espa.r;
                masks.ambientOcclusion = 1.0;
            }
            else
            {
                masks.specMult = 0.0;
                masks.specPower = 0.0;
                masks.skinMask = 0.0;
                masks.reflectivity = 0.0;
                masks.emissive = 0.0;
                masks.ambientOcclusion = 1.0;
                masks.rimMask = 1.0;
            }

            return masks;
        }

        float3 CalculateGameSpecular(float3 normal, float3 lightDir, float3 viewDir, float specMult, float specPower, float skinMask, PSInput input)
        {
            float3 L = normalize(-lightDir);
            float3 H = normalize(L + viewDir);
            float NdotH = max(dot(normal, H), 0.0);
            float finalSpecPower = uSpecularPower;
            if (uHasSMSPSK > 0.5)
            {
                finalSpecPower = lerp(uSpecularPower, uSpecularPower * 4.0, specPower) * uSpecularPowerMask;
                if (skinMask > 0.0)
                    finalSpecPower *= lerp(1.0, 2.0, skinMask);
            }

            float spec = pow(NdotH, finalSpecPower);
            float finalSpecMult = uHasSMSPSK > 0.5 ? lerp(uSpecMult, uSpecMultLQ, 0.0) : uSpecMult;
            float3 specColor = uHasSpecColorMap > 0.5 ? uSpecColorMap.Sample(uSampler, input.Uv).rgb : uSpecularColor;
            return specColor * spec * finalSpecMult * specMult;
        }

        float3 CalculateGameLighting(float3 normal, float3 viewDir, float3 diffuseColor, PSInput input)
        {
            MaterialMasks masks = GetMaterialMasks(input);
            diffuseColor *= masks.ambientOcclusion;

            float3 ambient = uLambertAmbient * uLightingAmbient;
            ambient += uShadowAmbientColor * uShadowAmbientMult;

            float3 diffuse0 = pow(max(dot(normal, normalize(-uLightDirection)), 0.0), uLambertDiffusePower).xxx;
            float3 diffuse1 = pow(max(dot(normal, normalize(float3(1.0, 1.0, -1.0))), 0.0), uPhongDiffusePower).xxx;
            float3 specular0 = CalculateGameSpecular(normal, uLightDirection, viewDir, masks.specMult, masks.specPower, masks.skinMask, input);
            float3 specular1 = CalculateGameSpecular(normal, float3(1.0, 1.0, -1.0), viewDir, masks.specMult, masks.specPower, masks.skinMask, input);
            float3 fillLight = uFillLightColor * max(0.0, dot(normal, float3(0.0, 1.0, 0.0))) * 0.5;
            float3 rimLight = float3(0.0, 0.0, 0.0);

            if (uRimColorMult > 0.0)
            {
                float rim = 1.0 - max(dot(normal, viewDir), 0.0);
                rim = pow(rim, uRimFalloff) * uRimColorMult;
                rimLight = uFillLightColor * rim * masks.rimMask;
            }

            float3 finalColor = diffuseColor * (ambient + diffuse0 + diffuse1 + fillLight);
            finalColor += specular0 + specular1 + rimLight;

            if (masks.reflectivity > 0.0)
                finalColor += ((masks.reflectivity * uReflectionMult) / (1.0 + uImageReflectionNormalDampening)).xxx * 0.2;

            if (masks.emissive > 0.0)
                finalColor += diffuseColor * masks.emissive * 2.0;

            return saturate(finalColor);
        }

        float4 PSMain(PSInput input) : SV_TARGET
        {
            if (uUseGameMaterial == 1)
            {
                float4 diffuseSample = uHasDiffuseMap > 0.5 ? uDiffuseMap.Sample(uSampler, input.Uv) : float4(uDiffuseColor, 1.0);
                if (uAlphaTest > 0.0 && diffuseSample.a < 0.5)
                    discard;

                float3 normal = normalize(input.Normal);
                if (uHasNormalMap > 0.5)
                {
                    float3 tangent = normalize(input.Tangent);
                    float3 bitangent = normalize(input.Bitangent);
                    float3 sampledNormal = uNormalMap.Sample(uSampler, input.Uv).rgb * 2.0 - 1.0;
                    float3x3 tbn = float3x3(tangent, bitangent, normal);
                    normal = normalize(mul(tbn, normalize(sampledNormal * float3(1.0, 1.0, uNormalStrength))));
                }

                float3 viewDir = normalize(uCameraPos - input.WorldPosition);
                float3 lit = CalculateGameLighting(normal, viewDir, diffuseSample.rgb, input);
                if (uMaterialChannel == 1)
                    lit = diffuseSample.rgb;
                else if (uMaterialChannel == 2)
                    lit = normal * 0.5 + 0.5;
                else if (uMaterialChannel == 3)
                {
                    float specView = uHasSpecColorMap > 0.5
                        ? dot(uSpecColorMap.Sample(uSampler, input.Uv).rgb, float3(0.3333, 0.3333, 0.3333))
                        : (uHasSMSPSK > 0.5 ? uSMSPSKMap.Sample(uSampler, input.Uv).r : (uHasSMRR > 0.5 ? uSMRRMap.Sample(uSampler, input.Uv).r : 0.0));
                    lit = specView.xxx;
                }
                else if (uMaterialChannel == 4)
                    lit = uHasESPA > 0.5 ? uESPAMap.Sample(uSampler, input.Uv).rgb : float3(0.0, 0.0, 0.0);
                else if (uMaterialChannel == 5)
                {
                    float3 maskView = float3(0.0, 0.0, 0.0);
                    if (uHasSMSPSK > 0.5)
                        maskView = uSMSPSKMap.Sample(uSampler, input.Uv).rgb;
                    else if (uHasSMRR > 0.5)
                        maskView = uSMRRMap.Sample(uSampler, input.Uv).rgb;
                    lit = maskView;
                }
                if (uShowSections == 1)
                    lit = lerp(lit, uSectionColor.rgb, 0.18);
                if (uHighlightSection == 1)
                    lit = lerp(lit, uSectionColor.rgb, 0.40);

                float outputAlpha = uAlphaTest > 0.0 ? diffuseSample.a : 1.0;
                return float4(lit, outputAlpha);
            }

            float3 normal = normalize(input.Normal);
            if (uUseNormalMap == 1)
            {
                float3 tangent = normalize(input.Tangent);
                float3 bitangent = normalize(input.Bitangent);
                float3 sampled = uNormalMap.Sample(uSampler, input.Uv).xyz * 2.0 - 1.0;
                float3x3 tbn = float3x3(tangent, bitangent, normal);
                normal = normalize(mul(tbn, sampled));
            }
            float3 lightDir = normalize(-uLightDirection);
            float diffuse = max(dot(normal, lightDir), 0.0);
            float3 fillDir = normalize(float3(0.35, 0.4, 0.85));
            float fill = max(dot(normal, fillDir), 0.0);
            float3 color = uBaseColor;

            if (uUseDiffuseMap == 1)
                color = uDiffuseMap.Sample(uSampler, input.Uv).rgb;

            if (uShowSections == 1)
                color = lerp(color, uSectionColor.rgb, 0.35);

            if (uHighlightSection == 1)
                color = lerp(color, uSectionColor.rgb, 0.72);

            if (uWeightMode == 1)
            {
                float weight = uWeightViewMode == 1
                    ? saturate(MaxInfluenceWeight(input))
                    : saturate(SelectedWeight(input));
                color = lerp(float3(0.05, 0.15, 0.95), float3(1.0, 0.1, 0.05), weight);
            }

            if (uShadingMode == 2)
                color = float3(0.78, 0.76, 0.72);

            float specularStrength = uUseSpecularMap == 1 ? dot(uSpecularMap.Sample(uSampler, input.Uv).rgb, float3(0.3333, 0.3333, 0.3333)) : 0.15;
            float3 viewDir = normalize(uCameraPos - input.WorldPosition);
            float3 reflectDir = reflect(-lightDir, normal);
            float specPower = uLightingPreset == 1 ? 36.0 : (uLightingPreset == 2 ? 48.0 : (uLightingPreset == 3 ? 18.0 : 24.0));
            float effectiveSpecPower = uShadingMode == 1 ? max(specPower, 36.0) : (uShadingMode == 4 ? max(specPower, 32.0) : specPower);
            float specular = pow(max(dot(viewDir, reflectDir), 0.0), effectiveSpecPower) * specularStrength * (uShadingMode == 4 ? 1.2 : 1.0);
            float rim = pow(1.0 - max(dot(normal, viewDir), 0.0), uShadingMode == 1 ? 2.2 : (uShadingMode == 4 ? 4.2 : 3.5)) * (uShadingMode == 1 ? 0.18 : (uShadingMode == 4 ? 0.03 : 0.06));

            if (uUseMaskMap == 1)
                color *= uMaskMap.Sample(uSampler, input.Uv).rgb;

            float3 emissive = uUseEmissiveMap == 1 ? uEmissiveMap.Sample(uSampler, input.Uv).rgb : float3(0.0, 0.0, 0.0);
            float ambient = uAmbientLight + (uLightingPreset == 1 ? 0.10 : (uLightingPreset == 2 ? 0.02 : (uLightingPreset == 3 ? 0.16 : 0.04))) + (uShadingMode == 4 ? 0.03 : 0.0);
            float fillStrength = uLightingPreset == 1 ? 0.32 : (uLightingPreset == 2 ? 0.05 : (uLightingPreset == 3 ? 0.28 : 0.14));
            float3 rimColor = uShadingMode == 1 ? float3(0.38, 0.44, 0.52) : (uShadingMode == 4 ? float3(0.12, 0.14, 0.16) : float3(0.18, 0.20, 0.24));
            if (uShadingMode == 3)
            {
                float facing = clamp(dot(normal, viewDir) * 0.5 + 0.5, 0.0, 1.0);
                float3 matCap = lerp(float3(0.22, 0.24, 0.28), float3(0.84, 0.86, 0.88), facing);
                color = matCap;
            }
            else if (uShadingMode == 4)
            {
                if (uUseDiffuseMap == 1)
                    color = uDiffuseMap.Sample(uSampler, input.Uv).rgb;

                if (uUseMaskMap == 1)
                    color *= lerp(float3(0.92, 0.92, 0.92), uMaskMap.Sample(uSampler, input.Uv).rgb, 0.45);
            }

            if (uMaterialChannel == 1)
                color = uUseDiffuseMap == 1 ? uDiffuseMap.Sample(uSampler, input.Uv).rgb : uBaseColor;
            else if (uMaterialChannel == 2)
                color = normal * 0.5 + 0.5;
            else if (uMaterialChannel == 3)
            {
                float specView = uUseSpecularMap == 1 ? dot(uSpecularMap.Sample(uSampler, input.Uv).rgb, float3(0.3333, 0.3333, 0.3333)) : specularStrength;
                color = specView.xxx;
            }
            else if (uMaterialChannel == 4)
                color = emissive;
            else if (uMaterialChannel == 5)
                color = uUseMaskMap == 1 ? uMaskMap.Sample(uSampler, input.Uv).rgb : float3(0.0, 0.0, 0.0);

            float3 lit = color * (ambient + diffuse * (uShadingMode == 4 ? 0.95 : 0.85) + fill * fillStrength) + specular.xxx + emissive + (rimColor * rim);
            if (uMaterialChannel != 0)
                lit = color;
            return float4(lit, 1.0);
        }
        """;

    private const string LineVertexShaderSource = """
        cbuffer LineConstants : register(b0)
        {
            row_major float4x4 uProjection;
            row_major float4x4 uView;
            row_major float4x4 uModel;
            float4 uColor;
        };

        struct VSInput
        {
            float3 Position : POSITION;
        };

        struct PSInput
        {
            float4 Position : SV_POSITION;
        };

        PSInput VSMain(VSInput input)
        {
            PSInput output;
            float4 world = mul(float4(input.Position, 1.0), uModel);
            output.Position = mul(mul(world, uView), uProjection);
            return output;
        }
        """;

    private const string LinePixelShaderSource = """
        cbuffer LineConstants : register(b0)
        {
            row_major float4x4 uProjection;
            row_major float4x4 uView;
            row_major float4x4 uModel;
            float4 uColor;
        };

        float4 PSMain() : SV_TARGET
        {
            return uColor;
        }
        """;
}
