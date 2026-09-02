using dnlib.DotNet;

namespace MemoryPackDumper.Context;

public sealed class ParserOptionsContext
{
    public static ParserOptionsContext Current { get; set; } = new();

    public bool SuppressWarnings { get; init; }
    public bool AllowHidden { get; init; }
    public bool EmitReferencedTypes { get; init; } = true;
    public string? NamespaceToLookFor { get; init; }
    public string? TypeToLookFor { get; init; }
    public List<TypeDef> DiscoveredEnums { get; } = [];
    public HashSet<string> ScannedAssemblies { get; } = new(StringComparer.OrdinalIgnoreCase);
}