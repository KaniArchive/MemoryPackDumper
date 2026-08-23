using dnlib.DotNet;
using MemoryPackDumper.Context;
using ZLinq;

namespace MemoryPackDumper.Assembly;

public static class MemberParser
{
    public static MemoryPackClass TypeToMemoryPackClass(TypeDef typeDef, HashSet<TypeDef> discoveredTypes)
    {
        var className = GetClassName(typeDef);
        var typeKeyword = TypeHelper.GetTypeKeyword(typeDef);
        var baseType = TypeHelper.GetBaseType(typeDef);
        var memoryPackClass = new MemoryPackClass(className, baseType, typeKeyword)
        {
            IsRecord = IsRecordType(typeDef),
            IsMemoryPackable = TypeReferenceTracker.IsMemoryPackable(typeDef),
            BaseTypeReference = typeDef.BaseType,
            FullName = typeDef.FullName,
            BaseTypeFullName = GetBaseTypeFullName(typeDef),
            OriginalNamespace = typeDef.Namespace ?? ""
        };

        AttributeExtractor.ExtractClassAttributes(typeDef, memoryPackClass);

        TrackBaseType(typeDef, discoveredTypes);
        TrackUnionTargets(typeDef, discoveredTypes);

        ProcessMembers(typeDef, memoryPackClass, discoveredTypes);
        RemoveDuplicateMembers(memoryPackClass);
        SortMembersByOrder(memoryPackClass);
        ProcessMethods(typeDef, memoryPackClass, discoveredTypes);
        ProcessNestedTypes(typeDef, memoryPackClass, discoveredTypes);

        return memoryPackClass;
    }
    
    private static string GetBaseTypeFullName(TypeDef typeDef)
    {
        if (typeDef.BaseType == null) return "";

        return typeDef.BaseType.ResolveTypeDef()?.FullName ?? typeDef.BaseType.FullName;
    }


    private static void TrackBaseType(TypeDef typeDef, HashSet<TypeDef> discoveredTypes)
    {
        if (typeDef.BaseType == null) return;

        var baseFullName = typeDef.BaseType.FullName;
        if (baseFullName is "System.Object" or "System.ValueType" or "System.Enum") return;

        TypeReferenceTracker.TrackReferencedType(typeDef.BaseType, discoveredTypes);
    }

    private static void TrackUnionTargets(TypeDef typeDef, HashSet<TypeDef> discoveredTypes)
    {
        foreach (var attr in typeDef.CustomAttributes.AsValueEnumerable()
                     .Where(a => a.AttributeType.Name == "MemoryPackUnionAttribute"))
        {
            if (attr.ConstructorArguments.Count < 2) continue;
            if (attr.ConstructorArguments[1].Value is TypeSig typeSig)
                TypeReferenceTracker.TrackReferencedType(typeSig, discoveredTypes);
        }
    }

    private static string GetClassName(TypeDef typeDef)
    {
        var name = typeDef.Name.String;

        if (!typeDef.HasGenericParameters)
            return name;

        if (name.Contains('`'))
            name = name[..name.IndexOf('`')];

        var genericParams = typeDef.GenericParameters.AsValueEnumerable().Select(p => p.Name.String).JoinToString(", ");
        return $"{name}<{genericParams}>";
    }

    private static bool IsRecordType(TypeDef typeDef) =>
        typeDef.CustomAttributes.AsValueEnumerable().Any(a =>
            a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute") ||
        (typeDef.BaseType != null && typeDef.BaseType.Name == "Record");

    private static void ProcessMembers(TypeDef typeDef, MemoryPackClass memoryPackClass,
        HashSet<TypeDef> discoveredTypes)
    {
        if (typeDef.IsInterface)
        {
            ProcessInterfaceProperties(typeDef, memoryPackClass, discoveredTypes);
            return;
        }

        var backingFields = MapBackingFields(typeDef);
        var emittedProperties = new HashSet<string>(StringComparer.Ordinal);
        var preserveLayout = IsRawLayoutStruct(typeDef);

        foreach (var field in typeDef.Fields.AsValueEnumerable().Where(f => !f.IsStatic && !f.IsLiteral))
        {
            if (backingFields.TryGetValue(field.Name.String, out var property))
            {
                var propertyMember = CreateMemberFromProperty(property);
                emittedProperties.Add(property.Name.String);
                AddMember(memoryPackClass, propertyMember, propertyMember.IsPublic, discoveredTypes);
                continue;
            }

            if (IsCompilerGeneratedBackingField(field)) continue;

            var fieldMember = CreateMemberFromField(field);
            AddMember(memoryPackClass, fieldMember, field.IsPublic || preserveLayout, discoveredTypes);
        }

        ProcessComputedProperties(typeDef, memoryPackClass, emittedProperties, discoveredTypes);
    }

    private static void ProcessInterfaceProperties(TypeDef typeDef, MemoryPackClass memoryPackClass,
        HashSet<TypeDef> discoveredTypes)
    {
        foreach (var property in typeDef.Properties)
        {
            var accessorMethod = property.GetMethod ?? property.SetMethod;
            if (accessorMethod == null || accessorMethod.IsStatic) continue;
            if (IsIndexer(property)) continue;

            var member = CreateMemberFromProperty(property);
            AddMember(memoryPackClass, member, accessorMethod.IsPublic, discoveredTypes);
        }
    }

    private static void ProcessComputedProperties(TypeDef typeDef, MemoryPackClass memoryPackClass,
        HashSet<string> emittedProperties, HashSet<TypeDef> discoveredTypes)
    {
        foreach (var property in typeDef.Properties)
        {
            if (emittedProperties.Contains(property.Name.String)) continue;

            var accessorMethod = property.GetMethod ?? property.SetMethod;
            if (accessorMethod == null || accessorMethod.IsStatic) continue;
            if (IsIndexer(property)) continue;

            var member = CreateMemberFromProperty(property);
            member.IsComputed = true;
            AddMember(memoryPackClass, member, accessorMethod.IsPublic, discoveredTypes);
        }
    }

    private static void AddMember(MemoryPackClass memoryPackClass, MemoryPackMember member, bool isPublic,
        HashSet<TypeDef> discoveredTypes)
    {
        if (!ShouldIncludeMember(member, isPublic)) return;

        memoryPackClass.Members.Add(member);
        TypeReferenceTracker.TrackReferencedType(member.Type, discoveredTypes);
    }

    private static Dictionary<string, PropertyDef> MapBackingFields(TypeDef typeDef)
    {
        var map = new Dictionary<string, PropertyDef>(StringComparer.Ordinal);

        foreach (var property in typeDef.Properties)
        {
            var accessorMethod = property.GetMethod ?? property.SetMethod;
            if (accessorMethod == null || accessorMethod.IsStatic) continue;
            if (IsIndexer(property)) continue;

            map[$"<{property.Name.String}>k__BackingField"] = property;
        }

        return map;
    }

    private static bool IsRawLayoutStruct(TypeDef typeDef) =>
        typeDef is { IsValueType: true, IsEnum: false } && !TypeReferenceTracker.IsMemoryPackable(typeDef);

    private static bool IsIndexer(PropertyDef property)
    {
        if (property.PropertySig?.Params.Count > 0) return true;

        if (property.GetMethod is { } getter && RealParameterCount(getter) > 0) return true;

        return property.SetMethod is { } setter && RealParameterCount(setter) > 1;
    }

    private static int RealParameterCount(MethodDef method) =>
        method.Parameters.AsValueEnumerable().Count(p => !p.IsHiddenThisParameter);

    private static void RemoveDuplicateMembers(MemoryPackClass memoryPackClass)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        memoryPackClass.Members.RemoveAll(m => !seen.Add(m.Name));
    }

    private static bool IsCompilerGeneratedBackingField(FieldDef field)
    {
        var name = field.Name.String;
        return name.StartsWith('<') && name.EndsWith(">k__BackingField");
    }

    private static bool ShouldIncludeMember(MemoryPackMember member, bool isPublic)
    {
        if (ParserOptionsContext.Current.AllowHidden) return !member.IsIgnored;
        return member.IsInclude || (!member.IsIgnored && isPublic);
    }

    private static void SortMembersByOrder(MemoryPackClass memoryPackClass)
    {
        if (!memoryPackClass.Members.AsValueEnumerable().Any(m => m.Order.HasValue)) return;

        memoryPackClass.Members.Sort((a, b) =>
        {
            return (a.Order, b.Order) switch
            {
                (not null, not null) => a.Order.Value.CompareTo(b.Order.Value),
                (not null, null) => -1,
                (null, not null) => 1,
                _ => 0
            };
        });
    }

    private static void ProcessMethods(TypeDef typeDef, MemoryPackClass memoryPackClass,
        HashSet<TypeDef> discoveredTypes)
    {
        foreach (var method in typeDef.Methods)
        {
            if (!IsMemoryPackMethod(method)) continue;

            var memMethod = CreateMethodFromDefinition(method);
            memMethod.IsConstructor = method is { IsConstructor: true, IsStatic: false };
            memoryPackClass.Methods.Add(memMethod);

            TrackMethodSignature(method, discoveredTypes);
        }
    }

    private static void TrackMethodSignature(MethodDef method, HashSet<TypeDef> discoveredTypes)
    {
        if (!method.IsConstructor)
            TypeReferenceTracker.TrackReferencedType(method.ReturnType, discoveredTypes);

        foreach (var param in method.Parameters.AsValueEnumerable().Where(p => !p.IsHiddenThisParameter))
            TypeReferenceTracker.TrackReferencedType(param.Type, discoveredTypes);
    }

    private static bool IsMemoryPackMethod(MethodDef method) =>
        IsMemoryPackConstructor(method) || IsParameterizedConstructor(method) || IsCallbackMethod(method) ||
        IsStaticConstructor(method) || IsOverrideMethod(method);

    private static bool IsOverrideMethod(MethodDef method)
    {
        if (method is not { IsVirtual: true, IsNewSlot: false, IsGetter: false, IsSetter: false })
            return false;

        return method.Name != "Serialize" && method.Name != "Deserialize";
    }

    private static bool IsMemoryPackConstructor(MethodDef method) =>
        method.IsConstructor &&
        method.CustomAttributes.AsValueEnumerable()
            .Any(a => a.AttributeType.Name == "MemoryPackConstructorAttribute");

    private static bool IsCallbackMethod(MethodDef method) =>
        method.CustomAttributes.AsValueEnumerable().Any(a =>
            a.AttributeType.Name == "MemoryPackOnSerializingAttribute" ||
            a.AttributeType.Name == "MemoryPackOnSerializedAttribute" ||
            a.AttributeType.Name == "MemoryPackOnDeserializingAttribute" ||
            a.AttributeType.Name == "MemoryPackOnDeserializedAttribute");

    private static bool IsStaticConstructor(MethodDef method) =>
        method is { IsStatic: true } &&
        method.Name.String == "StaticConstructor" &&
        method.ReturnType.FullName == "System.Void" &&
        method.Parameters.Count == 0;

    private static bool IsParameterizedConstructor(MethodDef method) =>
        method is { IsConstructor: true, IsStatic: false, IsPublic: true } &&
        method.Parameters.AsValueEnumerable().Any(p => !p.IsHiddenThisParameter);

    private static MemoryPackMethod CreateMethodFromDefinition(MethodDef methodDef)
    {
        var returnType = TypeStringConverter.TypeToString(methodDef.ReturnType);
        var visibility = GetMethodVisibility(methodDef);
        var method = new MemoryPackMethod(
            methodDef.Name,
            returnType,
            methodDef.IsStatic,
            visibility
        );

        AttributeExtractor.ExtractMethodAttributes(methodDef, method);

        foreach (var param in methodDef.Parameters)
        {
            if (param.IsHiddenThisParameter) continue;

            var paramType = TypeStringConverter.TypeToString(param.Type);
            method.Parameters.Add((paramType, param.Name));
        }

        return method;
    }

    private static string GetMethodVisibility(MethodDef methodDef)
    {
        if (methodDef.IsPublic) return "public";
        if (methodDef.IsFamily) return "protected";
        if (methodDef.IsAssembly) return "internal";
        if (methodDef.IsFamilyOrAssembly) return "protected internal";
        if (methodDef.IsFamilyAndAssembly) return "private protected";
        return "private";
    }

    private static MemoryPackMember CreateMemberFromProperty(PropertyDef property)
    {
        var accessorMethod = property.GetMethod ?? property.SetMethod;
        var member = new MemoryPackMember(property.Name, property.PropertySig.RetType, false)
        {
            IsPublic = accessorMethod?.IsPublic ?? false,
            IsInit = IsInitOnlySetter(property.SetMethod),
            IsReadOnly = property.SetMethod == null,
            HasSetter = property.SetMethod != null
        };
        AttributeExtractor.ExtractMemberAttributes(property.CustomAttributes, member);
        return member;
    }

    private static bool IsInitOnlySetter(MethodDef? setMethod) =>
        setMethod?.ReturnType is CModReqdSig modifier &&
        modifier.Modifier?.Name == "IsExternalInit";

    private static MemoryPackMember CreateMemberFromField(FieldDef field)
    {
        var member = new MemoryPackMember(field.Name, field.FieldType, true)
        {
            IsPublic = field.IsPublic,
            IsReadOnly = field.IsInitOnly
        };
        AttributeExtractor.ExtractMemberAttributes(field.CustomAttributes, member);
        return member;
    }

    public static MemoryPackEnum TypeToEnum(TypeDef typeDef)
    {
        var underlyingTypeName = GetEnumUnderlyingTypeName(typeDef);
        var memoryPackEnum = new MemoryPackEnum(underlyingTypeName, typeDef.Name)
        {
            OriginalNamespace = typeDef.Namespace ?? ""
        };

        foreach (var fieldDef in typeDef.Fields.AsValueEnumerable().Where(f => f.HasConstant))
        {
            var enumField = new MemoryPackEnumField(fieldDef.Name, Convert.ToInt64(fieldDef.Constant.Value));
            memoryPackEnum.Fields.Add(enumField);
        }

        return memoryPackEnum;
    }

    private static string GetEnumUnderlyingTypeName(TypeDef typeDef)
    {
        var valueField = typeDef.Fields.FirstOrDefault(f => f.Name == "value__");
        if (valueField != null)
            return valueField.FieldType.FullName switch
            {
                "System.Byte" => "byte",
                "System.SByte" => "sbyte",
                "System.Int16" => "short",
                "System.UInt16" => "ushort",
                "System.Int32" => "int",
                "System.UInt32" => "uint",
                "System.Int64" => "long",
                "System.UInt64" => "ulong",
                _ => "int"
            };

        return "int";
    }

    private static void ProcessNestedTypes(TypeDef typeDef, MemoryPackClass memoryPackClass,
        HashSet<TypeDef> discoveredTypes)
    {
        if (!typeDef.HasNestedTypes) return;

        var parentClassName = GetClassName(typeDef);

        foreach (var nestedType in typeDef.NestedTypes)
        {
            if (!nestedType.IsNestedPublic) continue;

            if (IsAutoGeneratedFormatter(nestedType, parentClassName)) continue;

            var nestedClass = TypeToMemoryPackClass(nestedType, discoveredTypes);
            memoryPackClass.NestedClasses.Add(nestedClass);
        }
    }

    private static bool IsAutoGeneratedFormatter(TypeDef nestedType, string parentClassName)
    {
        if (nestedType.BaseType == null) return false;

        var baseTypeName = nestedType.BaseType.Name.String;
        if (!baseTypeName.StartsWith("MemoryPackFormatter")) return false;

        if (nestedType.BaseType is not TypeSpec { TypeSig: GenericInstSig genericBase } ||
            genericBase.GenericArguments.Count <= 0) return false;
        var formattedTypeName = genericBase.GenericArguments[0].TypeName;
        var parentBaseName = parentClassName.Contains('<')
            ? parentClassName[..parentClassName.IndexOf('<')]
            : parentClassName;

        if (formattedTypeName == parentBaseName) return true;

        return false;
    }
}