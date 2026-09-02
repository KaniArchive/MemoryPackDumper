using ZLinq;

namespace MemoryPackDumper.Assembly;

public static class SchemaLinker
{
    public static void ResolveBaseConstructors(MemoryPackSchema schema)
    {
        var index = new Dictionary<string, MemoryPackClass>(StringComparer.Ordinal);

        foreach (var cls in schema.Classes)
            IndexClass(cls, index);

        foreach (var cls in index.Values)
            cls.BaseConstructorArity = ResolveArity(cls, index);
    }

    private static void IndexClass(MemoryPackClass cls, Dictionary<string, MemoryPackClass> index)
    {
        if (!string.IsNullOrEmpty(cls.FullName))
            index[cls.FullName] = cls;

        foreach (var nested in cls.NestedClasses)
            IndexClass(nested, index);
    }

    private static int ResolveArity(MemoryPackClass cls, Dictionary<string, MemoryPackClass> index)
    {
        if (string.IsNullOrEmpty(cls.BaseTypeFullName)) return 0;
        if (!index.TryGetValue(cls.BaseTypeFullName, out var baseClass)) return 0;

        var arity = int.MaxValue;

        foreach (var ctor in baseClass.Methods.AsValueEnumerable().Where(m => m.IsConstructor))
        {
            if (ctor.Parameters.Count == 0) return 0;
            if (ctor.Parameters.Count < arity) arity = ctor.Parameters.Count;
        }

        return arity == int.MaxValue ? 0 : arity;
    }
}
