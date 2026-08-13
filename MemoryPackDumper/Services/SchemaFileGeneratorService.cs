using MemoryPackDumper.Assembly;
using MemoryPackDumper.Context;
using Utf8StringInterpolation;

namespace MemoryPackDumper.Services;

public static class SchemaFileGeneratorService
{
    public const string SchemaExtension = ".mpk";

    public static void WriteSingleFile(MemoryPackSchema schema, CodeGenerationContext context)
    {
        using var buffer = Utf8String.CreateWriter(out var writer);

        foreach (var memoryPackEnum in schema.Enums)
        {
            SchemaWriterService.WriteEnum(ref writer, new EnumWriteContext(memoryPackEnum, ""));
            writer.AppendLine();
        }

        foreach (var memoryPackClass in schema.Classes)
        {
            SchemaWriterService.WriteClass(ref writer, new ClassWriteContext(memoryPackClass, ""));
            writer.AppendLine();
        }

        writer.Flush();
        File.WriteAllBytes(context.OutputPath, buffer.ToArray());
    }

    public static void WriteSplitFiles(MemoryPackSchema schema, CodeGenerationContext context)
    {
        OutputPathHelper.EnsureDirectory(context.OutputPath);

        foreach (var memoryPackEnum in schema.Enums)
            WriteEnumFile(BuildPath(context, memoryPackEnum.OriginalNamespace, memoryPackEnum.EnumName),
                memoryPackEnum);

        foreach (var memoryPackClass in schema.Classes)
            WriteClassFile(BuildPath(context, memoryPackClass.OriginalNamespace, memoryPackClass.ClassName),
                memoryPackClass);
    }

    private static string BuildPath(CodeGenerationContext context, string originalNamespace, string typeName)
    {
        var nsContext = NamespaceContext.Build(originalNamespace, context.CustomNamespace);
        return OutputPathHelper.BuildFilePath(context.OutputPath, nsContext.FinalNamespace,
            OutputPathHelper.SanitizeFileName(typeName), SchemaExtension);
    }

    private static void WriteEnumFile(string filePath, MemoryPackEnum memoryPackEnum)
    {
        using var buffer = Utf8String.CreateWriter(out var writer);

        SchemaWriterService.WriteEnum(ref writer, new EnumWriteContext(memoryPackEnum, ""));

        writer.Flush();
        File.WriteAllBytes(filePath, buffer.ToArray());
    }

    private static void WriteClassFile(string filePath, MemoryPackClass memoryPackClass)
    {
        using var buffer = Utf8String.CreateWriter(out var writer);

        SchemaWriterService.WriteClass(ref writer, new ClassWriteContext(memoryPackClass, ""));

        writer.Flush();
        File.WriteAllBytes(filePath, buffer.ToArray());
    }
}
