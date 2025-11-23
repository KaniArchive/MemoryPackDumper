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
            ret =
            [
                .. ret.AsValueEnumerable().Where(t =>
                    t.Name == Parser.Type2LookFor ||
                    t.BaseType.Name == Parser.Type2LookFor ||
                    IsSubTypeOf(t, Parser.Type2LookFor)
                ).ToArray()
            ];

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
        if (typeDef.BaseType == null || typeDef.BaseType.FullName == "System.Object" ||
            typeDef.BaseType.FullName == "System.ValueType" || typeDef.BaseType.FullName == "System.Enum") return "";
        if (typeDef.BaseType is GenericInstanceType genericBase)
            return TypeStringConverter.TypeToString(genericBase);

        var baseName = typeDef.BaseType.Name;
        if (baseName.Contains('`'))
            baseName = baseName[..baseName.IndexOf('`')];

        return baseName;
    }

    public static void CollectNamespaces(TypeReference typeRef, HashSet<string> namespaces)
    {
        switch (typeRef)
        {
            case GenericInstanceType genericType:
            {
                var elementType = genericType.ElementType.Resolve();
                AddNamespaceIfNeeded(elementType, namespaces);

                foreach (var arg in genericType.GenericArguments)
                    CollectNamespaces(arg, namespaces);
                break;
            }
            case ArrayType arrayType:
                CollectNamespaces(arrayType.ElementType, namespaces);
                break;
            default:
            {
                var resolved = typeRef.Resolve();
                AddNamespaceIfNeeded(resolved, namespaces);
                break;
            }
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

        if (typeDef.Namespace != "UnityEngine") return;
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

    public static void CollectNamespacesForSplitFile(TypeReference typeRef, HashSet<string> namespaces,
        string currentFileNamespace)
    {
        switch (typeRef)
        {
            case GenericInstanceType genericType:
            {
                var elementType = genericType.ElementType.Resolve();
                AddNamespaceForSplitFile(elementType, namespaces, currentFileNamespace);

                foreach (var arg in genericType.GenericArguments)
                    CollectNamespacesForSplitFile(arg, namespaces, currentFileNamespace);
                break;
            }
            case ArrayType arrayType:
                CollectNamespacesForSplitFile(arrayType.ElementType, namespaces, currentFileNamespace);
                break;
            default:
            {
                var resolved = typeRef.Resolve();
                AddNamespaceForSplitFile(resolved, namespaces, currentFileNamespace);
                break;
            }
        }
    }

    private static void AddNamespaceForSplitFile(TypeDefinition? typeDef, HashSet<string> namespaces,
        string currentFileNamespace)
    {
        if (typeDef?.Namespace == null) return;

        if (typeDef.Namespace == "System" || typeDef.Namespace.StartsWith("System.") ||
            typeDef.Namespace == "UnityEngine")
        {
            AddNamespaceIfNeeded(typeDef, namespaces);
            return;
        }

        if (typeDef.Namespace != currentFileNamespace) namespaces.Add(typeDef.Namespace);
    }

    private static bool IsSubTypeOf(TypeDefinition typeToCheck, string ancestorTypeName)
    {
        var currentBaseRef = typeToCheck.BaseType;

        while (currentBaseRef != null)
        {
            if (currentBaseRef.Name == ancestorTypeName)
                return true;

            var currentBaseDef = currentBaseRef.Resolve();

            if (currentBaseDef == null)
                break;
            currentBaseRef = currentBaseDef.BaseType;
        }

        return false;
    }
}