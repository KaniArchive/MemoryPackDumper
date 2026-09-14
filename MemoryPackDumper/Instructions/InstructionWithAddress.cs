using AsmArm64;
using Iced.Intel;

namespace MemoryPackDumper.Instructions;

internal record InstructionWithAddress(Arm64Instruction? Arm64Instruction, ulong Address)
{
    public Instruction X86Instruction { get; init; }
}
