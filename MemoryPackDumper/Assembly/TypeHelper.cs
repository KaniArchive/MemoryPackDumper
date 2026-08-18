using dnlib.DotNet;
using MemoryPackDumper.Context;
using ZLinq;

namespace MemoryPackDumper.Assembly;

public static class TypeHelper
{
    public static void RegisterScannedAssembly(ModuleDef module)
    {
        var scanned = ParserOptionsContext.Current.ScannedAssemblies;

        scanned.Add(module.Name.String);

        var assemblyName = module.Assembly?.Name.String;
        if (!string.IsNullOrEmpty(assemblyName)) scanned.Add(assemblyName);
    }

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

        return TypeStringConverter.TypeToString(typeDef.BaseType.ToTypeSig());
    }

    public static void CollectNamespaces(TypeSig typeSig, HashSet<string> namespaces)
    {
        switch (typeSig)
        {
            case GenericInstSig genericType:
            {
                AddNamespaceIfNeeded(genericType.GenericType?.TypeDefOrRef, namespaces);

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
                AddNamespaceIfNeeded(typeSig.ToTypeDefOrRef(), namespaces);
                break;
        }
    }

    public static void CollectNamespaces(ITypeDefOrRef? typeRef, HashSet<string> namespaces)
    {
        switch (typeRef)
        {
            case null:
                return;
            case TypeSpec typeSpec:
                CollectNamespaces(typeSpec.TypeSig, namespaces);
                return;
            default:
                AddNamespaceIfNeeded(typeRef, namespaces);
                break;
        }
    }

    private static void AddNamespaceIfNeeded(ITypeDefOrRef? typeRef, HashSet<string> namespaces)
    {
        if (typeRef == null) return;

        var ns = typeRef.Namespace;
        if (UTF8String.IsNullOrEmpty(ns)) return;

        if (ns.StartsWith("System."))
        {
            namespaces.Add(ns);
            return;
        }

        if (ns != "UnityEngine") return;
        var name = typeRef.Name?.String;
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

        if (nsUtf8 == "System" || nsUtf8.StartsWith("System."))
        {
            namespaces.Add(nsUtf8);
            return;
        }

        if (nsUtf8 == "UnityEngine")
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

        if (nsUtf8 != currentFileNamespace) namespaces.Add(nsUtf8);
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