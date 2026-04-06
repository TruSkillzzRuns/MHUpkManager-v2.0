# Next Tools

## Purpose

This file captures the highest-value tool additions to work on next based on the current state of:

- mesh retargeting
- mesh replacement in UPK
- texture injection
- material / shader debugging

The goal is to prioritize tools that directly help solve the current real blockers instead of adding generic utilities.

## Highest Priority

### 1. Character Texture Wizard

Purpose:

- provide a guided workflow for character texture replacement

Should do:

- accept `diffuse`, `normal`, and optional `specular` / `mask` inputs together
- auto-detect likely target `Texture2D` exports
- build target-safe DDS payloads using the target texture format, size, and mip count
- apply safe cache policy instead of blindly replacing the current cache
- inject the full related texture set as one operation
- log the full write result to a persistent diagnostic file

Why this matters:

- current texture injection still crashes the game
- current texture work is too manual and too easy to mismatch

### 2. Material Inspector

Purpose:

- inspect what the selected character mesh is actually using for materials and textures

Should do:

- show SkeletalMesh section index to material slot mapping
- show the assigned `UMaterialInstanceConstant` or parent material
- show resolved texture parameters for diffuse / normal / specular / emissive / mask
- show scalar and vector parameters that could affect appearance

Why this matters:

- the current holographic / blue look is likely material or texture related
- this is one of the fastest ways to verify what the mesh is really rendering with

### 3. Section / Material Mapping Preview

Purpose:

- show how imported / retargeted mesh sections map back to original UE3 section material indices

Should do:

- list each imported section
- show its final original section index
- show its final material index
- show whether the section was preserved, merged, or split

Why this matters:

- geometry replacement currently preserves original material hookups
- this is critical for understanding why a replaced character may render with unexpected materials

## Second Priority

### 4. Material / Texture Swap Tool

Purpose:

- explicitly rebind materials or texture parameters on a replaced character

Should do:

- select a character mesh material
- assign new diffuse / normal / specular textures
- save updated material instance references or texture parameter bindings safely

Why this matters:

- mesh replacement alone is not enough when the material setup is still wrong

### 5. Material Parameter Viewer

Purpose:

- inspect shader-related values that can cause strange rendering

Should do:

- show scalar parameters
- show vector parameters
- show texture parameters
- show whether a material may be translucent, emissive, masked, or otherwise unusual

Why this matters:

- current visual issues may be shader / material-instance property issues rather than geometry issues

### 6. Crash-Safe Texture Injection Report

Purpose:

- produce a single clear report for every texture injection attempt

Should include:

- source file path
- target export path
- target texture format
- target mip count
- selected cache policy
- final cache target
- manifest changes made

Why this matters:

- current texture crashes need precise write diagnostics

## Third Priority

### 7. Tangent / Normal Diagnostic Overlay

Purpose:

- help determine whether bad lighting is caused by tangent-space problems

Should do:

- overlay normals
- overlay tangents / bitangents
- optionally compare imported mesh tangents vs rebuilt UE3 tangents

Why this matters:

- bad tangent basis can make a mesh look wrongly lit or artificially holographic

### 8. Animation Pose Preview

Purpose:

- preview replaced characters in actual poses before testing in game

Should do:

- show idle / locomotion / other available poses
- make deformation and orientation issues easier to validate quickly

Why this matters:

- faster verification than repeated full in-game testing

### 9. Section Remove / Hide Tool

Purpose:

- intentionally disable or strip unwanted sections like capes or accessories

Should do:

- show original sections
- allow hide / preserve / remove behavior explicitly

Why this matters:

- section removal behavior has already come up as a real workflow gap

## Recommended Order

Recommended build order:

1. Character Texture Wizard
2. Material Inspector
3. Section / Material Mapping Preview
4. Material / Texture Swap Tool
5. Material Parameter Viewer

## Current Working Assumption

The most likely remaining visual issue on replaced characters is:

- material / texture hookup mismatch

More likely than:

- viewport renderer issue
- pure retarget geometry failure

So the next tools should focus first on:

- character texture workflow
- material inspection
- material mapping visibility
