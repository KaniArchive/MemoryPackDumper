using MemoryPackDumper.Context;
using dnlib.DotNet;
using ZLinq;

namespace MemoryPackDumper.Assembly;

internal static class TypeHelper
{
    public static List<TypeDef> GetAllMemoryPackableTypes(ModuleDef module)
    {
        List<TypeDef> ret =
        [
            .. module.GetTypes().AsValueEnumerable().Where(t =>
                t.CustomAttributes.AsValueEnumerable().Any(a => a.AttributeType.Name == "MemoryPackableAttribute") ||
                t.Interfaces.AsValueEnumerable().Any(i => i.Interface.Name == "IMemoryPackFormatterRegister")
            ).ToArray()
        ];

        var opts = ParserOptionsContext.Current;

        if (!string.IsNullOrEmpty(opts.NamespaceToLookFor))
            ret = [.. ret.AsValueEnumerable().Where(t => t.Namespace == opts.NamespaceToLookFor).ToArray()];

        if (!string.IsNullOrEmpty(opts.TypeToLookFor))
            ret =
            [
                .. ret.AsValueEnumerable().Where(t =>
                    t.Name == opts.TypeToLookFor ||
                    (t.BaseType != null && t.BaseType.Name == opts.TypeToLookFor) ||
                    IsSubTypeOf(t, opts.TypeToLookFor)
                ).ToArray()
            ];

        ret = [..ret.AsValueEnumerable().DistinctBy(t => t.FullName).ToArray()];

        return ret;
    }

    public static string GetTypeKeyword(TypeDef typeDef)
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

    public static string GetBaseType(TypeDef typeDef)
    {
        if (typeDef.BaseType == null || typeDef.BaseType.FullName == "System.Object" ||
            typeDef.BaseType.FullName == "System.ValueType" || typeDef.BaseType.FullName == "System.Enum") return "";
        
        if (typeDef.BaseType is TypeSpec typeSpec && typeSpec.TypeSig is GenericInstSig genericSig)
             return TypeStringConverter.TypeToString(genericSig);

        var baseName = typeDef.BaseType.Name.String;
        if (baseName.Contains('`'))
            baseName = baseName[..baseName.IndexOf('`')];

        return baseName;
    }

    public static void CollectNamespaces(TypeSig typeSig, HashSet<string> namespaces)
    {
        switch (typeSig)
        {
            case GenericInstSig genericType:
            {
                var elementType = genericType.GenericType.ToTypeDefOrRef().ResolveTypeDef();
                AddNamespaceIfNeeded(elementType, namespaces);

                foreach (var arg in genericType.GenericArguments)
                    CollectNamespaces(arg, namespaces);
                break;
            }
            case SZArraySig szArrayType:
                CollectNamespaces(szArrayType.Next, namespaces);
                break;
            case ArraySig arrayType:
                CollectNamespaces(arrayType.Next, namespaces);
                break;
            default:
            {
                var resolved = typeSig.TryGetTypeDef();
                AddNamespaceIfNeeded(resolved, namespaces);
                break;
            }
        }
    }

    public static void CollectNamespaces(ITypeDefOrRef? typeRef, HashSet<string> namespaces)
    {
        if (typeRef == null) return;

        if (typeRef is TypeSpec typeSpec)
        {
            CollectNamespaces(typeSpec.TypeSig, namespaces);
            return;
        }

        var resolved = typeRef.ResolveTypeDef();
        AddNamespaceIfNeeded(resolved, namespaces);
    }

    private static void AddNamespaceIfNeeded(TypeDef? typeDef, HashSet<string> namespaces)
    {
        if (typeDef == null) return;
        var ns = typeDef.Namespace?.String;
        if (string.IsNullOrEmpty(ns)) return;

        if (ns.StartsWith("System."))
        {
            namespaces.Add(ns);
            return;
        }

        if (ns != "UnityEngine") return;
        var name = typeDef.Name?.String;
        switch (name)
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

    public static void CollectNamespacesForSplitFile(TypeSig typeSig, HashSet<string> namespaces,
        string currentFileNamespace)
    {
        switch (typeSig)
        {
            case GenericInstSig genericType:
            {
                AddNamespaceForSplitFile(genericType.GenericType?.TypeDefOrRef, namespaces, currentFileNamespace);

                foreach (var arg in genericType.GenericArguments)
                    CollectNamespacesForSplitFile(arg, namespaces, currentFileNamespace);
                break;
            }
            case SZArraySig szArrayType:
                CollectNamespacesForSplitFile(szArrayType.Next, namespaces, currentFileNamespace);
                break;
            case ArraySig arrayType:
                CollectNamespacesForSplitFile(arrayType.Next, namespaces, currentFileNamespace);
                break;
            default:
                AddNamespaceForSplitFile(typeSig.ToTypeDefOrRef(), namespaces, currentFileNamespace);
                break;
        }
    }

    public static void CollectNamespacesForSplitFile(ITypeDefOrRef? typeRef, HashSet<string> namespaces,
        string currentFileNamespace)
    {
        if (typeRef == null) return;

        if (typeRef is TypeSpec typeSpec)
        {
            CollectNamespacesForSplitFile(typeSpec.TypeSig, namespaces, currentFileNamespace);
            return;
        }

        AddNamespaceForSplitFile(typeRef, namespaces, currentFileNamespace);
    }

    private static void AddNamespaceForSplitFile(ITypeDefOrRef? typeRef, HashSet<string> namespaces,
        string currentFileNamespace)
    {
        if (typeRef == null) return;

        var nsUtf8 = typeRef.Namespace;
        if (UTF8String.IsNullOrEmpty(nsUtf8)) return;

        var ns = nsUtf8.ToString();
        if (ns == "System" || ns.StartsWith("System."))
        {
            namespaces.Add(ns);
            return;
        }

        if (ns == "UnityEngine")
        {
            var name = typeRef.Name?.ToString();
            switch (name)
            {
                case "Vector2":
                case "Vector3":
                case "Vector4":
                case "Quaternion":
                case "Matrix4x4":
                    namespaces.Add("System.Numerics");
                    break;
            }
            return;
        }

        if (ns != currentFileNamespace) namespaces.Add(ns);
    }

    private static bool IsSubTypeOf(TypeDef typeToCheck, string ancestorTypeName)
    {
        var currentBaseRef = typeToCheck.BaseType;

        while (currentBaseRef != null)
        {
            if (currentBaseRef.Name == ancestorTypeName)
                return true;

            var currentBaseDef = currentBaseRef.ResolveTypeDef();

            if (currentBaseDef == null)
                break;
            currentBaseRef = currentBaseDef.BaseType;
        }

        return false;
    }
}