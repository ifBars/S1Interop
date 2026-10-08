using System.Reflection;
using System.Reflection.Metadata;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

/// <summary>Reads physical reference fields that Roslyn may hide behind event symbols.</summary>
internal sealed class ReferenceFieldCatalog(Compilation compilation)
{
    private CSharpCompilation? privateMetadata;

    public IEnumerable<string> FieldNames(INamedTypeSymbol type)
    {
        foreach (var reference in compilation.References.OfType<CompilationReference>())
            if (SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference), type.ContainingAssembly))
                return reference.Compilation.GetTypeByMetadataName(MetadataName(type))?.GetMembers()
                    .Where(member => member is IFieldSymbol or IEventSymbol).Select(member => member.Name).Distinct()
                    ?? Enumerable.Empty<string>();
        if (!assemblies.TryGetValue(type.ContainingAssembly, out var fields))
        {
            fields = ReadFields(type.ContainingAssembly);
            assemblies.Add(type.ContainingAssembly, fields);
        }
        string metadataName = MetadataName(type);
        return fields.Keys.Where(key => key.Type == metadataName).Select(key => key.Field);
    }

    public ITypeSymbol? FieldType(INamedTypeSymbol type, string name)
    {
        ITypeSymbol? MemberType(INamedTypeSymbol? owner, string memberName) =>
            owner?.GetMembers(memberName).Select(member => member switch
            {
                IFieldSymbol field => field.Type,
                IEventSymbol @event when EventStorageMatches(type, memberName) => @event.Type,
                _ => null
            }).FirstOrDefault(value => value is not null);
        if (MemberType(type, name) is { } visible) return visible;
        // Only this metadata view imports private members. Source binding and every
        // other lowering pass retain the caller's original compilation options.
        if (compilation is not CSharpCompilation csharp || type.IsGenericType) return null;
        privateMetadata ??= csharp.WithOptions(csharp.Options.WithMetadataImportOptions(MetadataImportOptions.All));
        var reference = compilation.References.FirstOrDefault(reference =>
            SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference), type.ContainingAssembly));
        return reference is null ? null : MemberType(
            (privateMetadata.GetAssemblyOrModuleSymbol(reference) as IAssemblySymbol)?.GetTypeByMetadataName(MetadataName(type)), name);
    }
    private bool EventStorageMatches(INamedTypeSymbol type, string name)
    {
        if (compilation.References.OfType<CompilationReference>().Any(reference =>
            SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference), type.ContainingAssembly)))
            return TryGetAttributes(type, name, out _);
        return assemblies.TryGetValue(type.ContainingAssembly, out var fields) &&
            fields.TryGetValue((MetadataName(type), name), out var entry) && entry.MatchesEvent;
    }

    private sealed record FieldEntry(string Name, FieldAttributes Attributes, bool Ambiguous = false, bool MatchesEvent = false);
    private readonly Dictionary<IAssemblySymbol, Dictionary<(string Type, string Field), FieldEntry>> assemblies =
        new(SymbolEqualityComparer.Default);

    public bool TryGetAttributes(INamedTypeSymbol type, string name, out FieldAttributes attributes)
    {
        // In-memory library compilations have no PE yet. C# field-like events have
        // private backing storage; custom, abstract and extern events do not.
        foreach (var reference in compilation.References.OfType<CompilationReference>())
        {
            if (!SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference), type.ContainingAssembly)) continue;
            var source = reference.Compilation.GetTypeByMetadataName(MetadataName(type));
            if (source?.GetMembers(name).OfType<IFieldSymbol>().FirstOrDefault() is { } field)
            {
                attributes = field.DeclaredAccessibility switch
                {
                    Accessibility.Public => FieldAttributes.Public,
                    Accessibility.Private => FieldAttributes.Private,
                    Accessibility.Protected => FieldAttributes.Family,
                    Accessibility.Internal => FieldAttributes.Assembly,
                    Accessibility.ProtectedOrInternal => FieldAttributes.FamORAssem,
                    Accessibility.ProtectedAndInternal => FieldAttributes.FamANDAssem,
                    _ => FieldAttributes.PrivateScope
                };
                if (field.IsStatic) attributes |= FieldAttributes.Static;
                if (field.IsReadOnly) attributes |= FieldAttributes.InitOnly;
                if (field.IsConst) attributes |= FieldAttributes.Literal | FieldAttributes.HasDefault;
                return true;
            }
            var @event = source?.GetMembers(name).OfType<IEventSymbol>().FirstOrDefault();
            if (@event is { IsAbstract: false, AddMethod.IsExtern: false } &&
                @event.DeclaringSyntaxReferences.Any(syntax => syntax.GetSyntax() is VariableDeclaratorSyntax
                    { Parent.Parent: EventFieldDeclarationSyntax }))
            {
                attributes = FieldAttributes.Private | (@event.IsStatic ? FieldAttributes.Static : 0);
                return true;
            }
        }
        if (!assemblies.TryGetValue(type.ContainingAssembly, out var fields))
        {
            fields = ReadFields(type.ContainingAssembly);
            assemblies.Add(type.ContainingAssembly, fields);
        }
        if (fields.TryGetValue((MetadataName(type), name), out var entry))
        {
            if (entry.Ambiguous)
                throw new ArgumentException($"Reflection lookup '{type}.{name}' has multiple field signatures; lowering an ambiguous field lookup is not yet supported.");
            attributes = entry.Attributes;
            return true;
        }
        attributes = default;
        return false;
    }

    private Dictionary<(string Type, string Field), FieldEntry> ReadFields(IAssemblySymbol assembly)
    {
        var physicalFields = new Dictionary<(string Type, int Index), FieldEntry>();
        foreach (var reference in compilation.References.OfType<PortableExecutableReference>())
        {
            if (!SymbolEqualityComparer.Default.Equals(compilation.GetAssemblyOrModuleSymbol(reference), assembly)) continue;
            // Metadata is owned by the reference and must not be disposed here.
            if (reference.GetMetadata() is not AssemblyMetadata metadata) continue;
            foreach (var module in metadata.GetModules())
            {
                var reader = module.GetMetadataReader();
                foreach (var handle in reader.TypeDefinitions)
                {
                    var definition = reader.GetTypeDefinition(handle);
                    string typeName = MetadataName(reader, handle);
                    int index = 0;
                    foreach (var fieldHandle in definition.GetFields())
                    {
                        var field = reader.GetFieldDefinition(fieldHandle);
                        string name = reader.GetString(field.Name);
                        var events = definition.GetEvents().Select(reader.GetEventDefinition)
                            .Where(@event => reader.GetString(@event.Name) == name).ToArray();
                        bool matchesEvent = events.Length == 1 && EventSignatureMatches(reader, field.Signature, events[0].Type);
                        physicalFields[(typeName, index++)] = new(name, field.Attributes, MatchesEvent: matchesEvent);
                    }
                }
            }
        }
        RestoreOriginalVisibility(assembly, physicalFields);
        var fields = new Dictionary<(string Type, string Field), FieldEntry>();
        foreach (var pair in physicalFields)
        {
            var key = (pair.Key.Type, pair.Value.Name);
            fields[key] = fields.ContainsKey(key) ? pair.Value with { Ambiguous = true } : pair.Value;
        }
        return fields;
    }

    private static bool EventSignatureMatches(MetadataReader reader, BlobHandle signature, EntityHandle eventType)
    {
        byte[] field = reader.GetBlobBytes(signature);
        if (field.Length < 2 || field[0] != 0x06) return false; // FIELD signature header
        byte[] expected;
        if (eventType.Kind == HandleKind.TypeSpecification)
            expected = reader.GetBlobBytes(reader.GetTypeSpecification((TypeSpecificationHandle)eventType).Signature);
        else if (eventType.Kind is HandleKind.TypeDefinition or HandleKind.TypeReference)
        {
            var builder = new BlobBuilder();
            builder.WriteByte(0x12); // Delegate storage is a CLASS, never a valuetype.
            int row = System.Reflection.Metadata.Ecma335.MetadataTokens.GetRowNumber(eventType);
            builder.WriteCompressedInteger((row << 2) | (eventType.Kind == HandleKind.TypeReference ? 1 : 0));
            expected = builder.ToArray();
        }
        else return false;
        return field.AsSpan(1).SequenceEqual(expected);
    }

    private static void RestoreOriginalVisibility(IAssemblySymbol assembly,
        Dictionary<(string Type, int Index), FieldEntry> fields)
    {
        var records = assembly.GetAttributes().Where(attribute =>
            attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute" &&
            attribute.ConstructorArguments.Length == 2 &&
            attribute.ConstructorArguments[0].Value as string == GameReferencePublicizer.OriginalFieldVisibilityMetadataKey).ToArray();
        if (records.Length == 0)
        {
            if (assembly.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute" &&
                attribute.ConstructorArguments.Length == 2 &&
                attribute.ConstructorArguments[0].Value as string == GameReferencePublicizer.OriginalHashMetadataKey))
                throw InvalidVisibility(assembly);
            return;
        }
        if (records.Length != 1 || records[0].ConstructorArguments[1].Value is not string json)
            throw InvalidVisibility(assembly);
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var seen = new HashSet<(string Type, int Index)>();
            foreach (var item in document.RootElement.EnumerateArray())
            {
                var key = (item.GetProperty("Type").GetString()!, item.GetProperty("Index").GetInt32());
                string? name = item.GetProperty("Name").GetString();
                int access = item.GetProperty("Access").GetInt32();
                if (key.Item1 is null || name is null || access < 0 || access >= (int)FieldAttributes.Public ||
                    !seen.Add(key) || !fields.TryGetValue(key, out var entry) || entry.Name != name)
                    throw InvalidVisibility(assembly);
                fields[key] = entry with { Attributes = (entry.Attributes & ~FieldAttributes.FieldAccessMask) | (FieldAttributes)access };
            }
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            throw InvalidVisibility(assembly, exception);
        }
    }

    private static ArgumentException InvalidVisibility(IAssemblySymbol assembly, Exception? inner = null) =>
        new($"Invalid original field visibility metadata in '{assembly.Identity.Name}'. Prepare references again from the original game assembly.", inner);

    private static string MetadataName(INamedTypeSymbol type) => type.ContainingType is { } parent
        ? MetadataName(parent) + "+" + type.MetadataName
        : (type.ContainingNamespace.IsGlobalNamespace ? "" : type.ContainingNamespace.ToDisplayString() + ".") + type.MetadataName;

    private static string MetadataName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        if (!type.GetDeclaringType().IsNil) return MetadataName(reader, type.GetDeclaringType()) + "+" + name;
        string ns = reader.GetString(type.Namespace);
        return ns.Length == 0 ? name : ns + "." + name;
    }
}
