using MemoryPackDumper.CLI;
using Mono.Cecil;
using ZLinq;

namespace MemoryPackDumper.Assembly;

internal static class TypeHelper
{
    public static List<TypeDefinition> GetAllMemoryPackableTypes(ModuleDefinition module)
    {
        List<TypeDefinition> ret =
        [
            .. module.GetTypes().AsValueEnumerable().Where(t =>
                t.CustomAttributes.AsValueEnumerable().Any(a => a.AttributeType.Name == "MemoryPackableAttribute") ||
                t.Interfaces.AsValueEnumerable().Any(i => i.InterfaceType.Name == "IMemoryPackFormatterRegister")
            ).ToArray()
        ];

        if (!string.IsNullOrEmpty(Parser.NameSpace2LookFor))
            ret = [..ret.AsValueEnumerable().Where(t => t.Namespace == Parser.NameSpace2LookFor).ToArray()];

        // Dedupe
        ret = [..ret.AsValueEnumerable().DistinctBy(t => t.FullName).ToArray()];

        return ret;
    }

    public static string GetTypeKeyword(TypeDefinition typeDef)
    {
        if (typeDef.IsInterface)
            return "interface";
        if (typeDef is { IsAbstract: true, IsSealed: true })
            return "static class";
        if (typeDef.IsAbstract)
            return "abstract class";
        if (typeDef is { IsValueType: true, IsEnum: false })
            return "struct";
        return "class";
    }

    public static string GetBaseType(TypeDefinition typeDef)
    {
        if (typeDef.BaseType != null && typeDef.BaseType.FullName != "System.Object" && typeDef.BaseType.FullName != "System.ValueType" && typeDef.BaseType.FullName != "System.Enum")
        {
            if (typeDef.BaseType is GenericInstanceType genericBase)
                return TypeStringConverter.TypeToString(genericBase);
            
            var baseName = typeDef.BaseType.Name;
            if (baseName.Contains('`'))
                baseName = baseName[..baseName.IndexOf('`')];
            
            return baseName;
        }
        return "";
    }
}