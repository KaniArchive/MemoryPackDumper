using System.Buffers;
using MemoryPackDumper.Helpers;
using MemoryPackDumper.Assembly;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Utf8StringInterpolation;
using ZLinq;

namespace MemoryPackDumper.CLI;

public static class Parser
{
    private static string _dummyAssemblyDir = "DummyDll";
    private static string _outputFileName = "MemoryPack.cs";
    private static string? _customNameSpace = "MemoryPackData";
    public static string? NameSpace2LookFor;
    public static readonly List<TypeDefinition> MemoryPackEnumsToAdd = [];
    public static bool SuppressWarnings;

    public static void Execute(string dummyDll, string outputFile, string nameSpace,
        string? namespaceToLookFor, string? targetDll, bool verbose, bool suppressWarnings)
    {
        if (verbose) Log.EnableDebugLogging();

        SuppressWarnings = suppressWarnings;

        _dummyAssemblyDir = dummyDll;
        _outputFileName = outputFile;
        _customNameSpace = nameSpace;
        NameSpace2LookFor = namespaceToLookFor;

        if (!Directory.Exists(_dummyAssemblyDir))
        {
            Log.Global.LogDummyDirNotFound(_dummyAssemblyDir);
            Log.Error("Please provide a valid path using -dummydll or -d.");
            Log.Shutdown();
            Environment.Exit(1);
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(_dummyAssemblyDir);
        var readerParameters = new ReaderParameters
        {
            AssemblyResolver = resolver
        };

        Log.Info("Reading game assemblies...");

        List<string> dllPaths = !string.IsNullOrEmpty(targetDll)
            ? [Path.Combine(_dummyAssemblyDir, targetDll)]
            : [.. Directory.GetFiles(_dummyAssemblyDir, "*.dll")
                .AsValueEnumerable()
                .Where(path =>
                {
                    var fileName = Path.GetFileName(path);
                    return !fileName.StartsWith("System") &&
                           !fileName.StartsWith("Unity") &&
                           !fileName.Equals("MemoryPack.dll", StringComparison.OrdinalIgnoreCase);
                })];

        if (!string.IsNullOrEmpty(targetDll))
        {
            if (!File.Exists(dllPaths[0]))
            {
                Log.Global.LogFileNotFound(targetDll!, _dummyAssemblyDir);
                Log.Shutdown();
                Environment.Exit(1);
            }
            Log.Info($"Processing single DLL: {targetDll}");
        }
        else
        {
            Log.Info($"Processing {dllPaths.Count} DLLs from directory");
        }

        var assemblies = new List<AssemblyDefinition>();
        var allMemoryPackableTypes = new List<TypeDefinition>();

        foreach (var dllPath in dllPaths)
        {
            try
            {
                var assembly = AssemblyDefinition.ReadAssembly(dllPath, readerParameters);
                assemblies.Add(assembly);
                var types = TypeHelper.GetAllMemoryPackableTypes(assembly.MainModule);
                allMemoryPackableTypes.AddRange(types);
                if (verbose) Log.Debug($"Found {types.Count} MemoryPackable types in {Path.GetFileName(dllPath)}");
            }
            catch (Exception ex)
            {
                Log.Warning($"Failed to read {Path.GetFileName(dllPath)}: {ex.Message}");
            }
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

            TypeDefinition? typeDef = null;
            foreach (var assembly in assemblies)
            {
                typeDef = assembly.MainModule.GetTypes().AsValueEnumerable().FirstOrDefault(t => t.FullName == typeFullName);
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
        {
            schema.Enums.Add(fEnum);
        }

        Log.Info($"Writing C# code to {_outputFileName}...");

        WriteSchema(_outputFileName, schema);

        Log.Info("Done.");
    }

    private static void WriteSchema(string fileName, MemoryPackSchema schema)
    {
        using var buffer = Utf8String.CreateWriter(out var stringWriter);

        var namespaces = new HashSet<string>
        {
            "MemoryPack"
        };

        foreach (var _ in schema.Classes
                     .SelectMany(cls => cls.Members, (_, member) => TypeStringConverter.TypeToString(member.Type))
                     .Where(typeStr =>
                         typeStr.Contains("List<") || typeStr.Contains("Dictionary<") || typeStr.Contains("HashSet<")))
            namespaces.Add("System.Collections.Generic");

        foreach (var ns in namespaces.AsValueEnumerable().OrderBy(n => n))
        {
            stringWriter.AppendFormat($"using {ns};\n");
        }
        stringWriter.AppendLine();

        if (!string.IsNullOrEmpty(_customNameSpace)) stringWriter.AppendFormat($"namespace {_customNameSpace}\n{{\n");

        foreach (var memoryPackEnum in schema.Enums)
        {
            WriteEnum(ref stringWriter, memoryPackEnum);
            stringWriter.AppendLine();
        }

        foreach (var memoryPackClass in schema.Classes)
        {
            WriteClass(ref stringWriter, memoryPackClass);
            stringWriter.AppendLine();
        }

        if (!string.IsNullOrEmpty(_customNameSpace)) stringWriter.AppendLiteral("}\n");

        stringWriter.Flush();

        File.WriteAllBytes(fileName, buffer.ToArray());
    }

    private static void WriteClass<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer,
        MemoryPackClass memoryPackClass)
        where TBufferWriter : IBufferWriter<byte>
    {
        var indent = string.IsNullOrEmpty(_customNameSpace) ? "" : "    ";
        var baseType = memoryPackClass.BaseClassName == "" ? "" : $" : {memoryPackClass.BaseClassName}";

        WriteMemoryPackableAttribute(ref writer, memoryPackClass, indent);

        foreach (var union in memoryPackClass.Unions.AsValueEnumerable().OrderBy(u => u.Tag))
            writer.AppendFormat($"{indent}[MemoryPackUnion({union.Tag}, typeof({union.TypeName}))]\n");

        writer.AppendFormat($"{indent}public partial {memoryPackClass.TypeKeyword} {memoryPackClass.ClassName}{baseType}\n");
        writer.AppendFormat($"{indent}{{\n");

        foreach (var member in memoryPackClass.Members) WriteMember(ref writer, member, indent);

        writer.AppendFormat($"{indent}}}\n");
    }

    private static void WriteMemoryPackableAttribute<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer,
        MemoryPackClass memoryPackClass, string indent)
        where TBufferWriter : IBufferWriter<byte>
    {
        var attrParams = new List<string>();

        if (!EnumMapper.IsDefaultGenerateType(memoryPackClass.GenerateType))
            attrParams.Add($"GenerateType.{memoryPackClass.GenerateType}");

        if (!EnumMapper.IsDefaultSerializeLayout(memoryPackClass.SerializeLayout))
            attrParams.Add($"SerializeLayout.{memoryPackClass.SerializeLayout}");

        if (attrParams.Count > 0)
            writer.AppendFormat($"{indent}[MemoryPackable({string.Join(", ", attrParams)})]\n");
        else
            writer.AppendFormat($"{indent}[MemoryPackable]\n");
    }

    private static void WriteMember<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer, MemoryPackMember member,
        string indent)
        where TBufferWriter : IBufferWriter<byte>
    {
        var memberIndent = indent + "    ";

        if (member.Order.HasValue) writer.AppendFormat($"{memberIndent}[MemoryPackOrder({member.Order.Value})]\n");

        if (member.IsInclude) writer.AppendFormat($"{memberIndent}[MemoryPackInclude]\n");

        if (member.SuppressDefaultInitialization)
            writer.AppendFormat($"{memberIndent}[SuppressDefaultInitialization]\n");

        if (member.AllowSerialize) writer.AppendFormat($"{memberIndent}[MemoryPackAllowSerialize]\n");

        foreach (var formatter in member.CustomFormatters) writer.AppendFormat($"{memberIndent}[{formatter}]\n");

        var typeStr = TypeStringConverter.TypeToString(member.Type);
        var visibility = member.IsPublic ? "public" : "private";

        if (member.IsField)
            writer.AppendFormat($"{memberIndent}{visibility} {typeStr} {member.Name};\n");
        else
            writer.AppendFormat($"{memberIndent}{visibility} {typeStr} {member.Name} {{ get; set; }}\n");
    }

    private static void WriteEnum<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer,
        MemoryPackEnum memoryPackEnum)
        where TBufferWriter : IBufferWriter<byte>
    {
        var indent = string.IsNullOrEmpty(_customNameSpace) ? "" : "    ";

        var enumTypeName = TypeStringConverter.SystemToStringType(memoryPackEnum.Type);
        writer.AppendFormat($"{indent}public enum {memoryPackEnum.EnumName} : {enumTypeName}\n");
        writer.AppendFormat($"{indent}{{\n");

        for (var i = 0; i < memoryPackEnum.Fields.Count; i++)
        {
            var field = memoryPackEnum.Fields[i];
            var isLast = i == memoryPackEnum.Fields.Count - 1;
            var fieldName = EscapeKeyword(field.Name);
            writer.AppendFormat($"{indent}    {fieldName} = {field.Value}{(isLast ? "" : ",")}\n");
        }

        writer.AppendFormat($"{indent}}}\n");
    }

    private static string EscapeKeyword(string identifier)
    {
        var kind = SyntaxFacts.GetKeywordKind(identifier);
        return kind != SyntaxKind.None ? $"@{identifier}" : identifier;
    }
}