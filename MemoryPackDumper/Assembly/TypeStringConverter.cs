using System.Collections.Frozen;
using dnlib.DotNet;
using ZLinq;

namespace MemoryPackDumper.Assembly;

public static class TypeStringConverter
{
    private static readonly FrozenDictionary<string, string> TypeMap = new Dictionary<string, string>
    {
        ["System.String"] = "string",
        ["System.Int16"] = "short",
        ["System.UInt16"] = "ushort",
        ["System.Int32"] = "int",
        ["System.UInt32"] = "uint",
        ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong",
        ["System.Boolean"] = "bool",
        ["System.Single"] = "float",
        ["System.Double"] = "double",
        ["System.SByte"] = "sbyte",
        ["System.Byte"] = "byte",
        ["System.Decimal"] = "decimal",
        ["System.Object"] = "object",
        ["System.Void"] = "void",
        ["System.Char"] = "char",
        ["System.IntPtr"] = "nint",
        ["System.UIntPtr"] = "nuint"
    }.ToFrozenDictionary();

    public static string TypeToString(TypeSig? typeSig)
    {
        if (typeSig == null) return "void";

        return typeSig switch
        {
            GenericInstSig genericInstance => ConvertGenericType(genericInstance),
            SZArraySig szArray => TypeToString(szArray.Next) + "[]",
            ArraySig array => TypeToString(array.Next) + "[]",
            ByRefSig or PtrSig or PinnedSig or CModReqdSig or CModOptSig => TypeToString(typeSig.Next),
            GenericSig => typeSig.TypeName,
            _ => TypeMap.GetValueOrDefault(typeSig.FullName, QualifiedName(typeSig))
        };
    }

    private static string ConvertGenericType(GenericInstSig genericInstance)
    {
        var baseType = StripArity(QualifiedName(genericInstance.GenericType?.TypeDefOrRef));

        var genericArgs = genericInstance.GenericArguments.AsValueEnumerable().Select(TypeToString).JoinToString(", ");
        return $"{baseType}<{genericArgs}>";
    }

    private static string QualifiedName(TypeSig typeSig) =>
        QualifiedName(typeSig.ToTypeDefOrRef()) is { Length: > 0 } name ? name : typeSig.TypeName;

    private static string QualifiedName(ITypeDefOrRef? typeDefOrRef)
    {
        if (typeDefOrRef == null) return "";
        if (typeDefOrRef is TypeSpec typeSpec) return TypeToString(typeSpec.TypeSig);

        var typeDef = typeDefOrRef.ResolveTypeDef();
        if (typeDef == null) return StripArity(typeDefOrRef.Name.String);

        var segments = new List<string>();
        var current = typeDef;

        while (current != null)
        {
            segments.Add(StripArity(current.Name.String));
            current = current.DeclaringType;
        }

        segments.Reverse();
        return string.Join('.', segments);
    }

    private static string StripArity(string name) =>
        name.Contains('`') ? name[..name.IndexOf('`')] : name;
}