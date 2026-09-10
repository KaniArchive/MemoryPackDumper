namespace MemoryPackDumper.Instructions;

internal interface IInstructionAnalyzer
{
    List<InstructionAccess> Analyze(IReadOnlyList<InstructionWithAddress> instructions, int instanceParameterIndex);
}
