using dnlib.DotNet;

namespace MemoryPackDumper.Assembly;

public class MemoryPackSchema
{
    public readonly List<MemoryPackClass> Classes = [];
    public readonly List<MemoryPackEnum> Enums = [];
}

public class MemoryPackClass(string className, string baseClassName, string typeKeyword = "class")
{
    public readonly List<string> Attributes = [];
    public readonly string BaseClassName = baseClassName;
    public readonly string ClassName = className;
    public readonly List<MemoryPackMember> Members = [];
    public readonly List<MemoryPackMethod> Methods = [];
    public readonly string TypeKeyword = typeKeyword;
    public readonly List<MemoryPackUnion> Unions = [];
    public ITypeDefOrRef? BaseTypeReference = null;
    public string BaseTypeFullName = "";
    public string FullName = "";
    public int BaseConstructorArity = 0;
    public string? GenerateType = null;
    public bool IsMemoryPackable = true;
    public bool IsRecord = false;
    public readonly List<MemoryPackClass> NestedClasses = [];
    public string OriginalNamespace = "";
    public string? SerializeLayout = null;
}

public class MemoryPackMethod(string name, string returnType, bool isStatic, string visibility)
{
    public readonly List<string> Attributes = [];
    public readonly bool IsStatic = isStatic;
    public readonly string Name = name;
    public readonly List<(string Type, string Name)> Parameters = [];
    public readonly string ReturnType = returnType;
    public readonly string Visibility = visibility;
    public bool IsConstructor = false;
}

public class MemoryPackUnion(int tag, string typeName)
{
    public readonly int Tag = tag;
    public readonly string TypeName = typeName;
}

public class MemoryPackMember(string name, TypeSig type, bool isField)
{
    public readonly List<string> CustomFormatters = [];
    public readonly bool IsField = isField;
    public readonly string Name = name;
    public readonly TypeSig Type = type;
    public bool AllowSerialize = false;
    public bool IsIgnored = false;
    public bool IsInclude = false;
    public bool IsInit = false;
    public bool IsPublic = true;
    public bool IsReadOnly = false;
    public bool IsRequired = false;
    public int? Order = null;
    public bool SuppressDefaultInitialization = false;
}

public class MemoryPackEnum(string underlyingType, string enumName)
{
    public readonly string EnumName = enumName;
    public readonly List<MemoryPackEnumField> Fields = [];
    public readonly string UnderlyingType = underlyingType;
    public string OriginalNamespace = "";
}

public class MemoryPackEnumField(string name, long value = 0)
{
    public readonly string Name = name;
    public readonly long Value = value;
}