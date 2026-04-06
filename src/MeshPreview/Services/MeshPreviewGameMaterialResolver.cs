using MHUpkManager.TexturePreview;
using UpkManager.Models.UpkFile.Classes;
using UpkManager.Models.UpkFile.Engine.Material;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Engine.Texture;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Models.UpkFile.Tables;

namespace MHUpkManager.MeshPreview;

internal sealed class MeshPreviewGameMaterialResolver
{
    private readonly UpkTextureLoader _upkTextureLoader = new();
    private readonly TextureToMaterialConverter _slotConverter = new();

    public async Task ApplyToSectionsAsync(string upkPath, USkeletalMesh skeletalMesh, MeshPreviewMesh previewMesh, Action<string> log = null)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);
        ArgumentNullException.ThrowIfNull(previewMesh);

        foreach (MeshPreviewSection section in previewMesh.Sections)
            section.GameMaterial = null;

        if (skeletalMesh.LODModels.Count == 0)
            return;

        FStaticLODModel lod = skeletalMesh.LODModels[0];
        log?.Invoke($"GameApprox: SkeletalMesh material slots = {skeletalMesh.Materials.Count}.");

        for (int sectionIndex = 0; sectionIndex < lod.Sections.Count && sectionIndex < previewMesh.Sections.Count; sectionIndex++)
        {
            FSkelMeshSection sourceSection = lod.Sections[sectionIndex];
            FObject materialObject = sourceSection.MaterialIndex >= 0 && sourceSection.MaterialIndex < skeletalMesh.Materials.Count
                ? skeletalMesh.Materials[sourceSection.MaterialIndex]
                : null;
            string materialPath = materialObject?.GetPathName() ?? "<missing>";
            string tableEntryType = materialObject?.TableEntry?.GetType().Name ?? "<null>";
            string className = ResolveClassName(materialObject?.TableEntry);
            object resolvedMaterial = materialObject?.LoadObject<UObject>();
            log?.Invoke($"GameApprox: section {sectionIndex} material index {sourceSection.MaterialIndex}, path {materialPath}, tableEntry {tableEntryType}, class {className}, resolved type {(resolvedMaterial?.GetType().Name ?? "<null>")}.");

            try
            {
                MeshPreviewGameMaterial material = await BuildSectionMaterialAsync(sectionIndex, materialObject, log).ConfigureAwait(true);
                previewMesh.Sections[sectionIndex].GameMaterial = material;
            }
            catch (Exception ex)
            {
                log?.Invoke($"GameApprox: skipped section {sectionIndex} game material build: {ex.Message}");
            }
        }
    }

    public async Task<MeshPreviewGameMaterialResult> BuildMaterialSetAsync(string upkPath, USkeletalMesh skeletalMesh, Action<string> log = null)
    {
        ArgumentNullException.ThrowIfNull(skeletalMesh);

        Dictionary<TexturePreviewMaterialSlot, ResolvedTextureTarget> targets = ResolveTextureTargets(skeletalMesh, log);
        if (targets.Count == 0)
            log?.Invoke("GameApprox: no previewable textures were resolved from the UE3 material chain or material-resource fallback.");
        else
            log?.Invoke($"GameApprox: resolved {targets.Count} candidate texture slot(s) from the UE3 material chain.");
        if (targets.Count == 0)
            return MeshPreviewGameMaterialResult.Empty;

        TexturePreviewMaterialSet materialSet = new() { Enabled = true };
        List<string> resolvedSources = [];

        foreach ((TexturePreviewMaterialSlot slot, ResolvedTextureTarget target) in targets.OrderBy(static entry => entry.Key))
        {
            if (target.TextureObject == null)
                continue;

            try
            {
                log?.Invoke($"GameApprox: loading {slot} from {target.TexturePath} (Section {target.SectionIndex}, Material {target.MaterialPath}, Param {target.ParameterName}).");
                TexturePreviewTexture texture = await _upkTextureLoader.LoadFromObjectAsync(target.TextureObject, slot, log).ConfigureAwait(true);
                texture.Slot = slot;
                materialSet.SetTexture(slot, texture);
                resolvedSources.Add($"{slot}: {target.TexturePath} (Section {target.SectionIndex})");
            }
            catch (Exception ex)
            {
                log?.Invoke($"GameApprox skipped {slot} from {target.TexturePath}: {ex.Message}");
            }
        }

        if (!materialSet.Textures.Any())
            return MeshPreviewGameMaterialResult.Empty;

        return new MeshPreviewGameMaterialResult
        {
            MaterialSet = materialSet,
            Summary = string.Join(", ", resolvedSources)
        };
    }

    private async Task<MeshPreviewGameMaterial> BuildSectionMaterialAsync(int sectionIndex, FObject materialObject, Action<string> log)
    {
        if (materialObject?.LoadObject<UMaterialInstanceConstant>() is not UMaterialInstanceConstant instanceConstant)
            return null;

        MeshPreviewGameMaterial material = new()
        {
            Enabled = true,
            MaterialPath = materialObject.GetPathName()
        };
        ApplyMaterialParameters(instanceConstant, material);
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
        FObject current = materialObject;
        while (current != null)
        {
            string currentPath = current.GetPathName() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentPath) && !seenPaths.Add(currentPath))
                break;

            UObject resolved = current.LoadObject<UObject>();
            switch (resolved)
            {
                case UMaterialInstanceConstant currentInstance:
                    await TryAssignTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.Diffuse, currentInstance.GetTextureParameterValue("Diffuse"), "Diffuse", log).ConfigureAwait(true);
                    await TryAssignTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.Normal, currentInstance.GetTextureParameterValue("Norm"), "Norm", log).ConfigureAwait(true);
                    await TryAssignTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.Smspsk, currentInstance.GetTextureParameterValue("specmult_specpow_skinmask"), "specmult_specpow_skinmask", log).ConfigureAwait(true);
                    await TryAssignTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.Espa, currentInstance.GetTextureParameterValue("emissivespecpow"), "emissivespecpow", log).ConfigureAwait(true);
                    await TryAssignTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.Smrr, currentInstance.GetTextureParameterValue("specmultrimmaskrefl"), "specmultrimmaskrefl", log).ConfigureAwait(true);
                    await TryAssignTextureAsync(sectionIndex, material, MeshPreviewGameTextureSlot.SpecColor, currentInstance.GetTextureParameterValue("SpecColor"), "SpecColor", log).ConfigureAwait(true);
                    current = currentInstance.Parent;
                    continue;

                case UMaterialInstance currentInstanceBase:
                    current = currentInstanceBase.Parent;
                    continue;

                case UMaterial parentMaterial:
                    await TryAssignMaterialResourceTexturesAsync(sectionIndex, material, currentPath, parentMaterial, log).ConfigureAwait(true);
                    current = null;
                    break;

                default:
                    current = null;
                    break;
            }
        }

        if (!material.Textures.Any())
            return null;

        log?.Invoke(
            $"GameApprox: section {sectionIndex} built material BlendMode={material.BlendMode}, TwoSided={material.TwoSided}, " +
            $"Textures=[{string.Join(", ", material.Textures.Select(static kvp => $"{kvp.Key}={kvp.Value?.ExportPath ?? "<null>"}"))}]");

        return material;
    }

    private async Task TryAssignTextureAsync(
        int sectionIndex,
        MeshPreviewGameMaterial material,
        MeshPreviewGameTextureSlot slot,
        FObject textureObject,
        string parameterName,
        Action<string> log)
    {
        if (material.HasTexture(slot) || textureObject == null)
            return;

        string texturePath = textureObject.GetPathName() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(texturePath))
            return;

        try
        {
            log?.Invoke($"GameApprox: loading {slot} from {texturePath} (Section {sectionIndex}, Material {material.MaterialPath}, Param {parameterName}).");
            TexturePreviewTexture texture = await _upkTextureLoader.LoadFromObjectAsync(textureObject, TexturePreviewMaterialSlot.Diffuse, log).ConfigureAwait(true);
            material.SetTexture(slot, texture);
        }
        catch (Exception ex)
        {
            log?.Invoke($"GameApprox skipped {slot} from {texturePath}: {ex.Message}");
        }
    }

    private async Task TryAssignMaterialResourceTexturesAsync(
        int sectionIndex,
        MeshPreviewGameMaterial material,
        string materialPath,
        UMaterial parentMaterial,
        Action<string> log)
    {
        FMaterialResource resource = parentMaterial.MaterialResource?.FirstOrDefault(static value => value != null);
        if (resource?.UniformExpressionTextures == null)
            return;

        foreach (FObject textureObject in resource.UniformExpressionTextures)
        {
            if (!TryResolveGameTextureSlot(textureObject, out MeshPreviewGameTextureSlot slot))
                continue;

            await TryAssignTextureAsync(
                sectionIndex,
                material,
                slot,
                textureObject,
                $"UniformExpressionTexture ({materialPath})",
                log).ConfigureAwait(true);
        }
    }

    private static void ApplyMaterialParameters(UMaterialInstanceConstant source, MeshPreviewGameMaterial target)
    {
        target.LambertDiffusePower = source.GetScalarParameterValue("lambertdiffusepower") ?? target.LambertDiffusePower;
        target.LightingAmbient = source.GetScalarParameterValue("lightingambient") ?? target.LightingAmbient;
        target.PhongDiffusePower = source.GetScalarParameterValue("phongdiffusepower") ?? target.PhongDiffusePower;
        target.ShadowAmbientMult = source.GetScalarParameterValue("shadowambientmult") ?? target.ShadowAmbientMult;
        target.NormalStrength = source.GetScalarParameterValue("normalstrength") ?? target.NormalStrength;
        target.ReflectionMult = source.GetScalarParameterValue("reflectionmult") ?? target.ReflectionMult;
        target.RimColorMult = source.GetScalarParameterValue("rimcolormult") ?? target.RimColorMult;
        target.RimFalloff = source.GetScalarParameterValue("rimfalloff") ?? target.RimFalloff;
        target.ScreenLightAmount = source.GetScalarParameterValue("screenlight_amount") ?? target.ScreenLightAmount;
        target.ScreenLightMult = source.GetScalarParameterValue("screenlight_mult") ?? target.ScreenLightMult;
        target.ScreenLightPower = source.GetScalarParameterValue("screenlight_power") ?? target.ScreenLightPower;
        target.SpecMult = source.GetScalarParameterValue("specmult") ?? target.SpecMult;
        target.SpecMultLq = source.GetScalarParameterValue("specmult_lq") ?? target.SpecMultLq;
        target.SpecularPower = source.GetScalarParameterValue("specularpower") ?? target.SpecularPower;
        target.SpecularPowerMask = source.GetScalarParameterValue("specularpowermask") ?? target.SpecularPowerMask;

        target.LambertAmbient = source.GetVectorParameterValue("lambertambient") ?? target.LambertAmbient;
        target.ShadowAmbientColor = source.GetVectorParameterValue("shadowambientcolor") ?? target.ShadowAmbientColor;
        target.FillLightColor = source.GetVectorParameterValue("filllightcolor") ?? target.FillLightColor;
        target.SpecularColor = source.GetVectorParameterValue("specularcolor") ?? target.SpecularColor;
        target.DiffuseColor = source.GetVectorParameterValue("diffusecolor") ?? target.DiffuseColor;
        target.SubsurfaceInscatteringColor = source.GetVectorParameterValue("subsurfaceinscatteringcolor") ?? target.SubsurfaceInscatteringColor;
        target.SubsurfaceAbsorptionColor = source.GetVectorParameterValue("subsurfaceabsorptioncolor") ?? target.SubsurfaceAbsorptionColor;

        UMaterial parentMaterial = source.Parent?.LoadObject<UMaterial>();
        if (parentMaterial != null)
        {
            target.TwoSided = parentMaterial.TwoSided;
            target.BlendMode = parentMaterial.BlendMode;
        }

        if (source.bHasStaticPermutationResource && source.StaticPermutationResources.Length > 0)
        {
            FMaterialResource resource = source.StaticPermutationResources[0];
            if (resource != null && resource.bIsMaskedOverrideValue && resource.BlendModeOverrideValue != EBlendMode.BLEND_Opaque)
                target.BlendMode = resource.BlendModeOverrideValue;
        }
    }

    private static bool TryResolveGameTextureSlot(FObject textureObject, out MeshPreviewGameTextureSlot slot)
    {
        string texturePath = textureObject?.GetPathName() ?? string.Empty;
        string path = texturePath.ToLowerInvariant();

        if (path.Contains("specmult_specpow_skinmask") || path.Contains("smspsk"))
        {
            slot = MeshPreviewGameTextureSlot.Smspsk;
            return true;
        }

        if (path.Contains("emissivespecpow") || path.Contains("espa"))
        {
            slot = MeshPreviewGameTextureSlot.Espa;
            return true;
        }

        if (path.Contains("specmultrimmaskrefl") || path.Contains("smrr"))
        {
            slot = MeshPreviewGameTextureSlot.Smrr;
            return true;
        }

        if (path.Contains("speccolor"))
        {
            slot = MeshPreviewGameTextureSlot.SpecColor;
            return true;
        }

        UTexture2D texture = textureObject?.LoadObject<UTexture2D>();
        if (texture != null)
        {
            if (texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_WorldNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_CharacterNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_WeaponNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_VehicleNormalMap)
            {
                slot = MeshPreviewGameTextureSlot.Normal;
                return true;
            }

            if (texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_CharacterSpecular)
            {
                slot = MeshPreviewGameTextureSlot.SpecColor;
                return true;
            }
        }

        if (path.Contains("_norm") || path.Contains("normal"))
        {
            slot = MeshPreviewGameTextureSlot.Normal;
            return true;
        }

        if (path.Contains("_diff") || path.Contains("diffuse") || path.Contains("albedo"))
        {
            slot = MeshPreviewGameTextureSlot.Diffuse;
            return true;
        }

        if (path.Contains("_spec"))
        {
            slot = MeshPreviewGameTextureSlot.SpecColor;
            return true;
        }

        slot = default;
        return false;
    }

    private Dictionary<TexturePreviewMaterialSlot, ResolvedTextureTarget> ResolveTextureTargets(USkeletalMesh skeletalMesh, Action<string> log)
    {
        Dictionary<TexturePreviewMaterialSlot, ResolvedTextureTarget> resolved = [];
        if (skeletalMesh.LODModels.Count == 0)
            return resolved;

        log?.Invoke($"GameApprox: SkeletalMesh material slots = {skeletalMesh.Materials.Count}.");
        FStaticLODModel lod = skeletalMesh.LODModels[0];
        for (int sectionIndex = 0; sectionIndex < lod.Sections.Count; sectionIndex++)
        {
            try
            {
                FSkelMeshSection section = lod.Sections[sectionIndex];
                FObject materialObject = section.MaterialIndex >= 0 && section.MaterialIndex < skeletalMesh.Materials.Count
                    ? skeletalMesh.Materials[section.MaterialIndex]
                    : null;
                string materialPath = materialObject?.GetPathName() ?? "<missing>";
                string tableEntryType = materialObject?.TableEntry?.GetType().Name ?? "<null>";
                string className = ResolveClassName(materialObject?.TableEntry);
                object resolvedMaterial = materialObject?.LoadObject<UObject>();
                log?.Invoke($"GameApprox: section {sectionIndex} material index {section.MaterialIndex}, path {materialPath}, tableEntry {tableEntryType}, class {className}, resolved type {(resolvedMaterial?.GetType().Name ?? "<null>")}.");

                foreach ((TexturePreviewMaterialSlot slot, ResolvedTextureTarget target) in ResolveSectionTargets(sectionIndex, materialObject))
                {
                    if (!resolved.ContainsKey(slot))
                        resolved[slot] = target;
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"GameApprox: skipped section {sectionIndex} during material resolution: {ex.Message}");
            }
        }

        return resolved;
    }

    private IEnumerable<KeyValuePair<TexturePreviewMaterialSlot, ResolvedTextureTarget>> ResolveSectionTargets(int sectionIndex, FObject materialObject)
    {
        Dictionary<TexturePreviewMaterialSlot, ResolvedTextureTarget> targets = [];
        HashSet<string> seenPaths = new(StringComparer.OrdinalIgnoreCase);
        FObject current = materialObject;

        while (current != null)
        {
            string currentPath = current.GetPathName() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(currentPath) && !seenPaths.Add(currentPath))
                break;

            object resolvedObject = current.LoadObject<UObject>();
            if (resolvedObject == null)
                break;
            if (resolvedObject is UMaterialInstanceConstant instanceConstant)
            {
                foreach ((TexturePreviewMaterialSlot slot, ResolvedTextureTarget target) in ResolveKnownInstanceParameters(sectionIndex, currentPath, instanceConstant))
                {
                    if (!targets.ContainsKey(slot))
                        targets[slot] = target;
                }

                foreach (FTextureParameterValue parameter in instanceConstant.TextureParameterValues ?? [])
                {
                    FObject textureObject = parameter.ParameterValue;
                    string texturePath = textureObject?.GetPathName() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(texturePath))
                        continue;

                    string parameterName = parameter.ParameterName?.Name ?? string.Empty;
                    TexturePreviewMaterialSlot slot = _slotConverter.ResolveSlot($"{parameterName} {texturePath}", TexturePreviewMaterialSlot.Diffuse);
                    if (targets.ContainsKey(slot))
                        continue;

                    targets[slot] = new ResolvedTextureTarget
                    {
                        SectionIndex = sectionIndex,
                        MaterialPath = currentPath,
                        ParameterName = parameterName,
                        TextureObject = textureObject,
                        TexturePath = texturePath
                    };
                }

                current = instanceConstant.Parent;
                continue;
            }

            if (resolvedObject is UMaterialInstance instance)
            {
                current = instance.Parent;
                continue;
            }

            if (resolvedObject is UMaterial material)
            {
                foreach ((TexturePreviewMaterialSlot slot, ResolvedTextureTarget target) in ResolveMaterialResourceTextures(sectionIndex, currentPath, material))
                {
                    if (!targets.ContainsKey(slot))
                        targets[slot] = target;
                }

                break;
            }

            break;
        }

        return targets;
    }

    private IEnumerable<KeyValuePair<TexturePreviewMaterialSlot, ResolvedTextureTarget>> ResolveKnownInstanceParameters(
        int sectionIndex,
        string materialPath,
        UMaterialInstanceConstant material)
    {
        (string ParameterName, TexturePreviewMaterialSlot Slot)[] knownParameters =
        [
            ("Diffuse", TexturePreviewMaterialSlot.Diffuse),
            ("Norm", TexturePreviewMaterialSlot.Normal),
            ("SpecColor", TexturePreviewMaterialSlot.Specular),
            ("specmult_specpow_skinmask", TexturePreviewMaterialSlot.Mask),
            ("emissivespecpow", TexturePreviewMaterialSlot.Emissive),
            ("specmultrimmaskrefl", TexturePreviewMaterialSlot.Mask)
        ];

        foreach ((string parameterName, TexturePreviewMaterialSlot slot) in knownParameters)
        {
            FObject textureObject = material.GetTextureParameterValue(parameterName);
            string texturePath = textureObject?.GetPathName() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(texturePath))
                continue;

            yield return new KeyValuePair<TexturePreviewMaterialSlot, ResolvedTextureTarget>(
                slot,
                new ResolvedTextureTarget
                {
                    SectionIndex = sectionIndex,
                    MaterialPath = materialPath,
                    ParameterName = parameterName,
                    TextureObject = textureObject,
                    TexturePath = texturePath
                });
        }
    }

    private IEnumerable<KeyValuePair<TexturePreviewMaterialSlot, ResolvedTextureTarget>> ResolveMaterialResourceTextures(
        int sectionIndex,
        string materialPath,
        UMaterial material)
    {
        FMaterialResource resource = material.MaterialResource?.FirstOrDefault(static value => value != null);
        if (resource?.UniformExpressionTextures == null || resource.UniformExpressionTextures.Count == 0)
            yield break;

        int diffuseFallbackIndex = 0;
        foreach (FObject textureObject in resource.UniformExpressionTextures)
        {
            string texturePath = textureObject?.GetPathName() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(texturePath))
                continue;

            TexturePreviewMaterialSlot slot = ResolveSlotFromTextureObject(textureObject, diffuseFallbackIndex == 0);
            if (slot == TexturePreviewMaterialSlot.Diffuse)
                diffuseFallbackIndex++;

            yield return new KeyValuePair<TexturePreviewMaterialSlot, ResolvedTextureTarget>(
                slot,
                new ResolvedTextureTarget
                {
                    SectionIndex = sectionIndex,
                    MaterialPath = materialPath,
                    ParameterName = "UniformExpressionTexture",
                    TextureObject = textureObject,
                    TexturePath = texturePath
                });
        }
    }

    private TexturePreviewMaterialSlot ResolveSlotFromTextureObject(FObject textureObject, bool preferDiffuseFallback)
    {
        string texturePath = textureObject?.GetPathName() ?? string.Empty;
        UTexture2D texture = textureObject?.LoadObject<UTexture2D>();

        if (texture != null)
        {
            if (texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_WorldNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_CharacterNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_WeaponNormalMap
                or UTexture.TextureGroup.TEXTUREGROUP_VehicleNormalMap)
            {
                return TexturePreviewMaterialSlot.Normal;
            }

            if (texture.LODGroup is UTexture.TextureGroup.TEXTUREGROUP_CharacterSpecular)
                return TexturePreviewMaterialSlot.Specular;
        }

        TexturePreviewMaterialSlot slot = _slotConverter.ResolveSlot(texturePath, preferDiffuseFallback ? TexturePreviewMaterialSlot.Diffuse : TexturePreviewMaterialSlot.Mask);
        return slot;
    }

    private static string ResolveClassName(UnrealObjectTableEntryBase entry)
    {
        return entry switch
        {
            UnrealExportTableEntry export => export.ClassReferenceNameIndex?.Name ?? "<unknown>",
            UnrealImportTableEntry import => import.ClassNameIndex?.Name ?? "<unknown>",
            _ => "<unknown>"
        };
    }

    private sealed class ResolvedTextureTarget
    {
        public int SectionIndex { get; init; }
        public string MaterialPath { get; init; } = string.Empty;
        public string ParameterName { get; init; } = string.Empty;
        public FObject TextureObject { get; init; }
        public string TexturePath { get; init; } = string.Empty;
    }
}

internal sealed class MeshPreviewGameMaterialResult
{
    public static MeshPreviewGameMaterialResult Empty { get; } = new()
    {
        MaterialSet = new TexturePreviewMaterialSet(),
        Summary = string.Empty
    };

    public required TexturePreviewMaterialSet MaterialSet { get; init; }
    public string Summary { get; init; } = string.Empty;
}
