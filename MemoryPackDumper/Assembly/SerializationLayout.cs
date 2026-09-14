using System;
using System.Collections.Generic;
using System.IO;
using dnlib.DotNet;
using MemoryPackDumper.Helpers;
using MemoryPackDumper.Instructions;
using ZLinq;

namespace MemoryPackDumper.Assembly;

public static class SerializationLayout
{
    private readonly record struct MemberReference(string Owner, string Name);

    public static void Apply(MemoryPackSchema schema, ModuleDef module, string gameAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(gameAssemblyPath) || !File.Exists(gameAssemblyPath)) return;

        var parser = new InstructionsParser(gameAssemblyPath);
        var analyzer = InstructionsAnalyzer.GetAnalyzer(parser.Architecture);

        var declarations = new Dictionary<string, MemoryPackClass>(StringComparer.Ordinal);
        foreach (var declaration in schema.Classes.AsValueEnumerable()
                     .Where(declaration => !string.IsNullOrEmpty(declaration.FullName)))
            declarations[declaration.FullName] = declaration;

        var layouts = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var type in module.GetTypes().AsValueEnumerable()
                     .Where(type => declarations.ContainsKey(type.FullName)))
        {
            if (!TryRead(parser, analyzer, type, declarations, out var members)) continue;

            foreach (var group in Group(members))
                if (!layouts.ContainsKey(group.Key))
                    layouts[group.Key] = group.Value;
        }

        foreach (var layout in layouts)
            if (declarations.TryGetValue(layout.Key, out var declaration))
                Reorder(declaration, layout.Value);
    }

    private static Dictionary<string, List<string>> Group(List<MemberReference> members)
    {
        var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (var member in members)
        {
            if (!grouped.TryGetValue(member.Owner, out var names))
            {
                names = [];
                grouped[member.Owner] = names;
            }

            names.Add(member.Name);
        }

        return grouped;
    }

    private static void Reorder(MemoryPackClass declaration, List<string> members)
    {
        var declared = declaration.Members.AsValueEnumerable()
            .ToDictionary(member => member.Name, StringComparer.Ordinal);

        if (members.AsValueEnumerable().Any(name => !declared.ContainsKey(name))) return;

        var serialized = new HashSet<string>(members, StringComparer.Ordinal);
        var computed = members.AsValueEnumerable().Where(name => !declared[name].IsField).ToList();
        var fields = members.AsValueEnumerable().Where(name => declared[name].IsField).ToList();

        var orderedNames = new List<string>();
        if (computed.Count > 0) orderedNames.Add(computed[0]);
        orderedNames.AddRange(fields);
        for (var index = 1; index < computed.Count; index++)
            orderedNames.Add(computed[index]);

        var ordered = orderedNames.AsValueEnumerable().Select(name => declared[name]).ToList();

        foreach (var member in ordered)
        {
            member.IsComputed = false;
            if (!member.IsField) member.HasSetter = true;
        }

        foreach (var member in declaration.Members.AsValueEnumerable()
                     .Where(member => !serialized.Contains(member.Name)))
            member.IsComputed = true;

        declaration.Members.Clear();
        declaration.Members.AddRange(ordered);

        foreach (var member in declared.Values.AsValueEnumerable()
                     .Where(member => !serialized.Contains(member.Name)))
            declaration.Members.Add(member);
    }

    private static bool TryRead(InstructionsParser parser, IInstructionAnalyzer analyzer, TypeDef type,
        Dictionary<string, MemoryPackClass> declarations, out List<MemberReference> members)
    {
        members = [];

        var method = SerializeMethod(type);
        if (method == null) return false;

        var accesses = analyzer.Analyze(parser.GetInstructions(method), 1);
        if (accesses.Count == 0) return false;

        var expected = 0;
        foreach (var (_, declaration) in Chain(type, declarations))
            expected += declaration.Members.Count;

        var fieldOffsets = FieldOffsets(type, declarations);
        var getterOffsets = GetterOffsets(parser, analyzer, type, declarations);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var properties = new List<MemberReference>();

        foreach (var access in accesses)
            if (access.IsCompare && getterOffsets.TryGetValue(access.Offset, out var property) &&
                seen.Add(property.Owner + "." + property.Name))
                properties.Add(property);

        var fields = fieldOffsets.AsValueEnumerable()
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Value)
            .ToList();

        if (fields.Count + properties.Count != expected) return false;

        if (properties.Count > 0) members.Add(properties[0]);
        members.AddRange(fields);
        for (var index = 1; index < properties.Count; index++)
            members.Add(properties[index]);

        Log.Debug($"Layout {type.Name}: {members.AsValueEnumerable().Select(m => m.Name).JoinToString(", ")}");

        return true;
    }

    private static MethodDef? SerializeMethod(TypeDef type)
    {
        var formatter = type.NestedTypes.AsValueEnumerable().FirstOrDefault(nested =>
            nested.BaseType?.Name.String.StartsWith("MemoryPackFormatter", StringComparison.Ordinal) == true);

        return type.Methods.AsValueEnumerable().FirstOrDefault(candidate => candidate.Name.String == "Serialize") ??
               formatter?.Methods.AsValueEnumerable().FirstOrDefault(candidate => candidate.Name.String == "Serialize");
    }

    private static IEnumerable<(TypeDef Type, MemoryPackClass Declaration)> Chain(TypeDef type,
        Dictionary<string, MemoryPackClass> declarations)
    {
        for (var current = type; current != null; current = current.BaseType?.ResolveTypeDef())
            if (declarations.TryGetValue(current.FullName, out var declaration))
                yield return (current, declaration);
    }

    private static Dictionary<long, MemberReference> GetterOffsets(InstructionsParser parser,
        IInstructionAnalyzer analyzer, TypeDef type, Dictionary<string, MemoryPackClass> declarations)
    {
        var offsets = new Dictionary<long, MemberReference>();

        foreach (var (owner, declaration) in Chain(type, declarations))
        {
            var names = new HashSet<string>(declaration.Members.AsValueEnumerable()
                .Where(member => !member.IsField).Select(member => member.Name).ToArray(), StringComparer.Ordinal);

            foreach (var property in owner.Properties.AsValueEnumerable()
                         .Where(property => names.Contains(property.Name.String)))
            {
                if (property.GetMethod is not { } getter) continue;

                var accesses = analyzer.Analyze(parser.GetInstructions(getter), 0);
                if (accesses.Count == 0) continue;

                offsets[accesses[0].Offset] = new MemberReference(owner.FullName, property.Name.String);
            }
        }

        return offsets;
    }

    private static Dictionary<long, MemberReference> FieldOffsets(TypeDef type,
        Dictionary<string, MemoryPackClass> declarations)
    {
        var offsets = new Dictionary<long, MemberReference>();

        foreach (var (owner, declaration) in Chain(type, declarations))
        {
            var names = new HashSet<string>(declaration.Members.AsValueEnumerable()
                .Where(member => member.IsField).Select(member => member.Name).ToArray(), StringComparer.Ordinal);

            foreach (var field in owner.Fields.AsValueEnumerable()
                         .Where(field => names.Contains(field.Name.String)))
            {
                var offset = FieldOffset(field);
                if (offset < 0) continue;

                offsets[offset] = new MemberReference(owner.FullName, field.Name.String);
            }
        }

        return offsets;
    }

    private static long FieldOffset(FieldDef field)
    {
        var attribute = field.CustomAttributes.AsValueEnumerable().FirstOrDefault(candidate =>
            candidate.AttributeType.Name.String == "FieldOffsetAttribute");
        var value = attribute?.Fields.AsValueEnumerable()
            .FirstOrDefault(candidate => candidate.Name.String == "Offset")?.Argument.Value?.ToString();

        return value == null ? -1 : Convert.ToInt64(value[2..], 16);
    }
}
