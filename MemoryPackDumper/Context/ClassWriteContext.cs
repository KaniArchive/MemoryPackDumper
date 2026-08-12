using MemoryPackDumper.Assembly;

namespace MemoryPackDumper.Context;

public readonly record struct ClassWriteContext(
    MemoryPackClass Class,
    string? Indent
)
{
    public string ActualIndent => Indent ?? "";
    public bool IsFileScopedNamespace => Indent is null;
}