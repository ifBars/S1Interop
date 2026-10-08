using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace S1Interop.Compiler;

/// <summary>
/// Lowers Unity-serialized fields of injected components onto Il2CppInterop native field wrappers.
/// </summary>
/// <remarks>
/// Each eligible field <c>T name = init;</c> becomes, at its original position:
/// a same-name public <c>Il2CppValueField&lt;T&gt;</c>/<c>Il2CppStringField</c> (the native serialized slot), padding
/// wrappers that fill the slot to 8 bytes so every native field stays pointer aligned, a private managed staging
/// field that keeps the authored initializer and its order, a private ready flag, and a generated public accessor
/// property that every authored reference is redirected to. The accessor creates the wrapper lazily and writes the
/// staged value exactly once, so constructors and base-constructor virtual calls observe authored values before
/// the loader populates wrappers. Expansion is delayed until member order is no longer needed by component injection.
/// </remarks>
internal sealed class ComponentFieldLowering(SemanticModel model, MetadataSymbolMap map, ImmutableArray<Diagnostic>.Builder diagnostics)
{
    public const string MarkerName = "S1Interop.Compiler.Generated.S1InteropFieldAccessorAttribute";

    private const string AnnotationKind = "S1Interop.ComponentField";
    private const string FieldsNamespace = "Il2CppInterop.Runtime.InteropTypes.Fields";
    private const string ValueFieldName = FieldsNamespace + ".Il2CppValueField`1";
    private const string StringFieldName = FieldsNamespace + ".Il2CppStringField";
    private const string ReferenceFieldName = FieldsNamespace + ".Il2CppReferenceField`1";
    private const string ValueFieldType = "global::" + FieldsNamespace + ".Il2CppValueField";
    private const string StringFieldType = "global::" + FieldsNamespace + ".Il2CppStringField";
    private const string ReferenceFieldType = "global::" + FieldsNamespace + ".Il2CppReferenceField";
    private const string HideAttribute = "global::Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp";
    private const string HideAttributeName = "Il2CppInterop.Runtime.Attributes.HideFromIl2CppAttribute";

    private static readonly DiagnosticDescriptor ReferenceAccess = new(
        "S1IC017",
        "Injected field is accessed by reference",
        "Field '{0}' is lowered to a native field accessor on IL2CPP and cannot be passed, returned, or addressed by reference",
        "S1Interop.Compiler", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor Blocked = new(
        "S1IC018",
        "Serialized component fields cannot be lowered",
        "Serialized fields of '{0}' cannot be lowered to IL2CPP native fields: {1}",
        "S1Interop.Compiler", DiagnosticSeverity.Error, true);

    /// <summary>One lowered field and every generated name that belongs to it.</summary>
    internal sealed record FieldPlan(
        IFieldSymbol Field,
        string Keyword,
        bool IsString,
        bool IsReference,
        string Property,
        string Staged,
        string Ready,
        ImmutableArray<(string Keyword, string Name)> Pads);

    private readonly Dictionary<string, MemberDeclarationSyntax[]> expansions = new();
    private readonly Dictionary<INamedTypeSymbol, ImmutableArray<FieldPlan>> plans = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, ImmutableArray<FieldPlan>> lowered = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<INamedTypeSymbol, ImmutableArray<(Location Location, string Reason)>> blockers = new(SymbolEqualityComparer.Default);
    private readonly Dictionary<IFieldSymbol, string?> libraryAccessors = new(SymbolEqualityComparer.Default);
    private bool? wrappersAvailable;

    /// <summary>Gets whether the target references contain every type that field lowering emits or calls.</summary>
    private bool WrappersAvailable => wrappersAvailable ??=
        map.FindTargetType(ValueFieldName) is not null &&
        map.FindTargetType(StringFieldName) is not null &&
        map.FindTargetType(HideAttributeName) is not null &&
        map.FindTargetType("S1Interop.Compiler.Generated.S1InteropInjection") is not null;

    /// <summary>Gets whether <paramref name="field"/> is an author field that this compilation lowers.</summary>
    public bool IsLowered(IFieldSymbol field) => PlanOf(field) is not null;

    /// <summary>
    /// Gets the generated accessor property that replaces <paramref name="field"/>, or null when the field stays as
    /// written. Fields of referenced component libraries resolve only through the marker the library was built with.
    /// </summary>
    public string? AccessorName(IFieldSymbol field)
    {
        field = field.OriginalDefinition;
        if (field.ContainingType is not { } owner)
        {
            return null;
        }

        return map.IsAuthorType(owner) ? PlanOf(field)?.Property : LibraryAccessor(field, owner);
    }

    /// <summary>Redirects one reference to a lowered field onto its accessor; null when the name is not such a reference.</summary>
    public SyntaxNode? Rewrite(IdentifierNameSyntax node)
    {
        if (model.GetSymbolInfo(node).Symbol is not IFieldSymbol field || AccessorName(field) is not { } accessor)
        {
            return null;
        }

        CheckReference(node, field);
        return IdentifierName(accessor).WithTriviaFrom(node);
    }

    /// <summary>Gets the statements that initialize every lowered field declared by <paramref name="type"/>, across all parts.</summary>
    public ImmutableArray<string> InitializationStatements(INamedTypeSymbol type)
    {
        ImmutableArray<FieldPlan> fields = Lowered(type);
        return fields.IsEmpty ? ImmutableArray<string>.Empty : [.. fields.Select(plan => Ensure(plan) + ";")];
    }

    /// <summary>Reports, from the part that declares them, the reasons <paramref name="type"/>'s serialized fields cannot be lowered.</summary>
    public void ReportBlockers(INamedTypeSymbol type, ClassDeclarationSyntax part)
    {
        if (!WrappersAvailable || Plan(type).IsEmpty)
        {
            return;
        }

        foreach ((Location location, string reason) in Blockers(type))
        {
            if (location.SourceTree == part.SyntaxTree && part.Span.Contains(location.SourceSpan))
            {
                diagnostics.Add(Diagnostic.Create(Blocked, location, type.Name, reason));
            }
        }
    }

    /// <summary>Marks one lowered field declaration for expansion; returns <paramref name="visited"/> unchanged otherwise.</summary>
    public SyntaxNode Mark(FieldDeclarationSyntax original, FieldDeclarationSyntax visited)
    {
        var fields = new List<FieldPlan>();
        foreach (VariableDeclaratorSyntax variable in original.Declaration.Variables)
        {
            if (model.GetDeclaredSymbol(variable) is IFieldSymbol symbol && PlanOf(symbol) is { } plan)
            {
                fields.Add(plan);
            }
        }

        if (fields.Count == 0 || fields.Count != original.Declaration.Variables.Count)
        {
            return visited;
        }

        (SyntaxList<AttributeListSyntax> attributes, SyntaxTriviaList pending) = KeptAttributes(original, visited);
        string typeText = visited.Declaration.Type.WithoutTrivia().ToString();
        string modifier = visited.Modifiers.Any(SyntaxKind.NewKeyword) ? "new " : "";
        string suppress = AllowsNullSuppression() ? " = null!" : "";
        SyntaxTriviaList modifierLeading = visited.Modifiers.Count > 0
            ? visited.Modifiers[0].LeadingTrivia
            : visited.Declaration.Type.GetLeadingTrivia();

        var members = new List<MemberDeclarationSyntax>();
        for (int i = 0; i < fields.Count; i++)
        {
            FieldPlan plan = fields[i];
            var wrapper = (FieldDeclarationSyntax)Parse($"public {modifier}{WrapperType(plan)} {Id(plan.Field.Name)}{suppress};");
            if (i == 0)
            {
                // The first wrapper owns the authored attribute lists and leading trivia, which keeps comments and
                // the original line count in place; the remaining generated members are single-line.
                SyntaxToken first = wrapper.Modifiers[0];
                wrapper = wrapper
                    .WithAttributeLists(attributes)
                    .WithModifiers(wrapper.Modifiers.Replace(first, first.WithLeadingTrivia(pending.AddRange(modifierLeading))));
            }
            else
            {
                wrapper = wrapper.WithAttributeLists(List(attributes.Select(list => list.WithoutTrivia().WithTrailingTrivia(Space))));
            }

            members.Add(wrapper);
            foreach ((string padKeyword, string padName) in plan.Pads)
            {
                members.Add(Parse($"[global::System.NonSerialized] public {ValueFieldType}<{padKeyword}> {padName}{suppress};"));
            }

            var staged = (FieldDeclarationSyntax)Parse($"private {typeText} {plan.Staged};");
            if (visited.Declaration.Variables[i].Initializer is { } initializer)
            {
                staged = staged.WithDeclaration(staged.Declaration.WithVariables(SingletonSeparatedList(
                    staged.Declaration.Variables[0].WithInitializer(initializer.WithLeadingTrivia(Space)))));
            }

            members.Add(staged);
            members.Add(Parse($"private bool {plan.Ready};"));
            members.Add(Parse(
                $"[global::{MarkerName}({Literal(plan.Field.Name)})] public {typeText} {plan.Property} {{ " +
                $"[{HideAttribute}] get {{ return {Ensure(plan)}.Value; }} " +
                $"[{HideAttribute}] set {{ {InjectionSupportSource.Helper}.SetField<{WrapperType(plan)}, {plan.Keyword}>" +
                $"(this, {Ensure(plan)}, {Literal(plan.Field.Name)}, value); }} }}"));
        }

        for (int i = 0; i < members.Count - 1; i++)
        {
            members[i] = members[i].WithTrailingTrivia(Space);
        }

        members[^1] = members[^1].WithTrailingTrivia(visited.GetTrailingTrivia());
        string id = expansions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        expansions.Add(id, [.. members]);
        return visited.WithAdditionalAnnotations(new SyntaxAnnotation(AnnotationKind, id));
    }

    /// <summary>Replaces every marked declaration with its generated members.</summary>
    public SyntaxNode Expand(SyntaxNode root) => expansions.Count == 0 ? root : new Expansion(expansions).Visit(root)!;

    private ImmutableArray<FieldPlan> Lowered(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        if (lowered.TryGetValue(type, out ImmutableArray<FieldPlan> cached))
        {
            return cached;
        }

        // Every gate here must agree with ComponentInjection.Rewrite, which injects exactly the injectable types.
        ImmutableArray<FieldPlan> result = WrappersAvailable &&
            map.IsAuthorType(type) &&
            ComponentInjection.IsInjectable(type, map) &&
            Plan(type) is { IsEmpty: false } candidate &&
            Blockers(type).IsEmpty
                ? candidate
                : ImmutableArray<FieldPlan>.Empty;
        lowered[type] = result;
        return result;
    }

    private FieldPlan? PlanOf(IFieldSymbol field)
    {
        field = field.OriginalDefinition;
        if (field.ContainingType is not { } owner || !map.IsAuthorType(owner))
        {
            return null;
        }

        return Lowered(owner).FirstOrDefault(plan => SymbolEqualityComparer.Default.Equals(plan.Field, field));
    }

    private string? LibraryAccessor(IFieldSymbol field, INamedTypeSymbol owner)
    {
        if (libraryAccessors.TryGetValue(field, out string? cached))
        {
            return cached;
        }

        // Only the marker identifies a library accessor; a guessed name could silently bind to an unrelated member.
        string? accessor = null;
        if (map.Resolve(owner).Target is { } target)
        {
            accessor = FindAccessor(target, field.Name)?.Name;
        }

        libraryAccessors[field] = accessor;
        return accessor;
    }

    private static bool HasMarker(IPropertySymbol property, string fieldName) =>
        property.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == MarkerName &&
            attribute.ConstructorArguments is [{ Value: string name }] && name == fieldName);

    internal static IPropertySymbol? FindAccessor(INamedTypeSymbol target, string fieldName)
    {
        var matches = target.GetMembers().OfType<IPropertySymbol>().Where(property => HasMarker(property, fieldName)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    /// <summary>Assigns every eligible field of <paramref name="type"/> its generated names, independent of whether the type is lowered.</summary>
    private ImmutableArray<FieldPlan> Plan(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        if (plans.TryGetValue(type, out ImmutableArray<FieldPlan> cached))
        {
            return cached;
        }

        FieldSymbolInfo[] eligible = type.GetMembers().OfType<IFieldSymbol>()
            .Where(IsEligible)
            .Select(field => new FieldSymbolInfo(field, FieldShape(field.Type)!.Value))
            .ToArray();
        ImmutableArray<FieldPlan> result = ImmutableArray<FieldPlan>.Empty;
        if (eligible.Length > 0)
        {
            HashSet<string> reserved = Reserved(type);
            var builder = ImmutableArray.CreateBuilder<FieldPlan>(eligible.Length);
            foreach ((IFieldSymbol field, (string keyword, int size)) in eligible)
            {
                string[] padKeywords = PadKeywords(size);
                for (int attempt = 0; ; attempt++)
                {
                    string suffix = attempt == 0 ? "" : "_" + attempt;
                    string property = "__S1InteropField_" + field.Name + suffix;
                    string staged = "__S1InteropStaged_" + field.Name + suffix;
                    string ready = "__S1InteropReady_" + field.Name + suffix;
                    var pads = padKeywords
                        .Select((padKeyword, index) => (padKeyword, "__S1InteropPad_" + field.Name + suffix + "_" + index))
                        .ToImmutableArray();
                    string[] names = [property, staged, ready, .. pads.Select(pad => pad.Item2)];
                    if (names.Any(reserved.Contains))
                    {
                        continue;
                    }

                    reserved.UnionWith(names);
                    builder.Add(new FieldPlan(field, keyword, field.Type.SpecialType == SpecialType.System_String,
                        ScalarShape(field.Type) is null, property, staged, ready, pads));
                    break;
                }
            }

            result = builder.ToImmutable();
        }

        plans[type] = result;
        return result;
    }

    private HashSet<string> Reserved(INamedTypeSymbol type)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            foreach (ISymbol member in current.GetMembers())
            {
                names.Add(member.Name);
            }

            if (!map.IsAuthorType(current))
            {
                // The target compilation of a referenced component library may declare other members, including its own accessors.
                if (map.Resolve(current).Target is { } target)
                {
                    foreach (ISymbol member in target.GetMembers())
                    {
                        names.Add(member.Name);
                    }
                }
            }
            else if (!SymbolEqualityComparer.Default.Equals(current, type))
            {
                foreach (FieldPlan plan in Plan(current))
                {
                    names.UnionWith([plan.Property, plan.Staged, plan.Ready, .. plan.Pads.Select(pad => pad.Name)]);
                }
            }
        }

        return names;
    }

    private ImmutableArray<(Location Location, string Reason)> Blockers(INamedTypeSymbol type)
    {
        type = type.OriginalDefinition;
        if (blockers.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var found = new List<(Location, string)>();
        foreach (IMethodSymbol constructor in type.InstanceConstructors.Where(c => !c.IsImplicitlyDeclared))
        {
            var syntax = constructor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() as ConstructorDeclarationSyntax;
            Location location = syntax?.Identifier.GetLocation() ?? constructor.Locations.FirstOrDefault() ?? Location.None;
            if (ComponentInjection.IsPointerConstructor(constructor))
            {
                if (constructor.DeclaredAccessibility != Accessibility.Public)
                {
                    found.Add((location, "the native IntPtr constructor must be public so field storage is initialized before authored code runs"));
                }
                else if (syntax?.Initializer?.ThisOrBaseKeyword.IsKind(SyntaxKind.ThisKeyword) == true)
                {
                    found.Add((location, "an IntPtr constructor that chains through 'this(...)' cannot initialize field storage"));
                }
            }
            else if (constructor.Parameters.Length == 0 && ComponentInjection.CallsDerivedConstructorPointer(constructor))
            {
                found.Add((location, "a constructor that already calls DerivedConstructorPointer cannot be rewritten to initialize field storage"));
            }
        }

        foreach (FieldPlan plan in Plan(type))
        {
            if (HasInjectedAncestorField(type, plan.Field.Name))
            {
                found.Add((plan.Field.Locations.FirstOrDefault() ?? Location.None,
                    $"field '{plan.Field.Name}' has the same name as a serialized ancestor field, and native fields are resolved by name"));
            }
        }

        ImmutableArray<(Location, string)> result = [.. found];
        blockers[type] = result;
        return result;
    }

    private bool HasInjectedAncestorField(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (map.IsAuthorType(current))
            {
                if (current.GetMembers(name).OfType<IFieldSymbol>().Any(IsEligible))
                {
                    return true;
                }
            }
            else if (map.Resolve(current).Target is { } target &&
                     target.GetMembers().OfType<IPropertySymbol>().Any(property => HasMarker(property, name)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Mirrors Unity's default serialization of a component field, restricted to what native wrappers can store.</summary>
    private bool IsEligible(IFieldSymbol field)
    {
        if (field.ContainingType is not { TypeKind: TypeKind.Class } ||
            field.IsStatic || field.IsConst || field.IsReadOnly || field.IsVolatile || field.IsImplicitlyDeclared ||
            field.IsFixedSizeBuffer || field.AssociatedSymbol is not null || field.RefKind != RefKind.None ||
            FieldShape(field.Type) is null)
        {
            return false;
        }

        string?[] attributes = field.GetAttributes().Select(attribute => attribute.AttributeClass?.ToDisplayString()).ToArray();
        if (attributes.Contains("System.NonSerializedAttribute") || attributes.Contains("UnityEngine.SerializeReference"))
        {
            return false;
        }

        return field.DeclaredAccessibility == Accessibility.Public || attributes.Contains("UnityEngine.SerializeField");
    }

    // decimal is deliberately absent: Unity does not serialize it.
    private (string Keyword, int Size)? FieldShape(ITypeSymbol type)
    {
        if (ScalarShape(type) is { } scalar) return scalar;
        if (map.SupportsNativeArrays && NativeArraySource.IsSupportedArray(map, type) && map.FindTargetType(ReferenceFieldName) is not null)
            return (NativeArrayLowering.Display(map, (IArrayTypeSymbol)type), 8);
        if (map.SupportsNativeReferenceArrays && NativeReferenceArraySource.IsMappedReferenceArray(map, type) &&
            type is IArrayTypeSymbol references && FieldShape(references.ElementType) is not null)
            return (NativeArrayLowering.Display(map, references), 8);
        if (type is not INamedTypeSymbol named || map.IsAuthorType(named) ||
            map.FindTargetType(ReferenceFieldName) is null || map.Resolve(named).Target is not { } target)
            return null;
        INamedTypeSymbol? unityObject = null;
        for (var current = named; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == "UnityEngine.Object") { unityObject = current; break; }
        if (unityObject is null || map.Resolve(unityObject).Target is not { } nativeUnityObject) return null;
        for (var current = target; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current, nativeUnityObject))
                return (map.TargetDisplay(type), 8);
        return null;
    }

    private (string Keyword, int Size)? ScalarShape(ITypeSymbol type)
    {
        if (Scalar(type) is { } scalar) return scalar;
        if (type is INamedTypeSymbol { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } underlying } named &&
            !map.IsAuthorType(named) &&
            map.Resolve(named).Target is { TypeKind: TypeKind.Enum, EnumUnderlyingType: { } targetUnderlying } &&
            underlying.SpecialType == targetUnderlying.SpecialType && Scalar(underlying) is { } storage)
            return (map.TargetDisplay(type), storage.Size);
        return null;
    }

    private static (string Keyword, int Size)? Scalar(ITypeSymbol type) => type.SpecialType switch
    {
        SpecialType.System_Boolean => ("bool", 1),
        SpecialType.System_SByte => ("sbyte", 1),
        SpecialType.System_Byte => ("byte", 1),
        SpecialType.System_Int16 => ("short", 2),
        SpecialType.System_UInt16 => ("ushort", 2),
        SpecialType.System_Char => ("char", 2),
        SpecialType.System_Int32 => ("int", 4),
        SpecialType.System_UInt32 => ("uint", 4),
        SpecialType.System_Single => ("float", 4),
        SpecialType.System_Int64 => ("long", 8),
        SpecialType.System_UInt64 => ("ulong", 8),
        SpecialType.System_Double => ("double", 8),
        SpecialType.System_String => ("string", 8),
        _ => null,
    };

    // Ascending sizes make the slot exactly 8 bytes under both packed and naturally aligned layout.
    private static string[] PadKeywords(int size) => size switch
    {
        1 => ["byte", "short", "int"],
        2 => ["short", "int"],
        4 => ["int"],
        _ => [],
    };

    private static string WrapperType(FieldPlan plan) =>
        plan.IsString ? StringFieldType : (plan.IsReference ? ReferenceFieldType : ValueFieldType) + "<" + plan.Keyword + ">";

    private static string Ensure(FieldPlan plan) =>
        $"{InjectionSupportSource.Helper}.EnsureField<{WrapperType(plan)}, {plan.Keyword}>" +
        $"(this, ref this.{Id(plan.Field.Name)}, ref this.{plan.Ready}, ref this.{plan.Staged}, {Literal(plan.Field.Name)})";

    private static string Id(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static MemberDeclarationSyntax Parse(string text) =>
        ParseMemberDeclaration(text) ?? throw new InvalidOperationException("Generated field member did not parse: " + text);

    // Generated wrappers need no initializer, but nullable-enabled projects would otherwise warn about them.
    private bool AllowsNullSuppression() =>
        model.SyntaxTree.Options is CSharpParseOptions options &&
        options.LanguageVersion.MapSpecifiedToEffectiveVersion() >= LanguageVersion.CSharp8;

    /// <summary>
    /// Drops UnityEngine.SerializeField, which the target exposes as a non-CLR attribute proxy, and keeps every other
    /// attribute. Trivia of dropped lists is carried forward so comments and line numbers stay stable.
    /// </summary>
    private (SyntaxList<AttributeListSyntax> Lists, SyntaxTriviaList Pending) KeptAttributes(
        FieldDeclarationSyntax original, FieldDeclarationSyntax visited)
    {
        var kept = new List<AttributeListSyntax>();
        SyntaxTriviaList pending = SyntaxTriviaList.Empty;
        for (int i = 0; i < original.AttributeLists.Count; i++)
        {
            AttributeListSyntax source = original.AttributeLists[i];
            AttributeListSyntax rewritten = visited.AttributeLists[i];
            var attributes = new List<AttributeSyntax>();
            for (int j = 0; j < source.Attributes.Count; j++)
            {
                if (!IsSerializeField(source.Attributes[j]))
                {
                    attributes.Add(rewritten.Attributes[j]);
                }
            }

            if (attributes.Count == 0)
            {
                pending = pending.AddRange(rewritten.GetLeadingTrivia()).AddRange(rewritten.GetTrailingTrivia());
                continue;
            }

            AttributeListSyntax result = attributes.Count == rewritten.Attributes.Count
                ? rewritten
                : rewritten.WithAttributes(SeparatedList(attributes));
            kept.Add(pending.Count == 0 ? result : result.WithLeadingTrivia(pending.AddRange(result.GetLeadingTrivia())));
            pending = SyntaxTriviaList.Empty;
        }

        return (List(kept), pending);
    }

    private bool IsSerializeField(AttributeSyntax attribute) =>
        model.GetSymbolInfo(attribute).Symbol is IMethodSymbol { ContainingType: { } type } &&
        type.ToDisplayString() == "UnityEngine.SerializeField";

    private void CheckReference(IdentifierNameSyntax name, IFieldSymbol field)
    {
        SyntaxNode current = name;
        while (current.Parent is MemberAccessExpressionSyntax access && access.Name == current ||
               current.Parent is ParenthesizedExpressionSyntax ||
               current.Parent is PostfixUnaryExpressionSyntax postfix && postfix.IsKind(SyntaxKind.SuppressNullableWarningExpression))
        {
            current = current.Parent!;
        }

        bool byReference = current.Parent switch
        {
            ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None),
            RefExpressionSyntax => true,
            PrefixUnaryExpressionSyntax prefix => prefix.IsKind(SyntaxKind.AddressOfExpression),
            _ => false,
        };
        if (byReference)
        {
            diagnostics.Add(Diagnostic.Create(ReferenceAccess, name.GetLocation(), field.Name));
        }
    }

    private readonly record struct FieldSymbolInfo(IFieldSymbol Field, (string Keyword, int Size) Scalar);

    private sealed class Expansion(Dictionary<string, MemberDeclarationSyntax[]> expansions) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
            return visited.WithMembers(List(visited.Members.SelectMany(member =>
                member.GetAnnotations(AnnotationKind).FirstOrDefault()?.Data is { } id ? expansions[id] : new[] { member })));
        }
    }
}
