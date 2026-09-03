using MemoryPackDumper.Assembly;
using MemoryPackDumper.Context;
using Utf8StringInterpolation;
using ZLinq;

namespace MemoryPackDumper.Services;

public static class FileGeneratorService
{
    public const string CSharpExtension = ".cs";

    public static void WriteSingleFile(MemoryPackSchema schema, CodeGenerationContext context) =>
        File.WriteAllBytes(context.OutputPath, BuildSingleFile(schema, context));

    public static byte[] BuildSingleFile(MemoryPackSchema schema, CodeGenerationContext context)
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

        if (!string.IsNullOrEmpty(context.CustomNamespace))
            stringWriter.AppendFormat($"namespace {context.CustomNamespace};\n\n");

        var indent = string.IsNullOrEmpty(context.CustomNamespace) ? "" : null;

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

        stringWriter.Flush();
        return buffer.ToArray();
    }

    public static void WriteSplitFiles(MemoryPackSchema schema, CodeGenerationContext context)
    {
        OutputPathHelper.EnsureDirectory(context.OutputPath);

        foreach (var memoryPackEnum in schema.Enums)
        {
            var nsContext = NamespaceContext.Build(memoryPackEnum.OriginalNamespace, context.CustomNamespace);
            var filePath = BuildFilePath(nsContext, context.OutputPath, memoryPackEnum.EnumName);
            WriteSingleEnum(filePath, memoryPackEnum, nsContext);
        }

        foreach (var memoryPackClass in schema.Classes)
        {
            var nsContext = NamespaceContext.Build(memoryPackClass.OriginalNamespace, context.CustomNamespace);
            var filePath = BuildFilePath(nsContext, context.OutputPath, memoryPackClass.ClassName);
            WriteSingleClass(filePath, memoryPackClass, nsContext);
        }
    }

    private static string BuildFilePath(NamespaceContext nsContext, string outputDir, string typeName) =>
        OutputPathHelper.BuildFilePath(outputDir, nsContext.FinalNamespace,
            OutputPathHelper.SanitizeFileName(typeName), CSharpExtension);

    private static void WriteSingleEnum(string filePath, MemoryPackEnum memoryPackEnum, NamespaceContext nsContext)
    {
        using var buffer = Utf8String.CreateWriter(out var stringWriter);

        stringWriter.AppendLine();

        if (!string.IsNullOrEmpty(nsContext.FinalNamespace))
            stringWriter.AppendFormat($"namespace {nsContext.FinalNamespace};\n\n");

        var indent = string.IsNullOrEmpty(nsContext.FinalNamespace) ? "" : null;
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

        if (!string.IsNullOrEmpty(nsContext.FinalNamespace))
            stringWriter.AppendFormat($"namespace {nsContext.FinalNamespace};\n\n");

        var indent = string.IsNullOrEmpty(nsContext.FinalNamespace) ? "" : null;
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
            else if (!string.IsNullOrEmpty(nsContext.RootPrefix))
                namespaces.Add($"{nsContext.RootPrefix}.{originalNs}");
            else
                namespaces.Add(originalNs);
    }
}