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
            _ => TypeMap.GetValueOrDefault(typeSig.FullName, typeSig.TypeName)
        };
    }

    private static string ConvertGenericType(GenericInstSig genericInstance)
    {
        var baseType = genericInstance.GenericType.TypeName;
        if (baseType.Contains('`')) baseType = baseType[..baseType.IndexOf('`')];

        var genericArgs = genericInstance.GenericArguments.AsValueEnumerable().Select(TypeToString).JoinToString(", ");
        return $"{baseType}<{genericArgs}>";
    }
}