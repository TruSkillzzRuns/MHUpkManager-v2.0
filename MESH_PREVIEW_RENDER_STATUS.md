# Mesh Preview Render Status

This document tracks the actual findings from the `Mesh > Preview` render work so future fixes start from the real cause instead of repeating dead ends.

## Actual Root Cause

The main “holograph / inside-out / normals inverted / backfacing” issue was not primarily a texture-resolution problem and not primarily a shader-lighting problem.

The root cause was:

- preview triangle winding was reversed for the UE3 preview mesh path
- backface culling then removed the real exterior faces
- the renderer showed the back-facing side of the mesh, which looked like:
  - inverted normals
  - hollow / translucent body
  - textures showing from inside the mesh
  - holographic look

This was visible in the user screenshot where:

- the left viewport front view was showing the back-facing side of the model
- the right viewport back view was showing the front-facing side

That symptom should be treated as a winding/culling problem first.

## What Fixed It

### UE3 Preview Mesh

`src/MeshPreview/Converters/UE3ToPreviewMeshConverter.cs`

- triangle winding is now reversed when building preview indices
- this makes the preview mesh line up with the viewport backface-culling rules
- result: the mesh renders as a solid exterior model instead of an inside-out shell

### Retarget Pose Preview

`src/MeshPreview/Converters/RetargetToPreviewMeshConverter.cs`

- the same winding fix was applied to retarget preview mesh indices
- this keeps retargeted meshes from showing the same holographic / inside-out problem in preview

### Preview Load Behavior

`src/MeshPreview/UI/MeshPreviewUI.cs`

- loading a UE3 mesh now switches preview to `Ue3Only`
- this avoids stale `Overlay` mode with an old FBX still drawn on top of the UE3 mesh
- this was not the main root cause, but it can create a misleading “double-shell” look

## What Works Now

- UE3 mesh preview texture/material resolution is working
- `TextureFileCacheManifest.bin` auto-load is working
- referenced material and texture exports resolve correctly
- `GameApprox` section material logging is working
- UE3 preview winding now matches backface culling correctly
- the same winding correction is now applied to retarget pose preview meshes
- build is currently clean with:
  - `0 warnings`
  - `0 errors`

## What Was Misleading

The following symptoms looked like shader/material problems but were downstream of wrong winding:

- “holograph” look
- “normals are inverted”
- “textures are showing from inside the body”
- “backface culling looks wrong”

Those symptoms can appear even when:

- the correct textures are loaded
- the material chain resolves correctly
- the shader branch is mostly fine

## What To Do Next Time

If a mesh preview looks hollow, inside-out, or like the interior is visible:

1. treat it as a geometry/culling issue first
2. check triangle winding
3. check front-face / backface cull assumptions
4. only then investigate normal maps or shader lighting

If a user explicitly says:

- “backface culling”
- “inside-out”
- “front is showing the back”
- “normals are inverted”

then prioritize:

- winding
- cull state
- face orientation

before:

- shader constants
- emissive/specular tuning
- texture slot remapping

## What Not To Do

Do not start by repeatedly tweaking:

- normal-map green channel
- emissive/specular fallback
- generic lighting presets
- material-channel display logic

when the visible symptom already points to:

- wrong face orientation
- wrong winding
- wrong culling behavior

Do not assume “holograph” automatically means:

- translucent blend mode
- bad material graph
- wrong texture binding

## Current Test Case

Primary test mesh:

- UPK: `E:\SteamLibrary\steamapps\common\Marvel Heroes\UnrealEngine3\MarvelGame\CookedPCConsole\UC__MarvelPlayer_Thing_FearItself_SF.upk`
- SkeletalMesh export: `thing_fearitself.thing_fearitself`

Resolved section materials for this test:

- section 0: `thing_fearitself.thing_fearitself_doublemasked_mat`
- section 1: `thing_fearitself.thing_fearitself_lavamat`

Resolved texture examples:

- `thing_fearitself.thing_fearitself_cloak_diff`
- `thing_fearitself.thing_fearitself_cloak_norm`
- `thing_fearitself.thing_fearitself_cloak_emissive`
- `thing_fearitself.thing_fearitself_diff`
- `thing_fearitself.thing_fearitself_norm`
- `thing_fearitself.thing_fearitself_spec`

## Important Supporting Fixes

### Object / Texture Loading

- `UpkManager/Models/UpkFile/Tables/FObject.cs`
  - referenced exports now call `ReadExportObjectAsync(...)` before parse
- `src/TexturePreview/Loaders/UpkTextureLoader.cs`
  - texture exports now call `ReadExportObjectAsync(...)` before parse
  - preview can auto-use `TextureFileCacheManifest.bin`

### GameApprox Material Resolution

- `src/MeshPreview/Services/MeshPreviewGameMaterialResolver.cs`
  - per-section `GameMaterial` building exists
  - parent material traversal exists
  - known MHO texture parameter names are resolved
  - section logging exists

## Key Files

- `src/MeshPreview/UI/MeshPreviewUI.cs`
- `src/MeshPreview/Converters/UE3ToPreviewMeshConverter.cs`
- `src/MeshPreview/Converters/RetargetToPreviewMeshConverter.cs`
- `src/MeshPreview/Services/MeshPreviewGameMaterialResolver.cs`
- `src/MeshPreview/Rendering/MeshPreviewRenderer.cs`
- `src/MeshPreview/Controls/VorticeMeshPreviewViewport.cs`
- `src/TexturePreview/Loaders/UpkTextureLoader.cs`
- `UpkManager/Models/UpkFile/Tables/FObject.cs`

## Build Command

```powershell
dotnet build 'C:\Users\TruSkillzzRuns\mhupkmanager-link\src\MHUpkManager.csproj' -c Release
```
