using Iced.Intel;

namespace MemoryPackDumper.Instructions.Analyzers;

internal sealed class X86Analyzer : IInstructionAnalyzer
{
    public List<InstructionAccess> Analyze(IReadOnlyList<InstructionWithAddress> instructions,
        int instanceParameterIndex)
    {
        Register[] parameters = [Register.RCX, Register.RDX, Register.R8, Register.R9];
        if (instanceParameterIndex < 0 || instanceParameterIndex >= parameters.Length) return [];

        var registers = new HashSet<Register> { Canonical(parameters[instanceParameterIndex]) };
        var accesses = new List<InstructionAccess>();
        foreach (var instructionWithAddress in instructions)
        {
            var instruction = instructionWithAddress.X86Instruction;
            if (instruction.Mnemonic is Mnemonic.Mov or Mnemonic.Lea && instruction.Op0Kind == OpKind.Register &&
                instruction.Op1Kind == OpKind.Register && registers.Contains(Canonical(instruction.Op1Register)))
                registers.Add(Canonical(instruction.Op0Register));

            if (instruction.MemoryBase != Register.None && registers.Contains(Canonical(instruction.MemoryBase)))
                accesses.Add(new InstructionAccess(instruction.MemoryDisplacement32, instruction.Mnemonic == Mnemonic.Cmp));
        }

        return accesses;
    }

    private static Register Canonical(Register register) => register.GetFullRegister();
}
