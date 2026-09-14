namespace MemoryPackDumper.Instructions;

internal readonly record struct InstructionAccess(long Offset, bool IsCompare);
