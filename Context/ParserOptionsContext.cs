using dnlib.DotNet;

namespace MemoryPackDumper.Context;

public sealed class ParserOptionsContext
{
    public static ParserOptionsContext Current { get; set; } = new();

    public bool SuppressWarnings { get; init; }
    public bool AllowHidden { get; init; }
    public string? NamespaceToLookFor { get; init; }
    public string? TypeToLookFor { get; init; }
    public List<TypeDef> DiscoveredEnums { get; } = [];
}
