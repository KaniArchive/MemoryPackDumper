using System.Buffers;
using MemoryPackDumper.Assembly;
using MemoryPackDumper.Context;
using Microsoft.CodeAnalysis.CSharp;
using Utf8StringInterpolation;
using ZLinq;

namespace MemoryPackDumper.Services;

public static class CodeWriterService
{
    public static void WriteClass<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer, ClassWriteContext context)
        where TBufferWriter : IBufferWriter<byte>
    {
        var actualIndent = context.actualIndent;
        var baseType = context.@class.BaseClassName == "" ? "" : $" : {context.@class.BaseClassName}";
        var isInterface = context.@class.TypeKeyword == "interface";

        WriteMemoryPackableAttribute(ref writer, context.@class, actualIndent);

        foreach (var union in context.@class.Unions.AsValueEnumerable().OrderBy(u => u.Tag))
            writer.AppendFormat($"{actualIndent}[MemoryPackUnion({union.Tag}, typeof({union.TypeName}))]\n");

        var typeDeclaration = context.@class.TypeKeyword switch
        {
            "interface" => $"{actualIndent}public partial interface {context.@class.ClassName}{baseType}\n",
            "struct" => $"{actualIndent}public partial struct {context.@class.ClassName}{baseType}\n",
            "abstract" => $"{actualIndent}public abstract partial class {context.@class.ClassName}{baseType}\n",
            "static" => $"{actualIndent}public static partial class {context.@class.ClassName}{baseType}\n",
            _ => $"{actualIndent}public partial class {context.@class.ClassName}{baseType}\n"
        };

        writer.AppendLiteral(typeDeclaration);
        writer.AppendFormat($"{actualIndent}{{\n");

        foreach (var memberContext in context.@class.Members.Select(member =>
                     new MemberWriteContext(member, actualIndent, isInterface))) WriteMember(ref writer, memberContext);

        foreach (var methodContext in context.@class.Methods.Select(method =>
                     new MethodWriteContext(method, actualIndent, context.@class.ClassName)))
            WriteMethod(ref writer, methodContext);

        // Write nested classes
        foreach (var nestedClass in context.@class.NestedClasses)
        {
            writer.AppendLine();
            var nestedContext = new ClassWriteContext(nestedClass, actualIndent + "    ");
            WriteClass(ref writer, nestedContext);
        }

        writer.AppendFormat($"{actualIndent}}}\n");
    }

    public static void WriteEnum<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer, EnumWriteContext context)
        where TBufferWriter : IBufferWriter<byte>
    {
        var actualIndent = context.actualIndent;

        var enumTypeName = TypeStringConverter.SystemToStringType(context.@enum.Type);
        writer.AppendFormat($"{actualIndent}public enum {context.@enum.EnumName} : {enumTypeName}\n");
        writer.AppendFormat($"{actualIndent}{{\n");

        for (var i = 0; i < context.@enum.Fields.Count; i++)
        {
            var field = context.@enum.Fields[i];
            var isLast = i == context.@enum.Fields.Count - 1;
            var fieldName = EscapeKeyword(field.Name);
            writer.AppendFormat($"{actualIndent}    {fieldName} = {field.Value}{(isLast ? "" : ",")}\n");
        }

        writer.AppendFormat($"{actualIndent}}}\n");
    }

    private static void WriteMember<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer,
        MemberWriteContext context)
        where TBufferWriter : IBufferWriter<byte>
    {
        var memberIndent = context.memberIndent;

        if (context.member.Order.HasValue)
            writer.AppendFormat($"{memberIndent}[MemoryPackOrder({context.member.Order.Value})]\n");

        if (context.member.IsInclude)
            writer.AppendFormat($"{memberIndent}[MemoryPackInclude]\n");

        if (context.member.SuppressDefaultInitialization)
            writer.AppendFormat($"{memberIndent}[SuppressDefaultInitialization]\n");

        if (context.member.AllowSerialize)
            writer.AppendFormat($"{memberIndent}[MemoryPackAllowSerialize]\n");

        foreach (var formatter in context.member.CustomFormatters)
            writer.AppendFormat($"{memberIndent}[{formatter}]\n");

        var typeStr = TypeStringConverter.TypeToString(context.member.Type);

        var visibility = context.isInterface ? "" : context.member.IsPublic ? "public " : "private ";

        if (context.member.IsField)
            writer.AppendFormat($"{memberIndent}{visibility}{typeStr} {context.member.Name};\n");
        else
            writer.AppendFormat($"{memberIndent}{visibility}{typeStr} {context.member.Name} {{ get; set; }}\n");
    }

    private static void WriteMethod<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer,
        MethodWriteContext context)
        where TBufferWriter : IBufferWriter<byte>
    {
        var memberIndent = context.memberIndent;

        foreach (var attr in context.method.Attributes)
            writer.AppendFormat($"{memberIndent}[{attr}]\n");

        var visibility = $"{context.method.Visibility} ";
        var staticModifier = context.method.IsStatic ? "static " : "";
        var overrideModifier = context.method.Name == "GetKeyForItem" ? "override " : "";

        var parameters = context.method.Parameters.AsValueEnumerable().Select(p => $"{p.Type} {p.Name}")
            .JoinToString(", ");

        if (context.method.IsConstructor)
        {
            var constructorName = context.className.Contains('<')
                ? context.className[..context.className.IndexOf('<')]
                : context.className;
            writer.AppendFormat($"{memberIndent}{visibility}{constructorName}({parameters}) {{ }}\n");
        }
        else
        {
            var returnType = context.method.ReturnType == "Void" ? "void" : context.method.ReturnType;
            writer.AppendFormat(
                $"{memberIndent}{overrideModifier}{visibility}{staticModifier}{returnType} {context.method.Name}({parameters}) => default;\n");
        }
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

    private static string EscapeKeyword(string identifier)
    {
        var kind = SyntaxFacts.GetKeywordKind(identifier);
        return kind != SyntaxKind.None ? $"@{identifier}" : identifier;
    }
}