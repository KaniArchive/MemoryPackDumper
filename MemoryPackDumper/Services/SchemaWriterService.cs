using System.Buffers;
using System.Collections.Frozen;
using MemoryPackDumper.Assembly;
using MemoryPackDumper.Context;
using Utf8StringInterpolation;
using ZLinq;

namespace MemoryPackDumper.Services;

public static class SchemaWriterService
{
    private const string PrimaryConstructorAttribute = "MemoryPackConstructor";

    private static readonly FrozenDictionary<string, string> ClassModifierMap = new Dictionary<string, string>
    {
        ["VersionTolerant"] = "[version_tolerant]",
        ["CircularReference"] = "[circular_ref]",
        ["Collection"] = "[collection]"
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> CallbackKindMap = new Dictionary<string, string>
    {
        ["MemoryPackOnSerializing"] = "on_serializing",
        ["MemoryPackOnSerialized"] = "on_serialized",
        ["MemoryPackOnDeserializing"] = "on_deserializing",
        ["MemoryPackOnDeserialized"] = "on_deserialized"
    }.ToFrozenDictionary();

    public static void WriteEnum<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer, EnumWriteContext context)
        where TBufferWriter : IBufferWriter<byte>
    {
        var indent = context.ActualIndent;
        var underlying = SchemaTypeConverter.MapEnumUnderlyingType(context.Enum.UnderlyingType);

        writer.AppendFormat($"{indent}enum {SchemaTypeConverter.Identifier(context.Enum.EnumName)} : {underlying} {{\n");

        foreach (var field in context.Enum.Fields)
            writer.AppendFormat($"{indent}    {SchemaTypeConverter.Identifier(field.Name)} = {field.Value},\n");

        writer.AppendFormat($"{indent}}}\n");
    }

    public static void WriteClass<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer, ClassWriteContext context)
        where TBufferWriter : IBufferWriter<byte>
    {
        var indent = context.ActualIndent;
        var memberIndent = $"{indent}    ";
        var memoryPackClass = context.Class;

        var baseType = SchemaTypeConverter.StripGenerics(memoryPackClass.BaseClassName);
        var baseClause = baseType.Length == 0 ? "" : $" : {SchemaTypeConverter.Identifier(baseType)}";
        var modifiers = BuildClassModifiers(memoryPackClass);

        writer.AppendFormat(
            $"{indent}class {SchemaTypeConverter.Identifier(memoryPackClass.ClassName)}{baseClause}{modifiers} {{\n");

        WriteMembers(ref writer, memoryPackClass, memberIndent);
        WriteConstructors(ref writer, memoryPackClass, memberIndent);
        WriteUnions(ref writer, memoryPackClass, memberIndent);
        WriteCallbacks(ref writer, memoryPackClass, memberIndent);

        writer.AppendFormat($"{indent}}}\n");

        foreach (var nestedClass in memoryPackClass.NestedClasses)
        {
            writer.AppendLine();
            WriteClass(ref writer, new ClassWriteContext(nestedClass, indent));
        }
    }

    private static string BuildClassModifiers(MemoryPackClass memoryPackClass) =>
        memoryPackClass.GenerateType != null &&
        ClassModifierMap.TryGetValue(memoryPackClass.GenerateType, out var modifier)
            ? $" {modifier}"
            : "";

    private static void WriteMembers<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer,
        MemoryPackClass memoryPackClass, string indent)
        where TBufferWriter : IBufferWriter<byte>
    {
        var nextIndex = 0;

        foreach (var member in memoryPackClass.Members)
        {
            var index = member.Order ?? nextIndex;
            nextIndex = index + 1;

            var type = SchemaTypeConverter.TypeToString(member.Type);
            var name = SchemaTypeConverter.Identifier(member.Name);

            writer.AppendFormat($"{indent}{index}: {type} {name}{BuildMemberModifiers(member)};\n");
        }
    }

    private static string BuildMemberModifiers(MemoryPackMember member)
    {
        var modifiers = new List<string>();

        if (member.IsReadOnly) modifiers.Add("readonly");
        if (member.IsRequired) modifiers.Add("required");
        if (member.IsInit) modifiers.Add("init");
        if (member.IsIgnored) modifiers.Add("ignore");

        foreach (var formatter in member.CustomFormatters)
            modifiers.Add($"@formatter(\"{formatter}\")");

        return modifiers.Count == 0 ? "" : $" {string.Join(' ', modifiers)}";
    }

    private static void WriteConstructors<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer,
        MemoryPackClass memoryPackClass, string indent)
        where TBufferWriter : IBufferWriter<byte>
    {
        foreach (var method in memoryPackClass.Methods.AsValueEnumerable().Where(m => m.IsConstructor))
        {
            var parameters = method.Parameters.AsValueEnumerable()
                .Select(p => SchemaTypeConverter.Identifier(p.Name)).JoinToString(", ");
            var primary = method.Attributes.Contains(PrimaryConstructorAttribute) ? " [primary]" : "";

            writer.AppendFormat($"{indent}constructor({parameters}){primary};\n");
        }
    }

    private static void WriteUnions<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer,
        MemoryPackClass memoryPackClass, string indent)
        where TBufferWriter : IBufferWriter<byte>
    {
        foreach (var union in memoryPackClass.Unions.AsValueEnumerable().OrderBy(u => u.Tag))
            writer.AppendFormat($"{indent}union {union.Tag}: {SchemaTypeConverter.Identifier(union.TypeName)};\n");
    }

    private static void WriteCallbacks<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer,
        MemoryPackClass memoryPackClass, string indent)
        where TBufferWriter : IBufferWriter<byte>
    {
        foreach (var method in memoryPackClass.Methods.AsValueEnumerable().Where(m => !m.IsConstructor))
        foreach (var attribute in method.Attributes)
        {
            if (!CallbackKindMap.TryGetValue(attribute, out var kind)) continue;

            writer.AppendFormat($"{indent}@callback {kind}({SchemaTypeConverter.Identifier(method.Name)});\n");
        }
    }
}
