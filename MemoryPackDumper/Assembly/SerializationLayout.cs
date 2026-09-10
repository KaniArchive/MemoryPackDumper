using System;
using System.Collections.Generic;
using System.IO;
using dnlib.DotNet;
using MemoryPackDumper.Instructions;
using ZLinq;

namespace MemoryPackDumper.Assembly;

public static class SerializationLayout
{
    public static void Apply(MemoryPackSchema schema, ModuleDef module, string gameAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(gameAssemblyPath) || !File.Exists(gameAssemblyPath)) return;
        var parser = new InstructionsParser(gameAssemblyPath);
        var analyzer = InstructionsAnalyzer.GetAnalyzer(parser.Architecture);

        foreach (var declaration in schema.Classes.AsValueEnumerable())
        {
            var type = module.GetTypes().AsValueEnumerable()
                .FirstOrDefault(candidate => candidate.FullName == declaration.FullName);
            if (type == null || !TryRead(parser, analyzer, type, declaration, out var members)) continue;

            var declared = declaration.Members.AsValueEnumerable().ToDictionary(member => member.Name, StringComparer.Ordinal);
            if (members.Count != declaration.Members.Count || members.AsValueEnumerable().Any(name => !declared.ContainsKey(name)))
                continue;

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

            foreach (var member in declaration.Members.AsValueEnumerable())
                if (!serialized.Contains(member.Name))
                    member.IsComputed = true;

            declaration.Members.Clear();
            declaration.Members.AddRange(ordered);
            foreach (var member in declared.Values)
                if (!serialized.Contains(member.Name))
                    declaration.Members.Add(member);
        }
    }

    private static bool TryRead(InstructionsParser parser, IInstructionAnalyzer analyzer, TypeDef type,
        MemoryPackClass declaration, out List<string> members)
    {
        members = [];
        var formatter = type.NestedTypes.AsValueEnumerable().FirstOrDefault(nested =>
            nested.BaseType?.Name.String.StartsWith("MemoryPackFormatter", StringComparison.Ordinal) == true);
        var method = type.Methods.AsValueEnumerable().FirstOrDefault(candidate => candidate.Name.String == "Serialize") ??
                     formatter?.Methods.AsValueEnumerable().FirstOrDefault(candidate => candidate.Name.String == "Serialize");
        if (method == null) return false;

        var fields = declaration.Members.AsValueEnumerable().Where(member => member.IsField)
            .Select(member => member.Name).ToList();
        var properties = declaration.Members.AsValueEnumerable().Where(member => !member.IsField)
            .Select(member => member.Name).ToList();
        var getterOffsets = GetGetterOffsets(parser, analyzer, type, properties);
        var fieldOffsets = GetFieldOffsets(type, fields);
        var accesses = analyzer.Analyze(parser.GetInstructions(method), 1);
        if (accesses.Count == 0) return false;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var serializedProperties = new List<string>();
        var serializedFields = new List<string>();
        foreach (var access in accesses)
        {
            var getter = getterOffsets.AsValueEnumerable().FirstOrDefault(pair => pair.Value == access.Offset);
            if (access.IsCompare && getter.Key != null && seen.Add(getter.Key))
            {
                serializedProperties.Add(getter.Key);
                continue;
            }

            if (!access.IsCompare && fieldOffsets.TryGetValue(access.Offset, out var field) && seen.Add(field))
                serializedFields.Add(field);
        }

        if (serializedProperties.Count > 0)
            members.Add(serializedProperties[0]);
        members.AddRange(serializedFields);
        for (var index = 1; index < serializedProperties.Count; index++)
            members.Add(serializedProperties[index]);

        return members.Count > 0;
    }

    private static Dictionary<string, long> GetGetterOffsets(InstructionsParser parser, IInstructionAnalyzer analyzer,
        TypeDef type, List<string> properties)
    {
        var offsets = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var property in type.Properties.AsValueEnumerable().Where(property => properties.Contains(property.Name.String)))
        {
            var getter = property.GetMethod;
            if (getter == null) continue;
            var accesses = analyzer.Analyze(parser.GetInstructions(getter), 0);
            if (accesses.Count > 0) offsets[property.Name.String] = accesses[0].Offset;
        }
        return offsets;
    }

    private static Dictionary<long, string> GetFieldOffsets(TypeDef type, List<string> fields)
    {
        var offsets = new Dictionary<long, string>();
        foreach (var field in type.Fields.AsValueEnumerable().Where(field => fields.Contains(field.Name.String)))
        {
            var attribute = field.CustomAttributes.AsValueEnumerable().FirstOrDefault(candidate =>
                candidate.AttributeType.Name.String == "FieldOffsetAttribute");
            var value = attribute?.Fields.AsValueEnumerable().FirstOrDefault(candidate => candidate.Name.String == "Offset")
                ?.Argument.Value?.ToString();
            if (value == null) continue;
            offsets[Convert.ToInt64(value[2..], 16)] = field.Name.String;
        }
        return offsets;
    }
}
