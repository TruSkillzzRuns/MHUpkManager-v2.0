# FBX -> UE3 SkeletalMesh Import Work Log

## Current summary

This repo now has a working C# FBX import pipeline wired into the UI, and it can:

- load an edited FBX with Assimp
- remap FBX bone names onto the original UE3 skeleton from the UPK
- normalize weights to 4 influences summing to 255
- rebuild a replacement `FStaticLODModel`
- inject that rebuilt LOD back into the selected `USkeletalMesh` export inside the current UPK

The system is not finished yet.

Current state:

- the rewritten UPK can now be opened again in MHUpkManager
- the import path completes without immediate importer exceptions on the current Rogue test asset
- the game still rejects the imported mesh at runtime with:
  - `Invalid number of texture coordinates`

That means the remaining problem is now in the serialized SkeletalMesh/LOD payload, not the basic UI path or the top-level package repack flow.

## Modules added

Added under `src/Model/Import`:

- `NeutralMesh.cs`
- `MeshImportContext.cs`
- `FbxMeshImporter.cs`
- `BoneRemapper.cs`
- `WeightNormalizer.cs`
- `UE3VertexBuilder.cs`
- `UE3IndexBuilder.cs`
- `UE3LodBuilder.cs`
- `UE3LodSerializer.cs`
- `UE3SkeletalMeshInjector.cs`
- `SkeletalMeshImportRunner.cs`
- `ImportDiagnostics.cs`

## What each module is doing

### `NeutralMesh.cs`

Neutral in-memory mesh format used between FBX import and UE3 rebuild:

- sections
- vertices
- normals/tangents/bitangents
- UVs
- indices
- named bone weights

### `MeshImportContext.cs`

Builds import context from the original `USkeletalMesh`:

- original skeleton lookup from `RefSkeleton`
- original `RequiredBones`
- original LOD settings
- original raw export bytes
- exact object-data offset
- exact original LOD byte range for patching

### `FbxMeshImporter.cs`

Uses `AssimpNet` to import FBX geometry:

- triangulates
- reads normals/tangents
- reads UVs
- reads bone weights
- converts coordinates back into the tool's UE3 layout

Important rule preserved:

- it does not import or replace the skeleton

### `BoneRemapper.cs`

Maps FBX bone names to original UE3 bone indices.

### `WeightNormalizer.cs`

Converts arbitrary FBX weight sets into UE3-compatible vertex weights:

- merge duplicate bones
- keep top 4
- normalize to 255 total
- emit 4 bone slots every time

### `UE3VertexBuilder.cs`

Builds:

- `FSoftSkinVertex`
- GPU skin vertices
- packed tangent/normal data
- influence records

### `UE3IndexBuilder.cs`

Builds 16-bit UE3 triangle indices.

### `UE3LodBuilder.cs`

Builds a replacement `FStaticLODModel`.

Important hardening added during debugging:

- preserves original `RequiredBones`
- preserves original material indices
- preserves original section ordering
- supports the common roundtrip case where the FBX comes back as one combined section and splits it back to original UE3 section ranges
- supports partial section replacement by preserving untouched original sections/chunks when the imported FBX does not carry every original section back
- preserved-section influences are rebuilt from original chunk vertices instead of assuming the optional `VertexInfluences` buffer is aligned one-to-one with chunk vertices

### `UE3LodSerializer.cs`

Serializes the rebuilt `FStaticLODModel` into binary.

Important debugging change:

- tracks embedded bulk-data offset patch sites for `RawPointIndices` so the injector can patch absolute archive offsets after final export placement is known

### `UE3SkeletalMeshInjector.cs`

Injects the rebuilt LOD into the selected `USkeletalMesh` export.

Current behavior:

- patches only the selected LOD byte range inside the original export buffer
- preserves the rest of the export body
- repacks the UPK export data and export-table offsets
- writes back to the currently opened UPK path
- creates `<original>.upk.bak` before overwrite

## UI integration

### Main form

Added to the Object Properties context menu:

- `Import FBX to SkeletalMesh...`

### Model viewer

Added to the `Model` menu:

- `Import FBX to SkeletalMesh...`

### Current UI behavior

The importer no longer asks for a separate output UPK path.

Current flow:

1. choose FBX
2. confirm replace-in-current-UPK
3. tool writes back to the currently opened `.upk`
4. tool creates `<file>.bak`

## Diagnostics added

Import failures now:

- show full exception text in the dialog
- write a timestamped log file to:
  - `Desktop\MHUpkManager_ImportLogs\fbx-import-YYYYMMDD-HHMMSS.log`

This was added specifically to trace the actual failing line during the Rogue roundtrip.

## Debugging history so far

### 1. Compressed UPK rejection

Original importer failed immediately on compressed source packages.

Work done:

- added compressed-package handling path in the injector
- decompressed package body is used for repack
- output is written as an uncompressed package form

### 2. Wrong UX: save-as-new-UPK flow

Original UI asked for a new output UPK file.

Work done:

- changed to replace-in-current-UPK behavior
- automatic `.bak` backup added

### 3. Game freeze on load

Initial full export-body rewrite was too risky.

Work done:

- importer changed from rebuilding the whole SkeletalMesh body
- now patches only the original target LOD byte range inside the export

### 4. Wrong object-data offset

Importer crashed with `startIndex`/out-of-range failures when scanning SkeletalMesh data.

Work done:

- `MeshImportContext` now parses the raw export property stream directly to find the real post-property object-data offset

### 5. Section count mismatch

Edited FBX came back with `1` section while original UE3 LOD had `2`.

Work done:

- if the FBX is a single combined section and triangle counts allow it, the importer splits it back into the original section layout

### 6. Partial section import

Edited FBX later came back with fewer total triangles than the full original LOD, indicating one original section was likely omitted from the edit/export pipeline.

Work done:

- importer now supports preserving untouched original sections/chunks instead of failing outright

### 7. Preserved-section influence assumption was wrong

Importer assumed `VertexInfluences[0].Influences` aligned directly with original chunk vertices.

Work done:

- preserved-section influence data is now rebuilt from original rigid/soft chunk vertices instead

### 8. Rewritten UPK could not reopen in MHUpkManager

Two header-level issues were found while trying to reopen the modified package:

- bad `AdditionalPackagesToCook` parsing alignment
- compressed chunk table parsing on files now marked uncompressed

Work done:

- package compression flags are cleared without blindly zeroing the chunk-table count
- `UnrealHeader` now skips compressed-chunk parsing when `CompressionFlags == 0`

Files involved:

- `src/Model/Import/UE3SkeletalMeshInjector.cs`
- `UpkManager/Models/UpkFile/UnrealHeader.cs`

### 9. Current runtime blocker

The current imported UPK can be reopened in MHUpkManager, but loading the game produces:

- `Invalid number of texture coordinates`

This strongly suggests the remaining defect is in the serialized skeletal LOD data, most likely around:

- `NumTexCoords`
- GPU vertex buffer layout
- UV array count/stride alignment
- or a nearby serialization mismatch causing the game to read the wrong value at that point

### 10. RawPointIndices serialization hardening

Because `RawPointIndices` is serialized immediately before `NumTexCoords`, it became a primary suspect for a nearby misalignment bug.

Work done:

- `MeshImportContext` now captures the original serialized `RawPointIndices` blob byte-for-byte from the original target LOD
- `UE3LodSerializer` now reuses that original raw bulk-data blob when the rebuilt payload length still matches
- only the embedded absolute bulk-data offset field is patched during injection

Reason:

- this removes one more serializer difference directly before the field the game is currently rejecting

### 11. Remaining likely root cause after latest change

If the game still reports `Invalid number of texture coordinates` after the latest rebuild, the most likely remaining defect is the GPU skin vertex buffer format itself.

The main current suspicion is:

- the original serialized mesh may use a different vertex storage form than the importer is rewriting
- especially around:
  - packed vs unpacked position layout
  - element stride
  - UV-count-dependent vertex size

There is an existing sign of risk in the codebase:

- `FSkeletalMeshVertexBuffer.ReadData(...)` reads `bUsePackedPosition`
- then immediately forces it to `false` for PC parsing
- the importer currently also rewrites the vertex buffer as unpacked

That means the tool can successfully parse the mesh for display while still failing to preserve the exact original serialized vertex layout the game expects

## Important implementation notes

- skeleton hierarchy is not replaced
- bind pose is not intentionally modified
- original `RequiredBones` order is preserved
- bone names are resolved only against the original UE3 skeleton
- weights are forced to 4 influences
- material indices are preserved from the original LOD
- section order is preserved from the original LOD where possible
- the injector is still a raw export repacker, not a full generic `UnrealObject<T>` writer

## Current build status

Latest verification:

- `dotnet build src\MHUpkManager.csproj -c Release`
- success
- warnings: `0`
- errors: `0`

## What is proven vs not proven

### Proven

- project compiles cleanly
- UI actions exist in main form and model viewer
- importer reaches package rewrite stage
- modified output UPK can be reopened in MHUpkManager after the header parser fixes

### Not yet proven

- byte-exact runtime-valid SkeletalMesh LOD serialization for Marvel Heroes
- in-game load without runtime errors
- full correctness of UV-count / GPU vertex buffer serialization

## Current next step

The next debugging target is the runtime UV-count error.

Most likely areas to inspect next:

1. compare original vs rebuilt values for:
   - `lod.NumTexCoords`
   - `VertexBufferGPUSkin.NumTexCoords`
   - GPU vertex element size
   - vertex buffer count
2. verify that rebuilt GPU vertex arrays exactly match the original UV-count expectations
3. verify whether the original serialized mesh used packed-position GPU vertices or another vertex format variation the importer is not preserving
4. confirm that no earlier field in the LOD serializer is misaligned and causing the game to read a garbage `NumTexCoords`

## Bottom line

This importer is now in a late debugging phase.

The work has moved past:

- UI wiring
- import orchestration
- compressed-package rejection
- several package/header corruption issues

The remaining blocker is:

- producing a SkeletalMesh LOD binary that Marvel Heroes accepts at runtime

## 2026-04-01 follow-up: new Mesh Importer cutover status

The original runtime blocker is no longer the active issue for the tested asset.

New confirmed state:

- a newly imported mesh was tested in-game
- the game loaded successfully
- no crash occurred on that tested import

That means the old importer path under `src/Model/Import` has at least one proven in-game-valid import path.

### New task now in progress

The current task is no longer debugging the old importer runtime crash.

The current task is:

- make the newer prompt-shaped importer under `src/MeshImporter` fully standalone
- follow the prompt structure exactly
- do not use the old importer implementation in `src/Model/Import` for the new importer path
- do not modify the old importer logic while doing this cutover

### What was already changed in `src/MeshImporter`

The following new-importer files were already replaced so they no longer act as simple wrappers around `MHUpkManager.Model.Import`:

- `src/MeshImporter/NeutralMesh/NeutralMesh.cs`
- `src/MeshImporter/Processing/BoneRemapper.cs`
- `src/MeshImporter/Processing/WeightNormalizer.cs`
- `src/MeshImporter/Processing/SectionRebuilder.cs`
- `src/MeshImporter/UE3/UE3IndexBuilder.cs`
- `src/MeshImporter/UE3/UE3LodModel.cs`
- `src/MeshImporter/UE3/RequiredBonesBuilder.cs`
- `src/MeshImporter/UE3/LodBoneMapBuilder.cs`
- `src/MeshImporter/MeshImportContext.cs`
- `src/MeshImporter/UE3/UE3VertexBuilder.cs`
- `src/MeshImporter/FBX/FbxMeshImporter.cs`

These files were rewritten into standalone `MHUpkManager.MeshImporter` implementations.

### Exact remaining dependency points found

At the point this work paused, `rg` still showed the new importer depending on the old importer in exactly these places:

- `src/MeshImporter/MeshPreProcessor.cs`
- `src/MeshImporter/Injection/UpkSkeletalMeshInjector.cs`
- `src/MeshImporter/UE3/UE3LodBuilder.cs`
- `src/MeshImporter/UE3/UE3LodSerializer.cs`

The direct remaining references were:

- `MHUpkManager.Model.Import.SkeletalMeshImportPipeline`
- `MHUpkManager.Model.Import.UE3SkeletalMeshInjector`
- `MHUpkManager.Model.Import.UE3LodBuilder`
- `MHUpkManager.Model.Import.UE3LodSerializer`
- old `.Inner` wrapper passthrough usage in those files

### Intended next edits

Next session should continue by replacing these four files in this order:

1. `src/MeshImporter/Injection/UpkSkeletalMeshInjector.cs`
2. `src/MeshImporter/UE3/UE3LodSerializer.cs`
3. `src/MeshImporter/UE3/UE3LodBuilder.cs`
4. `src/MeshImporter/MeshPreProcessor.cs`

Then:

5. add a standalone new-importer diagnostics helper for summary/exception logs
6. update the new Mesh Importer tab in `src/MainForm.cs` to use the new diagnostics path only
7. build the project
8. run `rg` again over `src/MeshImporter` to verify no `MHUpkManager.Model.Import` references remain
9. test the new Mesh Importer UI path, not the old context-menu importer path

### Important constraint for the next session

The user explicitly clarified this requirement:

- the new Mesh Importer must follow the prompt as its own implementation
- the new Mesh Importer must not use anything from the original importer in the new path
- the old importer should remain present and untouched for now

So the target end state is:

- old importer still exists in `src/Model/Import`
- new importer works independently from `src/MeshImporter`
- the new Mesh Importer tab uses only the standalone new importer code path

## Deferred follow-up note for later

The user explicitly wants to come back later to these next-step items for the standalone Mesh Importer and keep them in mind:

1. add stronger FBX validation before import starts
2. surface clearer UI diagnostics for missing bones, section mismatches, dropped weights, and LOD mapping decisions
3. save richer structured logs so failed imports are easier to diff
4. add export-side or round-trip helpers so users can prepare FBX files in the expected format
5. build a small test matrix of known-good assets and edge cases so future changes do not silently regress imports

These are intentionally deferred for now while other work continues.

## 2026-04-01 follow-up: Mesh Preview tab added

A new in-app `Mesh Preview` tab has now been added under `src/MeshPreview`.

Current implemented scope:

- FBX mesh preview load path
- UE3 `USkeletalMesh` preview load path from UPK
- OpenGL preview viewport hosted inside MHUpkManager
- display modes for:
  - overlay
  - side-by-side
  - FBX only
  - UE3 only
- left-panel controls for:
  - mesh visibility toggles
  - wireframe
  - bones
  - weights
  - sections
  - normals
  - tangents
  - UV seams
  - influence-bone selection
  - ambient-light slider
  - reset camera
- bottom log panel

Important implementation note:

- this preview work was added without changing the importer or renderer logic outside the new preview path

### Mesh Preview UI layout fix status

The Mesh Preview left panel was later reworked to use:

- a left-docked parent `Panel`
- `AutoScroll = true`
- a single-column `TableLayoutPanel`
- one control per row

Reason:

- the original left-panel layout clipped controls and did not scale correctly

Current state:

- all Mesh Preview left-panel controls are now placed in a clean vertical stack
- vertical scrolling is available when needed
- no manual overlap positioning remains in that left panel

### Current build status after Mesh Preview work

Latest verification:

- `dotnet build src\\MHUpkManager.csproj -c Release`
- success

Current warnings still present:

- OpenTK package version resolution warnings related to `OpenTK.GLControl`

These warnings are known but are not currently blocking build success.

## 2026-04-02 follow-up: Mesh Preview renderer backends

The Mesh Preview tab no longer uses only a single hardwired OpenGL viewport.

Current state:

- the preview host now supports multiple viewport backends
- `OpenTK` remains available
- `VorticeDirect3D11` was added as a second renderer option
- the Mesh Preview left panel now includes a `Renderer` dropdown for backend selection

### Backend integration notes

The current structure now separates:

- shared preview scene state
- shared camera behavior
- shared Mesh Preview UI
- backend-specific viewport/rendering implementations

This was done so renderer selection can switch the viewport implementation without changing:

- FBX conversion
- UE3 mesh conversion
- scene toggles
- camera semantics
- Mesh Preview tab workflow

### Files added or reworked for backend selection

Main files involved:

- `src/MeshPreview/Controls/MeshPreviewControl.cs`
- `src/MeshPreview/Controls/OpenTkMeshPreviewViewport.cs`
- `src/MeshPreview/Controls/VorticeMeshPreviewViewport.cs`
- `src/MeshPreview/Controls/IMeshPreviewViewportBackend.cs`
- `src/MeshPreview/Controls/MeshPreviewBackend.cs`
- `src/MeshPreview/UI/MeshPreviewUI.cs`
- `src/MHUpkManager.csproj`

### Vortice implementation notes

The first Vortice pass required several follow-up fixes before it became usable:

1. removed a recursive resize/device path that could close the app shortly after switching to the Vortice backend
2. corrected the matrix upload path so the Direct3D renderer no longer double-transposed view/projection/model matrices
3. kept the Vortice shader compilation embedded-source workflow by compiling temporary HLSL files at runtime

### Current Mesh Preview renderer status

Current confirmed state:

- `OpenTK` backend works
- `VorticeDirect3D11` backend now switches successfully
- FBX and UE3 preview content now renders in the Vortice backend

This is considered good enough for now.

No persistence of the selected renderer has been added yet.

### Additional Mesh Preview fix during this period

The `Show UV Seams` option was also corrected during this phase.

Original issue:

- seam lines were being generated between duplicate vertices sharing the same 3D position
- this produced zero-length lines, so the UV seam overlay appeared to do nothing

Current fix:

- UV seams are now detected from actual mesh edges whose UV mapping differs across adjacent triangles
- this change was applied in both:
  - `src/MeshPreview/Converters/FbxToPreviewMeshConverter.cs`
  - `src/MeshPreview/Converters/UE3ToPreviewMeshConverter.cs`

## 2026-04-02 follow-up: Texture Preview tab added

A new in-app `Texture Preview` tab has now been added under `src/TexturePreview`.

Current implemented scope:

- load textures from disk:
  - `PNG`
  - `DDS`
  - `TGA`
  - `JPG`
- load `Texture2D` exports from UPK
- drag-and-drop texture loading into the 2D preview
- metadata display for:
  - resolution
  - format
  - mip count
  - compression
- export current preview texture
- apply loaded textures to the Mesh Preview material state in real time

### Current material-preview integration state

The Texture Preview tab is integrated with the Mesh Preview tab.

Current behavior:

- the selected texture can be assigned to:
  - diffuse
  - normal
  - specular
  - emissive
  - mask
- `Apply To Mesh Preview` pushes the current texture into the Mesh Preview material state
- live preview works on the current mesh preview path

Important note:

- real UPK texture injection from the Texture Preview tab is still intentionally left as a TODO

## 2026-04-02 follow-up: Preview tab layout corrections

The Mesh Preview and Texture Preview tabs were both reworked so the left control panel stays visible and the preview viewport occupies the main area to the right.

Current top-area layout for both tabs:

- parent content panel docked `Fill`
- left control panel docked `Left`
- left control panel width fixed to `260`
- viewport host panel docked `Fill`
- preview control docked `Fill` inside the viewport host

Reason:

- the viewport had previously been able to appear on the far left and visually cover the left-side controls

Current state:

- left-side controls stay visible
- viewport expands into the remaining center/right area
- Mesh Preview and Texture Preview now use the same basic top-area layout structure

Files involved:

- `src/MeshPreview/UI/MeshPreviewUI.cs`
- `src/TexturePreview/UI/TexturePreviewUI.cs`

## 2026-04-02 follow-up: Vortice material-preview debugging note

Additional Vortice material-preview work was started after the Texture Preview integration.

Work attempted during this pass included:

- simplifying the sampler binding path to a single shared sampler
- removing eager fallback texture creation during backend switch
- correcting HLSL/C# constant-buffer padding for the expanded material flag block

Current note:

- a startup-time `ID3D11Device.CreateBuffer(...)` `E_INVALIDARG` was traced to the Vortice mesh constant-buffer creation path during one debugging pass
- further Vortice debugging may still be needed depending on the latest runtime behavior being tested

This note is intentionally preserved so the exact failure area is not lost between sessions.

## 2026-04-02 follow-up: Texture Preview UPK multi-select

The `Select Texture2D Export` dialog in the Texture Preview tab now supports multi-selection.

Current behavior:

- the UPK texture picker uses multi-select list behavior
- Ctrl+Click and Shift+Click selection are supported
- double-click confirms the current multi-selection
- the dialog now returns all selected texture export paths instead of a single path

### Texture Preview load behavior after this change

When loading textures from a UPK:

- every selected `Texture2D` export is loaded
- each loaded texture is logged individually
- the total count of loaded textures is logged
- the first selected texture becomes the active texture shown in the preview control
- if `Apply To Mesh Preview` is enabled, only the first selected texture is applied

Important note:

- multi-texture browsing inside the preview control is still intentionally deferred
- a TODO was left in the control for future thumbnail or cycling support

Files involved:

- `src/TexturePreview/UI/TexturePreviewUI.cs`
- `src/TexturePreview/UI/TexturePreviewControl.cs`

## 2026-04-02 follow-up: Texture Preview reset and labeling pass

The Texture Preview left panel was updated again for clarity and easier state reset.

Current changes:

- the file-load button now clearly indicates supported disk formats
- the UPK-load button now clearly indicates it loads texture exports from a UPK
- the reset action now clears:
  - the currently loaded texture selection
  - the loaded texture batch
  - the preview image
  - the metadata labels
  - the mesh material preview state

Current button labels:

- `Load Texture File (PNG/DDS/TGA/JPG)`
- `Load Texture Export(s) From UPK`
- `Clear Loaded Textures`

File involved:

- `src/TexturePreview/UI/TexturePreviewUI.cs`

## 2026-04-02 follow-up: package warning cleanup completed

The previous deferred OpenTK package-warning cleanup was completed.

Work done:

- removed the old `OpenTK` meta-package reference from `src/MHUpkManager.csproj`
- added explicit `OpenTK.Graphics` and `OpenTK.Mathematics` package references aligned to `4.9.3`
- kept `OpenTK.GLControl` in place

Current result:

- `dotnet build src\\MHUpkManager.csproj -c Release`
- success
- warnings: `0`
- errors: `0`

## 2026-04-02 follow-up: Texture Preview label and batch-load updates

The Texture Preview left panel was refined again after the earlier labeling/reset pass.

Current button labels:

- top button: `Load From Disk`
- second button: `Load From UPK`
- reset button: `Clear Loaded Textures`

Additional UI change:

- the Texture Preview left panel width was increased so the updated labels do not crowd the preview viewport

Additional load-path change:

- `Load From Disk` now supports multi-select file loading
- multiple selected disk textures are loaded into the existing batch-preview path
- the first selected disk texture becomes the active preview texture
- each loaded disk texture is logged individually
- the total disk texture count is logged

Files involved:

- `src/TexturePreview/UI/TexturePreviewUI.cs`

## 2026-04-02 follow-up: Mesh Preview UPK load-speed investigation

Additional Mesh Preview work was done to reduce avoidable UPK-load overhead and to identify the real remaining bottleneck.

### Load-path improvements completed

Work done:

- Mesh Preview now reuses a single `UpkFileRepository` instance instead of creating a new repository for each step
- Mesh Preview now caches the current `UnrealHeader` for the selected UPK path
- the old double-load path was removed so the export picker and the actual mesh load use the same already-open header
- `UnrealHeader` was split into lighter staged read paths so Mesh Preview can stop paying the full object-read cost just to enumerate SkeletalMesh exports

New staged header methods added:

- `ReadTablesAsync(...)`
- `ReadDependsTableAsync(...)`
- `ReadExportObjectAsync(...)`

Files involved:

- `src/MeshPreview/UI/MeshPreviewUI.cs`
- `UpkManager/Models/UpkFile/UnrealHeader.cs`

### Timing instrumentation added

Timing logs were added around the Mesh Preview UPK load path to measure:

- UPK tables ready
- export lookup
- export object reader preparation
- SkeletalMesh parse
- preview mesh conversion
- scene update and refresh
- total load time

### Current measured result

A real-world timed load showed:

- `Mesh Preview timing: preview mesh conversion completed in 85148 ms.`
- `Mesh Preview timing: scene update and refresh completed in 34 ms.`
- `Mesh Preview timing: total load completed in 85273 ms.`

Conclusion:

- the remaining bottleneck is not the UPK loader path
- the dominant cost is now in:
  - `src/MeshPreview/Converters/UE3ToPreviewMeshConverter.cs`

## Starting point for tomorrow

The next session should start in:

- `src/MeshPreview/Converters/UE3ToPreviewMeshConverter.cs`

Exact next steps:

1. add sub-stage timings inside `UE3ToPreviewMeshConverter.Convert(...)`
2. measure:
   - section extraction
   - vertex build
   - bone build
   - bounds build
   - UV seam build
3. optimize the slowest sub-step first

Current best guess:

- `BuildUvSeams(...)` or the section-resolution logic is the most likely hotspot
- but this should be verified with one timing pass before changing behavior

Important note:

- the next optimization target is the UE3 preview converter, not the UPK loader
