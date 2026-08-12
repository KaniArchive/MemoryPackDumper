using MemoryPackDumper.Assembly;

namespace MemoryPackDumper.Context;

public readonly record struct MethodWriteContext(
    MemoryPackMethod method,
    string indent,
    string className
)
{
    public string memberIndent => indent + "    ";
}
