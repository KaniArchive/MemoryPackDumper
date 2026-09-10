using AsmArm64;
using dnlib.DotNet;
using Iced.Intel;
using ZLinq;

namespace MemoryPackDumper.Instructions;

internal sealed class InstructionsParser
{
    private const ushort DosHeaderMz = 0x5A4D;
    private const uint ElfMagic = 0x464C457F;
    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const ushort ImageFileMachineArm64 = 0xAA64;
    private const ushort ImageFileMachineArmnt = 0x01C4;
    private const ushort EmX86 = 0x003E;
    private readonly byte[] _fileBytes;

    public InstructionsParser(string gameAssemblyPath)
    {
        _fileBytes = File.ReadAllBytes(gameAssemblyPath);
        Architecture = DetectArchitecture(gameAssemblyPath);
    }

    public Architecture Architecture { get; }

    public List<InstructionWithAddress> GetInstructions(MethodDef method)
    {
        return Architecture == Architecture.Arm64
            ? GetArmInstructions(method)
            : GetX86Instructions(method);
    }

    private static Architecture DetectArchitecture(string gameAssemblyPath)
    {
        try
        {
            using var stream = new FileStream(gameAssemblyPath, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);

            if (IsPeFile(reader)) return GetPeArchitecture(reader);

            return IsElfFile(reader) ? GetElfArchitecture(reader) : GetArchitectureFromFilename(gameAssemblyPath);
        }
        catch
        {
            return GetArchitectureFromFilename(gameAssemblyPath);
        }
    }

    private static Architecture GetArchitectureFromFilename(string gameAssemblyPath) =>
        Path.GetExtension(gameAssemblyPath).Equals(".so", StringComparison.OrdinalIgnoreCase)
            ? Architecture.Arm64
            : Architecture.X86;

    private static bool IsPeFile(BinaryReader reader)
    {
        reader.BaseStream.Seek(0, SeekOrigin.Begin);
        return reader.ReadUInt16() == DosHeaderMz;
    }

    private static bool IsElfFile(BinaryReader reader)
    {
        reader.BaseStream.Seek(0, SeekOrigin.Begin);
        return reader.ReadUInt32() == ElfMagic;
    }

    private static Architecture GetPeArchitecture(BinaryReader reader)
    {
        reader.BaseStream.Seek(0x3C, SeekOrigin.Begin);
        var peHeaderOffset = reader.ReadUInt32();
        reader.BaseStream.Seek(peHeaderOffset + 4, SeekOrigin.Begin);
        var machine = reader.ReadUInt16();

        return machine is ImageFileMachineArm64 or ImageFileMachineArmnt ? Architecture.Arm64 : Architecture.X86;
    }

    private static Architecture GetElfArchitecture(BinaryReader reader)
    {
        reader.BaseStream.Seek(18, SeekOrigin.Begin);
        return reader.ReadUInt16() == EmX86 ? Architecture.X86 : Architecture.Arm64;
    }

    private List<InstructionWithAddress> GetX86Instructions(MethodDef method)
    {
        var rva = GetMethodAddress(method, "RVA");
        var offset = GetMethodAddress(method, "Offset");
        if (rva == 0 || offset == 0 || offset >= _fileBytes.Length) return [];

        var reader = new ByteArrayCodeReader(_fileBytes) { Position = checked((int)offset) };
        var decoder = Decoder.Create(64, reader);
        decoder.IP = checked((ulong)rva);
        var instructions = new List<InstructionWithAddress>();

        for (var count = 0; count < 4096 && reader.Position < _fileBytes.Length; count++)
        {
            var instruction = decoder.Decode();
            if (instruction.Code == Code.INVALID) break;

            instructions.Add(new InstructionWithAddress(null, instruction.IP) { X86Instruction = instruction });
            if (instruction.Mnemonic == Mnemonic.Ret) break;
        }

        return instructions;
    }

    private List<InstructionWithAddress> GetArmInstructions(MethodDef method)
    {
        var offset = GetMethodAddress(method, "Offset");
        if (offset == 0 || offset >= _fileBytes.Length) return [];

        var instructions = new List<InstructionWithAddress>();
        for (var position = offset; position + 4 <= _fileBytes.Length; position += 4)
        {
            var instruction = Arm64Instruction.Decode(BitConverter.ToUInt32(_fileBytes, checked((int)position)));
            instructions.Add(new InstructionWithAddress(instruction, checked((ulong)position)));
            if (instruction.Mnemonic.ToString().Equals("ret", StringComparison.OrdinalIgnoreCase)) break;
        }

        return instructions;
    }

    private static long GetMethodAddress(MethodDef method, string name)
    {
        var attribute = method.CustomAttributes.AsValueEnumerable().FirstOrDefault(candidate =>
            candidate.AttributeType.Name.String == "AddressAttribute");
        var value = attribute?.Fields.AsValueEnumerable().FirstOrDefault(candidate => candidate.Name.String == name)
            ?.Argument.Value?.ToString();

        return value == null ? 0 : Convert.ToInt64(value[2..], 16);
    }
}
