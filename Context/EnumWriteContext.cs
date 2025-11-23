using MemoryPackDumper.Assembly;

namespace MemoryPackDumper.Context;

public readonly record struct EnumWriteContext(
    MemoryPackEnum @enum,
    string? indent
)
{
    public string actualIndent => indent ?? "";
    public bool isFileScopedNamespace => indent is null;
}
