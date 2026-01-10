using dnlib.DotNet;
using MemoryPackDumper.Helpers;
using ZLinq;

namespace MemoryPackDumper.Assembly;

public static class TypeStringConverter
{
    private static readonly Dictionary<string, string> TypeMap = new()
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
        ["System.Decimal"] = "decimal"
    };

    public static string TypeToString(TypeSig typeSig)
    {
        if (typeSig == null) return "void";

        if (typeSig is GenericInstSig genericInstance) return ConvertGenericType(genericInstance);

        if (typeSig is SZArraySig szArray)
        {
            return TypeToString(szArray.Next) + "[]";
        }
        
        if (typeSig is ArraySig array)
        {
            return TypeToString(array.Next) + "[]";
        }

        var typeDef = typeSig.TryGetTypeDef();
        return typeDef != null ? SystemToStringType(typeDef) : CheckSystemType(typeSig);
    }

    private static string CheckSystemType(TypeSig typeSig)
    {
        if (TypeMap.TryGetValue(typeSig.FullName, out var type))
            return type;

        var name = typeSig.TypeName;
        var ns = typeSig.Namespace ?? "";
        if (ns.StartsWith("System"))
             Log.Global.LogUnknownSystemType(name);

        return name;
    }

    private static string ConvertGenericType(GenericInstSig genericInstance)
    {
        var baseType = genericInstance.GenericType.TypeName;

        if (baseType.Contains('`')) baseType = baseType[..baseType.IndexOf('`')];

        var genericArgs = genericInstance.GenericArguments.AsValueEnumerable().Select(TypeToString).JoinToString(", ");
        return $"{baseType}<{genericArgs}>";
    }

    public static string SystemToStringType(TypeDef typeDef)
    {
        var fullName = typeDef.FullName;
        if (TypeMap.TryGetValue(fullName, out var type))
            return type;

        var name = typeDef.Name.String;
        var ns = typeDef.Namespace?.String ?? "";

        if (ns.StartsWith("System"))
            Log.Global.LogUnknownSystemType(name);

        return name;
    }
}