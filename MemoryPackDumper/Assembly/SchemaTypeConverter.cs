using System.Collections.Frozen;
using System.Text;
using dnlib.DotNet;

namespace MemoryPackDumper.Assembly;

public static class SchemaTypeConverter
{
    private const string UnknownType = "unknown";

    private static readonly FrozenDictionary<string, string> PrimitiveMap = new Dictionary<string, string>
    {
        ["System.SByte"] = "i8",
        ["System.Byte"] = "u8",
        ["System.Int16"] = "i16",
        ["System.UInt16"] = "u16",
        ["System.Int32"] = "i32",
        ["System.UInt32"] = "u32",
        ["System.Int64"] = "i64",
        ["System.UInt64"] = "u64",
        ["System.Single"] = "f32",
        ["System.Double"] = "f64",
        ["System.Boolean"] = "bool",
        ["System.String"] = "string",
        ["System.Char"] = "u16",
        ["System.DateTime"] = "datetime",
        ["System.DateTimeOffset"] = "datetime",
        ["System.Guid"] = "guid"
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> EnumUnderlyingMap = new Dictionary<string, string>
    {
        ["sbyte"] = "i8",
        ["byte"] = "u8",
        ["short"] = "i16",
        ["ushort"] = "u16",
        ["int"] = "i32",
        ["uint"] = "u32",
        ["long"] = "i64",
        ["ulong"] = "u64"
    }.ToFrozenDictionary();

    private static readonly FrozenSet<string> ListLikeTypes = new[]
    {
        "List`1", "IList`1", "IReadOnlyList`1", "ICollection`1", "IReadOnlyCollection`1", "IEnumerable`1",
        "Collection`1", "ObservableCollection`1", "ReadOnlyCollection`1", "HashSet`1", "ISet`1", "IReadOnlySet`1",
        "SortedSet`1", "Queue`1", "Stack`1", "LinkedList`1", "ConcurrentQueue`1", "ConcurrentStack`1",
        "ConcurrentBag`1", "ImmutableList`1", "ImmutableArray`1", "ImmutableHashSet`1", "ImmutableSortedSet`1",
        "ImmutableQueue`1", "ImmutableStack`1"
    }.ToFrozenSet();

    private static readonly FrozenSet<string> DictionaryLikeTypes = new[]
    {
        "Dictionary`2", "IDictionary`2", "IReadOnlyDictionary`2", "SortedDictionary`2", "SortedList`2",
        "ConcurrentDictionary`2", "ImmutableDictionary`2", "ImmutableSortedDictionary`2"
    }.ToFrozenSet();

    private static readonly FrozenSet<string> ByteBufferTypes = new[]
    {
        "Memory`1", "ReadOnlyMemory`1", "ArraySegment`1", "Span`1", "ReadOnlySpan`1"
    }.ToFrozenSet();

    public static string TypeToString(TypeSig? typeSig)
    {
        while (true)
            switch (typeSig)
            {
                case null:
                    return UnknownType;
                case ByRefSig or PtrSig or PinnedSig or CModOptSig or CModReqdSig:
                    typeSig = typeSig.Next;
                    continue;
                case SZArraySig szArray:
                    return ConvertArray(szArray.Next);
                case ArraySig array:
                    return ConvertArray(array.Next);
                case GenericInstSig generic:
                    return ConvertGeneric(generic);
                case GenericSig genericParameter:
                    return Identifier(genericParameter.TypeName);
                default:
                    return PrimitiveMap.TryGetValue(typeSig.FullName, out var primitive)
                        ? primitive
                        : Identifier(typeSig.TypeName);
            }
    }

    public static string MapEnumUnderlyingType(string? csharpKeyword) =>
        csharpKeyword != null && EnumUnderlyingMap.TryGetValue(csharpKeyword, out var mapped) ? mapped : "i32";

    public static string Identifier(string? name)
    {
        var stripped = StripGenerics(name);
        if (stripped.Length == 0) return UnknownType;

        var builder = new StringBuilder(stripped.Length);
        foreach (var c in stripped)
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');

        var result = builder.ToString();
        return char.IsDigit(result[0]) ? $"_{result}" : result;
    }

    public static string StripGenerics(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        var genericIndex = name.IndexOf('<');
        if (genericIndex > 0) name = name[..genericIndex];

        var arityIndex = name.IndexOf('`');
        return arityIndex > 0 ? name[..arityIndex] : name;
    }

    private static string ConvertArray(TypeSig? elementType) =>
        elementType == null ? $"List<{UnknownType}>"
        : elementType.FullName == "System.Byte" ? "bytes"
        : $"List<{TypeToString(elementType)}>";

    private static string ConvertGeneric(GenericInstSig generic)
    {
        var genericName = generic.GenericType?.TypeName ?? "";
        var arguments = generic.GenericArguments;

        switch (arguments.Count)
        {
            case 1 when genericName == "Nullable`1":
                return MakeNullable(TypeToString(arguments[0]));
            case 1 when ByteBufferTypes.Contains(genericName) && arguments[0].FullName == "System.Byte":
                return "bytes";
            case 1 when ListLikeTypes.Contains(genericName):
                return $"List<{TypeToString(arguments[0])}>";
            case 2 when DictionaryLikeTypes.Contains(genericName):
                return $"Dictionary<{TypeToString(arguments[0])}, {TypeToString(arguments[1])}>";
        }

        var builder = new StringBuilder(Identifier(genericName));
        foreach (var argument in arguments)
            builder.Append('_').Append(Identifier(TypeToString(argument)));

        return builder.ToString();
    }

    private static string MakeNullable(string type) => type.EndsWith('?') ? type : $"{type}?";
}