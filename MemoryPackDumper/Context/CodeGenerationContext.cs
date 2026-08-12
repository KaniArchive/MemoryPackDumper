namespace MemoryPackDumper.Context;

public readonly record struct CodeGenerationContext(
    string? CustomNamespace,
    bool IsSplitMode,
    string OutputPath
);