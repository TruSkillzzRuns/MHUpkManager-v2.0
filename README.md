# MHUpkManager
Thanks to AlexBond for the orignal manager and work.
Upk Manager for Marvel Heroes

## Local Build Notes

- Repo SDK is pinned by [global.json](C:\Users\TruSkillzzRuns\mhupkmanager-link\global.json) to `.NET SDK 8.0.419`.
- Local NuGet package cache should resolve through the user NuGet config at:
  - `C:\Users\TruSkillzzRuns\AppData\Roaming\NuGet\NuGet.Config`
- On this machine, the stable direct local build command is:

```powershell
dotnet build "C:\Users\TruSkillzzRuns\mhupkmanager-link\src\MHUpkManager.csproj" -c Debug -m:1
```

- The plain parallel form without `-m:1` is still flaky on this environment during project-graph evaluation.
- Current known warnings after a successful build are nullable-annotation warnings in:
  - `src\EnemyConverter\EnemyAvatarPrototypeFieldIdBridgeAnalyzer.cs`
