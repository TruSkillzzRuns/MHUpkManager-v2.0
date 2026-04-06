# MHUpkManager Project Reference

This file keeps the more detailed technical reference that sits behind `PROJECT_STATUS.md`.

Use `PROJECT_STATUS.md` for the fast current summary.
Use this file when you need more implementation and subsystem detail.

## App Structure

### Objects

Purpose:

- browse the current UPK
- inspect exports, imports, properties, and package tables
- hand off selected assets to the tool workspaces

### Mesh

Contains:

- `Preview`
- `Exporter`
- `Importer`

Purpose:

- inspect meshes visually
- export FBX
- import FBX back into UE3 SkeletalMesh data

### Backup

Purpose:

- scan `.bak` files created by import, retarget, and texture workflows
- inspect the original file each backup maps to
- restore selected backups

### Texture

Contains:

- `Texture Preview`
- `Material Inspector`
- `Section Mapping`
- `Material Swap`
- `Character Workflow`

Purpose:

- inspect actual texture/material hookups
- preview and inject textures
- validate mesh section/material mapping
- run guided character texture replacement workflows

### Retarget

Purpose:

- import replacement meshes
- bind them to the original MHO skeleton
- run compatibility fixes
- export or replace the target mesh in the UPK

## Detailed Tool Status

### Mesh Export

Current status:

- working for the validated skeletal test case
- uses the dedicated skeletal export path in `src/Model/SkeletalFbxExporter.cs`
- exports geometry and sidecar textures next to the chosen FBX path

Confirmed:

- Blender now imports the tested skeletal armature correctly
- the earlier armature-collapse issue was traced to matrix layout during `System.Numerics.Matrix4x4 -> Assimp.Matrix4x4` conversion
- transposing the matrix in `SkeletalFbxExporter.ToAssimp(...)` fixed the “bones collapsed on the floor” issue

Important files:

- `src/Model/ModelFormats.cs`
- `src/Model/SkeletalFbxExporter.cs`
- `src/Model/FbxExporter.cs`
- `src/Model/ModelMesh.cs`

Current practical note:

- do not change the current skeletal matrix conversion unless a new exporter regression is reproduced

### Mesh Import

Current status:

- package rewrite path is functional
- imported SkeletalMesh can be rebuilt and injected back into UPK
- imported mesh can be viewed again and loaded in-game in tested scenarios

Confirmed implementation areas:

- `src/MeshImporter`
- `src/Model/Import`
- `src/MeshImporter/Injection`

Known remaining gap:

- section omission / section deletion is not treated as true section removal
- if an imported FBX omits an original section, the importer currently tends to preserve the original section instead of explicitly stripping it

### Mesh Preview

Current status:

- working as the main visual validation tool
- supports both `OpenTK` and `Vortice` backends
- now uses a structured three-panel workspace layout

Purpose:

- compare imported FBX meshes and UE3 meshes
- inspect sections, bones, weights, normals, tangents, and seams

Practical note:

- `Vortice` is currently the better long-term Windows-native viewport direction
- renderer choice is not the main visual blocker; material, texture, and tangent-state issues matter more than backend choice

### Texture Workspace

Current status:

- now a full grouped workspace instead of scattered standalone tabs
- includes guided and low-level texture workflows

Subtools:

- `Texture Preview`
  - low-level texture load, preview, export, and injection
- `Material Inspector`
  - shows what a selected SkeletalMesh is actually using
- `Section Mapping`
  - previews how imported sections map back to original UE3 sections/material indices
- `Material Swap`
  - bridges from a mesh/material parameter to the actual texture target
- `Character Workflow`
  - guided character texture replacement flow with:
    - detect targets
    - preview apply plan
    - apply replacement set
    - manifest output
    - rollback from manifest

Current texture-side status:

- texture injection completes
- manifest/cache update path is working
- backups and rollback support exist
- the remaining problem is runtime compatibility, not basic file writing

Most likely remaining texture-side causes:

- wrong target texture export selection
- wrong material / texture hookup
- wrong final cache / manifest pairing
- shader or material-instance state mismatch
- tangent / normal basis issue affecting lighting

Current visual symptom:

- the replaced character can still appear blue / holographic
- this appears across both preview backends, which makes renderer-specific failure less likely

Current working assumption:

- the remaining issue is more likely material / shader / texture-state related than raw geometry failure

### Retarget

Current status:

- structurally functional
- can bind imported replacement geometry onto the original MHO skeleton
- can export and replace the target mesh in UPK
- in-game load and locomotion are working in tested scenarios
- final deformation quality is still not solved

MHO-only rules:

1. The valid runtime skeleton is the original MHO skeleton from the selected target SkeletalMesh.
2. The tool must not generate a new runtime skeleton.
3. The tool must preserve MHO bone order, hierarchy, and animation expectations.

Current one-click replacement path:

1. Select target MHO UPK and SkeletalMesh
2. Import replacement mesh
3. Scale to original reference mesh
4. Align imported mesh into original body frame
5. Pose conform
6. Transfer weights
7. Bind to original MHO skeleton
8. Apply UE3 compatibility fixes
9. Export or replace

What is confirmed working:

- original MHO skeleton auto-load
- original MHO weighted reference mesh conversion
- one-click bind path for unrigged meshes
- improved scale matching
- manual orientation controls
- UE3 compatibility on processed mesh
- UPK replacement and `.bak` creation
- in-game load
- runtime locomotion

What is not solved:

- reliable low-distortion deformation for arbitrary replacement characters
- fully robust automatic final facing selection for some unrigged meshes
- strong region isolation for shoulders, hips, spine, and limbs during transfer

Current practical retarget expectation:

- the tool can get a replacement mesh into game and animated
- the final result may still deform badly and may require offline cleanup or further tooling

## Confirmed Test Outcomes

### Skeletal FBX Export

- Blender imports the tested skeletal export correctly after the matrix transpose fix

### Reimport / Replacement

- imported skeletal mesh can be written back to UPK
- the package can reopen in MHUpkManager
- the mesh can load in-game

### Hulk Maestro -> Unrigged Thanos

- original MHO skeleton and reference mesh loaded correctly
- imported unrigged source was accepted
- one-click bind, compatibility, and replacement succeeded
- runtime injection succeeded
- locomotion worked
- size matching improved
- remaining blocker: severe deformation quality

### Rogue Savage Land -> Mr Sinister Enemy Mesh

- transfer / bind / compatibility / replacement completed
- UPK replacement succeeded
- runtime load succeeded
- deformation was still poor

Conclusion:

- package writing and animation hookup are no longer the main blocker
- deformation fidelity is the main blocker

## Detailed Priorities

### Retarget deformation quality

Needed:

- stronger region-aware pose conform
- stronger region-aware weight transfer
- better shoulder / clavicle / upper-arm isolation
- better pelvis / thigh / spine isolation
- more stabilization after transfer

### Texture runtime compatibility debugging

Needed:

- tighter validation of material / texture hookups
- stronger inspection of target exports and material expectations
- more direct comparison between good and broken material setups

### Section removal behavior

Needed:

- explicit handling when imported FBX omits an original section
- current preservation behavior should eventually become an intentional choice, not an implicit default

## In-App Tooling Added Recently

- `Material Inspector`
- `Section Mapping`
- `Material Swap`
- `Character Workflow`
- `Backup Manager`

These provide visibility for:

- actual material chains
- section/material mapping behavior
- texture target selection
- replacement plan / apply / rollback
- `.bak` recovery

## Important Code Areas

### Mesh Export

- `src/Model/SkeletalFbxExporter.cs`
- `src/Model/FbxExporter.cs`
- `src/Model/ModelFormats.cs`

### Mesh Import / Injection

- `src/MeshImporter`
- `src/Model/Import`
- `src/MeshImporter/Injection`

### Texture / Material

- `src/TexturePreview`
- `src/TextureWorkspace`
- `src/MaterialInspector`
- `src/SectionMapping`
- `src/TextureManager`

### Retarget

- `src/Retargeting`
- `src/MainForm.cs`

### Backups

- `src/BackupManager`

## Build

Validation command:

```powershell
dotnet build 'C:\Users\TruSkillzzRuns\mhupkmanager-link\src\MHUpkManager.csproj' -c Release
```

Expected state:

- should build cleanly when `MHUpkManager.exe` is not already running

## Archive Note

Older markdown files under `archive/docs` remain historical references only.
