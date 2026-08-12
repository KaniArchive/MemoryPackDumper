using MemoryPackDumper.Assembly;

namespace MemoryPackDumper.Context;

public readonly record struct MemberWriteContext(
    MemoryPackMember member,
    string indent,
    bool isInterface = false
)
{
    public string memberIndent => indent + "    ";
}
