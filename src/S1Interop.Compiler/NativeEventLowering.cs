using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace S1Interop.Compiler;

/// <summary>Preserves authored event operations when their delegate is a native proxy class on the target.</summary>
internal sealed class NativeEventLowering(SemanticModel model, MetadataSymbolMap map, ImmutableArray<Diagnostic>.Builder diagnostics)
{
    private const string AnnotationKind = "S1Interop.NativeEvent";
    private const string MarkerName = "S1Interop.Compiler.Generated.S1InteropEventAccessorAttribute";
    private readonly Dictionary<string, MemberDeclarationSyntax[]> expansions = new();

    public SyntaxNode Mark(EventFieldDeclarationSyntax original, EventFieldDeclarationSyntax visited)
    {
        if (model.GetDeclaredSymbol(original.Declaration.Variables[0]) is not IEventSymbol first || !IsNativeDelegate(first.Type)) return visited;
        CheckEventAttributes(original.AttributeLists, first);
        var members = new List<MemberDeclarationSyntax>();
        for (int i = 0; i < original.Declaration.Variables.Count; i++)
        {
            var symbol = (IEventSymbol)model.GetDeclaredSymbol(original.Declaration.Variables[i])!;
            var variable = visited.Declaration.Variables[i];
            bool contract = symbol.IsAbstract || symbol.ContainingType.TypeKind == TypeKind.Interface;
            if (!contract)
            {
                var modifiers = TokenList(Token(SyntaxKind.PrivateKeyword));
                if (symbol.IsStatic) modifiers = modifiers.Add(Token(SyntaxKind.StaticKeyword));
                members.Add(FieldDeclaration(visited.Declaration.WithVariables(SingletonSeparatedList(variable)))
                    .WithModifiers(modifiers)
                    .WithAttributeLists(List(visited.AttributeLists.Where(list => list.Target?.Identifier.ValueText == "field"))));
            }
            foreach (bool add in new[] { true, false })
            {
                string operation = add ? "Add" : "Remove";
                var body = contract ? null : Block(ParseStatement(
                    $"{NativeDelegateCacheSource.TypeName}.{operation}(ref {variable.Identifier.Text}, value);"));
                var method = Accessor(symbol, add, visited.Declaration.Type, visited.Modifiers, body, null);
                members.Add(method.AddAttributeLists(visited.AttributeLists
                    .Where(list => list.Target?.Identifier.ValueText == "method")
                    .Select(list => list.WithTarget(null)).ToArray()));
            }
        }
        return Store(visited, members);
    }

    public SyntaxNode Mark(EventDeclarationSyntax original, EventDeclarationSyntax visited)
    {
        if (model.GetDeclaredSymbol(original) is not { } symbol || !IsNativeDelegate(symbol.Type)) return visited;
        CheckEventAttributes(original.AttributeLists, symbol);
        var members = visited.AccessorList!.Accessors.Select(accessor => (MemberDeclarationSyntax)Accessor(symbol,
            accessor.IsKind(SyntaxKind.AddAccessorDeclaration), visited.Type, visited.Modifiers,
            accessor.Body ?? (accessor.ExpressionBody is { } arrow ? Block(ExpressionStatement(arrow.Expression)) : null),
            visited.ExplicitInterfaceSpecifier).AddAttributeLists(accessor.AttributeLists.ToArray())).ToArray();
        return Store(visited, members);
    }

    public ExpressionSyntax Subscription(AssignmentExpressionSyntax original, AssignmentExpressionSyntax visited)
    {
        if (!original.IsKind(SyntaxKind.AddAssignmentExpression) && !original.IsKind(SyntaxKind.SubtractAssignmentExpression)) return visited;
        if (model.GetSymbolInfo(original.Left).Symbol is not IEventSymbol symbol || !IsNativeDelegate(symbol.Type)) return visited;
        bool add = original.IsKind(SyntaxKind.AddAssignmentExpression);
        string name = AccessorName(symbol, add);
        if (!map.IsAuthorType(symbol.ContainingType) &&
            !(map.Resolve(symbol.ContainingType).Target?.GetMembers(name).OfType<IMethodSymbol>().Any(method =>
                method.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() ==
                    MarkerName)) ?? false)) return visited;
        ExpressionSyntax call = visited.Left is MemberAccessExpressionSyntax access
            ? access.WithName(IdentifierName(name)) : IdentifierName(name).WithTriviaFrom(visited.Left);
        return InvocationExpression(call, ArgumentList(SingletonSeparatedList(Argument(visited.Right.WithoutTrivia())))).WithTriviaFrom(original);
    }

    public SyntaxNode Expand(SyntaxNode root) => new Expansion(expansions).Visit(root)!;

    private void CheckEventAttributes(SyntaxList<AttributeListSyntax> lists, IEventSymbol symbol)
    {
        foreach (var list in lists)
            if (list.Target is null || list.Target.Identifier.ValueText == "event")
                diagnostics.Add(Diagnostic.Create(LoweringDiagnostics.UnsupportedNativeEventMetadata,
                    list.GetLocation(), symbol.ToDisplayString()));
    }

    private SyntaxNode Store(MemberDeclarationSyntax visited, IEnumerable<MemberDeclarationSyntax> members)
    {
        string id = expansions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var expanded = members.Select(member => (MemberDeclarationSyntax)member.NormalizeWhitespace()).ToArray();
        expanded[0] = expanded[0].WithLeadingTrivia(visited.GetLeadingTrivia());
        expanded[^1] = expanded[^1].WithTrailingTrivia(visited.GetTrailingTrivia());
        expansions.Add(id, expanded);
        return visited.WithAdditionalAnnotations(new SyntaxAnnotation(AnnotationKind, id));
    }

    private MethodDeclarationSyntax Accessor(IEventSymbol symbol, bool add, TypeSyntax type,
        SyntaxTokenList modifiers, BlockSyntax? body, ExplicitInterfaceSpecifierSyntax? explicitInterface)
    {
        var method = MethodDeclaration(PredefinedType(Token(SyntaxKind.VoidKeyword)), AccessorName(symbol, add))
            .WithModifiers(modifiers)
            .WithExplicitInterfaceSpecifier(explicitInterface)
            .WithParameterList(ParameterList(SingletonSeparatedList(Parameter(Identifier("value")).WithType(type.WithoutTrivia()))))
            .WithBody(body)
            .WithAttributeLists(List(new[] {
                AttributeList(SingletonSeparatedList(Attribute(ParseName("global::System.Runtime.CompilerServices.CompilerGenerated")))),
                AttributeList(SingletonSeparatedList(Attribute(ParseName("global::Il2CppInterop.Runtime.Attributes.HideFromIl2Cpp")))),
                AttributeList(SingletonSeparatedList(Attribute(ParseName("global::" + MarkerName))
                    .WithArgumentList(AttributeArgumentList(SeparatedList(new[] {
                        AttributeArgument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(symbol.MetadataName))),
                        AttributeArgument(LiteralExpression(add ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression))
                    })))))
            }));
        return body is null ? method.WithSemicolonToken(Token(SyntaxKind.SemicolonToken)) : method;
    }

    private string AccessorName(IEventSymbol symbol, bool add)
    {
        if (!map.IsAuthorType(symbol.ContainingType) && map.Resolve(symbol.ContainingType).Target is { } target)
        {
            var matches = target.GetMembers().OfType<IMethodSymbol>().Where(method => method.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == MarkerName && attribute.ConstructorArguments.Length == 2 &&
                attribute.ConstructorArguments[0].Value as string == symbol.MetadataName &&
                attribute.ConstructorArguments[1].Value is bool action && action == add)).ToArray();
            if (matches.Length == 1) return matches[0].Name;
        }
        if (symbol.OverriddenEvent is { } parent) return AccessorName(parent, add);
        if (symbol.ExplicitInterfaceImplementations.FirstOrDefault() is { } implementation) return AccessorName(implementation, add);
        if (symbol.ContainingType.TypeKind != TypeKind.Interface)
        {
            var contract = symbol.ContainingType.AllInterfaces.SelectMany(type => type.GetMembers().OfType<IEventSymbol>())
                .FirstOrDefault(candidate => SymbolEqualityComparer.Default.Equals(
                    symbol.ContainingType.FindImplementationForInterfaceMember(candidate), symbol));
            if (contract is not null) return AccessorName(contract, add);
        }
        string prefix = "__S1InteropEvent_" + (add ? "add_" : "remove_") + symbol.Name;
        string name = prefix;
        int suffix = 0;
        while (symbol.ContainingType.GetMembers(name).Length != 0) name = prefix + ++suffix;
        return name;
    }

    private bool IsNativeDelegate(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Delegate } named) return false;
        for (var target = map.Resolve(named).Target; target is not null; target = target.BaseType)
            if (target.ToDisplayString() == "Il2CppSystem.Delegate") return true;
        return false;
    }

    private sealed class Expansion(Dictionary<string, MemberDeclarationSyntax[]> expansions) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) =>
            Expand((ClassDeclarationSyntax)base.VisitClassDeclaration(node)!);
        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node) =>
            Expand((StructDeclarationSyntax)base.VisitStructDeclaration(node)!);
        public override SyntaxNode? VisitInterfaceDeclaration(InterfaceDeclarationSyntax node) =>
            Expand((InterfaceDeclarationSyntax)base.VisitInterfaceDeclaration(node)!);
        public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node) =>
            Expand((RecordDeclarationSyntax)base.VisitRecordDeclaration(node)!);
        private T Expand<T>(T type) where T : TypeDeclarationSyntax => (T)type.WithMembers(List(type.Members.SelectMany(member =>
            member.GetAnnotations(AnnotationKind).FirstOrDefault()?.Data is { } id ? expansions[id] : new[] { member })));
    }
}
