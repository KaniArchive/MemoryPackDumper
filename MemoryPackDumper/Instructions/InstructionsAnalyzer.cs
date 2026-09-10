using MemoryPackDumper.Instructions.Analyzers;

namespace MemoryPackDumper.Instructions;

internal static class InstructionsAnalyzer
{
    public static IInstructionAnalyzer GetAnalyzer(Architecture architecture) => architecture switch
    {
        Architecture.Arm64 => new Arm64Analyzer(),
        Architecture.X86 => new X86Analyzer(),
        _ => throw new ArgumentOutOfRangeException(nameof(architecture))
    };
}
