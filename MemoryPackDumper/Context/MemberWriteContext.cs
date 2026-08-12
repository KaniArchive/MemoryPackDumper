using MemoryPackDumper.Assembly;

namespace MemoryPackDumper.Context;

public readonly record struct MemberWriteContext(
    MemoryPackMember Member,
    string Indent,
    bool IsInterface = false
)
{
    public string MemberIndent => Indent + "    ";
}