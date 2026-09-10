using System.Globalization;

namespace MemoryPackDumper.Instructions.Analyzers;

internal sealed class Arm64Analyzer : IInstructionAnalyzer
{
    public List<InstructionAccess> Analyze(IReadOnlyList<InstructionWithAddress> instructions,
        int instanceParameterIndex)
    {
        var registers = new HashSet<string>(StringComparer.Ordinal) { $"x{instanceParameterIndex}" };
        var accesses = new List<InstructionAccess>();
        foreach (var instructionWithAddress in instructions)
        {
            if (instructionWithAddress.Arm64Instruction is not { } instruction) continue;

            var text = instruction.ToString();
            if (TryReadMove(text, out var destination, out var source) && registers.Contains(source))
                registers.Add(destination);

            if (!TryReadMemoryAccess(text, out var register, out var offset) || !registers.Contains(register)) continue;
            accesses.Add(new InstructionAccess(offset,
                instruction.Mnemonic.ToString().Equals("cmp", StringComparison.OrdinalIgnoreCase)));
        }

        return accesses;
    }

    private static bool TryReadMove(string instruction, out string destination, out string source)
    {
        destination = string.Empty;
        source = string.Empty;
        if (!instruction.StartsWith("mov ", StringComparison.OrdinalIgnoreCase)) return false;

        var comma = instruction.IndexOf(',');
        if (comma < 0) return false;

        destination = instruction.Substring(4, comma - 4).Trim();
        source = instruction[(comma + 1)..].Trim();
        return IsXRegister(destination) && IsXRegister(source);
    }

    private static bool TryReadMemoryAccess(string instruction, out string register, out long offset)
    {
        register = string.Empty;
        offset = 0;
        var open = instruction.IndexOf('[');
        var close = instruction.IndexOf(']', open + 1);
        if (open < 0 || close < 0) return false;

        var operands = instruction.Substring(open + 1, close - open - 1)
            .Split([','], StringSplitOptions.RemoveEmptyEntries);
        if (operands.Length == 0) return false;

        register = operands[0].Trim();
        if (!IsXRegister(register)) return false;
        if (operands.Length == 1) return true;

        var value = operands[1].TrimStart('#');
        return value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? long.TryParse(value[2..], NumberStyles.HexNumber, null, out offset)
            : long.TryParse(value, out offset);
    }

    private static bool IsXRegister(string value) =>
        value.Length > 1 && value[0] == 'x' && int.TryParse(value[1..], out _);
}
