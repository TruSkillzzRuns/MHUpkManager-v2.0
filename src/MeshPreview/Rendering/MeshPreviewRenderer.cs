using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using MHUpkManager.TexturePreview;

namespace MHUpkManager.MeshPreview;

internal sealed class MeshPreviewRenderer : IDisposable
{
    private MeshPreviewShader _backgroundShader;
    private MeshPreviewShader _meshShader;
    private MeshPreviewShader _lineShader;
    private int _screenQuadVao;
    private int _sphereVao;
    private int _sphereVbo;
    private int _sphereEbo;
    private int _sphereIndexCount;
    private int _whiteTexture;
    private int _materialRevision = -1;
    private readonly Dictionary<TexturePreviewMaterialSlot, int> _materialTextures = [];
    private readonly Dictionary<string, int> _gameMaterialTextureCache = [];

    public void Initialize()
    {
        if (_meshShader != null)
            return;

        _backgroundShader = new MeshPreviewShader(BackgroundVertexShaderSource, BackgroundFragmentShaderSource);
        _meshShader = new MeshPreviewShader(MeshVertexShaderSource, MeshFragmentShaderSource);
        _lineShader = new MeshPreviewShader(LineVertexShaderSource, LineFragmentShaderSource);
        _screenQuadVao = GL.GenVertexArray();
        CreateJointSphere();
        CreateWhiteTexture();

        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.CullFace);
        GL.ClearColor(0.11f, 0.12f, 0.14f, 1.0f);
    }

    public void Render(MeshPreviewScene scene, MeshPreviewCamera camera, int width, int height)
    {
        Initialize();

        GL.Viewport(0, 0, Math.Max(1, width), Math.Max(1, height));
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);
        DrawBackground(scene);

        float aspect = Math.Max(1, width) / (float)Math.Max(1, height);
        Matrix4x4 projection = camera.GetProjectionMatrix(aspect);
        Matrix4x4 view = camera.GetViewMatrix();
        Vector3 cameraPosition = camera.GetPosition();

        if (scene.ShowGroundPlane)
            DrawGrounding(scene, projection, view);

        if (scene.DisplayMode == MeshPreviewDisplayMode.SideBySide)
        {
            RenderHalf(scene, camera, projection, view, cameraPosition, 0, width / 2, height, scene.FbxMesh, scene.ShowFbxMesh);
            RenderHalf(scene, camera, projection, view, cameraPosition, width / 2, width - (width / 2), height, scene.Ue3Mesh, scene.ShowUe3Mesh);
            GL.Viewport(0, 0, Math.Max(1, width), Math.Max(1, height));
            return;
        }

        DrawSceneMesh(scene, projection, view, cameraPosition, scene.FbxMesh, scene.ShowFbxMesh && scene.DisplayMode != MeshPreviewDisplayMode.Ue3Only, false, ResolveBaseColor(scene, false, sideBySide: false));
        DrawSceneMesh(scene, projection, view, cameraPosition, scene.Ue3Mesh, scene.ShowUe3Mesh && scene.DisplayMode != MeshPreviewDisplayMode.FbxOnly, true, ResolveBaseColor(scene, true, sideBySide: false));
    }

    public void Dispose()
    {
        _backgroundShader?.Dispose();
        _meshShader?.Dispose();
        _lineShader?.Dispose();
        if (_screenQuadVao != 0) GL.DeleteVertexArray(_screenQuadVao);
        if (_sphereVbo != 0) GL.DeleteBuffer(_sphereVbo);
        if (_sphereEbo != 0) GL.DeleteBuffer(_sphereEbo);
        if (_sphereVao != 0) GL.DeleteVertexArray(_sphereVao);
        if (_whiteTexture != 0) GL.DeleteTexture(_whiteTexture);
        foreach ((_, int handle) in _materialTextures)
        {
            if (handle != 0)
                GL.DeleteTexture(handle);
        }
        foreach ((_, int handle) in _gameMaterialTextureCache)
        {
            if (handle != 0)
                GL.DeleteTexture(handle);
        }
    }

    private void DrawBackground(MeshPreviewScene scene)
    {
        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);

        _backgroundShader.Use();
        _backgroundShader.SetInt("uBackgroundStyle", (int)scene.BackgroundStyle);
        GL.BindVertexArray(_screenQuadVao);
        GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
        GL.BindVertexArray(0);

        GL.Enable(EnableCap.CullFace);
        GL.Enable(EnableCap.DepthTest);
    }

    private void RenderHalf(MeshPreviewScene scene, MeshPreviewCamera camera, Matrix4x4 projection, Matrix4x4 view, Vector3 cameraPosition, int x, int width, int height, MeshPreviewMesh mesh, bool visible)
    {
        GL.Viewport(x, 0, Math.Max(1, width), Math.Max(1, height));
        GL.Clear(ClearBufferMask.DepthBufferBit);
        if (visible)
            DrawSceneMesh(scene, camera.GetProjectionMatrix(Math.Max(1, width) / (float)Math.Max(1, height)), view, cameraPosition, mesh, true, x != 0, ResolveBaseColor(scene, x != 0, sideBySide: true));
    }

    private void DrawSceneMesh(MeshPreviewScene scene, Matrix4x4 projection, Matrix4x4 view, Vector3 cameraPosition, MeshPreviewMesh mesh, bool visible, bool ue3Mesh, Vector3 baseColor)
    {
        if (!visible || mesh == null || mesh.Vertices.Count == 0)
            return;

        mesh.Upload();

        if (scene.Wireframe)
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Line);

        _meshShader.Use();
        _meshShader.SetMatrix4("uProjection", projection);
        _meshShader.SetMatrix4("uView", view);
        _meshShader.SetMatrix4("uModel", Matrix4x4.Identity);
        _meshShader.SetVector3("uCameraPos", cameraPosition);
        _meshShader.SetVector3("uLightDirection", Vector3.Normalize(new Vector3(-0.45f, -0.7f, -0.4f)));
        _meshShader.SetFloat("uAmbientLight", scene.AmbientLight);
        _meshShader.SetInt("uLightingPreset", (int)scene.LightingPreset);
        _meshShader.SetInt("uMaterialChannel", (int)scene.MaterialChannel);
        _meshShader.SetInt("uWeightMode", scene.ShowWeights ? 1 : 0);
        _meshShader.SetInt("uWeightViewMode", (int)scene.WeightViewMode);
        _meshShader.SetInt("uShadingMode", (int)scene.ShadingMode);
        _meshShader.SetInt("uShowSections", scene.ShowSections ? 1 : 0);
        _meshShader.SetInt("uSelectedBone", ResolveBoneIndex(mesh, scene.SelectedBoneName));
        _meshShader.SetVector3("uBaseColor", baseColor);

        GL.BindVertexArray(mesh.VertexArrayObject);
        foreach (MeshPreviewSection section in mesh.Sections)
        {
            if (!scene.IsSectionVisible(ue3Mesh, section.Index))
                continue;

            ApplySectionMaterialState(scene, section, ue3Mesh);
            _meshShader.SetVector4("uSectionColor", section.Color);
            _meshShader.SetInt("uHighlightSection", scene.IsSectionHighlighted(ue3Mesh, section.Index) ? 1 : 0);
            GL.DrawElements(PrimitiveType.Triangles, section.IndexCount, DrawElementsType.UnsignedInt, section.BaseIndex * sizeof(uint));
        }
        GL.BindVertexArray(0);
        ResetSectionMaterialState();

        if (scene.Wireframe)
            GL.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

        if (scene.ShowNormals)
            DrawLines(mesh.NormalLineVao, mesh.NormalLineVertexCount, projection, view, new Vector4(1f, 0.1f, 0.9f, 1f));
        if (scene.ShowTangents)
            DrawLines(mesh.TangentLineVao, mesh.TangentLineVertexCount, projection, view, new Vector4(0.1f, 1f, 1f, 1f));
        if (scene.ShowUvSeams)
            DrawLines(mesh.UvSeamVao, mesh.UvSeamVertexCount, projection, view, new Vector4(1f, 0.8f, 0.2f, 1f));
        if (scene.ShowBones)
            DrawBones(mesh, projection, view);
    }

    private void DrawLines(int vao, int count, Matrix4x4 projection, Matrix4x4 view, Vector4 color)
    {
        if (vao == 0 || count == 0)
            return;

        GL.Disable(EnableCap.CullFace);
        _lineShader.Use();
        _lineShader.SetMatrix4("uProjection", projection);
        _lineShader.SetMatrix4("uView", view);
        _lineShader.SetMatrix4("uModel", Matrix4x4.Identity);
        _lineShader.SetVector4("uColor", color);
        GL.BindVertexArray(vao);
        GL.DrawArrays(PrimitiveType.Lines, 0, count);
        GL.BindVertexArray(0);
        GL.Enable(EnableCap.CullFace);
    }

    private void DrawBones(MeshPreviewMesh mesh, Matrix4x4 projection, Matrix4x4 view)
    {
        if (mesh.Bones.Count == 0)
            return;

        List<Vector3> boneLines = [];
        foreach (MeshPreviewBone bone in mesh.Bones)
        {
            if (bone.ParentIndex < 0 || bone.ParentIndex >= mesh.Bones.Count)
                continue;

            boneLines.Add(mesh.Bones[bone.ParentIndex].GlobalTransform.Translation);
            boneLines.Add(bone.GlobalTransform.Translation);
        }

        if (boneLines.Count > 0)
        {
            int vao = GL.GenVertexArray();
            int vbo = GL.GenBuffer();
            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, boneLines.Count * sizeof(float) * 3, boneLines.ToArray(), BufferUsageHint.StreamDraw);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
            DrawLines(vao, boneLines.Count, projection, view, new Vector4(0.8f, 0.8f, 0.8f, 1f));
            GL.DeleteBuffer(vbo);
            GL.DeleteVertexArray(vao);
        }

        _lineShader.Use();
        _lineShader.SetMatrix4("uProjection", projection);
        _lineShader.SetMatrix4("uView", view);
        _lineShader.SetVector4("uColor", new Vector4(1f, 0.9f, 0.2f, 1f));
        GL.BindVertexArray(_sphereVao);
        foreach (MeshPreviewBone bone in mesh.Bones)
        {
            Matrix4x4 model = Matrix4x4.CreateScale(MathF.Max(0.03f, mesh.Radius * 0.015f)) * Matrix4x4.CreateTranslation(bone.GlobalTransform.Translation);
            _lineShader.SetMatrix4("uModel", model);
            GL.DrawElements(PrimitiveType.Triangles, _sphereIndexCount, DrawElementsType.UnsignedInt, 0);
        }
        GL.BindVertexArray(0);
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

    private void DrawDynamicLines(List<Vector3> points, Matrix4x4 projection, Matrix4x4 view, Vector4 color)
    {
        if (points.Count == 0)
            return;

        int vao = GL.GenVertexArray();
        int vbo = GL.GenBuffer();
        GL.BindVertexArray(vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, points.Count * sizeof(float) * 3, points.ToArray(), BufferUsageHint.StreamDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        DrawLines(vao, points.Count, projection, view, color);
        GL.DeleteBuffer(vbo);
        GL.DeleteVertexArray(vao);
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

    private void CreateJointSphere()
    {
        List<Vector3> vertices = [];
        List<uint> indices = [];
        const int stacks = 8;
        const int slices = 10;

        for (int stack = 0; stack <= stacks; stack++)
        {
            float phi = MathF.PI * stack / stacks;
            float y = MathF.Cos(phi);
            float r = MathF.Sin(phi);
            for (int slice = 0; slice <= slices; slice++)
            {
                float theta = 2.0f * MathF.PI * slice / slices;
                vertices.Add(new Vector3(r * MathF.Cos(theta), r * MathF.Sin(theta), y));
            }
        }

        for (int stack = 0; stack < stacks; stack++)
        {
            for (int slice = 0; slice < slices; slice++)
            {
                int first = (stack * (slices + 1)) + slice;
                int second = first + slices + 1;
                indices.Add((uint)first);
                indices.Add((uint)second);
                indices.Add((uint)(first + 1));
                indices.Add((uint)(first + 1));
                indices.Add((uint)second);
                indices.Add((uint)(second + 1));
            }
        }

        _sphereIndexCount = indices.Count;
        _sphereVao = GL.GenVertexArray();
        _sphereVbo = GL.GenBuffer();
        _sphereEbo = GL.GenBuffer();
        GL.BindVertexArray(_sphereVao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _sphereVbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float) * 3, vertices.ToArray(), BufferUsageHint.StaticDraw);
        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _sphereEbo);
        GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Count * sizeof(uint), indices.ToArray(), BufferUsageHint.StaticDraw);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);
        GL.BindVertexArray(0);
    }

    private void CreateWhiteTexture()
    {
        _whiteTexture = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        byte[] whitePixel = [255, 255, 255, 255];
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 1, 1, 0, PixelFormat.Rgba, PixelType.UnsignedByte, whitePixel);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
    }

    private void ApplyMaterialState(MeshPreviewScene scene)
    {
        ApplyMaterialState(scene, scene.MaterialSet);
    }

    private void ApplyMaterialState(MeshPreviewScene scene, TexturePreviewMaterialSet materialSet)
    {
        if (materialSet.Revision != _materialRevision)
        {
            RefreshMaterialTextures(materialSet);
            _materialRevision = materialSet.Revision;
        }

        bool enabled = scene.MaterialPreviewEnabled && materialSet.Enabled;
        TexturePreviewMaterialMode mode = materialSet.ResolveMode();

        BindMaterialTexture(TextureUnit.Texture0, 0, TexturePreviewMaterialSlot.Diffuse, enabled && materialSet.HasTexture(TexturePreviewMaterialSlot.Diffuse));
        BindMaterialTexture(TextureUnit.Texture1, 1, TexturePreviewMaterialSlot.Normal, enabled && mode >= TexturePreviewMaterialMode.DiffuseAndNormal && materialSet.HasTexture(TexturePreviewMaterialSlot.Normal));
        BindMaterialTexture(TextureUnit.Texture2, 2, TexturePreviewMaterialSlot.Specular, enabled && mode == TexturePreviewMaterialMode.FullMaterial && materialSet.HasTexture(TexturePreviewMaterialSlot.Specular));
        BindMaterialTexture(TextureUnit.Texture3, 3, TexturePreviewMaterialSlot.Emissive, enabled && mode == TexturePreviewMaterialMode.FullMaterial && materialSet.HasTexture(TexturePreviewMaterialSlot.Emissive));
        BindMaterialTexture(TextureUnit.Texture4, 4, TexturePreviewMaterialSlot.Mask, enabled && mode == TexturePreviewMaterialMode.FullMaterial && materialSet.HasTexture(TexturePreviewMaterialSlot.Mask));

        _meshShader.SetInt("uDiffuseMap", 0);
        _meshShader.SetInt("uNormalMap", 1);
        _meshShader.SetInt("uSpecularMap", 2);
        _meshShader.SetInt("uEmissiveMap", 3);
        _meshShader.SetInt("uMaskMap", 4);
        _meshShader.SetInt("uUseDiffuseMap", enabled && materialSet.HasTexture(TexturePreviewMaterialSlot.Diffuse) ? 1 : 0);
        _meshShader.SetInt("uUseNormalMap", enabled && mode >= TexturePreviewMaterialMode.DiffuseAndNormal && materialSet.HasTexture(TexturePreviewMaterialSlot.Normal) ? 1 : 0);
        _meshShader.SetInt("uUseSpecularMap", enabled && mode == TexturePreviewMaterialMode.FullMaterial && materialSet.HasTexture(TexturePreviewMaterialSlot.Specular) ? 1 : 0);
        _meshShader.SetInt("uUseEmissiveMap", enabled && mode == TexturePreviewMaterialMode.FullMaterial && materialSet.HasTexture(TexturePreviewMaterialSlot.Emissive) ? 1 : 0);
        _meshShader.SetInt("uUseMaskMap", enabled && mode == TexturePreviewMaterialMode.FullMaterial && materialSet.HasTexture(TexturePreviewMaterialSlot.Mask) ? 1 : 0);
    }

    private void ApplySectionMaterialState(MeshPreviewScene scene, MeshPreviewSection section, bool ue3Mesh)
    {
        bool gameApprox = ue3Mesh && scene.ShadingMode == MeshPreviewShadingMode.GameApprox;
        bool useGameMaterial = gameApprox && section.GameMaterial?.Enabled == true;

        if (!useGameMaterial)
        {
            if (scene.MaterialPreviewEnabled && ue3Mesh && scene.TryGetUe3SectionMaterialSet(section.Index, out TexturePreviewMaterialSet sectionMaterialSet))
            {
                ApplyMaterialState(scene, sectionMaterialSet);
                _meshShader.SetInt("uUseGameMaterial", 0);
                GL.Disable(EnableCap.Blend);
                GL.DepthMask(true);
                GL.Enable(EnableCap.CullFace);
                _meshShader.SetFloat("uAlphaTest", 0f);
                return;
            }

            if (gameApprox)
            {
                ApplyDisabledMaterialState();
                _meshShader.SetInt("uUseGameMaterial", 0);
                return;
            }

            ApplyMaterialState(scene);
            _meshShader.SetInt("uUseGameMaterial", 0);
            return;
        }

        MeshPreviewGameMaterial material = section.GameMaterial;
        _meshShader.SetInt("uUseGameMaterial", 0);
        _meshShader.SetInt("uShadingMode", (int)MeshPreviewShadingMode.Lit);
        _meshShader.SetInt("uMaterialChannel", (int)MeshPreviewMaterialChannel.BaseColor);
        _meshShader.SetInt("uUseDiffuseMap", material.HasTexture(MeshPreviewGameTextureSlot.Diffuse) ? 1 : 0);
        _meshShader.SetInt("uUseNormalMap", 0);
        _meshShader.SetInt("uUseSpecularMap", 0);
        _meshShader.SetInt("uUseEmissiveMap", 0);
        _meshShader.SetInt("uUseMaskMap", 0);
        _meshShader.SetFloat("uLambertDiffusePower", material.LambertDiffusePower);
        _meshShader.SetFloat("uPhongDiffusePower", material.PhongDiffusePower);
        _meshShader.SetFloat("uLightingAmbient", material.LightingAmbient);
        _meshShader.SetFloat("uShadowAmbientMult", material.ShadowAmbientMult);
        _meshShader.SetFloat("uNormalStrength", material.NormalStrength);
        _meshShader.SetFloat("uReflectionMult", material.ReflectionMult);
        _meshShader.SetFloat("uRimColorMult", material.RimColorMult);
        _meshShader.SetFloat("uRimFalloff", material.RimFalloff);
        _meshShader.SetFloat("uScreenLightAmount", material.ScreenLightAmount);
        _meshShader.SetFloat("uScreenLightMult", material.ScreenLightMult);
        _meshShader.SetFloat("uScreenLightPower", material.ScreenLightPower);
        _meshShader.SetFloat("uSpecMult", material.SpecMult);
        _meshShader.SetFloat("uSpecMultLQ", material.SpecMultLq);
        _meshShader.SetFloat("uSpecularPower", material.SpecularPower);
        _meshShader.SetFloat("uSpecularPowerMask", material.SpecularPowerMask);
        _meshShader.SetVector3("uLambertAmbient", material.LambertAmbient);
        _meshShader.SetVector3("uShadowAmbientColor", material.ShadowAmbientColor);
        _meshShader.SetVector3("uFillLightColor", material.FillLightColor);
        _meshShader.SetVector3("uDiffuseColor", material.DiffuseColor);
        _meshShader.SetVector3("uSpecularColor", material.SpecularColor);
        _meshShader.SetVector3("uSubsurfaceInscatteringColor", material.SubsurfaceInscatteringColor);
        _meshShader.SetVector3("uSubsurfaceAbsorptionColor", material.SubsurfaceAbsorptionColor);
        _meshShader.SetFloat("uImageReflectionNormalDampening", material.ImageReflectionNormalDampening);
        _meshShader.SetFloat("uSkinScatterStrength", material.SkinScatterStrength);
        _meshShader.SetFloat("uTwoSidedLighting", material.TwoSidedLighting);

        BindGameMaterialTexture(TextureUnit.Texture0, 0, material, MeshPreviewGameTextureSlot.Diffuse, "uDiffuseMap", "uHasDiffuseMap");
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        _meshShader.SetInt("uNormalMap", 1);
        _meshShader.SetFloat("uHasNormalMap", 0.0f);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        GL.ActiveTexture(TextureUnit.Texture4);
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        GL.ActiveTexture(TextureUnit.Texture5);
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        _meshShader.SetInt("uSMSPSKMap", 2);
        _meshShader.SetInt("uESPAMap", 3);
        _meshShader.SetInt("uSMRRMap", 4);
        _meshShader.SetInt("uSpecColorMap", 5);
        _meshShader.SetFloat("uHasSMSPSK", 0.0f);
        _meshShader.SetFloat("uHasESPA", 0.0f);
        _meshShader.SetFloat("uHasSMRR", 0.0f);
        _meshShader.SetFloat("uHasSpecColorMap", 0.0f);

        if (material.TwoSided)
            GL.Disable(EnableCap.CullFace);
        else
            GL.Enable(EnableCap.CullFace);

        float alphaTest = 0f;
        switch (material.BlendMode)
        {
            case UpkManager.Models.UpkFile.Engine.Material.EBlendMode.BLEND_Opaque:
                GL.Disable(EnableCap.Blend);
                GL.DepthMask(true);
                break;
            case UpkManager.Models.UpkFile.Engine.Material.EBlendMode.BLEND_Masked:
                GL.Disable(EnableCap.Blend);
                GL.DepthMask(true);
                alphaTest = 1f;
                break;
            case UpkManager.Models.UpkFile.Engine.Material.EBlendMode.BLEND_Translucent:
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.DepthMask(false);
                break;
            case UpkManager.Models.UpkFile.Engine.Material.EBlendMode.BLEND_Additive:
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.One);
                GL.DepthMask(false);
                break;
            default:
                GL.Enable(EnableCap.Blend);
                GL.BlendFunc(BlendingFactor.DstColor, BlendingFactor.Zero);
                GL.DepthMask(false);
                break;
        }

        _meshShader.SetFloat("uAlphaTest", alphaTest);
    }

    private void ResetSectionMaterialState()
    {
        ApplyDisabledMaterialState();
        _meshShader.SetInt("uUseGameMaterial", 0);
        GL.Disable(EnableCap.Blend);
        GL.DepthMask(true);
        GL.Enable(EnableCap.CullFace);
        _meshShader.SetFloat("uAlphaTest", 0f);
    }

    private void ApplyDisabledMaterialState()
    {
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        GL.ActiveTexture(TextureUnit.Texture1);
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        GL.ActiveTexture(TextureUnit.Texture2);
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        GL.ActiveTexture(TextureUnit.Texture3);
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        GL.ActiveTexture(TextureUnit.Texture4);
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        GL.ActiveTexture(TextureUnit.Texture5);
        GL.BindTexture(TextureTarget.Texture2D, _whiteTexture);

        _meshShader.SetInt("uDiffuseMap", 0);
        _meshShader.SetInt("uNormalMap", 1);
        _meshShader.SetInt("uSpecularMap", 2);
        _meshShader.SetInt("uEmissiveMap", 3);
        _meshShader.SetInt("uMaskMap", 4);
        _meshShader.SetInt("uSMSPSKMap", 2);
        _meshShader.SetInt("uESPAMap", 3);
        _meshShader.SetInt("uSMRRMap", 4);
        _meshShader.SetInt("uSpecColorMap", 5);
        _meshShader.SetInt("uUseDiffuseMap", 0);
        _meshShader.SetInt("uUseNormalMap", 0);
        _meshShader.SetInt("uUseSpecularMap", 0);
        _meshShader.SetInt("uUseEmissiveMap", 0);
        _meshShader.SetInt("uUseMaskMap", 0);
        _meshShader.SetFloat("uHasDiffuseMap", 0.0f);
        _meshShader.SetFloat("uHasNormalMap", 0.0f);
        _meshShader.SetFloat("uHasSMSPSK", 0.0f);
        _meshShader.SetFloat("uHasESPA", 0.0f);
        _meshShader.SetFloat("uHasSMRR", 0.0f);
        _meshShader.SetFloat("uHasSpecColorMap", 0.0f);
    }

    private void BindGameMaterialTexture(TextureUnit unit, int uniformIndex, MeshPreviewGameMaterial material, MeshPreviewGameTextureSlot slot, string samplerName, string flagName)
    {
        GL.ActiveTexture(unit);
        bool hasTexture = material.HasTexture(slot);
        int handle = hasTexture ? GetOrCreateGameMaterialTextureHandle(material.GetTexture(slot)) : _whiteTexture;
        GL.BindTexture(TextureTarget.Texture2D, handle);
        _meshShader.SetInt(samplerName, uniformIndex);
        _meshShader.SetFloat(flagName, hasTexture ? 1.0f : 0.0f);
    }

    private int GetOrCreateGameMaterialTextureHandle(TexturePreviewTexture texture)
    {
        string key = $"{texture.SourcePath}|{texture.ExportPath}|{texture.SelectedMipIndex}|{texture.Width}x{texture.Height}";
        if (_gameMaterialTextureCache.TryGetValue(key, out int handle))
            return handle;

        handle = UploadMaterialTexture(texture);
        _gameMaterialTextureCache[key] = handle;
        return handle;
    }

    private void BindMaterialTexture(TextureUnit unit, int uniformIndex, TexturePreviewMaterialSlot slot, bool useSlotTexture)
    {
        GL.ActiveTexture(unit);
        int handle = useSlotTexture && _materialTextures.TryGetValue(slot, out int textureHandle)
            ? textureHandle
            : _whiteTexture;
        GL.BindTexture(TextureTarget.Texture2D, handle);
    }

    private void RefreshMaterialTextures(TexturePreviewMaterialSet materialSet)
    {
        foreach ((_, int handle) in _materialTextures)
        {
            if (handle != 0)
                GL.DeleteTexture(handle);
        }

        _materialTextures.Clear();
        foreach ((TexturePreviewMaterialSlot slot, TexturePreviewTexture texture) in materialSet.Textures)
            _materialTextures[slot] = UploadMaterialTexture(texture);
    }

    private static int UploadMaterialTexture(TexturePreviewTexture texture)
    {
        int handle = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, handle);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, texture.Width, texture.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, texture.RgbaPixels);
        GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
        return handle;
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

    private const string MeshVertexShaderSource = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec3 aTangent;
        layout(location = 3) in vec3 aBitangent;
        layout(location = 4) in vec2 aUv;
        layout(location = 5) in ivec4 aBoneIndices;
        layout(location = 6) in vec4 aBoneWeights;
        layout(location = 7) in int aSectionIndex;

        uniform mat4 uProjection;
        uniform mat4 uView;
        uniform mat4 uModel;

        out vec3 vWorldPosition;
        out vec3 vNormal;
        out vec3 vTangent;
        out vec3 vBitangent;
        out vec2 vUv;
        flat out ivec4 vBoneIndices;
        out vec4 vBoneWeights;
        flat out int vSectionIndex;

        void main()
        {
            vec4 worldPosition = uModel * vec4(aPosition, 1.0);
            vWorldPosition = worldPosition.xyz;
            vNormal = mat3(uModel) * aNormal;
            vTangent = mat3(uModel) * aTangent;
            vBitangent = mat3(uModel) * aBitangent;
            vUv = aUv;
            vBoneIndices = aBoneIndices;
            vBoneWeights = aBoneWeights;
            vSectionIndex = aSectionIndex;
            gl_Position = uProjection * uView * worldPosition;
        }
        """;

    private const string BackgroundVertexShaderSource = """
        #version 330 core
        out vec2 vUv;

        void main()
        {
            vec2 positions[4] = vec2[](
                vec2(-1.0, -1.0),
                vec2( 1.0, -1.0),
                vec2(-1.0,  1.0),
                vec2( 1.0,  1.0)
            );

            vec2 position = positions[gl_VertexID];
            vUv = position * 0.5 + 0.5;
            gl_Position = vec4(position, 0.0, 1.0);
        }
        """;

    private const string BackgroundFragmentShaderSource = """
        #version 330 core
        in vec2 vUv;
        uniform int uBackgroundStyle;
        out vec4 FragColor;

        vec3 darkGradient()
        {
            vec3 top = vec3(0.07, 0.08, 0.10);
            vec3 bottom = vec3(0.14, 0.15, 0.18);
            vec3 baseColor = mix(bottom, top, smoothstep(0.0, 1.0, vUv.y));
            float floorFade = smoothstep(0.0, 0.40, vUv.y);
            baseColor += vec3(0.03, 0.03, 0.035) * (1.0 - floorFade);
            return baseColor;
        }

        vec3 studioGray()
        {
            vec3 top = vec3(0.22, 0.23, 0.24);
            vec3 bottom = vec3(0.30, 0.30, 0.31);
            vec3 baseColor = mix(bottom, top, smoothstep(0.0, 1.0, vUv.y));
            float floorFade = smoothstep(0.0, 0.36, vUv.y);
            return baseColor + vec3(0.025) * (1.0 - floorFade);
        }

        vec3 checker()
        {
            vec3 top = vec3(0.12, 0.13, 0.15);
            vec3 bottom = vec3(0.18, 0.18, 0.20);
            vec3 baseColor = mix(bottom, top, smoothstep(0.0, 1.0, vUv.y));
            vec2 gridUv = vec2(vUv.x * 14.0, vUv.y * 9.0);
            float check = mod(floor(gridUv.x) + floor(gridUv.y), 2.0);
            float checkerMask = 1.0 - smoothstep(0.42, 0.85, vUv.y);
            baseColor = mix(baseColor, baseColor + vec3((check * 2.0 - 1.0) * 0.025), checkerMask * 0.65);
            return baseColor;
        }

        void main()
        {
            vec3 color;
            if (uBackgroundStyle == 1)
                color = studioGray();
            else if (uBackgroundStyle == 2)
                color = vec3(0.02, 0.02, 0.025);
            else if (uBackgroundStyle == 3)
                color = checker();
            else
                color = darkGradient();

            float vignette = smoothstep(1.15, 0.25, distance(vUv, vec2(0.5, 0.52)));
            color *= mix(0.86, 1.0, vignette);
            FragColor = vec4(color, 1.0);
        }
        """;

    private const string MeshFragmentShaderSource = """
        #version 330 core
        in vec3 vWorldPosition;
        in vec3 vNormal;
        in vec3 vTangent;
        in vec3 vBitangent;
        in vec2 vUv;
        flat in ivec4 vBoneIndices;
        in vec4 vBoneWeights;
        flat in int vSectionIndex;

        uniform vec3 uCameraPos;
        uniform vec3 uLightDirection;
        uniform vec3 uBaseColor;
        uniform vec4 uSectionColor;
        uniform float uAmbientLight;
        uniform int uLightingPreset;
        uniform int uMaterialChannel;
        uniform int uWeightMode;
        uniform int uWeightViewMode;
        uniform int uShadingMode;
        uniform int uSelectedBone;
        uniform int uShowSections;
        uniform int uHighlightSection;
        uniform sampler2D uDiffuseMap;
        uniform sampler2D uNormalMap;
        uniform sampler2D uSpecularMap;
        uniform sampler2D uEmissiveMap;
        uniform sampler2D uMaskMap;
        uniform int uUseDiffuseMap;
        uniform int uUseNormalMap;
        uniform int uUseSpecularMap;
        uniform int uUseEmissiveMap;
        uniform int uUseMaskMap;
        uniform int uUseGameMaterial;
        uniform sampler2D uSMSPSKMap;
        uniform sampler2D uESPAMap;
        uniform sampler2D uSMRRMap;
        uniform sampler2D uSpecColorMap;
        uniform float uHasDiffuseMap;
        uniform float uHasNormalMap;
        uniform float uHasSMSPSK;
        uniform float uHasESPA;
        uniform float uHasSMRR;
        uniform float uHasSpecColorMap;
        uniform float uLambertDiffusePower;
        uniform float uPhongDiffusePower;
        uniform float uLightingAmbient;
        uniform float uShadowAmbientMult;
        uniform float uNormalStrength;
        uniform float uReflectionMult;
        uniform float uRimColorMult;
        uniform float uRimFalloff;
        uniform float uScreenLightAmount;
        uniform float uScreenLightMult;
        uniform float uScreenLightPower;
        uniform float uSpecMult;
        uniform float uSpecMultLQ;
        uniform float uSpecularPower;
        uniform float uSpecularPowerMask;
        uniform vec3 uLambertAmbient;
        uniform vec3 uShadowAmbientColor;
        uniform vec3 uFillLightColor;
        uniform vec3 uDiffuseColor;
        uniform vec3 uSpecularColor;
        uniform vec3 uSubsurfaceInscatteringColor;
        uniform vec3 uSubsurfaceAbsorptionColor;
        uniform float uImageReflectionNormalDampening;
        uniform float uSkinScatterStrength;
        uniform float uTwoSidedLighting;
        uniform float uAlphaTest;

        out vec4 FragColor;

        float selectedWeight()
        {
            if (uSelectedBone < 0)
                return max(max(vBoneWeights.x, vBoneWeights.y), max(vBoneWeights.z, vBoneWeights.w));

            float result = 0.0;
            if (vBoneIndices.x == uSelectedBone) result = max(result, vBoneWeights.x);
            if (vBoneIndices.y == uSelectedBone) result = max(result, vBoneWeights.y);
            if (vBoneIndices.z == uSelectedBone) result = max(result, vBoneWeights.z);
            if (vBoneIndices.w == uSelectedBone) result = max(result, vBoneWeights.w);
            return result;
        }

        float maxInfluenceWeight()
        {
            return max(max(vBoneWeights.x, vBoneWeights.y), max(vBoneWeights.z, vBoneWeights.w));
        }

        struct MaterialMasks {
            float specMult;
            float specPower;
            float skinMask;
            float reflectivity;
            float emissive;
            float ambientOcclusion;
            float rimMask;
        };

        MaterialMasks getMaterialMasks()
        {
            MaterialMasks masks;
            if (uHasSMSPSK > 0.5)
            {
                vec4 smspsk = texture(uSMSPSKMap, vUv);
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
                vec4 espa = texture(uESPAMap, vUv);
                vec4 smrr = texture(uSMRRMap, vUv);
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

        vec3 calculateGameSpecular(vec3 normal, vec3 lightDir, vec3 viewDir, float specMult, float specPower, float skinMask)
        {
            vec3 L = normalize(-lightDir);
            vec3 H = normalize(L + viewDir);
            float NdotH = max(dot(normal, H), 0.0);
            float finalSpecPower = uSpecularPower;
            if (uHasSMSPSK > 0.5)
            {
                finalSpecPower = mix(uSpecularPower, uSpecularPower * 4.0, specPower) * uSpecularPowerMask;
                if (skinMask > 0.0)
                    finalSpecPower *= mix(1.0, 2.0, skinMask);
            }

            float spec = pow(NdotH, finalSpecPower);
            float finalSpecMult = uHasSMSPSK > 0.5 ? mix(uSpecMult, uSpecMultLQ, 0.0) : uSpecMult;
            vec3 specColor = uHasSpecColorMap > 0.5 ? texture(uSpecColorMap, vUv).rgb : uSpecularColor;
            return specColor * spec * finalSpecMult * specMult;
        }

        vec3 calculateGameLighting(vec3 normal, vec3 viewDir, vec3 diffuseColor)
        {
            MaterialMasks masks = getMaterialMasks();
            diffuseColor *= masks.ambientOcclusion;

            vec3 ambient = uLambertAmbient * uLightingAmbient;
            ambient += uShadowAmbientColor * uShadowAmbientMult;

            vec3 diffuse0 = vec3(pow(max(dot(normal, normalize(-uLightDirection)), 0.0), uLambertDiffusePower));
            vec3 diffuse1 = vec3(pow(max(dot(normal, normalize(vec3(1.0, 1.0, -1.0))), 0.0), uPhongDiffusePower));
            vec3 specular0 = calculateGameSpecular(normal, uLightDirection, viewDir, masks.specMult, masks.specPower, masks.skinMask);
            vec3 specular1 = calculateGameSpecular(normal, vec3(1.0, 1.0, -1.0), viewDir, masks.specMult, masks.specPower, masks.skinMask);
            vec3 fillLight = uFillLightColor * max(0.0, dot(normal, vec3(0.0, 1.0, 0.0))) * 0.5;
            vec3 rimLight = vec3(0.0);
            if (uRimColorMult > 0.0)
            {
                float rim = 1.0 - max(dot(normal, viewDir), 0.0);
                rim = pow(rim, uRimFalloff) * uRimColorMult;
                rimLight = uFillLightColor * rim * masks.rimMask;
            }

            vec3 finalColor = diffuseColor * (ambient + diffuse0 + diffuse1 + fillLight);
            finalColor += specular0 + specular1 + rimLight;
            if (masks.reflectivity > 0.0)
                finalColor += vec3((masks.reflectivity * uReflectionMult) / (1.0 + uImageReflectionNormalDampening)) * 0.2;
            if (masks.emissive > 0.0)
                finalColor += diffuseColor * masks.emissive * 2.0;
            return clamp(finalColor, 0.0, 1.0);
        }

        void main()
        {
            if (uUseGameMaterial == 1)
            {
                vec4 diffuseSample = uHasDiffuseMap > 0.5 ? texture(uDiffuseMap, vUv) : vec4(uDiffuseColor, 1.0);
                if (uAlphaTest > 0.0 && diffuseSample.a < 0.5)
                    discard;

                vec3 normal = normalize(vNormal);
                if (uHasNormalMap > 0.5)
                {
                    vec3 tangent = normalize(vTangent);
                    vec3 bitangent = normalize(vBitangent);
                    mat3 tbn = mat3(tangent, bitangent, normal);
                    vec3 sampledNormal = texture(uNormalMap, vUv).rgb * 2.0 - 1.0;
                    normal = normalize(tbn * normalize(sampledNormal * vec3(1.0, 1.0, uNormalStrength)));
                }

                vec3 viewDir = normalize(uCameraPos - vWorldPosition);
                vec3 lit = calculateGameLighting(normal, viewDir, diffuseSample.rgb);
                if (uMaterialChannel == 1)
                    lit = diffuseSample.rgb;
                else if (uMaterialChannel == 2)
                    lit = normal * 0.5 + 0.5;
                else if (uMaterialChannel == 3)
                {
                    float specView = uHasSpecColorMap > 0.5
                        ? dot(texture(uSpecColorMap, vUv).rgb, vec3(0.3333))
                        : (uHasSMSPSK > 0.5 ? texture(uSMSPSKMap, vUv).r : (uHasSMRR > 0.5 ? texture(uSMRRMap, vUv).r : 0.0));
                    lit = vec3(specView);
                }
                else if (uMaterialChannel == 4)
                    lit = uHasESPA > 0.5 ? texture(uESPAMap, vUv).rgb : vec3(0.0);
                else if (uMaterialChannel == 5)
                {
                    vec3 maskView = vec3(0.0);
                    if (uHasSMSPSK > 0.5)
                        maskView = texture(uSMSPSKMap, vUv).rgb;
                    else if (uHasSMRR > 0.5)
                        maskView = texture(uSMRRMap, vUv).rgb;
                    lit = maskView;
                }
                if (uShowSections == 1)
                    lit = mix(lit, uSectionColor.rgb, 0.18);
                if (uHighlightSection == 1)
                    lit = mix(lit, uSectionColor.rgb, 0.40);
                float outputAlpha = uAlphaTest > 0.0 ? diffuseSample.a : 1.0;
                FragColor = vec4(lit, outputAlpha);
                return;
            }

            vec3 normal = normalize(vNormal);
            if (uUseNormalMap == 1)
            {
                vec3 tangent = normalize(vTangent);
                vec3 bitangent = normalize(vBitangent);
                mat3 tbn = mat3(tangent, bitangent, normal);
                vec3 sampledNormal = texture(uNormalMap, vUv).xyz * 2.0 - 1.0;
                normal = normalize(tbn * sampledNormal);
            }
            vec3 lightDir = normalize(-uLightDirection);
            float diffuse = max(dot(normal, lightDir), 0.0);
            vec3 fillDir = normalize(vec3(0.35, 0.4, 0.85));
            float fill = max(dot(normal, fillDir), 0.0);
            vec3 color = uBaseColor;

             if (uUseDiffuseMap == 1)
                color = texture(uDiffuseMap, vUv).rgb;

            if (uShowSections == 1)
                color = mix(color, uSectionColor.rgb, 0.35);

            if (uHighlightSection == 1)
                color = mix(color, uSectionColor.rgb, 0.72);

            if (uWeightMode == 1)
            {
                float weight = uWeightViewMode == 1
                    ? clamp(maxInfluenceWeight(), 0.0, 1.0)
                    : clamp(selectedWeight(), 0.0, 1.0);
                color = mix(vec3(0.05, 0.15, 0.95), vec3(1.0, 0.1, 0.05), weight);
            }

            if (uShadingMode == 2)
                color = vec3(0.78, 0.76, 0.72);

            float specularStrength = uUseSpecularMap == 1 ? dot(texture(uSpecularMap, vUv).rgb, vec3(0.3333)) : 0.15;
            vec3 viewDir = normalize(uCameraPos - vWorldPosition);
            vec3 reflectDir = reflect(-lightDir, normal);
            float specPower = uLightingPreset == 1 ? 36.0 : (uLightingPreset == 2 ? 48.0 : (uLightingPreset == 3 ? 18.0 : 24.0));
            float effectiveSpecPower = uShadingMode == 1 ? max(specPower, 36.0) : (uShadingMode == 4 ? max(specPower, 32.0) : specPower);
            float specular = pow(max(dot(viewDir, reflectDir), 0.0), effectiveSpecPower) * specularStrength * (uShadingMode == 4 ? 1.2 : 1.0);
            float rim = pow(1.0 - max(dot(normal, viewDir), 0.0), uShadingMode == 1 ? 2.2 : (uShadingMode == 4 ? 4.2 : 3.5)) * (uShadingMode == 1 ? 0.18 : (uShadingMode == 4 ? 0.03 : 0.06));

            if (uUseMaskMap == 1)
                color *= texture(uMaskMap, vUv).rgb;

            vec3 emissive = uUseEmissiveMap == 1 ? texture(uEmissiveMap, vUv).rgb : vec3(0.0);
            float ambient = uAmbientLight + (uLightingPreset == 1 ? 0.10 : (uLightingPreset == 2 ? 0.02 : (uLightingPreset == 3 ? 0.16 : 0.04))) + (uShadingMode == 4 ? 0.03 : 0.0);
            float fillStrength = uLightingPreset == 1 ? 0.32 : (uLightingPreset == 2 ? 0.05 : (uLightingPreset == 3 ? 0.28 : 0.14));
            vec3 rimColor = uShadingMode == 1 ? vec3(0.38, 0.44, 0.52) : (uShadingMode == 4 ? vec3(0.12, 0.14, 0.16) : vec3(0.18, 0.20, 0.24));
            if (uShadingMode == 3)
            {
                float facing = clamp(dot(normal, viewDir) * 0.5 + 0.5, 0.0, 1.0);
                vec3 matCap = mix(vec3(0.22, 0.24, 0.28), vec3(0.84, 0.86, 0.88), facing);
                color = matCap;
            }
            else if (uShadingMode == 4)
            {
                if (uUseDiffuseMap == 1)
                    color = texture(uDiffuseMap, vUv).rgb;

                if (uUseMaskMap == 1)
                    color *= mix(vec3(0.92), texture(uMaskMap, vUv).rgb, 0.45);
            }

            if (uMaterialChannel == 1)
                color = uUseDiffuseMap == 1 ? texture(uDiffuseMap, vUv).rgb : uBaseColor;
            else if (uMaterialChannel == 2)
                color = normal * 0.5 + 0.5;
            else if (uMaterialChannel == 3)
            {
                float specView = uUseSpecularMap == 1 ? dot(texture(uSpecularMap, vUv).rgb, vec3(0.3333)) : specularStrength;
                color = vec3(specView);
            }
            else if (uMaterialChannel == 4)
                color = emissive;
            else if (uMaterialChannel == 5)
                color = uUseMaskMap == 1 ? texture(uMaskMap, vUv).rgb : vec3(0.0);

            vec3 lit = color * (ambient + diffuse * (uShadingMode == 4 ? 0.95 : 0.85) + fill * fillStrength) + vec3(specular) + emissive + (rimColor * rim);
            if (uMaterialChannel != 0)
                lit = color;
            FragColor = vec4(lit, 1.0);
        }
        """;

    private const string LineVertexShaderSource = """
        #version 330 core
        layout(location = 0) in vec3 aPosition;

        uniform mat4 uProjection;
        uniform mat4 uView;
        uniform mat4 uModel;

        void main()
        {
            gl_Position = uProjection * uView * uModel * vec4(aPosition, 1.0);
        }
        """;

    private const string LineFragmentShaderSource = """
        #version 330 core
        uniform vec4 uColor;
        out vec4 FragColor;

        void main()
        {
            FragColor = uColor;
        }
        """;
}
