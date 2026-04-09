param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [switch]$BuildDependencies
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$srcDir = Join-Path $repoRoot "src"
$mainProject = Join-Path $srcDir "MHUpkManager.csproj"
$tempProject = Join-Path $srcDir "MHUpkManager.localbuild.csproj"

$projects = @(
    @{
        Project = (Join-Path $repoRoot "DDSLib\DDSLib.csproj")
        Output = (Join-Path $repoRoot "DDSLib\bin\$Configuration\net8.0-windows\DDSLib.dll")
    },
    @{
        Project = (Join-Path $repoRoot "SharpGL\SharpGL\SharpGL.csproj")
        Output = (Join-Path $repoRoot "SharpGL\SharpGL\bin\$Configuration\net8.0-windows\SharpGL.dll")
    },
    @{
        Project = (Join-Path $repoRoot "SharpGL\SharpGL.WinForms\SharpGL.WinForms.csproj")
        Output = (Join-Path $repoRoot "SharpGL\SharpGL.WinForms\bin\$Configuration\net8.0-windows\SharpGL.WinForms.dll")
    },
    @{
        Project = (Join-Path $repoRoot "UpkManager\UpkManager.csproj")
        Output = (Join-Path $repoRoot "UpkManager\bin\$Configuration\net8.0-windows\UpkManager.dll")
    }
)

$referenceBlock = @'
  <ItemGroup>
    <Reference Include="DDSLib">
      <HintPath>..\DDSLib\bin\$(Configuration)\net8.0-windows\DDSLib.dll</HintPath>
    </Reference>
    <Reference Include="SharpGL.WinForms">
      <HintPath>..\SharpGL\SharpGL.WinForms\bin\$(Configuration)\net8.0-windows\SharpGL.WinForms.dll</HintPath>
    </Reference>
    <Reference Include="SharpGL">
      <HintPath>..\SharpGL\SharpGL\bin\$(Configuration)\net8.0-windows\SharpGL.dll</HintPath>
    </Reference>
    <Reference Include="UpkManager">
      <HintPath>..\UpkManager\bin\$(Configuration)\net8.0-windows\UpkManager.dll</HintPath>
    </Reference>
  </ItemGroup>
'@

function Invoke-DotnetBuild {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [string[]]$ExtraArguments = @()
    )

    $arguments = @(
        "build",
        $ProjectPath,
        "-c", $Configuration,
        "-p:Platform=x64",
        "--no-restore"
    )

    if ($ExtraArguments.Count -gt 0) {
        $arguments += $ExtraArguments
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for $ProjectPath"
    }
}

try {
    foreach ($project in $projects) {
        if ($BuildDependencies) {
            Invoke-DotnetBuild -ProjectPath $project.Project
        }
        elseif (-not (Test-Path $project.Output)) {
            throw "Missing dependency output: $($project.Output). Run the script once with -BuildDependencies after NuGet access is available."
        }
    }

    $projectText = Get-Content $mainProject -Raw
    $projectReferencePattern = '(?s)<ItemGroup>\s*<ProjectReference Include="\.\.\\DDSLib\\DDSLib\.csproj"\s*/>\s*<ProjectReference Include="\.\.\\SharpGL\\SharpGL\.WinForms\\SharpGL\.WinForms\.csproj"\s*/>\s*<ProjectReference Include="\.\.\\SharpGL\\SharpGL\\SharpGL\.csproj"\s*/>\s*<ProjectReference Include="\.\.\\UpkManager\\UpkManager\.csproj"\s*/>\s*</ItemGroup>'
    $tempProjectText = [System.Text.RegularExpressions.Regex]::Replace($projectText, $projectReferencePattern, $referenceBlock)

    if ($tempProjectText -eq $projectText) {
        throw "Failed to rewrite project references in $mainProject"
    }

    Set-Content -Path $tempProject -Value $tempProjectText -Encoding UTF8
    Invoke-DotnetBuild -ProjectPath $tempProject -ExtraArguments @(
        "-p:AssemblyName=MHUpkManager",
        "-p:OutputPath=bin\\Debug\\net8.0-windows\\"
    )
}
finally {
    if (Test-Path $tempProject) {
        Remove-Item -LiteralPath $tempProject -Force
    }
}
