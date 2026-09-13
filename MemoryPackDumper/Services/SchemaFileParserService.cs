using dnlib.DotNet;
using MemoryPackDumper.Assembly;
using ZLinq;

namespace MemoryPackDumper.Services;

public static class SchemaFileParserService
{
    public static MemoryPackSchema Read(string input) => Parse(File.ReadAllLines(input));

    public static MemoryPackSchema Parse(string input) => Parse(input.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None));

    private static MemoryPackSchema Parse(string[] lines)
    {
        var reader = new SchemaReader(lines);
        return reader.Read();
    }

    private static readonly (string Prefix, string Keyword)[] TypeKeywords =
    [
        ("static class ", "static"),
        ("abstract class ", "abstract"),
        ("interface class ", "interface"),
        ("struct class ", "struct"),
        ("interface ", "interface"),
        ("struct ", "struct"),
        ("class ", "")
    ];

    private sealed class SchemaReader(string[] lines)
    {
        private readonly ModuleDef _module = new ModuleDefUser("MemoryPackSchema");
        private readonly string[] _lines = lines;
        private int _position;

        public MemoryPackSchema Read()
        {
            var schema = new MemoryPackSchema();

            while (TryReadLine(out var line))
            {
                if (line.StartsWith("enum ", StringComparison.Ordinal))
                    schema.Enums.Add(ReadEnum(line));
                else if (TrySplitTypeKeyword(line, out _, out _))
                    schema.Classes.Add(ReadClass(line));
                else
                    throw Error($"Unexpected declaration '{line}'.");
            }

            SchemaLinker.ResolveBaseConstructors(schema);
            return schema;
        }

        private MemoryPackEnum ReadEnum(string line)
        {
            var declaration = TrimBlockStart(line, "enum")["enum ".Length..].TrimStart();
            var separator = declaration.IndexOf(" : ", StringComparison.Ordinal);
            var name = separator < 0 ? declaration : declaration[..separator];
            var underlying = separator < 0 ? "i32" : declaration[(separator + 3)..];
            var memoryPackEnum = new MemoryPackEnum(MapEnumUnderlyingType(underlying), name);

            while (TryReadLine(out var member))
            {
                if (member == "}") return memoryPackEnum;
                var equals = member.IndexOf('=');
                if (equals < 0 || !member.EndsWith(',', StringComparison.Ordinal))
                    throw Error($"Invalid enum member '{member}'.");

                var fieldName = member[..equals].Trim();
                var value = member[(equals + 1)..^1].Trim();
                memoryPackEnum.Fields.Add(new MemoryPackEnumField(fieldName, long.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
            }

            throw Error($"Enum '{name}' is missing its closing brace.");
        }

        private MemoryPackClass ReadClass(string line)
        {
            var declaration = TrimBlockStart(line, "type");
            TrySplitTypeKeyword(declaration, out var keyword, out var content);
            var modifiers = ExtractModifiers(ref content);
            var separator = content.IndexOf(" : ", StringComparison.Ordinal);
            var name = separator < 0 ? content.Trim() : content[..separator].Trim();
            var baseType = separator < 0 ? "" : content[(separator + 3)..].Trim();
            var memoryPackClass = new MemoryPackClass(name, baseType, keyword)
            {
                IsMemoryPackable = !modifiers.Contains("raw"),
                BaseTypeReference = baseType.Length == 0 ? null : ParseType(baseType).ToTypeDefOrRef()
            };

            foreach (var modifier in modifiers)
                switch (modifier)
                {
                    case "version_tolerant":
                        memoryPackClass.GenerateType = "VersionTolerant";
                        break;
                    case "circular_ref":
                        memoryPackClass.GenerateType = "CircularReference";
                        break;
                    case "collection":
                        memoryPackClass.GenerateType = "Collection";
                        break;
                }

            while (TryReadLine(out var member))
            {
                if (member == "}") return memoryPackClass;
                ReadClassMember(memoryPackClass, member);
            }

            throw Error($"Class '{name}' is missing its closing brace.");
        }

        private static bool TrySplitTypeKeyword(string declaration, out string keyword, out string content)
        {
            foreach (var (prefix, mapped) in TypeKeywords)
                if (declaration.StartsWith(prefix, StringComparison.Ordinal))
                {
                    keyword = mapped;
                    content = declaration[prefix.Length..].TrimStart();
                    return true;
                }

            keyword = "";
            content = declaration;
            return false;
        }

        private void ReadClassMember(MemoryPackClass memoryPackClass, string line)
        {
            if (line.StartsWith("@method ", StringComparison.Ordinal))
            {
                ReadMethod(memoryPackClass, line);
                return;
            }

            if (line.StartsWith("union ", StringComparison.Ordinal))
            {
                var content = TrimTerminator(line["union ".Length..]);
                var separator = content.IndexOf(": ", StringComparison.Ordinal);
                if (separator < 0) throw Error($"Invalid union '{line}'.");
                memoryPackClass.Unions.Add(new MemoryPackUnion(
                    int.Parse(content[..separator], System.Globalization.CultureInfo.InvariantCulture),
                    content[(separator + 2)..]));
                return;
            }

            if (line.StartsWith("constructor(", StringComparison.Ordinal))
            {
                ReadConstructor(memoryPackClass, line);
                return;
            }

            if (line.StartsWith("@callback ", StringComparison.Ordinal)) return;
            ReadMember(memoryPackClass, line);
        }

        private void ReadMethod(MemoryPackClass memoryPackClass, string line)
        {
            var content = TrimTerminator(line["@method ".Length..]);
            var open = content.IndexOf('(');
            if (open < 0) throw Error($"Invalid method '{line}'.");

            var close = content.LastIndexOf(')');
            if (close < open) throw Error($"Invalid method '{line}'.");

            var signature = content[..open].Trim();
            var parts = signature.Split(' ');
            if (parts.Length < 2) throw Error($"Invalid method signature '{line}'.");

            var visibility = parts[0];
            var name = parts[parts.Length - 1];
            var isStatic = Array.IndexOf(parts, "static", 1, parts.Length - 2) >= 0;
            var returnTypeStart = isStatic ? 2 : 1;
            var returnType = string.Join(" ", parts, returnTypeStart, parts.Length - returnTypeStart - 1);

            var method = new MemoryPackMethod(name, returnType, isStatic, visibility);

            foreach (var parameter in SplitArguments(content[(open + 1)..close]))
            {
                var separator = parameter.LastIndexOf(' ');
                if (separator < 0)
                {
                    method.Parameters.Add(("object", parameter));
                    continue;
                }

                method.Parameters.Add((parameter[..separator], parameter[(separator + 1)..]));
            }

            memoryPackClass.Methods.Add(method);
        }

        private void ReadConstructor(MemoryPackClass memoryPackClass, string line)
        {
            var close = line.IndexOf(')');
            if (close < 0) throw Error($"Invalid constructor '{line}'.");

            var method = new MemoryPackMethod(".ctor", "void", false, "public")
            {
                IsConstructor = true
            };
            var parameters = line["constructor(".Length..close];
            foreach (var parameter in SplitArguments(parameters))
            {
                var separator = parameter.LastIndexOf(' ');
                if (separator < 0)
                {
                    method.Parameters.Add(("object", parameter));
                    continue;
                }

                method.Parameters.Add((parameter[..separator], parameter[(separator + 1)..]));
            }

            if (line.Contains("[primary]", StringComparison.Ordinal)) method.Attributes.Add("MemoryPackConstructor");
            memoryPackClass.Methods.Add(method);
        }

        private void ReadMember(MemoryPackClass memoryPackClass, string line)
        {
            var content = TrimTerminator(line);
            if (content.EndsWith(" computed", StringComparison.Ordinal))
            {
                var computedDeclaration = content[..^" computed".Length];
                var computedSeparator = computedDeclaration.LastIndexOf(' ');
                if (computedSeparator < 0) throw Error($"Invalid computed member '{line}'.");
                var computed = new MemoryPackMember(computedDeclaration[(computedSeparator + 1)..],
                    ParseType(computedDeclaration[..computedSeparator]), false)
                {
                    IsComputed = true,
                    HasSetter = false
                };
                memoryPackClass.Members.Add(computed);
                return;
            }

            var colon = content.IndexOf(": ", StringComparison.Ordinal);
            if (colon < 0) throw Error($"Invalid member '{line}'.");

            var order = int.Parse(content[..colon], System.Globalization.CultureInfo.InvariantCulture);
            var declaration = content[(colon + 2)..];
            var modifiers = ExtractMemberModifiers(ref declaration);
            var separator = declaration.LastIndexOf(' ');
            if (separator < 0) throw Error($"Invalid member declaration '{line}'.");

            var member = new MemoryPackMember(declaration[(separator + 1)..], ParseType(declaration[..separator]),
                !modifiers.Contains("property"))
            {
                Order = order,
                IsReadOnly = modifiers.Contains("readonly"),
                IsRequired = modifiers.Contains("required"),
                IsInit = modifiers.Contains("init"),
                IsIgnored = modifiers.Contains("ignore")
            };

            foreach (var modifier in modifiers.AsValueEnumerable().Where(value => value.StartsWith("@formatter(\"", StringComparison.Ordinal)))
                member.CustomFormatters.Add(modifier["@formatter(\"".Length..^2]);

            memoryPackClass.Members.Add(member);
        }

        private TypeSig ParseType(string text)
        {
            text = text.Trim();
            if (text.EndsWith("?", StringComparison.Ordinal))
                return new GenericInstSig(new ValueTypeSig(new TypeRefUser(_module, "System", "Nullable`1")), ParseType(text[..^1]));
            if (text == "bytes") return new SZArraySig(_module.CorLibTypes.Byte);

            var genericStart = text.IndexOf('<');
            if (genericStart >= 0 && text.EndsWith(">", StringComparison.Ordinal))
            {
                var name = text[..genericStart];
                var arguments = SplitArguments(text[(genericStart + 1)..^1]).AsValueEnumerable().Select(ParseType).ToArray();
                var typeRef = new TypeRefUser(_module, GenericNamespace(name), name + "`" + arguments.Length);
                return new GenericInstSig(new ClassSig(typeRef), arguments);
            }

            return text switch
            {
                "i8" => _module.CorLibTypes.SByte,
                "u8" => _module.CorLibTypes.Byte,
                "i16" => _module.CorLibTypes.Int16,
                "u16" => _module.CorLibTypes.UInt16,
                "i32" => _module.CorLibTypes.Int32,
                "u32" => _module.CorLibTypes.UInt32,
                "i64" => _module.CorLibTypes.Int64,
                "u64" => _module.CorLibTypes.UInt64,
                "f32" => _module.CorLibTypes.Single,
                "f64" => _module.CorLibTypes.Double,
                "bool" => _module.CorLibTypes.Boolean,
                "string" => _module.CorLibTypes.String,
                "datetime" => new ValueTypeSig(new TypeRefUser(_module, "System", "DateTime")),
                "guid" => new ValueTypeSig(new TypeRefUser(_module, "System", "Guid")),
                "Vector2" or "Vector3" or "Vector4" or "Quaternion" or "Matrix4x4" => new ValueTypeSig(new TypeRefUser(_module, "UnityEngine", text)),
                _ => new ClassSig(new TypeRefUser(_module, "", text))
            };
        }

        private bool TryReadLine(out string line)
        {
            while (_position < _lines.Length)
            {
                line = _lines[_position++].Trim();
                if (line.Length > 0) return true;
            }

            line = "";
            return false;
        }

        private InvalidDataException Error(string message) => new($"{message} Line {_position}.");

        private static string TrimBlockStart(string line, string keyword)
        {
            if (!line.EndsWith('{')) throw new InvalidDataException($"Invalid {keyword} declaration '{line}'.");
            return line[..^1].Trim();
        }

        private static string TrimTerminator(string line)
        {
            if (!line.EndsWith(';')) throw new InvalidDataException($"Missing terminator in '{line}'.");
            return line[..^1].Trim();
        }

        private static List<string> ExtractModifiers(ref string content)
        {
            var modifiers = new List<string>();
            while (content.EndsWith(']'))
            {
                var start = content.LastIndexOf('[');
                if (start < 0) break;
                modifiers.Add(content[(start + 1)..^1]);
                content = content[..start].TrimEnd();
            }

            return modifiers;
        }

        private static List<string> ExtractMemberModifiers(ref string content)
        {
            var modifiers = new List<string>();
            while (true)
            {
                var separator = content.LastIndexOf(' ');
                if (separator < 0) return modifiers;
                var modifier = content[(separator + 1)..];
                if (modifier is not ("readonly" or "required" or "init" or "ignore" or "field" or "property") &&
                    !modifier.StartsWith("@formatter(\"", StringComparison.Ordinal)) return modifiers;
                modifiers.Add(modifier);
                content = content[..separator];
            }
        }

        private static List<string> SplitArguments(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return [];

            var values = new List<string>();
            var start = 0;
            var depth = 0;
            for (var index = 0; index < text.Length; index++)
            {
                switch (text[index])
                {
                    case '<': depth++; break;
                    case '>': depth--; break;
                    case ',' when depth == 0:
                        values.Add(text[start..index].Trim());
                        start = index + 1;
                        break;
                }
            }

            values.Add(text[start..].Trim());
            return values;
        }

        private static string GenericNamespace(string name) => name switch
        {
            "List" => "System.Collections.Generic",
            "Dictionary" => "System.Collections.Generic",
            "Collection" => "System.Collections.ObjectModel",
            "KeyedCollection" => "System.Collections.ObjectModel",
            _ => ""
        };

        private static string MapEnumUnderlyingType(string type) => type switch
        {
            "i8" => "sbyte",
            "u8" => "byte",
            "i16" => "short",
            "u16" => "ushort",
            "i32" => "int",
            "u32" => "uint",
            "i64" => "long",
            "u64" => "ulong",
            _ => "int"
        };
    }
}
