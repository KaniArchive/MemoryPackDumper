namespace MemoryPackDumper.Context;

public readonly record struct FileWriteContext(
    string filePath,
    string? @namespace,
    string? indent
)
{
    public bool hasNamespace => !string.IsNullOrEmpty(@namespace);
    public string actualIndent => indent ?? "";
}
