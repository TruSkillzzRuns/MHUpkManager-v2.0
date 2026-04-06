# FBX Export Status

## Scope

This file records the current FBX export state in `MHUpkManager` as of `2026-04-04`.

It focuses on:

- normal model export
- skeletal FBX export
- Blender validation results
- what was changed
- what still does not work

## Current Export Paths

The app currently has two FBX-related exporters:

- `src/Model/FbxExporter.cs`
  - custom authored FBX writer
  - mainly appropriate for static / generic model export
- `src/Model/SkeletalFbxExporter.cs`
  - dedicated skeletal exporter using Assimp scene construction

The export routing is controlled in:

- `src/Model/ModelFormats.cs`

Current routing:

- skeletal models: `SkeletalFbxExporter.Export(...)`
- non-skeletal models: `FbxExporter.Export(...)`

## What Was Changed During This Session

### Texture Sidecar Export Path Fix

Problem:

- diffuse and normal textures were exporting into:
  - `src\bin\Release\net8.0-windows`
- instead of next to the chosen FBX output path

Fix:

- updated `src/Model/SkeletalFbxExporter.cs`
- texture sidecars now use the chosen export directory

Status:

- fixed

### Custom FBX Writer Skeleton Metadata Work

Several changes were made in `src/Model/FbxExporter.cs`:

- added explicit `NodeAttribute` skeleton objects
- connected skeleton attributes to bone `Model` entries
- preserved bind-pose and cluster matrix output
- removed the ASCII -> Assimp -> binary conversion round-trip

Reason:

- the binary conversion path was likely stripping or flattening authored skeleton semantics

Status:

- these changes did not solve the Blender armature issue for the tested skeletal export case

### Export Routing Rollback

At one point skeletal exports were forced through the custom `FbxExporter`.

That route was rolled back.

Current behavior:

- skeletal exports again use `SkeletalFbxExporter`
- static exports continue using `FbxExporter`

Reason:

- this is the closest visible path in the current codebase to the earlier known-good skeletal export behavior

## What Has Been Tried

The following were tried while debugging the Blender armature problem:

1. Use the custom `FbxExporter` for skeletal exports.
2. Add explicit skeleton node attributes to the custom writer.
3. Output real bind-pose / cluster matrices in the custom writer.
4. Stop converting authored ASCII FBX through Assimp into binary.
5. Restore skeletal export routing back to `SkeletalFbxExporter`.

## Current Blender Validation Result

This is now resolved for the tested skeletal export path.

Validated outcome:

- exported skeletal mesh imports into Blender successfully
- armature imports correctly
- bones no longer collapse on the floor
- bone display now matches the expected skeleton layout again

## Root Cause And Fix

Root cause:

- `src/Model/SkeletalFbxExporter.cs`
- the `System.Numerics.Matrix4x4 -> Assimp.Matrix4x4` conversion used by the skeletal exporter was effectively in the wrong layout for Assimp / Blender import expectations
- Blender was importing the hierarchy, but all bone heads landed at the origin with tiny default tails

Fix:

- transpose the matrix before creating `Assimp.Matrix4x4` in `SkeletalFbxExporter.ToAssimp(...)`

Why this fixed it:

- bone translations were previously landing in the wrong matrix slots
- after transposition, Blender received the expected bone transforms and the armature imported correctly

## Important Code Locations

- `src/Model/ModelFormats.cs`
  - export routing
- `src/Model/SkeletalFbxExporter.cs`
  - current skeletal FBX export path
- `src/Model/FbxExporter.cs`
  - custom FBX writer
- `src/Model/ModelMesh.cs`
  - source bone extraction from `USkeletalMesh.RefSkeleton`
- `UpkManager/Models/UpkFile/Engine/Mesh/USkeletalMesh.cs`
  - `VJointPos.ToMatrix()`

## What Is Confirmed

- FBX export does run successfully
- mesh geometry exports
- diffuse / normal sidecar textures can export next to the chosen FBX path
- Blender now imports the tested skeletal armature correctly after the matrix transpose fix

## Final Note

Do not change the current skeletal FBX matrix conversion again unless a new regression is reproduced.
