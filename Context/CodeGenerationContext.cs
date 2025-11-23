namespace MemoryPackDumper.Context;

public readonly record struct CodeGenerationContext(
    string? customNamespace,
    bool isSplitMode,
    string outputPath
);
