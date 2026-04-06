# SkeletalMesh Retargeter Status

## Overview

This file records the current state of the `SkeletalMesh Retargeter` subsystem in `MHUpkManager` as of `2026-04-04`.

It is focused on the Marvel Heroes Omega workflow only:

- original MHO skeleton only
- original MHO bone order only
- original MHO hierarchy only
- original MHO animations only
- new mesh geometry bound onto the original MHO skeleton

This document is intended to capture:

- what has been implemented
- what has been tried in real tests
- what is currently working
- what is still failing
- what the next engineering step should be

## MHO Rules For This Tool

The retargeter is now being treated under the following constraints:

1. The only valid runtime skeleton is the original MHO skeleton from the selected game `SkeletalMesh`.
2. The tool must not generate a new skeleton.
3. The tool must not rename bones, reorder bones, change hierarchy, change reference pose metadata, change bone count, or change CRC expectations.
4. The valid workflow is:
   - select original MHO `SkeletalMesh`
   - import new replacement mesh
   - transfer weights onto the original MHO skeleton
   - export / inject back into the UPK

This means the unrigged replacement workflow is not “create a new rig”. It is:

- `new mesh geometry`
- `original MHO skeleton`
- `original MHO animation system`

## Current Intended Workflow

The intended current workflow for the unrigged replacement path is:

1. Select the original MHO UPK.
2. Select the original MHO `SkeletalMesh` export.
3. Import the replacement mesh.
4. If the imported unrigged mesh is still facing sideways after import, apply a manual `90` degree rotation before binding.
5. Run `One-Click Bind To Original Skeleton`.
6. Run `Apply UE3 Compatibility Fixes`.
7. Export FBX or replace the mesh inside the UPK.

For rigged-source workflows, the older path still exists:

- import mesh
- import skeleton
- auto bone mapping
- auto orientation
- auto scale
- apply weight transfer
- apply UE3 compatibility
- export / replace

That older path is still present, but the MHO-only replacement work is centered on the new one-click bind flow.

## What Has Been Implemented

### Retargeter UI

The `SkeletalMesh Retargeter` tab includes:

- UPK selection
- SkeletalMesh export selection
- LOD selection
- mesh import
- AnimSet import
- texture import
- skeleton import
- auto bone mapping
- auto orientation
- auto scale
- weight transfer
- one-click bind to original skeleton
- UE3 compatibility
- FBX export
- mesh replacement in UPK
- log output

### Original MHO Context Auto-Load

When the selected target MHO `SkeletalMesh` changes, the retargeter now:

- loads the original MHO destination skeleton from the selected export
- converts the selected original MHO mesh into an internal weighted reference mesh
- attempts AnimSet discovery best-effort

This part is now working reliably enough for the tested Hulk package.

### Original MHO Weighted Reference Mesh Conversion

Added a conversion path from the selected MHO `USkeletalMesh` into an internal transfer source mesh:

- `MhoSkeletalMeshConverter`

This was hardened to tolerate:

- UV count mismatches
- invalid section chunk references
- bad section index ranges
- vertex index overruns
- invalid parent bone indices

The converter now successfully loads the tested Hulk Maestro mesh and reports:

- `4515` vertices
- `7413` triangles
- `85` bones

### One-Click Bind To Original Skeleton

Added a new MHO-only binding path:

- `One-Click Bind To Original Skeleton`

This path currently:

- requires the selected original MHO `SkeletalMesh`
- accepts an imported replacement mesh with `0` bones
- scales the new mesh against the original MHO reference mesh
- runs a pose-conform pass
- transfers interpolated weights from the original MHO mesh
- binds the new geometry to the original MHO skeleton

### Weight Transfer Stack

Implemented / added:

- `MeshSurfaceKDTree`
- `TriangleBarycentricSolver`
- `BoneWeightInterpolator`
- `WeightNormalizer`
- `BoneIndexMapper`
- `WeightTransferEngine`
- `SkeletonBinder`

The transfer engine now includes:

- nearest-triangle barycentric interpolation
- top-4 influence normalization
- original-skeleton bone validation
- dominant-bone-filtered triangle lookup
- nearest-skeleton-anchor preference filtering

### Pose Conform Pass

Added:

- `PoseConformProcessor`

This currently runs on the unrigged one-click path after scale and before weight transfer.

Its goal is to pull the imported mesh toward the original MHO reference pose regions before binding.

This is a first-pass conform system, not a finished region solver.

### Orientation Controls

The retargeter now has explicit manual orientation controls for imported meshes:

- `Rotate -90`
- `Rotate +90`
- `Rotate 180`
- `Pitch -90`
- `Pitch +90`
- `Roll -90`
- `Roll +90`

Automatic orientation is still kept for the older rigged-source path.

Automatic orientation for the unrigged one-click path is now enabled again and is materially better than the earlier disabled state, but it is still not fully solved for the tested Thanos unrigged import.

Current observed state on the tested Thanos mesh:

- the one-click unrigged auto-orientation now stands the mesh upright
- it no longer leaves the mesh horizontal
- it no longer chooses the upside-down vertical result
- but it still does not always choose the correct final quarter-turn / facing automatically

Current practical workaround:

- manually rotate the imported mesh `90` degrees first
- then run `One-Click Bind To Original Skeleton`
- then run `Apply UE3 Compatibility Fixes`
- then run `Replace Mesh In UPK`

### Scale Matching

The unrigged one-click path now scales against the original MHO reference mesh bounds instead of the weaker skeleton-bounds fallback.

This is working better than the earlier skeleton-only fallback.

### UE3 Compatibility And Replacement

The retargeter still supports:

- UE3 compatibility processing
- FBX 2013 export
- UPK replacement
- `.bak` backup creation

Important bug fix:

`Apply UE3 Compatibility Fixes` was previously operating on `retargetSourceMesh` before `retargetProcessedMesh`, which could discard the one-click bind output. That was corrected so the compatibility pass now prefers the already processed mesh first.

### UE3 Vertex Limit Guard

The importer now fails earlier and more clearly when the processed mesh exceeds UE3 skeletal mesh vertex limits.

This was needed because one tested mesh had:

- `170,386` vertices

which exceeded the importer’s `UInt16` addressable range for a UE3 LOD.

## What Has Been Tested

### Tested Original MHO Mesh

Real test package:

- `UC__MarvelPlayer_Hulk_Maestro_SF.upk`
- export: `hulk_maestro.hulk_maestro`

Observed successful auto-load:

- original MHO skeleton loaded
- original MHO weighted reference mesh loaded
- AnimSet discovery returned none, which was non-blocking

### Tested Replacement Mesh Case

Real tested replacement:

- unrigged FBX mesh
- imported with `0` bones

Observed import:

- mesh import succeeded
- unrigged source accepted by the one-click path

### Real In-Game Injection Results

The replacement path was tested end-to-end in game.

Observed across multiple iterations:

- injection works
- the character can load in game
- the character can animate
- the replacement can inherit locomotion
- the character size can now match the original MHO mesh better than before

This confirms that:

- skeleton binding is happening
- animation hookup is happening
- the exported/imported skeletal mesh is structurally valid enough for runtime use

## What Has Worked

### Structural Runtime Success

The following are confirmed working in the current branch:

- app startup with the retargeter UI
- original MHO skeletal mesh selection
- original MHO skeleton auto-load
- original MHO weighted reference mesh conversion
- unrigged mesh import
- one-click bind pipeline execution
- UE3 compatibility pass on processed mesh
- UPK replacement and backup generation
- in-game load without hard failure
- runtime locomotion on the replacement mesh
- improved overall size matching to the original MHO character

### Tooling/Debugging Improvements That Worked

The following fixes materially improved reliability:

- separated AnimSet discovery failures from original skeleton/reference mesh load failures
- guarded malformed export metadata during AnimSet discovery
- hardened MHO mesh conversion against broken section/index data
- clearer vertex-limit failure for oversized meshes
- manual orientation controls instead of relying entirely on unstable auto orientation
- stage-by-stage retarget spatial summaries and one-click diagnostic log export to desktop

## What Has Been Tried

The following approaches have already been implemented and tested in some form:

1. Original rigged-source retarget workflow using:
   - auto map
   - auto orientation
   - auto scale
   - weight transfer

2. Original MHO-only one-click binding workflow using:
   - selected original MHO mesh as reference
   - imported unrigged mesh as target
   - automatic scale
   - nearest-triangle weight transfer
   - bind to original MHO skeleton

3. Geometry-based automatic orientation for unrigged meshes.

4. Manual yaw rotation controls.

5. Manual pitch/roll rotation controls.

6. Reference-mesh-based scale matching.

7. Bone-region-filtered nearest-triangle weight transfer.

8. First-pass pose conform before weight transfer.

9. Reference-frame alignment of the imported mesh to the original MHO body before pose conform and weight transfer.

## What Did Not Work Well

### Automatic Orientation For Unrigged Meshes

The geometry-based automatic orientation heuristic is improved, but still not fully reliable.

Latest tested state:

- it can now automatically move the imported mesh from horizontal to upright
- it can now avoid the upside-down result seen in earlier tests
- it can still miss the final `90` degree facing choice on some unrigged meshes

So the current retarget blocker is no longer "mesh remains horizontal." The remaining orientation gap is:

- automatic final quarter-turn / facing selection for unrigged meshes

Observed failures included:

- wrong facing direction
- upside-down spawn
- unstable guess quality depending on the replacement mesh

Because of that, automatic unrigged orientation was removed from the one-click path and manual controls were kept instead.

### Earlier Skeleton-Only Scale Fallback

Earlier scale matching used geometry vs skeleton bounds fallback.

That did not size the tested replacement mesh well enough.

This was improved by switching to reference-mesh-based scale matching for the unrigged one-click path.

### Missing Mesh-To-Skeleton Frame Alignment

One remaining issue was that the one-click path scaled the imported mesh, but did not explicitly translate it into the original MHO body frame before pose conform and weight transfer.

This meant a mesh could be:

- roughly the correct size
- bound to the correct skeleton
- still offset at pelvis / chest / feet level

That kind of offset is enough to produce severe deformation even when the final bind is structurally valid.

This has now been improved by adding a dedicated alignment stage before pose conform.

### Oversized Replacement Mesh Injection

One earlier test replacement mesh exceeded UE3 LOD limits.

Observed result:

- UPK replacement failure due to `UInt16` index buffer overflow

This is now understood as a UE3 limit, not a logic bug.

### Deformation Quality

This is still the main unsolved problem.

Even after:

- successful bind
- successful injection
- successful runtime animation
- better scale
- better orientation controls
- pose conform pass

the final in-game mesh can still be heavily deformed.

## Current Failure Mode

The latest real result is:

- the mesh is no longer purely stuck in a dead A-pose all the time
- locomotion is present
- size is much closer to correct
- one-click bind now also includes explicit body-frame alignment before conform / transfer
- the mesh still deforms badly in game

That means the tool has advanced from:

- `cannot bind / cannot inject`

to:

- `binds and runs, but deformation quality is still poor`

This is an important distinction.

The current blocker is no longer package writing or animation hookup.

The current blocker is:

- deformation fidelity
- bind-pose conformance quality
- body-region correspondence quality during transfer

## Best Current Diagnosis

The current system is now structurally functional but still too global in how it conforms and transfers.

The likely remaining causes are:

- pose conform is too coarse and not region-specific enough
- torso/arm/leg regions can still influence the wrong areas
- shoulders, hips, and spine need more constrained region handling
- a very different silhouette from the original MHO mesh still collapses under reference weights that were authored for a different body shape

In practical terms:

- the original MHO skeleton path works
- the weight transfer is not yet anatomically stable enough for arbitrary replacement humanoids
- global placement should now be better than before because the imported mesh is aligned into the original MHO body frame before conform / transfer

## What Is Not Solved Yet

The following are still not solved:

- reliable low-distortion deformation for arbitrary imported humanoid meshes
- robust bind-pose conforming from an unrigged imported mesh to the original MHO reference pose
- strong region isolation for torso, shoulders, hips, spine, and limbs
- a final replacement workflow that consistently looks correct in game without manual cleanup

## Recommended Next Work

The next engineering step should be:

1. Region-aware pose conform.
2. Region-aware weight transfer.
3. Stronger shoulder / clavicle / upper-arm isolation.
4. Stronger pelvis / thigh / spine isolation.
5. Additional smoothing / stabilization after transfer.

## Latest Conclusion

After additional real in-game testing, the current automatic path should now be considered:

- structurally functional
- useful for loading, binding, scaling, exporting, and injecting
- not yet reliable for clean final deformation on arbitrary enemy-to-playable conversions

The current implementation has reached the point where further improvement is no longer a small bug-fix pass.

If this tool continues to be pushed toward fully automatic enemy-to-playable conversion, it becomes a larger feature effort that likely requires:

1. stronger per-region pose conform instead of broad global conform
2. stronger per-region weight transfer instead of mostly heuristic nearest-surface interpolation
3. explicit shoulder/chest solvers
4. explicit pelvis/hip/thigh solvers
5. manual weight editing or region override tools
6. manual pose fitting tools
7. additional deformation validation and preview tooling

In other words:

- `binding / injection / animation hookup` is now mostly solved
- `high-quality deformation` is not solved

For practical use right now, the safest expectation is:

- this tool can get a replacement mesh into game and animated
- this tool may still produce unacceptable deformation for many enemy-to-playable conversions
- truly clean final results will likely require offline cleanup or significantly more in-app tooling

The current code should not be treated as “done”.

It should be treated as:

- structurally functional
- good enough to inject and animate
- not yet good enough for reliable final deformation quality

## Summary

### Working

- original MHO skeleton auto-load
- original MHO weighted reference mesh conversion
- one-click bind path for unrigged meshes
- scale matching improved
- manual orientation controls
- UE3 compatibility on processed mesh
- UPK replacement
- in-game load
- runtime locomotion

### Partially Working

- weight transfer quality
- pose conform quality
- reference-frame alignment quality
- final deformation quality

### Not Working Reliably Yet

- low-distortion final mesh deformation for arbitrary replacement characters

## Additional Confirmed Test Cases

### Hulk Maestro -> Unrigged Thanos

Observed across multiple iterations:

- original MHO skeleton and reference mesh loaded correctly
- imported unrigged mesh with `0` bones was accepted by the one-click path
- one-click bind, UE3 compatibility, and UPK replacement all completed
- runtime injection succeeded
- early iterations had:
  - wrong facing
  - upside-down spawn
  - poor size matching
  - frequent A-pose result
- later iterations improved:
  - runtime locomotion worked
  - scale became much closer to the original character
  - A-pose issue was reduced in some runs
- final blocker remained:
  - severe deformation quality

### Rogue Savage Land -> Mr Sinister Enemy Mesh

Observed:

- original MHO skeleton loaded with `163` bones
- imported enemy mesh loaded with `88` bones
- transfer / bind / compatibility / replacement all completed
- UPK replacement succeeded
- runtime load succeeded
- deformation was still poor

This confirmed that package writing, animation hookup, and basic binding are no longer the main blockers. The main blocker is still deformation fidelity.

## Section Count Compatibility Fix

One tested enemy-to-playable replacement originally failed because imported FBX section count did not match the original LOD section count.

Implemented:

- section merge support in `src/MeshImporter/UE3/UE3LodBuilder.Layout.cs`

Result:

- imported meshes with more sections than the target playable LOD can now be merged instead of hard-failing on section-count mismatch
- this improved structural compatibility, but did not solve deformation quality

## File Purpose

This file is the running engineering note for the retargeter and should be updated whenever:

- the one-click MHO workflow changes
- a new real in-game test result is observed
- a current blocker is resolved
- a previous assumption is proven wrong
