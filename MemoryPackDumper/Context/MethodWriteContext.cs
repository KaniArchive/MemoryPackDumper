using MemoryPackDumper.Assembly;

namespace MemoryPackDumper.Context;

public readonly record struct MethodWriteContext(
    MemoryPackMethod Method,
    string Indent,
    string ClassName,
    int BaseConstructorArity = 0
)
{
    public string MemberIndent => Indent + "    ";
}