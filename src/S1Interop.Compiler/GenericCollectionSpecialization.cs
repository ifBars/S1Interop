using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace S1Interop.Compiler;

/// <summary>Separates native collection calls without replacing generic type parameters or changing overload binding.</summary>
internal static class GenericCollectionSpecialization
{
    private const string AnnotationKind = "S1Interop.GenericCollectionSpecialization";

    internal static bool IsSpecialized(IMethodSymbol method) => method.OriginalDefinition.DeclaringSyntaxReferences
        .Any(reference => reference.GetSyntax().HasAnnotations(AnnotationKind));

    internal static CSharpCompilation Apply(CSharpCompilation author, MetadataSymbolMap map,
        CancellationToken cancellationToken, out int rewritten)
    {
        rewritten = 0;
        if (!author.SyntaxTrees.Any(tree => tree.GetRoot(cancellationToken).DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Any(method => method.TypeParameterList is not null && method.Modifiers.Any(SyntaxKind.StaticKeyword))))
            return author;
        var storage = new CollectionStorageAnalysis(author, map, cancellationToken);
        var replacements = new Dictionary<InvocationExpressionSyntax, string>();
        var additions = new Dictionary<TypeDeclarationSyntax, List<Specialization>>();
        var usedNames = new HashSet<string>(author.SyntaxTrees.SelectMany(tree => tree.GetRoot(cancellationToken)
            .DescendantTokens().Where(token => token.IsKind(SyntaxKind.IdentifierToken)).Select(token => token.ValueText)));
        int ordinal = 0;
        foreach (var tree in author.SyntaxTrees)
        {
            var model = author.GetSemanticModel(tree);
            foreach (var call in tree.GetRoot(cancellationToken).DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (model.GetSymbolInfo(call, cancellationToken).Symbol is not IMethodSymbol method ||
                    !method.IsGenericMethod || !method.IsStatic || method.IsExtensionMethod ||
                    method.DeclaredAccessibility != Accessibility.Private || method.ContainingType.IsGenericType ||
                    method.TypeArguments.Any(type => !CollectionStorageAnalysis.IsRepresentable(map, type)) ||
                    !HasCollectionParameter(method.OriginalDefinition) ||
                    !(storage.IsBridged(call, model) || call.ArgumentList.Arguments.Any(argument => storage.IsBridged(argument.Expression, model))) ||
                    method.OriginalDefinition.DeclaringSyntaxReferences.Length != 1 ||
                    method.OriginalDefinition.DeclaringSyntaxReferences[0].GetSyntax(cancellationToken) is not MethodDeclarationSyntax declaration ||
                    declaration.Parent is not TypeDeclarationSyntax owner || owner is InterfaceDeclarationSyntax || owner.ContainsDirectives ||
                    declaration.AttributeLists.Count != 0 || declaration.Modifiers.Any(SyntaxKind.PartialKeyword) || declaration.Modifiers.Any(SyntaxKind.AsyncKeyword) ||
                    declaration.DescendantNodes().OfType<YieldStatementSyntax>().Any() ||
                    declaration.Body is null && declaration.ExpressionBody is null) continue;

                var definitionModel = author.GetSemanticModel(declaration.SyntaxTree);
                // Caller-info arguments describe the original method, including constructors and indexers.
                if (declaration.DescendantNodes().OfType<ExpressionSyntax>().Any(candidate =>
                    (definitionModel.GetSymbolInfo(candidate).Symbol switch
                    {
                        IMethodSymbol called => called.Parameters,
                        IPropertySymbol property => property.Parameters,
                        _ => System.Collections.Immutable.ImmutableArray<IParameterSymbol>.Empty
                    }).Any(parameter => parameter.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() is
                        "System.Runtime.CompilerServices.CallerMemberNameAttribute" or
                        "System.Runtime.CompilerServices.CallerLineNumberAttribute" or
                        "System.Runtime.CompilerServices.CallerFilePathAttribute" or
                        "System.Runtime.CompilerServices.CallerArgumentExpressionAttribute")))) continue;
                var selfCalls = declaration.DescendantNodes().OfType<InvocationExpressionSyntax>()
                    .Where(candidate => definitionModel.GetSymbolInfo(candidate).Symbol is IMethodSymbol self &&
                        SymbolEqualityComparer.Default.Equals(self.OriginalDefinition, method.OriginalDefinition)).ToArray();
                // Polymorphic recursion can change element representations between invocations.
                if (selfCalls.Any(candidate => definitionModel.GetSymbolInfo(candidate).Symbol is not IMethodSymbol self ||
                    self.TypeArguments.Where((type, index) => !SymbolEqualityComparer.Default.Equals(type, method.OriginalDefinition.TypeParameters[index])).Any())) continue;

                string name;
                do { name = "__S1InteropCollection_" + ordinal++; } while (!usedNames.Add(name));
                if (!additions.TryGetValue(owner, out var methods)) additions.Add(owner, methods = new());
                methods.Add(new Specialization(declaration, name, selfCalls));
                replacements.Add(call, name);
            }
        }
        rewritten = replacements.Count;
        if (rewritten == 0) return author;
        foreach (var tree in author.SyntaxTrees)
        {
            var root = new Rewriter(replacements, additions).Visit(tree.GetRoot(cancellationToken))!;
            author = author.ReplaceSyntaxTree(tree, tree.WithRootAndOptions(root, tree.Options));
        }
        return author;
    }

    private static bool HasCollectionParameter(IMethodSymbol method) =>
        CollectionContainsParameter(method.ReturnType) || method.Parameters.Any(parameter => CollectionContainsParameter(parameter.Type));

    private static bool CollectionContainsParameter(ITypeSymbol type) => type is INamedTypeSymbol named &&
        named.OriginalDefinition.ToDisplayString() is "System.Collections.Generic.List<T>" or "System.Collections.Generic.Dictionary<TKey, TValue>" &&
        named.TypeArguments.Any(argument => argument is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method });

    private static InvocationExpressionSyntax Rename(InvocationExpressionSyntax call, string name)
    {
        SimpleNameSyntax RenameName(SimpleNameSyntax syntax) => syntax switch
        {
            GenericNameSyntax generic => generic.WithIdentifier(SyntaxFactory.Identifier(name).WithTriviaFrom(generic.Identifier)),
            _ => SyntaxFactory.IdentifierName(name).WithTriviaFrom(syntax)
        };
        return call.Expression switch
        {
            SimpleNameSyntax simple => call.WithExpression(RenameName(simple)),
            MemberAccessExpressionSyntax access => call.WithExpression(access.WithName(RenameName(access.Name))),
            _ => call
        };
    }

    private sealed record Specialization(MethodDeclarationSyntax Declaration, string Name, InvocationExpressionSyntax[] SelfCalls);

    private static SyntaxTriviaList SourceLine(Location location)
    {
        var origin = location.GetMappedLineSpan();
        string file = string.IsNullOrEmpty(origin.Path) ? string.Empty : " " + SymbolDisplay.FormatLiteral(origin.Path, true);
        return SyntaxFactory.ParseLeadingTrivia($"\n#line {origin.StartLinePosition.Line + 1}{file}\n");
    }

    private sealed class Rewriter(Dictionary<InvocationExpressionSyntax, string> replacements,
        Dictionary<TypeDeclarationSyntax, List<Specialization>> additions) : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            return replacements.TryGetValue(node, out string? name) ? Rename(visited, name) : visited;
        }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) => Add(node, (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!);
        public override SyntaxNode? VisitStructDeclaration(StructDeclarationSyntax node) => Add(node, (StructDeclarationSyntax)base.VisitStructDeclaration(node)!);
        public override SyntaxNode? VisitRecordDeclaration(RecordDeclarationSyntax node) => Add(node, (RecordDeclarationSyntax)base.VisitRecordDeclaration(node)!);

        private T Add<T>(T original, T visited) where T : TypeDeclarationSyntax
        {
            if (!additions.TryGetValue(original, out var methods)) return visited;
            var clones = methods.Select(specialization =>
            {
                var calls = new Dictionary<InvocationExpressionSyntax, string>(replacements);
                foreach (var self in specialization.SelfCalls) calls[self] = specialization.Name;
                // Visit original nodes before copying so selected rewrites inside the helper survive.
                var clone = (MethodDeclarationSyntax)new Rewriter(calls, new()).Visit(specialization.Declaration)!;
                return clone.WithIdentifier(SyntaxFactory.Identifier(specialization.Name).WithTriviaFrom(clone.Identifier))
                    .WithLeadingTrivia(SourceLine(specialization.Declaration.GetLocation()))
                    .WithAdditionalAnnotations(new SyntaxAnnotation(AnnotationKind));
            }).ToArray();
            return (T)visited.AddMembers(clones).WithCloseBraceToken(visited.CloseBraceToken.WithLeadingTrivia(
                visited.CloseBraceToken.LeadingTrivia.AddRange(SourceLine(original.CloseBraceToken.GetLocation()))));
        }
    }
}
