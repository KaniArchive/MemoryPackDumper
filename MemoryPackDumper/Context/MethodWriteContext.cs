using MemoryPackDumper.Assembly;

namespace MemoryPackDumper.Context;

public readonly record struct MethodWriteContext(
    MemoryPackMethod Method,
    string Indent,
    string ClassName
)
{
    public string MemberIndent => Indent + "    ";
}