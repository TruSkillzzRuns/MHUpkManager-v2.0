using System.Reflection;
using UpkManager.Models.UpkFile.Engine.Mesh;
using UpkManager.Models.UpkFile.Objects;
using UpkManager.Repository;

if (args.Length < 4)
{
    Console.Error.WriteLine("Usage: SkeletalExportProbe <mode: ascii|binary|direct> <upkPath> <exportPath> <outputPath>");
    return 2;
}

string mode = args[0];
string upkPath = args[1];
string exportPath = args[2];
string outputPath = args[3];

UpkFileRepository repository = new();
var header = await repository.LoadUpkFile(upkPath).ConfigureAwait(false);
await header.ReadHeaderAsync(null).ConfigureAwait(false);

var export = header.ExportTable
    .FirstOrDefault(e => string.Equals(e.GetPathName(), exportPath, StringComparison.OrdinalIgnoreCase));

if (export == null)
{
    Console.Error.WriteLine($"Export not found: {exportPath}");
    return 3;
}

await export.ParseUnrealObject(false, false).ConfigureAwait(false);

if (export.UnrealObject is not IUnrealObject unrealObject || unrealObject.UObject is not USkeletalMesh skeletalMesh)
{
    Console.Error.WriteLine("Target export is not a parsed USkeletalMesh.");
    return 4;
}

Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);

switch (mode.ToLowerInvariant())
{
    case "ascii":
    {
        object model = CreateModelMesh(skeletalMesh, exportPath);
        MethodInfo exportAscii = GetMhType("MHUpkManager.Model.FbxExporter")
            .GetMethod("ExportAscii", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("MHUpkManager.Model.FbxExporter", "ExportAscii");

        exportAscii.Invoke(null, [outputPath, model]);
        break;
    }
    case "binary":
    {
        object model = CreateModelMesh(skeletalMesh, exportPath);
        MethodInfo export = GetMhType("MHUpkManager.Model.FbxExporter")
            .GetMethod("Export", BindingFlags.Public | BindingFlags.Static, [typeof(string), model.GetType()])
            ?? throw new MissingMethodException("MHUpkManager.Model.FbxExporter", "Export");

        export.Invoke(null, [outputPath, model]);
        break;
    }
    case "direct":
    {
        Type exporterType = GetMhType("MHUpkManager.Model.SkeletalFbxExporter");
        MethodInfo export = exporterType.GetMethod(
            "Export",
            BindingFlags.Public | BindingFlags.Static,
            [typeof(string), typeof(USkeletalMesh), typeof(string), typeof(int), typeof(Action<string>)])
            ?? throw new MissingMethodException(exporterType.FullName, "Export");

        export.Invoke(null, [outputPath, skeletalMesh, exportPath, 0, (Action<string>)Console.WriteLine]);
        break;
    }
    default:
        Console.Error.WriteLine($"Unknown mode: {mode}");
        return 5;
}

Console.WriteLine(outputPath);
return 0;

static object CreateModelMesh(USkeletalMesh skeletalMesh, string meshName)
{
    Type modelMeshType = GetMhType("MHUpkManager.Model.ModelMesh");
    ConstructorInfo ctor = modelMeshType.GetConstructor([typeof(UObject), typeof(string)])
        ?? throw new MissingMethodException(modelMeshType.FullName, ".ctor(UObject, string)");

    return ctor.Invoke([skeletalMesh, meshName]);
}

static Type GetMhType(string fullName)
{
    Assembly assembly = LoadMhAssembly();
    return assembly.GetType(fullName, throwOnError: true)!;
}

static Assembly LoadMhAssembly()
{
    string assemblyPath = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "..",
        "src", "bin", "Release", "net8.0-windows", "MHUpkManager.dll"));

    return Assembly.LoadFrom(assemblyPath);
}
