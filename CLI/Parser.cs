using MemoryPackDumper.Assembly;
using MemoryPackDumper.Context;
using MemoryPackDumper.Helpers;
using MemoryPackDumper.Services;
using dnlib.DotNet;
using ZLinq;

namespace MemoryPackDumper.CLI;

public static class Parser
{
    private static string _dummyAssemblyDir = "DummyDll";
    private static string _outputFileName = "MemoryPack.cs";
    private static string? _customNameSpace = "MemoryPackData";
    private static bool _splitClass;
    public static string? NameSpace2LookFor;
    public static string? Type2LookFor;
    public static readonly List<TypeDef> MemoryPackEnumsToAdd = [];
    public static bool SuppressWarnings;

    public static void Execute(string dummyDll, string outputFile, string nameSpace,
        string? namespaceToLookFor, string? type2LookFor, string? targetDll, bool splitClass, bool verbose,
        bool suppressWarnings)
    {
        if (verbose) Log.EnableDebugLogging();

        SuppressWarnings = suppressWarnings;

        _dummyAssemblyDir = dummyDll;
        _outputFileName = outputFile;
        _customNameSpace = nameSpace;
        _splitClass = splitClass;
        NameSpace2LookFor = namespaceToLookFor;
        Type2LookFor = type2LookFor;

        if (!Directory.Exists(_dummyAssemblyDir))
        {
            Log.Global.LogDummyDirNotFound(_dummyAssemblyDir);
            Log.Error("Please provide a valid path using -dummydll or -d.");
            Log.Shutdown();
            Environment.Exit(1);
        }

        var assemblyResolver = new AssemblyResolver();
        assemblyResolver.PreSearchPaths.Add(_dummyAssemblyDir);
        var moduleContext = new ModuleContext(assemblyResolver);
        var readerParameters = new ModuleCreationOptions(moduleContext);

        Log.Info("Reading game assemblies...");

        List<string> dllPaths = !string.IsNullOrEmpty(targetDll)
            ? [Path.Combine(_dummyAssemblyDir, targetDll)]
            :
            [
                .. Directory.GetFiles(_dummyAssemblyDir, "*.dll")
                    .AsValueEnumerable()
                    .Where(path =>
                    {
                        var fileName = Path.GetFileName(path);
                        return !fileName.StartsWith("System") &&
                               !fileName.StartsWith("Unity") &&
                               !fileName.Equals("MemoryPack.dll", StringComparison.OrdinalIgnoreCase);
                    })
            ];

        if (!string.IsNullOrEmpty(targetDll))
        {
            if (!File.Exists(dllPaths[0]))
            {
                Log.Global.LogFileNotFound(targetDll, _dummyAssemblyDir);
                Log.Shutdown();
                Environment.Exit(1);
            }

            Log.Info($"Processing single DLL: {targetDll}");
        }
        else
        {
            Log.Info($"Processing {dllPaths.Count} DLLs from directory");
        }

        var modules = new List<ModuleDef>();
        var allMemoryPackableTypes = new List<TypeDef>();

        foreach (var dllPath in dllPaths)
            try
            {
                var module = ModuleDefMD.Load(dllPath, readerParameters);
                modules.Add(module);
                var types = TypeHelper.GetAllMemoryPackableTypes(module);
                allMemoryPackableTypes.AddRange(types);
                if (verbose) Log.Debug($"Found {types.Count} MemoryPackable types in {Path.GetFileName(dllPath)}");
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to read {Path.GetFileName(dllPath)}: {ex.Message}");
            }

        Log.Info($"Getting a list of MemoryPackable types... Found {allMemoryPackableTypes.Count} total");

        MemoryPackSchema schema = new();
        var processedTypes = new HashSet<string>();
        var typesToProcess = new Queue<string>([.. allMemoryPackableTypes.AsValueEnumerable().Select(t => t.FullName)]);

        while (typesToProcess.Count > 0)
        {
            var typeFullName = typesToProcess.Dequeue();

            if (!processedTypes.Add(typeFullName))
                continue;

            TypeDef? typeDef = null;
            foreach (var module in modules)
            {
                typeDef = module.GetTypes().AsValueEnumerable()
                    .FirstOrDefault(t => t.FullName == typeFullName);
                if (typeDef != null) break;
            }

            if (typeDef == null)
                continue;

            var discoveredTypes = new HashSet<string>();
            var memoryPackClass = MemberParser.TypeToMemoryPackClass(typeDef, discoveredTypes);
            schema.Classes.Add(memoryPackClass);

            foreach (var newType in discoveredTypes)
                typesToProcess.Enqueue(newType);

            Log.Global.LogProgress(processedTypes.Count, allMemoryPackableTypes.Count);
        }

        Log.Info("Adding enums...");
        foreach (var fEnum in MemoryPackEnumsToAdd.AsValueEnumerable().Select(MemberParser.TypeToEnum))
            schema.Enums.Add(fEnum);

        var context = new CodeGenerationContext(_customNameSpace, _splitClass, _outputFileName);

        if (_splitClass)
        {
            Log.Info($"Writing split C# files to {_outputFileName}/...");
            FileGeneratorService.WriteSplitFiles(schema, context);
        }
        else
        {
            Log.Info($"Writing C# code to {_outputFileName}...");
            FileGeneratorService.WriteSingleFile(schema, context);
        }

        Log.Info("Done.");
    }
}