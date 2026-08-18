using dnlib.DotNet;
using MemoryPackDumper.Context;
using ZLinq;

namespace MemoryPackDumper.Assembly;

public static class TypeReferenceTracker
{
    public static void TrackReferencedType(TypeSig? typeSig, HashSet<TypeDef> discoveredTypes)
    {
        switch (typeSig)
        {
            case null:
                return;

            case GenericInstSig genericSig:
                foreach (var genericArg in genericSig.GenericArguments.AsValueEnumerable())
                    TrackReferencedType(genericArg, discoveredTypes);
                TrackTypeDefOrRef(genericSig.GenericType?.TypeDefOrRef, discoveredTypes);
                return;

            case GenericSig:
                return;

            case SZArraySig or ArraySig or ByRefSig or PtrSig or PinnedSig or CModReqdSig or CModOptSig:
                TrackReferencedType(typeSig.Next, discoveredTypes);
                return;

            default:
                TrackTypeDefOrRef(typeSig.ToTypeDefOrRef(), discoveredTypes);
                return;
        }
    }

    public static void TrackReferencedType(ITypeDefOrRef? typeDefOrRef, HashSet<TypeDef> discoveredTypes)
    {
        switch (typeDefOrRef)
        {
            case null:
                return;
            case TypeSpec typeSpec:
                TrackReferencedType(typeSpec.TypeSig, discoveredTypes);
                return;
            default:
                TrackTypeDefOrRef(typeDefOrRef, discoveredTypes);
                return;
        }
    }

    private static void TrackTypeDefOrRef(ITypeDefOrRef? typeDefOrRef, HashSet<TypeDef> discoveredTypes)
    {
        var typeDef = typeDefOrRef?.ResolveTypeDef();
        if (typeDef == null) return;

        if (typeDef.IsEnum)
        {
            TrackEnum(typeDef);
            return;
        }

        if (IsMemoryPackable(typeDef))
        {
            discoveredTypes.Add(ResolveEmitTarget(typeDef));
            return;
        }

        if (!ParserOptionsContext.Current.EmitReferencedTypes) return;
        if (!IsEmittableUserType(typeDef)) return;

        discoveredTypes.Add(ResolveEmitTarget(typeDef));
    }

    private static void TrackEnum(TypeDef typeDef)
    {
        if (!IsScannedType(typeDef)) return;

        var enums = ParserOptionsContext.Current.DiscoveredEnums;
        if (enums.AsValueEnumerable().Any(e => e.FullName == typeDef.FullName)) return;

        enums.Add(typeDef);
    }

    public static TypeDef ResolveEmitTarget(TypeDef typeDef)
    {
        var current = typeDef;

        while (current.DeclaringType is { } declaring &&
               current.IsNestedPublic &&
               (IsMemoryPackable(declaring) || IsEmittableUserType(declaring)))
            current = declaring;

        return current;
    }

    public static bool IsMemoryPackable(TypeDef typeDef) => typeDef.CustomAttributes.AsValueEnumerable()
        .Any(a => a.AttributeType.Name == "MemoryPackableAttribute");

    private static bool IsEmittableUserType(TypeDef typeDef)
    {
        if (typeDef.IsGlobalModuleType) return false;
        if (!IsScannedType(typeDef)) return false;
        if (IsDelegate(typeDef)) return false;
        if (IsCompilerGenerated(typeDef)) return false;

        return true;
    }

    private static bool IsDelegate(TypeDef typeDef) =>
        typeDef.BaseType?.FullName is "System.Delegate" or "System.MulticastDelegate";

    private static bool IsCompilerGenerated(TypeDef typeDef)
    {
        var name = typeDef.Name.String;
        return name.Contains('<') || name.Contains('>');
    }

    private static bool IsScannedType(TypeDef typeDef)
    {
        var scanned = ParserOptionsContext.Current.ScannedAssemblies;
        if (scanned.Count == 0) return true;

        var moduleName = typeDef.Module?.Name.String;
        if (!string.IsNullOrEmpty(moduleName) && scanned.Contains(moduleName)) return true;

        var assemblyName = typeDef.DefinitionAssembly?.Name.String;
        return !string.IsNullOrEmpty(assemblyName) && scanned.Contains(assemblyName);
    }
}
