using MemoryPackDumper.CLI;
using dnlib.DotNet;
using ZLinq;

namespace MemoryPackDumper.Assembly;

public static class TypeReferenceTracker
{
    public static void TrackReferencedType(TypeSig typeSig, HashSet<string> discoveredTypes)
    {
        switch (typeSig)
        {
            case GenericInstSig genericSig:
                foreach (var genericArg in genericSig.GenericArguments.AsValueEnumerable())
                    TrackReferencedType(genericArg, discoveredTypes);
                TrackTypeDefOrRef(genericSig.GenericType?.TypeDefOrRef, discoveredTypes);
                break;
            case SZArraySig szArraySig:
                TrackReferencedType(szArraySig.Next, discoveredTypes);
                break;
            case ArraySig arraySig:
                TrackReferencedType(arraySig.Next, discoveredTypes);
                break;
            default:
                TrackTypeDefOrRef(typeSig.ToTypeDefOrRef(), discoveredTypes);
                break;
        }
    }

    private static void TrackTypeDefOrRef(ITypeDefOrRef? typeDefOrRef, HashSet<string> discoveredTypes)
    {
        if (typeDefOrRef == null) return;

        var typeDef = typeDefOrRef.ResolveTypeDef();
        if (typeDef == null) return;

        if (typeDef.IsEnum && !Parser.MemoryPackEnumsToAdd.AsValueEnumerable().Any(e => e.FullName == typeDef.FullName))
        {
            Parser.MemoryPackEnumsToAdd.Add(typeDef);
            return;
        }

        if (IsMemoryPackable(typeDef))
            discoveredTypes.Add(typeDef.FullName);
    }

    private static bool IsMemoryPackable(TypeDef typeDef)
    {
        return typeDef.CustomAttributes.AsValueEnumerable().Any(a => a.AttributeType.Name == "MemoryPackableAttribute");
    }
}