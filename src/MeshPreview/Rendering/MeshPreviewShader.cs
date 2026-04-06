using System.Numerics;
using OpenTK.Graphics.OpenGL4;

namespace MHUpkManager.MeshPreview;

internal sealed class MeshPreviewShader : IDisposable
{
    public int ProgramHandle { get; }

    public MeshPreviewShader(string vertexSource, string fragmentSource)
    {
        int vertexShader = CompileShader(ShaderType.VertexShader, vertexSource);
        int fragmentShader = CompileShader(ShaderType.FragmentShader, fragmentSource);

        ProgramHandle = GL.CreateProgram();
        GL.AttachShader(ProgramHandle, vertexShader);
        GL.AttachShader(ProgramHandle, fragmentShader);
        GL.LinkProgram(ProgramHandle);
        GL.GetProgram(ProgramHandle, GetProgramParameterName.LinkStatus, out int success);
        if (success == 0)
            throw new InvalidOperationException($"Shader program link failed: {GL.GetProgramInfoLog(ProgramHandle)}");

        GL.DetachShader(ProgramHandle, vertexShader);
        GL.DetachShader(ProgramHandle, fragmentShader);
        GL.DeleteShader(vertexShader);
        GL.DeleteShader(fragmentShader);
    }

    public void Use()
    {
        GL.UseProgram(ProgramHandle);
    }

    public void SetMatrix4(string name, Matrix4x4 value)
    {
        GL.UniformMatrix4(GL.GetUniformLocation(ProgramHandle, name), 1, false, MatrixToArray(value));
    }

    public void SetVector3(string name, Vector3 value)
    {
        GL.Uniform3(GL.GetUniformLocation(ProgramHandle, name), value.X, value.Y, value.Z);
    }

    public void SetVector4(string name, Vector4 value)
    {
        GL.Uniform4(GL.GetUniformLocation(ProgramHandle, name), value.X, value.Y, value.Z, value.W);
    }

    public void SetFloat(string name, float value)
    {
        GL.Uniform1(GL.GetUniformLocation(ProgramHandle, name), value);
    }

    public void SetInt(string name, int value)
    {
        GL.Uniform1(GL.GetUniformLocation(ProgramHandle, name), value);
    }

    public void Dispose()
    {
        if (ProgramHandle != 0)
            GL.DeleteProgram(ProgramHandle);
    }

    private static int CompileShader(ShaderType shaderType, string source)
    {
        int shader = GL.CreateShader(shaderType);
        GL.ShaderSource(shader, source);
        GL.CompileShader(shader);
        GL.GetShader(shader, ShaderParameter.CompileStatus, out int success);
        if (success == 0)
            throw new InvalidOperationException($"{shaderType} compilation failed: {GL.GetShaderInfoLog(shader)}");

        return shader;
    }

    private static float[] MatrixToArray(Matrix4x4 matrix)
    {
        return
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44
        ];
    }
}
