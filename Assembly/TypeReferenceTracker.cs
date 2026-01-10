using MemoryPackDumper.CLI;
using dnlib.DotNet;
using ZLinq;

namespace MemoryPackDumper.Assembly;

public static class TypeReferenceTracker
{
    public static void TrackReferencedType(TypeSig typeSig, HashSet<string> discoveredTypes)
    {
        var typeDef = typeSig.TryGetTypeDef();
        if (typeDef == null)
            return;

        if (typeDef.IsEnum && !Parser.MemoryPackEnumsToAdd.Contains(typeDef))
        {
            Parser.MemoryPackEnumsToAdd.Add(typeDef);
            return;
        }

        if (IsMemoryPackable(typeDef)) discoveredTypes.Add(typeDef.FullName);

        if (typeSig is not GenericInstSig genericInstance) return;
        foreach (var genericArg in genericInstance.GenericArguments.AsValueEnumerable())
            TrackReferencedType(genericArg, discoveredTypes);
    }

    private static bool IsMemoryPackable(TypeDef typeDef)
    {
        return typeDef.CustomAttributes.AsValueEnumerable().Any(a => a.AttributeType.Name == "MemoryPackableAttribute");
    }
}