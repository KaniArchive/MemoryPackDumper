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
        var actualIndent = context.ActualIndent;
        var baseType = context.Class.BaseClassName == "" ? "" : $" : {context.Class.BaseClassName}";
        var isInterface = context.Class.TypeKeyword == "interface";

        WriteMemoryPackableAttribute(ref writer, context.Class, actualIndent);

        foreach (var union in context.Class.Unions.AsValueEnumerable().OrderBy(u => u.Tag))
            writer.AppendFormat($"{actualIndent}[MemoryPackUnion({union.Tag}, typeof({union.TypeName}))]\n");

        var typeDeclaration = context.Class.TypeKeyword switch
        {
            "interface" => $"{actualIndent}public partial interface {context.Class.ClassName}{baseType}\n",
            "struct" => $"{actualIndent}public partial struct {context.Class.ClassName}{baseType}\n",
            "abstract" => $"{actualIndent}public abstract partial class {context.Class.ClassName}{baseType}\n",
            "static" => $"{actualIndent}public static partial class {context.Class.ClassName}{baseType}\n",
            _ => $"{actualIndent}public partial class {context.Class.ClassName}{baseType}\n"
        };

        writer.AppendLiteral(typeDeclaration);
        writer.AppendFormat($"{actualIndent}{{\n");

        foreach (var memberContext in context.Class.Members.Select(member =>
                     new MemberWriteContext(member, actualIndent, isInterface))) WriteMember(ref writer, memberContext);

        foreach (var methodContext in context.Class.Methods.Select(method =>
                     new MethodWriteContext(method, actualIndent, context.Class.ClassName)))
            WriteMethod(ref writer, methodContext);

        foreach (var nestedClass in context.Class.NestedClasses)
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
        var actualIndent = context.ActualIndent;

        writer.AppendFormat($"{actualIndent}public enum {context.Enum.EnumName} : {context.Enum.UnderlyingType}\n");
        writer.AppendFormat($"{actualIndent}{{\n");

        for (var i = 0; i < context.Enum.Fields.Count; i++)
        {
            var field = context.Enum.Fields[i];
            var isLast = i == context.Enum.Fields.Count - 1;
            var fieldName = EscapeKeyword(field.Name);
            writer.AppendFormat($"{actualIndent}    {fieldName} = {field.Value}{(isLast ? "" : ",")}\n");
        }

        writer.AppendFormat($"{actualIndent}}}\n");
    }

    private static void WriteMember<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer,
        MemberWriteContext context)
        where TBufferWriter : IBufferWriter<byte>
    {
        var memberIndent = context.MemberIndent;

        if (context.Member.Order.HasValue)
            writer.AppendFormat($"{memberIndent}[MemoryPackOrder({context.Member.Order.Value})]\n");

        if (context.Member.IsInclude)
            writer.AppendFormat($"{memberIndent}[MemoryPackInclude]\n");

        if (context.Member.SuppressDefaultInitialization)
            writer.AppendFormat($"{memberIndent}[SuppressDefaultInitialization]\n");

        if (context.Member.AllowSerialize)
            writer.AppendFormat($"{memberIndent}[MemoryPackAllowSerialize]\n");

        foreach (var formatter in context.Member.CustomFormatters)
            writer.AppendFormat($"{memberIndent}[{formatter}]\n");

        var typeStr = TypeStringConverter.TypeToString(context.Member.Type);

        var visibility = context.IsInterface ? "" : context.Member.IsPublic ? "public " : "private ";

        if (context.Member.IsField)
            writer.AppendFormat($"{memberIndent}{visibility}{typeStr} {context.Member.Name};\n");
        else
            writer.AppendFormat($"{memberIndent}{visibility}{typeStr} {context.Member.Name} {{ get; set; }}\n");
    }

    private static void WriteMethod<TBufferWriter>(ref Utf8StringWriter<TBufferWriter> writer,
        MethodWriteContext context)
        where TBufferWriter : IBufferWriter<byte>
    {
        var memberIndent = context.MemberIndent;

        foreach (var attr in context.Method.Attributes)
            writer.AppendFormat($"{memberIndent}[{attr}]\n");

        var visibility = $"{context.Method.Visibility} ";
        var staticModifier = context.Method.IsStatic ? "static " : "";
        var overrideModifier = context.Method.Name == "GetKeyForItem" ? "override " : "";

        var parameters = context.Method.Parameters.AsValueEnumerable().Select(p => $"{p.Type} {p.Name}")
            .JoinToString(", ");

        if (context.Method.IsConstructor)
        {
            var constructorName = context.ClassName.Contains('<')
                ? context.ClassName[..context.ClassName.IndexOf('<')]
                : context.ClassName;
            writer.AppendFormat($"{memberIndent}{visibility}{constructorName}({parameters}) {{ }}\n");
        }
        else
        {
            var returnType = context.Method.ReturnType == "Void" ? "void" : context.Method.ReturnType;
            writer.AppendFormat(
                $"{memberIndent}{overrideModifier}{visibility}{staticModifier}{returnType} {context.Method.Name}({parameters}) => default;\n");
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