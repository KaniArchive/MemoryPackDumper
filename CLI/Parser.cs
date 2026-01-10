using MemoryPackDumper.Assembly;
using MemoryPackDumper.Context;
using MemoryPackDumper.Helpers;
using MemoryPackDumper.Services;
using dnlib.DotNet;
using ZLinq;

namespace MemoryPackDumper.CLI;

public static class Parser
{
    public static void Execute(string dummyDll, string outputFile, string nameSpace,
        string? namespaceToLookFor, string? type2LookFor, string? targetDll, bool splitClass, bool allowHidden,
        bool verbose, bool suppressWarnings)
    {
        ParserOptionsContext.Current = new ParserOptionsContext
        {
            SuppressWarnings = suppressWarnings,
            AllowHidden = allowHidden,
            NamespaceToLookFor = namespaceToLookFor,
            TypeToLookFor = type2LookFor
        };

        if (verbose) Log.EnableDebugLogging();

        if (!Directory.Exists(dummyDll))
        {
            Log.Global.LogDummyDirNotFound(dummyDll);
            Log.Error("Please provide a valid path using -dummydll or -d.");
            Log.Shutdown();
            Environment.Exit(1);
        }

        var assemblyResolver = new AssemblyResolver();
        assemblyResolver.PreSearchPaths.Add(dummyDll);
        var moduleContext = new ModuleContext(assemblyResolver);
        var readerParameters = new ModuleCreationOptions(moduleContext);

        Log.Info("Reading game assemblies...");

        List<string> dllPaths = !string.IsNullOrEmpty(targetDll)
            ? [Path.Combine(dummyDll, targetDll)]
            :
            [
                .. Directory.GetFiles(dummyDll, "*.dll")
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
                Log.Global.LogFileNotFound(targetDll, dummyDll);
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
                typeDef = module.Find(typeFullName, false);
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
        foreach (var fEnum in ParserOptionsContext.Current.DiscoveredEnums.AsValueEnumerable().Select(MemberParser.TypeToEnum))
            schema.Enums.Add(fEnum);

        var context = new CodeGenerationContext(nameSpace, splitClass, outputFile);

        if (splitClass)
        {
            Log.Info($"Writing split C# files to {outputFile}/...");
            FileGeneratorService.WriteSplitFiles(schema, context);
        }
        else
        {
            Log.Info($"Writing C# code to {outputFile}...");
            FileGeneratorService.WriteSingleFile(schema, context);
        }

        Log.Info("Done.");
    }
}