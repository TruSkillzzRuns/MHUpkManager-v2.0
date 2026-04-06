# Texture Preview Handoff

## Purpose

This file is a continuation note for the next session if usage is low. It captures:

- the exact next task prompt
- current project state
- what has already been changed in MHUpkManager
- what to avoid touching
- where to continue from

## Exact Next Prompt

You are analyzing two separate C# codebases:

1. SOURCE PROGRAM (reference implementation)
   Path: `D:\Marvel Heroes Omega\MHTextureManager-1.1.0\MHTextureManager-1.1.0`
   Purpose: This program contains the correct, working implementation of:
   - UPK texture extraction
   - UPK texture preview
   - Texture type detection
   - Texture decoding
   - Texture format conversion
   - UI logic for previewing textures
   - Any helper classes, utilities, or parsers used by the preview system

2. TARGET PROGRAM (the program to modify)
   Path: `C:\Users\TruSkillzzRuns\mhupkmanager-link`
   Purpose: This program contains an incomplete or outdated Texture Preview tool.
   Your task is to update and rewrite ONLY the Texture Preview subsystem
   so that it behaves EXACTLY like the SOURCE PROGRAM.

## Task Objective

Study the SOURCE PROGRAM in full detail and extract:

- Class structure
- Namespaces
- Texture loading pipeline
- UPK parsing logic
- Texture type detection logic
- Texture decoding logic
- MipMap handling
- Normal map handling
- UI update logic
- Any helper utilities required for preview
- Any dependencies required for preview

Then rewrite the Texture Preview subsystem inside the TARGET PROGRAM so that:

- It uses the same architecture as the SOURCE PROGRAM
- It uses the same UPK parsing logic
- It uses the same texture decoding logic
- It uses the same preview rendering logic
- It uses the same UI update patterns
- It supports all texture types supported by the SOURCE PROGRAM
- It is fully compatible with the TARGET PROGRAM’s existing codebase
- It does NOT modify unrelated systems
- It does NOT break existing UPK parsing, mesh preview, or AnimSet logic

## Output Requirements

### MODULE A — ARCHITECTURE SUMMARY

- Summarize the SOURCE PROGRAM’s texture preview architecture
- List all classes, methods, and data structures involved
- Describe how textures are loaded, decoded, and displayed

### MODULE B — DIFFERENCE ANALYSIS

- Compare the TARGET PROGRAM’s current Texture Preview system
- Identify missing features
- Identify incorrect logic
- Identify outdated or incompatible code

### MODULE C — UPGRADED IMPLEMENTATION PLAN

- Provide a step-by-step plan to update the TARGET PROGRAM
- Include file names, class names, and method names
- Include exact integration points

### MODULE D — CODE IMPLEMENTATION

- Generate the full updated code for the TARGET PROGRAM
- Include all required classes, methods, and utilities
- Ensure the code compiles without modification
- Ensure the code matches the SOURCE PROGRAM’s behavior exactly

### MODULE E — FINAL VALIDATION

- Explain how to test the updated Texture Preview tool
- Confirm feature parity with the SOURCE PROGRAM
- Confirm UPK compatibility
- Confirm UI behavior matches the SOURCE PROGRAM

## Rules

- Do NOT modify unrelated systems.
- Do NOT change skeleton, mesh preview, or AnimSet logic.
- Do NOT introduce new dependencies unless required by the SOURCE PROGRAM.
- Preserve existing namespaces unless a compatibility change is required.
- All code must be deterministic and offline.
- Maintain strict separation between SOURCE and TARGET logic.
- If ambiguity exists, ask for clarification before generating code.

## Current Target Project State

The target repo at `C:\Users\TruSkillzzRuns\mhupkmanager-link` has already been modified significantly for the `SkeletalMesh Retargeter` subsystem.

Recent completed work includes:

- added `MHUpkManager.Retargeting`
- added the `SkeletalMesh Retargeter` UI tab
- fixed startup crashes caused by retargeter layout
- hid the right panel when the retargeter tab is active
- updated window title to:
  - `MH UPK Manager v.1.0 by AlexBond - Upgraded by TruskillzzRuns`
- added:
  - `SkeletalMesh-Retargeter-Guide.md`
  - `retargettool.md`
- added retarget features:
  - auto game skeleton loading from selected SkeletalMesh
  - best-effort AnimSet auto-discovery
  - auto bone mapping
  - weight transfer
  - first-pass bind-pose conversion
  - auto scale to target character
  - UPK mesh replacement

## Files Already Touched Recently

These files have active retargeter-related changes and should be treated carefully:

- `src/MainForm.cs`
- `src/MainForm.Designer.cs`
- `src/Program.cs`
- `src/Retargeting/RetargetingModels.cs`
- `src/Retargeting/MeshImporter.cs`
- `src/Retargeting/SkeletonImporter.cs`
- `src/Retargeting/BoneMapper.cs`
- `src/Retargeting/WeightTransfer.cs`
- `src/Retargeting/UE3CompatibilityProcessor.cs`
- `src/Retargeting/FBX2013Exporter.cs`
- `src/Retargeting/MeshReplacer.cs`
- `src/Retargeting/AutoScaleProcessor.cs`
- `src/Retargeting/UI/SkeletalMeshRetargeterPanel.cs`
- `src/MeshImporter/NeutralMesh/NeutralMesh.cs`
- `src/MeshImporter/UE3/UE3LodBuilder.Layout.cs`

The texture preview rewrite should avoid disturbing these unless there is a required shared integration point in `MainForm.cs`.

## Where To Continue From

Start with the SOURCE PROGRAM first, not the target.

Recommended sequence:

1. Read the SOURCE PROGRAM texture preview subsystem completely.
2. Identify all texture-related classes, namespaces, and UI pieces.
3. Map the full preview pipeline:
   - UPK lookup
   - texture object parsing
   - compression/format detection
   - mip selection
   - decode path
   - bitmap conversion
   - preview rendering
4. Read the TARGET PROGRAM texture preview subsystem.
5. Compare source vs target and document every gap before editing code.
6. Only then patch the TARGET PROGRAM.
7. Build and verify the target app after the rewrite.

## Likely Target Areas To Inspect Next

In the TARGET PROGRAM, the next session should inspect:

- `src/TexturePreview/`
- any texture preview UI classes
- `MainForm.cs` integration points for texture preview
- existing texture parsing / texture manager utilities
- `UpkManager` texture object handling used by the preview path

## Constraints For The Next Session

- Texture Preview only
- no retargeter work
- no mesh preview changes
- no AnimSet changes
- no unrelated parser rewrites unless the source preview system requires them for parity

## Build Target

When implementation starts, verify with:

```powershell
dotnet build C:\Users\TruSkillzzRuns\mhupkmanager-link\src\MHUpkManager.csproj -c Release
```

## Notes

The retargeter is still only partially complete in deformation quality, but that is separate work. The next session should stay focused on Texture Preview parity with the source program.
