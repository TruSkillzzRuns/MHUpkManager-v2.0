using OpenTK.Graphics.OpenGL4;
using System.Numerics;

namespace MHUpkManager.TexturePreview;

internal sealed class TexturePreviewRenderer : IDisposable
{
    private int _shaderProgram;
    private int _vao;
    private int _vbo;
    private int _textureHandle;
    private TexturePreviewTexture _currentTexture;

    public void Initialize()
    {
        if (_shaderProgram != 0)
            return;

        _shaderProgram = CreateShaderProgram();

        float[] vertices = new float[24];
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 4 * sizeof(float), 2 * sizeof(float));
        GL.BindVertexArray(0);

        GL.ClearColor(0.08f, 0.09f, 0.1f, 1.0f);
    }

    public void SetTexture(TexturePreviewTexture texture)
    {
        Initialize();
        if (ReferenceEquals(_currentTexture, texture))
            return;

        _currentTexture = texture;
        UploadTexture(texture);
    }

    public void Render(int viewportWidth, int viewportHeight, float zoom, Vector2 pan)
    {
        Initialize();
        GL.Viewport(0, 0, Math.Max(1, viewportWidth), Math.Max(1, viewportHeight));
        GL.Clear(ClearBufferMask.ColorBufferBit);

        if (_textureHandle == 0 || _currentTexture == null)
            return;

        float fitScale = MathF.Min(viewportWidth / (float)_currentTexture.Width, viewportHeight / (float)_currentTexture.Height);
        fitScale = MathF.Max(1.0f, fitScale);
        float displayWidth = _currentTexture.Width * fitScale * zoom;
        float displayHeight = _currentTexture.Height * fitScale * zoom;
        float centerX = (viewportWidth * 0.5f) + pan.X;
        float centerY = (viewportHeight * 0.5f) + pan.Y;
        float left = centerX - (displayWidth * 0.5f);
        float right = centerX + (displayWidth * 0.5f);
        float top = centerY - (displayHeight * 0.5f);
        float bottom = centerY + (displayHeight * 0.5f);

        float[] vertices =
        [
            ToNdcX(left, viewportWidth), ToNdcY(bottom, viewportHeight), 0f, 1f,
            ToNdcX(right, viewportWidth), ToNdcY(bottom, viewportHeight), 1f, 1f,
            ToNdcX(right, viewportWidth), ToNdcY(top, viewportHeight), 1f, 0f,
            ToNdcX(left, viewportWidth), ToNdcY(bottom, viewportHeight), 0f, 1f,
            ToNdcX(right, viewportWidth), ToNdcY(top, viewportHeight), 1f, 0f,
            ToNdcX(left, viewportWidth), ToNdcY(top, viewportHeight), 0f, 0f
        ];

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferSubData(BufferTarget.ArrayBuffer, IntPtr.Zero, vertices.Length * sizeof(float), vertices);

        GL.UseProgram(_shaderProgram);
        GL.Uniform1(GL.GetUniformLocation(_shaderProgram, "uTexture"), 0);
        GL.Uniform1(GL.GetUniformLocation(_shaderProgram, "uZoom"), zoom);
        OpenTK.Mathematics.Vector2 textureSize = new(_currentTexture.Width, _currentTexture.Height);
        GL.Uniform2(GL.GetUniformLocation(_shaderProgram, "uTextureSize"), ref textureSize);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _textureHandle);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_textureHandle != 0)
            GL.DeleteTexture(_textureHandle);
        if (_vbo != 0)
            GL.DeleteBuffer(_vbo);
        if (_vao != 0)
            GL.DeleteVertexArray(_vao);
        if (_shaderProgram != 0)
            GL.DeleteProgram(_shaderProgram);
    }

    private void UploadTexture(TexturePreviewTexture texture)
    {
        if (_textureHandle != 0)
        {
            GL.DeleteTexture(_textureHandle);
            _textureHandle = 0;
        }

        if (texture == null)
            return;

        _textureHandle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _textureHandle);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, texture.Width, texture.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, texture.RgbaPixels);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
    }

    private static float ToNdcX(float x, int width) => ((x / width) * 2.0f) - 1.0f;
    private static float ToNdcY(float y, int height) => 1.0f - ((y / height) * 2.0f);

    private static int CreateShaderProgram()
    {
        int vertexShader = CompileShader(ShaderType.VertexShader, VertexShaderSource);
        int fragmentShader = CompileShader(ShaderType.FragmentShader, FragmentShaderSource);
        int program = GL.CreateProgram();
        GL.AttachShader(program, vertexShader);
        GL.AttachShader(program, fragmentShader);
        GL.LinkProgram(program);
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
        if (linked == 0)
            throw new InvalidOperationException($"Texture preview shader link failed: {GL.GetProgramInfoLog(program)}");

        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);
        return program;
    }

    private static int CompileShader(ShaderType type, string source)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
        if (compiled == 0)
            throw new InvalidOperationException($"Texture preview shader compile failed: {GL.GetShaderInfoLog(shader)}");

        return shader;
    }

    private const string VertexShaderSource = """
        #version 330 core
        layout(location = 0) in vec2 aPosition;
        layout(location = 1) in vec2 aUv;

        out vec2 vUv;

        void main()
        {
            vUv = aUv;
            gl_Position = vec4(aPosition, 0.0, 1.0);
        }
        """;

    private const string FragmentShaderSource = """
        #version 330 core
        in vec2 vUv;

        uniform sampler2D uTexture;
        uniform float uZoom;
        uniform vec2 uTextureSize;

        out vec4 FragColor;

        void main()
        {
            vec4 color = texture(uTexture, vUv);
            if (uZoom >= 8.0)
            {
                vec2 pixelCoord = vUv * uTextureSize;
                vec2 grid = abs(fract(pixelCoord) - 0.5);
                float line = 1.0 - min(min(grid.x, grid.y) * 2.0, 1.0);
                color.rgb = mix(color.rgb, vec3(0.0, 0.0, 0.0), line * 0.25);
            }

            FragColor = color;
        }
        """;
}
