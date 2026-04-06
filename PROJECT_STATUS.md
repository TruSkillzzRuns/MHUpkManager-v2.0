# MHUpkManager Project Status

This is the short current-status document for `MHUpkManager`.

For more detailed subsystem notes, see [PROJECT_REFERENCE.md](C:/Users/TruSkillzzRuns/mhupkmanager-link/PROJECT_REFERENCE.md).

## What The App Is

`MHUpkManager` is a Marvel Heroes Omega modding tool focused on:

- UPK browsing and inspection
- skeletal mesh export and import
- mesh preview
- texture and material workflows
- skeletal mesh retargeting onto original MHO skeletons
- backup recovery

## Current Top-Level Workspaces

- `Objects`
- `Mesh`
- `Backup`
- `Texture`
- `Retarget`

## Current State By Area

### Mesh Export

- working for the validated skeletal test case
- Blender skeletal import issue was fixed in `src/Model/SkeletalFbxExporter.cs`
- current exporter matrix conversion should be treated as stable unless a new regression is reproduced

### Mesh Import

- UPK rewrite and mesh injection path is functional
- imported SkeletalMesh can be rebuilt and written back into UPK
- remaining importer gap is true section deletion behavior

### Mesh Preview

- working as the main mesh validation tool
- supports `OpenTK` and `Vortice`
- `Vortice` is still the better long-term Windows-native direction

### Texture

- texture workspace is fully grouped and functional
- texture injection, apply-plan preview, manifest output, and rollback all exist
- remaining active problem is runtime compatibility, not basic file writing
- current likely blocker is material / texture / shader-state mismatch

### Retarget

- structurally functional
- one-click bind onto original MHO skeleton works
- export and UPK replacement work
- in-game load and locomotion work in tested scenarios
- main unsolved problem is final deformation quality

### Backup

- scans `.bak` files
- shows original target paths
- restores selected backups

## Most Important Current Blockers

1. Retarget deformation fidelity
2. Texture runtime compatibility and material hookup debugging
3. Explicit section removal behavior during skeletal mesh import

## Most Important Confirmed Conclusions

- package writing is no longer the main blocker for retargeting
- animation hookup is no longer the main blocker for retargeting
- deformation quality is the main retarget blocker
- texture injection writes successfully, but runtime compatibility is still not solved
- the holographic / blue visual issue is more likely material or texture-state related than renderer related

## Most Important Code Areas

- `src/Model/SkeletalFbxExporter.cs`
- `src/MeshImporter`
- `src/TexturePreview`
- `src/TextureWorkspace`
- `src/Retargeting`
- `src/BackupManager`
- `src/MainForm.cs`

## Build

Validation command:

```powershell
dotnet build 'C:\Users\TruSkillzzRuns\mhupkmanager-link\src\MHUpkManager.csproj' -c Release
```

Expected state:

- builds cleanly when `MHUpkManager.exe` is not already running

## Archived Docs

Older markdown notes are now in `archive/docs`.
