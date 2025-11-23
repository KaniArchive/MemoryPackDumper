using MemoryPackDumper.Assembly;
using MemoryPackDumper.Context;
using Utf8StringInterpolation;
using ZLinq;

namespace MemoryPackDumper.Services;

public static class FileGeneratorService
{
    public static void WriteSingleFile(MemoryPackSchema schema, CodeGenerationContext context)
    {
        using var buffer = Utf8String.CreateWriter(out var stringWriter);

        var namespaces = new HashSet<string> { "MemoryPack" };

        foreach (var cls in schema.Classes)
        {
            foreach (var member in cls.Members)
                TypeHelper.CollectNamespaces(member.Type, namespaces);

            if (cls.BaseTypeReference != null)
                TypeHelper.CollectNamespaces(cls.BaseTypeReference, namespaces);
        }

        foreach (var ns in namespaces.AsValueEnumerable().OrderBy(n => n))
            stringWriter.AppendFormat($"using {ns};\n");
        stringWriter.AppendLine();

        if (!string.IsNullOrEmpty(context.customNamespace))
            stringWriter.AppendFormat($"namespace {context.customNamespace}\n{{\n");

        var indent = string.IsNullOrEmpty(context.customNamespace) ? "" : "    ";

        foreach (var enumContext in schema.Enums.Select(memoryPackEnum => new EnumWriteContext(memoryPackEnum, indent)))
        {
            CodeWriterService.WriteEnum(ref stringWriter, enumContext);
            stringWriter.AppendLine();
        }

        foreach (var classContext in schema.Classes.Select(memoryPackClass =>
                     new ClassWriteContext(memoryPackClass, indent)))
        {
            CodeWriterService.WriteClass(ref stringWriter, classContext);
            stringWriter.AppendLine();
        }

        if (!string.IsNullOrEmpty(context.customNamespace))
            stringWriter.AppendLiteral("}\n");

        stringWriter.Flush();
        File.WriteAllBytes(context.outputPath, buffer.ToArray());
    }

    public static void WriteSplitFiles(MemoryPackSchema schema, CodeGenerationContext context)
    {
        if (!Directory.Exists(context.outputPath))
            Directory.CreateDirectory(context.outputPath);

        foreach (var memoryPackEnum in schema.Enums)
        {
            var nsContext = NamespaceContext.Build(memoryPackEnum.OriginalNamespace, context.customNamespace);
            var filePath = BuildFilePath(context.outputPath, nsContext.finalNamespace, memoryPackEnum.EnumName);
            WriteSingleEnum(filePath, memoryPackEnum, nsContext);
        }

        foreach (var memoryPackClass in schema.Classes)
        {
            var nsContext = NamespaceContext.Build(memoryPackClass.OriginalNamespace, context.customNamespace);
            var fileName = SanitizeFileName(memoryPackClass.ClassName);
            var filePath = BuildFilePath(context.outputPath, nsContext.finalNamespace, fileName);
            WriteSingleClass(filePath, memoryPackClass, nsContext);
        }
    }

    private static string SanitizeFileName(string className)
    {
        var genericIndex = className.IndexOf('<');
        return genericIndex > 0 ? className[..genericIndex] : className;
    }

    private static string BuildFilePath(string outputDir, string namespacePath, string fileName)
    {
        if (string.IsNullOrEmpty(namespacePath))
            return Path.Combine(outputDir, $"{fileName}.cs");

        var folderPath = Path.Combine(outputDir, namespacePath.Replace('.', Path.DirectorySeparatorChar));
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        return Path.Combine(folderPath, $"{fileName}.cs");
    }

    private static void WriteSingleEnum(string filePath, MemoryPackEnum memoryPackEnum, NamespaceContext nsContext)
    {
        using var buffer = Utf8String.CreateWriter(out var stringWriter);

        stringWriter.AppendLine();

        if (!string.IsNullOrEmpty(nsContext.finalNamespace))
            stringWriter.AppendFormat($"namespace {nsContext.finalNamespace};\n\n");

        var indent = string.IsNullOrEmpty(nsContext.finalNamespace) ? "" : null;
        var enumContext = new EnumWriteContext(memoryPackEnum, indent);
        CodeWriterService.WriteEnum(ref stringWriter, enumContext);

        stringWriter.Flush();
        File.WriteAllBytes(filePath, buffer.ToArray());
    }

    private static void WriteSingleClass(string filePath, MemoryPackClass memoryPackClass, NamespaceContext nsContext)
    {
        using var buffer = Utf8String.CreateWriter(out var stringWriter);

        var namespaces = new HashSet<string> { "MemoryPack" };
        CollectClassNamespaces(memoryPackClass, namespaces, nsContext);

        foreach (var ns in namespaces.AsValueEnumerable().OrderBy(n => n))
            stringWriter.AppendFormat($"using {ns};\n");
        stringWriter.AppendLine();

        if (!string.IsNullOrEmpty(nsContext.finalNamespace))
            stringWriter.AppendFormat($"namespace {nsContext.finalNamespace};\n\n");

        var indent = string.IsNullOrEmpty(nsContext.finalNamespace) ? "" : null;
        var classContext = new ClassWriteContext(memoryPackClass, indent);
        CodeWriterService.WriteClass(ref stringWriter, classContext);

        stringWriter.Flush();
        File.WriteAllBytes(filePath, buffer.ToArray());
    }

    private static void CollectClassNamespaces(MemoryPackClass memoryPackClass, HashSet<string> namespaces,
        NamespaceContext nsContext)
    {
        var originalNamespaces = new HashSet<string>();

        foreach (var member in memoryPackClass.Members)
            TypeHelper.CollectNamespacesForSplitFile(member.Type, originalNamespaces,
                memoryPackClass.OriginalNamespace);

        if (memoryPackClass.BaseTypeReference != null)
            TypeHelper.CollectNamespacesForSplitFile(memoryPackClass.BaseTypeReference, originalNamespaces,
                memoryPackClass.OriginalNamespace);

        foreach (var nestedClass in memoryPackClass.NestedClasses)
            CollectClassNamespaces(nestedClass, namespaces, nsContext);

        foreach (var originalNs in originalNamespaces)
            if (originalNs == "System" || originalNs.StartsWith("System."))
                namespaces.Add(originalNs);
            else if (!string.IsNullOrEmpty(nsContext.rootPrefix))
                namespaces.Add($"{nsContext.rootPrefix}.{originalNs}");
            else
                namespaces.Add(originalNs);
    }
}