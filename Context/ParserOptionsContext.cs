using dnlib.DotNet;

namespace MemoryPackDumper.Context;

public sealed class ParserOptionsContext
{
    public static ParserOptionsContext current { get; set; } = new();

    public bool suppressWarnings { get; init; }
    public bool allowHidden { get; init; }
    public string? namespaceToLookFor { get; init; }
    public string? typeToLookFor { get; init; }
    public List<TypeDef> discoveredEnums { get; } = [];
}