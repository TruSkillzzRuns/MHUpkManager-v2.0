using UpkManager.Models.UpkFile.Engine.Material;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Classes;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;
using UpkManager.Repository;

namespace MHUpkManager.MaterialInspector;

internal sealed class MaterialInspectorService
{
    private readonly UpkFileRepository _repository = new();

    public async Task<List<string>> GetSkeletalMeshExportsAsync(string upkPath)
    {
        var header = await _repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadTablesAsync(null).ConfigureAwait(true);

        return header.ExportTable
            .Where(static export =>
                string.Equals(export.ClassReferenceNameIndex?.Name, nameof(USkeletalMesh), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(export.ClassReferenceNameIndex?.Name, "SkeletalMesh", StringComparison.OrdinalIgnoreCase))
            .Select(static export => export.GetPathName())
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<MaterialInspectorResult> InspectAsync(string upkPath, string skeletalMeshExportPath)
    {
        var header = await _repository.LoadUpkFile(upkPath).ConfigureAwait(true);
        await header.ReadHeaderAsync(null).ConfigureAwait(true);

        UnrealExportTableEntry export = header.ExportTable
            .FirstOrDefault(entry => string.Equals(entry.GetPathName(), skeletalMeshExportPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Could not find SkeletalMesh export '{skeletalMeshExportPath}'.");

        if (export.UnrealObject == null)
            await export.ParseUnrealObject(false, false).ConfigureAwait(true);

        if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh skeletalMesh)
            throw new InvalidOperationException($"Export '{skeletalMeshExportPath}' is not a SkeletalMesh.");

        var sections = new List<MaterialInspectorSectionInfo>();
        if (skeletalMesh.LODModels.Count == 0)
            return new MaterialInspectorResult { UpkPath = upkPath, SkeletalMeshExportPath = skeletalMeshExportPath, Sections = sections };

        var lod = skeletalMesh.LODModels[0];
        for (int i = 0; i < lod.Sections.Count; i++)
        {
            var section = lod.Sections[i];
            FObject materialObject = section.MaterialIndex >= 0 && section.MaterialIndex < skeletalMesh.Materials.Count
                ? skeletalMesh.Materials[section.MaterialIndex]
                : null;

            sections.Add(new MaterialInspectorSectionInfo
            {
                SectionIndex = i,
                MaterialIndex = section.MaterialIndex,
                MaterialPath = materialObject?.GetPathName() ?? "<missing>",
                MaterialType = ResolveMaterialType(materialObject),
                MaterialChain = BuildMaterialChain(materialObject)
            });
        }

        return new MaterialInspectorResult
        {
            UpkPath = upkPath,
            SkeletalMeshExportPath = skeletalMeshExportPath,
            Sections = sections
        };
    }

    private static string ResolveMaterialType(FObject materialObject)
    {
        object material = materialObject?.LoadObject<UObject>();
        return material?.GetType().Name ?? "<unresolved>";
    }

    private static IReadOnlyList<MaterialInspectorMaterialNode> BuildMaterialChain(FObject materialObject)
    {
        List<MaterialInspectorMaterialNode> chain = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        FObject current = materialObject;

        while (current != null)
        {
            string path = current.GetPathName() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(path) && !seen.Add(path))
                break;

            object resolved = current.LoadObject<UObject>();
            if (resolved == null)
            {
                chain.Add(new MaterialInspectorMaterialNode
                {
                    Path = string.IsNullOrWhiteSpace(path) ? "<unresolved>" : path,
                    TypeName = "<unresolved>"
                });
                break;
            }

            if (resolved is UMaterialInstanceConstant instanceConstant)
            {
                UMaterial parentMaterial = instanceConstant.Parent?.LoadObject<UMaterial>();
                chain.Add(new MaterialInspectorMaterialNode
                {
                    Path = path,
                    TypeName = nameof(UMaterialInstanceConstant),
                    BlendMode = parentMaterial?.BlendMode,
                    TwoSided = parentMaterial?.TwoSided,
                    TextureParameters = (instanceConstant.TextureParameterValues ?? []).Select(static parameter => new MaterialInspectorTextureParameter
                    {
                        Name = parameter.ParameterName?.Name ?? "<unnamed>",
                        TexturePath = parameter.ParameterValue?.GetPathName() ?? "<null>"
                    }).OrderBy(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                    ScalarParameters = (instanceConstant.ScalarParameterValues ?? []).Select(static parameter => new MaterialInspectorScalarParameter
                    {
                        Name = parameter.ParameterName?.Name ?? "<unnamed>",
                        Value = parameter.ParameterValue
                    }).OrderBy(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase).ToList(),
                    VectorParameters = (instanceConstant.VectorParameterValues ?? []).Select(static parameter => new MaterialInspectorVectorParameter
                    {
                        Name = parameter.ParameterName?.Name ?? "<unnamed>",
                        Value = parameter.ParameterValue.ToVector3()
                    }).OrderBy(static parameter => parameter.Name, StringComparer.OrdinalIgnoreCase).ToList()
                });
                current = instanceConstant.Parent;
                continue;
            }

            if (resolved is UMaterialInstance instance)
            {
                chain.Add(new MaterialInspectorMaterialNode
                {
                    Path = path,
                    TypeName = nameof(UMaterialInstance)
                });
                current = instance.Parent;
                continue;
            }

            if (resolved is UMaterial material)
            {
                chain.Add(new MaterialInspectorMaterialNode
                {
                    Path = path,
                    TypeName = nameof(UMaterial),
                    BlendMode = material.BlendMode,
                    TwoSided = material.TwoSided
                });
                break;
            }

            chain.Add(new MaterialInspectorMaterialNode
            {
                Path = path,
                TypeName = resolved.GetType().Name
            });
            break;
        }

        return chain;
    }
}
