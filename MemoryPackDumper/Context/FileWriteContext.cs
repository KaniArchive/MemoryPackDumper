namespace MemoryPackDumper.Context;

public readonly record struct FileWriteContext(
    string FilePath,
    string? Namespace,
    string? Indent
)
{
    public bool HasNamespace => !string.IsNullOrEmpty(Namespace);
    public string ActualIndent => Indent ?? "";
}