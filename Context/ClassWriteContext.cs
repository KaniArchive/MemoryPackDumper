using MemoryPackDumper.Assembly;

namespace MemoryPackDumper.Context;

public readonly record struct ClassWriteContext(
    MemoryPackClass @class,
    string? indent
)
{
    public string actualIndent => indent ?? "";
    public bool isFileScopedNamespace => indent is null;
}