using MemoryPackDumper.Assembly;

namespace MemoryPackDumper.Context;

public readonly record struct EnumWriteContext(
    MemoryPackEnum Enum,
    string? Indent
)
{
    public string ActualIndent => Indent ?? "";
    public bool IsFileScopedNamespace => Indent is null;
}