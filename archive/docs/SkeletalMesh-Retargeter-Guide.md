# SkeletalMesh Retargeter Guide

## What It Does

The `SkeletalMesh Retargeter` tab is used to:

- import an enemy or source mesh from `.psk` or `.fbx`
- import a player skeleton from `.fbx`
- auto-map source bones onto the player skeleton
- transfer skin weights onto the mapped player bones
- apply UE3 compatibility cleanup
- export the retargeted result as FBX 2013
- replace the target `SkeletalMesh` inside a `.upk`

This workflow is designed to fit MHUpkManager's existing UE3 SkeletalMesh import and package write path.

## Where To Find It

Open MHUpkManager and click the tab named:

- `SkeletalMesh Retargeter`

When this tab is active, the app's normal right-side object/property panel is hidden so the retargeter has more room.

## Basic Workflow

1. Load the target `.upk`
2. Select the target `SkeletalMesh` export
3. Select the target `LOD`
4. Import the source mesh
5. Import the player skeleton
6. Optionally import an `AnimSet`
7. Optionally import textures
8. Run auto bone mapping
9. Run weight transfer
10. Run UE3 compatibility fixes
11. Export FBX 2013 if you want to inspect the result
12. Replace the mesh in the `.upk`

## Step By Step

### 1. Select The UPK

Use:

- `Browse UPK`

Pick the `.upk` that contains the `SkeletalMesh` you want to replace.

After that, the retargeter will scan the package and list available `SkeletalMesh` exports.

### 2. Select The Target SkeletalMesh

Use:

- `SkeletalMesh Export`

Choose the exact export path you want to overwrite.

Example:

- `somepackage.some_character_mesh`

### 3. Select The LOD

Use:

- `LOD Selection`

Normally start with:

- `LOD 0`

That is the main runtime mesh and the safest place to test first.

### 4. Import Mesh (.psk/.fbx)

Use:

- `Import Mesh (.psk/.fbx)`

Supported inputs:

- `.psk`
- `.fbx`

What this step reads:

- vertices
- normals
- tangents
- bitangents
- UVs
- vertex colors when available
- source bone weights
- source bone hierarchy when available

Notes:

- `.fbx` is the better choice when the mesh already has a usable armature and skin weights
- `.psk` is useful for UE-style skeletal exports

### 5. Import Player Skeleton (.fbx)

Use:

- `Import Player Skeleton (.fbx)`

This should be the destination skeleton you want the mesh to follow in UE3.

What this step reads:

- bone names
- parent/child hierarchy
- local transforms
- global transforms

Important:

- use the actual player skeleton you want the final retargeted mesh to bind to
- if the wrong skeleton is used, mapping and weights will still complete, but the runtime result may deform incorrectly

### 6. Import AnimSet

Use:

- `Import AnimSet`

This loads an `AnimSet` export from the currently selected `.upk`.

Current purpose:

- keeps the selected `AnimSet` associated with the retargeted working mesh inside the tool
- helps keep the retargeting workflow aligned with UE3 animation usage

Important:

- this does not rewrite animation data
- it imports the `AnimSet` reference/work context for the retargeting workflow

### 7. Import Textures

Use:

- `Import Textures`

Supported inputs:

- `PNG`
- `JPG`
- `DDS`
- `TGA`

Current purpose:

- loads texture references into the retargeting session
- keeps the session assets grouped together while preparing the mesh

Important:

- this step does not by itself inject textures into the package
- it is mainly for retargeting session organization and downstream export context

### 8. Auto Bone Mapping Panel

Use:

- `Auto Bone Mapping`

This builds a source-to-target mapping:

- `EnemyBone -> PlayerBone`

Mapping behavior:

- exact bone-name match first
- normalized-name match second
- nearest mapped parent fallback after that
- final fallback to the first player skeleton bone if nothing else resolves

The mapping grid shows:

- `EnemyBone`
- `PlayerBone`

Use this to confirm that major chains were mapped correctly:

- pelvis / hips
- spine
- neck / head
- clavicles
- upperarm / forearm / hand
- thigh / calf / foot

If the names are very different between source and target skeletons, review the grid carefully before continuing.

## 9. Weight Transfer Panel

Use:

- `Apply Weight Transfer`

This step:

- reassigns each source vertex weight to the mapped player bone
- merges duplicate influences
- keeps the strongest influences
- normalizes the result

Current target behavior:

- UE3-friendly influence sets
- up to 4 useful influences after normalization

This is the step that turns the imported source mesh into a mesh that is actually skinned to the player skeleton.

## 10. UE3 Compatibility Fix Panel

Use:

- `Apply UE3 Compatibility Fixes`

This step prepares the working mesh for UE3 packaging and the existing MHUpkManager injector path.

Current fixes include:

- strip non-UE3-style working data from the retargeting result
- collapse to `LOD0`
- clamp UV channels to UE3-safe usage
- keep UE3 bone ordering aligned with the selected player skeleton
- preserve vertex colors, UVs, and smoothing data as much as possible

Run this before exporting or replacing the mesh in the package.

## 11. Export FBX 2013

Use:

- `Export FBX 2013`

This writes an ASCII FBX 2013 file containing:

- mesh geometry
- UVs
- vertex colors
- smoothing
- skeleton hierarchy
- skin clusters

Current export rules:

- exports only the retargeted `LOD0`
- no animation export
- intended for inspection, round-trip prep, and verification

Use this when you want to:

- inspect the retargeted skeleton/weights in another tool
- verify section layout and skinning before touching the `.upk`

## 12. Replace Mesh In UPK

Use:

- `Replace Mesh in UPK`

This step:

- converts the retargeted mesh into the neutral mesh/import format used by MHUpkManager
- rebuilds a replacement UE3 `FStaticLODModel`
- injects the rebuilt `LOD` into the selected `SkeletalMesh` export
- repacks the `.upk`
- creates a `.bak` backup next to the original file

Important:

- this modifies the selected `.upk`
- a backup is written automatically as:
  - `<yourfile>.upk.bak`

## Recommended First Test

For your first test:

1. choose a known-good target player mesh in a package copy
2. import the source mesh
3. import the player skeleton
4. auto-map bones
5. apply weight transfer
6. apply UE3 compatibility fixes
7. export FBX 2013 and inspect the result
8. replace only `LOD 0`
9. test the package in MHUpkManager first
10. then test in-game

## What To Check Before Replacing The Mesh

Make sure:

- the source mesh imported with the expected section count
- the player skeleton imported with the expected bone hierarchy
- major limb and spine bones mapped correctly
- the weight transfer summary looks reasonable
- UE3 compatibility fixes were applied
- the target `.upk` and `SkeletalMesh` export are correct

## Troubleshooting

### App Loads But Retargeting Looks Wrong

Check:

- wrong player skeleton selected
- bad bone-name matches
- source mesh has missing or broken skin weights
- source mesh imported with merged or missing sections

### Bone Mapping Looks Bad

Common cause:

- source and target skeletons use very different naming conventions

What to do:

- inspect the mapping grid before running weight transfer
- re-export the source mesh with cleaner bone names if possible

### Imported Mesh Deforms Wrong After Replacement

Check:

- player skeleton choice
- bone mapping quality
- whether the imported source mesh was actually skinned correctly
- whether the target mesh uses material or section assumptions different from the source mesh

### Package Opens But Runtime Result Is Wrong

Possible causes:

- source and target skeleton proportions are too different
- section/material layout mismatch
- source mesh topology differs too much from the original target expectations

## Current Limits

The retargeter is built to integrate with the current MHUpkManager architecture, so keep these limits in mind:

- it uses the existing UE3 LOD rebuild and UPK injection path
- it does not replace animation data
- it does not directly rewrite texture exports as part of the retargeting tab
- it is safest to test on copied packages first

## Short Version

If you just want the quick workflow:

1. `Browse UPK`
2. select `SkeletalMesh Export`
3. choose `LOD 0`
4. `Import Mesh (.psk/.fbx)`
5. `Import Player Skeleton (.fbx)`
6. `Auto Bone Mapping`
7. `Apply Weight Transfer`
8. `Apply UE3 Compatibility Fixes`
9. `Export FBX 2013` to inspect if needed
10. `Replace Mesh in UPK`
