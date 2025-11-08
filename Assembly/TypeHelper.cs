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
            ret = [.. ret.AsValueEnumerable().Where(t => t.Namespace == Parser.NameSpace2LookFor).ToArray()];
        
        if (!string.IsNullOrEmpty(Parser.Type2LookFor))
            ret = [.. ret.AsValueEnumerable().Where(t =>
                t.Name == Parser.Type2LookFor ||
                t.BaseType.Name == Parser.Type2LookFor ||
                IsSubTypeOf(t, Parser.Type2LookFor)
            ).ToArray()];

        // Dedupe
        ret = [..ret.AsValueEnumerable().DistinctBy(t => t.FullName).ToArray()];

        return ret;
    }

    public static string GetTypeKeyword(TypeDefinition typeDef)
    {
        if (typeDef.IsInterface)
            return "interface";
        if (typeDef is { IsAbstract: true, IsSealed: true })
            return "static";
        if (typeDef.IsAbstract)
            return "abstract";
        if (typeDef is { IsValueType: true, IsEnum: false })
            return "struct";
        return "";
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

    public static void CollectNamespaces(TypeReference typeRef, HashSet<string> namespaces)
    {
        if (typeRef is GenericInstanceType genericType)
        {
            var elementType = genericType.ElementType.Resolve();
            AddNamespaceIfNeeded(elementType, namespaces);
            
            foreach (var arg in genericType.GenericArguments)
                CollectNamespaces(arg, namespaces);
        }
        else if (typeRef is ArrayType arrayType)
        {
            CollectNamespaces(arrayType.ElementType, namespaces);
        }
        else
        {
            var resolved = typeRef.Resolve();
            AddNamespaceIfNeeded(resolved, namespaces);
        }
    }

    private static void AddNamespaceIfNeeded(TypeDefinition? typeDef, HashSet<string> namespaces)
    {
        if (typeDef?.Namespace == null) return;

        if (typeDef.Namespace.StartsWith("System."))
        {
            namespaces.Add(typeDef.Namespace);
            return;
        }

        if (typeDef.Namespace == "UnityEngine")
        {
            switch (typeDef.Name)
            {
                case "Vector2":
                case "Vector3":
                case "Vector4":
                case "Quaternion":
                case "Matrix4x4":
                    namespaces.Add("System.Numerics");
                    break;
            }
        }
    }

    public static bool IsSubTypeOf(TypeDefinition typeToCheck, string ancestorTypeName)
    {
        TypeReference? currentBaseRef = typeToCheck?.BaseType;

        while (currentBaseRef != null)
        {
            if (currentBaseRef.Name == ancestorTypeName)
                return true;

            TypeDefinition currentBaseDef = currentBaseRef.Resolve();

            if (currentBaseDef == null)
                break;
            currentBaseRef = currentBaseDef.BaseType;
        }
        return false;
    }
}