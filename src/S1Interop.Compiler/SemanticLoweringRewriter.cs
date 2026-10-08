using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace S1Interop.Compiler;

/// <summary>
/// Rewrites one author tree. Every decision is made against the original node through the author semantic model;
/// only names that bind to referenced types and native downcasts are replaced, so locals, members, strings, and
/// authored declarations stay as written.
/// </summary>
internal sealed class SemanticLoweringRewriter : CSharpSyntaxRewriter
{
    private static readonly SyntaxAnnotation RemoveAnnotation = new("S1Interop.Compiler.RemoveUsing");

    private readonly SemanticModel model;
    private readonly MetadataSymbolMap map;
    private readonly ImmutableArray<Diagnostic>.Builder diagnostics;
    private readonly HashSet<PatternSyntax> loweredPatterns = new();
    private readonly DelegateConversions delegateConversions;
    private readonly EnumerationAdapters enumerationAdapters;
    private readonly EqualityLowering equalityLowering;
    private readonly CollectionStorageAnalysis collectionStorage;
    private readonly NativeArrayLowering arrays;
    private readonly NativeEventLowering nativeEvents;
    private readonly ComponentFieldLowering componentFields;

    public SemanticLoweringRewriter(SemanticModel model, MetadataSymbolMap map, ImmutableArray<Diagnostic>.Builder diagnostics,
        EnumerationAdapters enumerationAdapters, CollectionStorageAnalysis collectionStorage)
    {
        this.model = model;
        this.map = map;
        this.diagnostics = diagnostics;
        delegateConversions = new DelegateConversions(model, map, diagnostics);
        this.enumerationAdapters = enumerationAdapters;
        this.collectionStorage = collectionStorage;
        arrays = new NativeArrayLowering(model, map, collectionStorage, diagnostics);
        nativeEvents = new NativeEventLowering(model, map, diagnostics);
        componentFields = new ComponentFieldLowering(model, map, diagnostics);
        equalityLowering = new EqualityLowering(model, map);
    }

    public int RewrittenNodes { get; private set; }
    public bool RequiresDelegateSupport => delegateConversions.Used;
    public bool RequiresEqualitySupport => equalityLowering.Used;

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        SyntaxNode? visited = base.Visit(node);
        if (node is ExpressionSyntax original && visited is ExpressionSyntax expression)
        {
            arrays.CheckConversion(original);
            ExpressionSyntax arrayRead = expression;
            if (!IsQualifiedRightSide(original) && model.GetSymbolInfo(original).Symbol is IMethodSymbol or IPropertySymbol or IFieldSymbol &&
                collectionStorage.IsNativeArray(original, model, out IArrayTypeSymbol? arrayType) &&
                NativeReferenceArraySource.IsMappedReferenceArray(map, arrayType) &&
                delegateConversions.InferredTargetType(original) is { Name: "Il2CppArrayBase", Arity: 1 } arraySlot &&
                DelegateConversions.IsNativeArray(arraySlot) &&
                !(original.Parent is AssignmentExpressionSyntax arrayAssignment && arrayAssignment.Left == original) &&
                !(original.Parent is ConditionalAccessExpressionSyntax arrayAccess && arrayAccess.WhenNotNull == original))
                arrayRead = InvocationExpression(ParseExpression(NativeReferenceArraySource.TypeName + ".FromNative<" +
                    map.TargetDisplay(arrayType!.ElementType) + ">"), ArgumentList(SingletonSeparatedList(Argument(expression.WithoutTrivia()))))
                    .WithTriviaFrom(original);
            // An implicit covariant conversion between native reference arrays re-wraps the same native array in place.
            ExpressionSyntax covariant = IsQualifiedRightSide(original) ? arrayRead : arrays.Covariance(original, arrayRead);
            if (covariant != expression) RewrittenNodes++;
            ExpressionSyntax converted = delegateConversions.Rewrite(original, covariant);
            if (map.NativeObjectBase is not null) converted = equalityLowering.Rewrite(original, converted);
            if (original is InvocationExpressionSyntax && collectionStorage.IsBridged(original, model) &&
                model.GetSymbolInfo(original).Symbol is IMethodSymbol { Name: "ToDictionary" } dictionaryFactory &&
                dictionaryFactory.ContainingType.ToDisplayString() == "System.Linq.Enumerable")
                converted = InvocationExpression(ParseExpression(collectionStorage.Display(dictionaryFactory.ReturnType) + ".CopyManaged"),
                    ArgumentList(SingletonSeparatedList(Argument(converted.WithoutTrivia())))).WithTriviaFrom(original);
            if (map.FindTargetType(CoroutineSupportSource.AdapterType) is not null && !IsQualifiedRightSide(original))
            {
                ISymbol? iteratorSymbol = model.GetSymbolInfo(original is ConditionalAccessExpressionSyntax conditionalIterator
                    ? conditionalIterator.WhenNotNull : original).Symbol;
                bool nativeRead = model.GetTypeInfo(original).Type?.ToDisplayString() == "System.Collections.IEnumerator" &&
                    delegateConversions.InferredTargetType(original)?.ToDisplayString() == CoroutineSupportSource.NativeEnumerator &&
                    iteratorSymbol is IMethodSymbol or IPropertySymbol or IFieldSymbol &&
                    !(iteratorSymbol is IMethodSymbol iteratorMethod &&
                        ComponentInjection.ManagedCoroutineName(iteratorMethod, map) is not null) &&
                    !(original.Parent is ConditionalAccessExpressionSyntax conditionalParent && conditionalParent.WhenNotNull == original) &&
                    !(original.Parent is AssignmentExpressionSyntax iteratorAssignment && iteratorAssignment.Left == original);
                if (nativeRead)
                    converted = InvocationExpression(ParseExpression(CoroutineSupportSource.HelperType + ".FromNative"),
                        ArgumentList(SingletonSeparatedList(Argument(converted.WithoutTrivia())))).WithTriviaFrom(original);
                if (delegateConversions.ExpectedTargetType(original)?.ToDisplayString() == CoroutineSupportSource.NativeEnumerator &&
                    model.GetTypeInfo(original).ConvertedType?.ToDisplayString() == "System.Collections.IEnumerator")
                    converted = InvocationExpression(ParseExpression(CoroutineSupportSource.HelperType + ".Wrap"),
                        ArgumentList(SingletonSeparatedList(Argument(converted.WithoutTrivia())))).WithTriviaFrom(original);
            }
            if (converted != expression) RewrittenNodes++;
            if (!IsQualifiedRightSide(original) && model.GetSymbolInfo(original).Symbol is not ITypeSymbol &&
                (map.IsBridgedList(model.GetTypeInfo(original).Type) || collectionStorage.IsBridged(original, model)) &&
                delegateConversions.InferredTargetType(original)?.OriginalDefinition.ToDisplayString() is
                    "Il2CppSystem.Collections.Generic.List<T>" or "Il2CppSystem.Collections.Generic.Dictionary<TKey, TValue>" &&
                !(original.Parent is AssignmentExpressionSyntax assignment && assignment.Left == original) &&
                !(original.Parent is ConditionalAccessExpressionSyntax access && access.WhenNotNull == original))
            {
                converted = ParenthesizedExpression(CastExpression(ParseTypeName(collectionStorage.IsBridged(original, model)
                    ? collectionStorage.Display(model.GetTypeInfo(original).Type!) : map.TargetDisplay(model.GetTypeInfo(original).Type!)),
                    ParenthesizedExpression(converted.WithoutTrivia()))).WithTriviaFrom(original);
                RewrittenNodes++;
            }
            if (!IsQualifiedRightSide(original) && model.GetSymbolInfo(original).Symbol is not ITypeSymbol &&
                model.GetTypeInfo(original) is { Type: { } input, ConvertedType: { TypeKind: TypeKind.Interface } narrowed } &&
                !SymbolEqualityComparer.Default.Equals(input, narrowed) && map.IsNative(narrowed) &&
                model.Compilation.ClassifyConversion(input, narrowed) is { IsImplicit: true, IsReference: true })
            {
                // IL2CPP interfaces are proxy classes, so CLR assignment cannot perform this native upcast.
                converted = CallHelper("Cast", ParseTypeName(map.TargetDisplay(narrowed)), converted, original);
            }
            return converted;
        }
        return visited;
    }

    public SyntaxNode Rewrite(SyntaxNode root)
    {
        // Both expansions replace members only after component injection finished with the original member order.
        SyntaxNode rewritten = componentFields.Expand(nativeEvents.Expand(Visit(root)!));
        List<SyntaxNode> removals = rewritten.GetAnnotatedNodes(RemoveAnnotation).ToList();
        return removals.Count == 0
            ? rewritten
            : rewritten.RemoveNodes(removals, SyntaxRemoveOptions.KeepExteriorTrivia)!;
    }

    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (!node.StaticKeyword.IsKind(SyntaxKind.None) ||
            model.GetSymbolInfo(node.NamespaceOrType).Symbol is not INamespaceSymbol { IsGlobalNamespace: false } ns)
        {
            return base.VisitUsingDirective(node);
        }

        (NamespaceMappingKind kind, string name) = map.MapNamespace(ns.ToDisplayString());
        switch (kind)
        {
            case NamespaceMappingKind.Rename:
                RewrittenNodes++;
                return node.WithNamespaceOrType(ParseName("global::" + name).WithTriviaFrom(node.NamespaceOrType));
            case NamespaceMappingKind.Remove when node.Alias is null:
                // Every type reference is fully qualified below, so a namespace absent from the target is dead.
                RewrittenNodes++;
                return node.WithAdditionalAnnotations(RemoveAnnotation);
            default:
                return node;
        }
    }

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node)
    {
        if (componentFields.Rewrite(node) is { } accessor)
        {
            RewrittenNodes++;
            return accessor;
        }

        if (model.GetSymbolInfo(node).Symbol is IMethodSymbol method && ComponentInjection.ManagedCoroutineName(method, map) is { } name)
        {
            RewrittenNodes++;
            return IdentifierName(name).WithTriviaFrom(node);
        }
        return RewriteTypeName(node, null) ?? base.VisitIdentifierName(node);
    }

    public override SyntaxNode? VisitGenericName(GenericNameSyntax node) =>
        RewriteTypeName(node, node.TypeArgumentList) ?? base.VisitGenericName(node);

    public override SyntaxNode? VisitAnonymousObjectMemberDeclarator(AnonymousObjectMemberDeclaratorSyntax node)
    {
        var visited = (AnonymousObjectMemberDeclaratorSyntax)base.VisitAnonymousObjectMemberDeclarator(node)!;
        if (node.NameEquals is null && model.GetSymbolInfo(node.Expression).Symbol is IFieldSymbol field &&
            componentFields.AccessorName(field) is not null)
            return visited.WithNameEquals(NameEquals(IdentifierName("@" + field.Name)));
        return visited;
    }

    public override SyntaxNode? VisitArgument(ArgumentSyntax node)
    {
        var visited = (ArgumentSyntax)base.VisitArgument(node)!;
        if (node.Parent is TupleExpressionSyntax tuple && node.NameColon is null &&
            model.GetSymbolInfo(node.Expression).Symbol is IFieldSymbol field && componentFields.AccessorName(field) is not null)
        {
            if (tuple.Ancestors().OfType<AssignmentExpressionSyntax>().Any(assignment =>
                    assignment.Left.Span.Contains(tuple.Span) && model.GetOperation(assignment) is IDeconstructionAssignmentOperation))
                return visited;
            int index = tuple.Arguments.IndexOf(node);
            if (model.GetTypeInfo(tuple).Type is INamedTypeSymbol { IsTupleType: true } tupleType &&
                tupleType.TupleElements[index].Name == field.Name && field.Name != "Item" + (index + 1))
                return visited.WithNameColon(NameColon(IdentifierName("@" + field.Name)));
            // Duplicate or reserved inferred names were absent in the original tuple contract.
            return visited.WithExpression(ParenthesizedExpression(visited.Expression.WithoutTrivia()).WithTriviaFrom(visited.Expression));
        }
        return visited;
    }

    public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node) =>
        RewriteTypeName(node, (node.Right as GenericNameSyntax)?.TypeArgumentList) ?? base.VisitQualifiedName(node);

    public override SyntaxNode? VisitAliasQualifiedName(AliasQualifiedNameSyntax node) =>
        RewriteTypeName(node, (node.Name as GenericNameSyntax)?.TypeArgumentList) ?? base.VisitAliasQualifiedName(node);

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (RewriteTypeName(node, (node.Name as GenericNameSyntax)?.TypeArgumentList) is { } typeName) return typeName;
        var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
        if (arrays.Member(node, visited) is not { } lowered) return visited;
        RewrittenNodes++;
        return lowered;
    }

    public override SyntaxNode? VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var visited = (ElementAccessExpressionSyntax)base.VisitElementAccessExpression(node)!;
        if (arrays.Element(node, visited) is not { } lowered) return visited;
        RewrittenNodes++;
        return lowered;
    }

    public override SyntaxNode? VisitElementBindingExpression(ElementBindingExpressionSyntax node)
    {
        var visited = (ElementBindingExpressionSyntax)base.VisitElementBindingExpression(node)!;
        if (arrays.ElementBinding(node, visited) is not { } lowered) return visited;
        RewrittenNodes++;
        return lowered;
    }

    public override SyntaxNode? VisitConditionalAccessExpression(ConditionalAccessExpressionSyntax node)
    {
        var visited = (ConditionalAccessExpressionSyntax)base.VisitConditionalAccessExpression(node)!;
        if (arrays.ConditionalElementAccess(node, visited) is not { } lowered) return visited;
        RewrittenNodes++;
        return lowered;
    }

    public override SyntaxNode? VisitArrayType(ArrayTypeSyntax node)
    {
        if (arrays.Type(node) is not { } lowered) return base.VisitArrayType(node);
        RewrittenNodes++;
        return lowered;
    }

    public override SyntaxNode? VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
    {
        var visited = (ArrayCreationExpressionSyntax)base.VisitArrayCreationExpression(node)!;
        if (arrays.Creation(node, visited) is not { } lowered) return visited;
        RewrittenNodes++;
        return lowered;
    }

    public override SyntaxNode? VisitCollectionExpression(CollectionExpressionSyntax node)
    {
        var visited = (CollectionExpressionSyntax)base.VisitCollectionExpression(node)!;
        if (arrays.Creation(node, visited) is not { } lowered) return visited;
        RewrittenNodes++;
        return lowered;
    }

    public override SyntaxNode? VisitImplicitArrayCreationExpression(ImplicitArrayCreationExpressionSyntax node)
    {
        var visited = (ImplicitArrayCreationExpressionSyntax)base.VisitImplicitArrayCreationExpression(node)!;
        if (arrays.Creation(node, visited) is not { } lowered) return visited;
        RewrittenNodes++;
        return lowered;
    }

    public override SyntaxNode? VisitInitializerExpression(InitializerExpressionSyntax node)
    {
        var visited = (InitializerExpressionSyntax)base.VisitInitializerExpression(node)!;
        if (arrays.DeclaratorInitializer(node, visited) is not { } lowered) return visited;
        RewrittenNodes++;
        return lowered;
    }

    public override SyntaxNode? VisitInterpolation(InterpolationSyntax node)
    {
        var visited = (InterpolationSyntax)base.VisitInterpolation(node)!;
        if (visited.Expression == node.Expression) return visited;

        // In reparsed interpolated strings, the ':' in a generated global:: qualifier would otherwise
        // start a format clause. Parentheses keep the complete lowered expression inside the hole.
        RewrittenNodes++;
        return visited.WithExpression(ParenthesizedExpression(visited.Expression.WithoutTrivia())
            .WithTriviaFrom(visited.Expression));
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        if (node.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } &&
            model.GetConstantValue(node) is { HasValue: true, Value: string name })
        {
            RewrittenNodes++;
            return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(name)).WithTriviaFrom(node);
        }

        var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
        if (NativeTraverseLowering.Rewrite(node, visited, model, map) is { } traverseRead)
        {
            RewrittenNodes++;
            return traverseRead;
        }
        if (NativeReflectionVerifier.Rewrite(node, visited, model, map) is { } reflectionCall)
        {
            RewrittenNodes++;
            return reflectionCall;
        }
        if (arrays.Invocation(node, visited) is { } arrayCall)
        {
            RewrittenNodes++;
            return arrayCall;
        }
        if (model.GetSymbolInfo(node).Symbol is IMethodSymbol { Name: "ConvertAll" } convert &&
            convert.ContainingType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.List<T>" &&
            (map.IsBridgedList(convert.ReturnType) || collectionStorage.IsBridged(node, model)))
        {
            RewrittenNodes++;
            return ObjectCreationExpression(ParseTypeName(collectionStorage.IsBridged(node, model) ? collectionStorage.Display(convert.ReturnType) : map.TargetDisplay(convert.ReturnType)))
                .WithNewKeyword(Token(SyntaxKind.NewKeyword).WithTrailingTrivia(Space))
                .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(visited.WithoutTrivia())))).WithTriviaFrom(node);
        }
        if (map.NativeObjectBase is not null && model.GetSymbolInfo(node).Symbol is IMethodSymbol
                { Name: "ReferenceEquals", IsStatic: true, ContainingType.SpecialType: SpecialType.System_Object })
        {
            RewrittenNodes++;
            return visited.WithExpression(ParseExpression(NativeCastHelperSource.TypeName + ".Same").WithTriviaFrom(visited.Expression))
                .WithArgumentList(visited.ArgumentList.WithArguments(SeparatedList(
                    visited.ArgumentList.Arguments.Select(argument => argument.WithNameColon(null)))));
        }
        if (model.GetSymbolInfo(node).Symbol is IMethodSymbol linq &&
            linq.ContainingType.ToDisplayString() == "System.Linq.Enumerable")
        {
            if (linq.Name == "ToList" && (map.IsBridgedList(linq.ReturnType) || collectionStorage.IsBridged(node, model)))
            {
                ExpressionSyntax receiver = linq.ReducedFrom is not null
                    ? ((MemberAccessExpressionSyntax)visited.Expression).Expression
                    : visited.ArgumentList.Arguments[0].Expression;
                RewrittenNodes++;
                return ObjectCreationExpression(ParseTypeName(collectionStorage.IsBridged(node, model) ? collectionStorage.Display(linq.ReturnType) : map.TargetDisplay(linq.ReturnType)))
                    .WithNewKeyword(Token(SyntaxKind.NewKeyword).WithTrailingTrivia(Space))
                    .WithArgumentList(ArgumentList(SingletonSeparatedList(Argument(receiver.WithoutTrivia())))).WithTriviaFrom(node);
            }
            IMethodSymbol definition = linq.ReducedFrom ?? linq;
            if (linq.Name is "Cast" or "OfType" && linq.TypeArguments is [{ } castType] && map.IsNative(castType))
            {
                ExpressionSyntax? receiver = linq.ReducedFrom is not null
                    ? (node.Expression as MemberAccessExpressionSyntax)?.Expression
                    : node.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                ExpressionSyntax? lowered = linq.ReducedFrom is not null
                    ? (visited.Expression as MemberAccessExpressionSyntax)?.Expression
                    : visited.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                if (receiver is not null && lowered is not null)
                {
                    ExpressionSyntax sequence = map.IsBridgedList(model.GetTypeInfo(receiver).Type) ? lowered : enumerationAdapters.Adapt(lowered,
                        delegateConversions.InferredTargetType(receiver), model.Compilation.GetSpecialType(SpecialType.System_Object)) ?? lowered;
                    return CallHelper(linq.Name + "Sequence", ParseTypeName(map.TargetDisplay(castType)), sequence, node);
                }
            }
            if (definition.Parameters.FirstOrDefault()?.Type is INamedTypeSymbol first &&
                first.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>" &&
                linq.TypeArguments.Length > 0)
            {
                ExpressionSyntax? originalReceiver = linq.ReducedFrom is not null
                    ? (node.Expression as MemberAccessExpressionSyntax)?.Expression
                    : node.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                ExpressionSyntax? loweredReceiver = linq.ReducedFrom is not null
                    ? (visited.Expression as MemberAccessExpressionSyntax)?.Expression
                    : visited.ArgumentList.Arguments.FirstOrDefault()?.Expression;
                if (originalReceiver is not null && loweredReceiver is not null &&
                    !map.IsBridgedList(model.GetTypeInfo(originalReceiver).Type) &&
                    !collectionStorage.IsBridged(originalReceiver, model) &&
                    enumerationAdapters.Adapt(loweredReceiver, delegateConversions.InferredTargetType(originalReceiver), linq.TypeArguments[0]) is { } adapted)
                {
                    RewrittenNodes++;
                    visited = linq.ReducedFrom is not null
                        ? visited.WithExpression(((MemberAccessExpressionSyntax)visited.Expression).WithExpression(adapted))
                        : visited.WithArgumentList(visited.ArgumentList.WithArguments(visited.ArgumentList.Arguments.Replace(
                            visited.ArgumentList.Arguments[0], visited.ArgumentList.Arguments[0].WithExpression(adapted))));
                }
            }
        }
        if (model.GetSymbolInfo(node).Symbol is IMethodSymbol { MethodKind: MethodKind.DelegateInvoke } &&
            delegateConversions.IsNativeExpression(node.Expression))
        {
            RewrittenNodes++;
            return visited.WithExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                ParenthesizedExpression(visited.Expression.WithoutTrivia()), IdentifierName("Invoke"))
                .WithTriviaFrom(visited.Expression));
        }
        return visited;
    }

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
        var injected = ComponentInjection.Rewrite(node, visited, model, map, componentFields, diagnostics, out int changes);
        RewrittenNodes += changes;
        return injected;
    }

    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node)
    {
        var visited = (FieldDeclarationSyntax)base.VisitFieldDeclaration(node)!;
        var result = componentFields.Mark(node, visited);
        if (!ReferenceEquals(result, visited)) RewrittenNodes++;
        return result;
    }

    public override SyntaxNode? VisitEventFieldDeclaration(EventFieldDeclarationSyntax node)
    {
        var visited = (EventFieldDeclarationSyntax)base.VisitEventFieldDeclaration(node)!;
        var result = nativeEvents.Mark(node, visited);
        if (!ReferenceEquals(result, visited)) RewrittenNodes++;
        return result;
    }

    public override SyntaxNode? VisitEventDeclaration(EventDeclarationSyntax node)
    {
        var visited = (EventDeclarationSyntax)base.VisitEventDeclaration(node)!;
        var result = nativeEvents.Mark(node, visited);
        if (!ReferenceEquals(result, visited)) RewrittenNodes++;
        return result;
    }

    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        var visited = (AssignmentExpressionSyntax)base.VisitAssignmentExpression(node)!;
        var result = nativeEvents.Subscription(node, visited);
        if (!ReferenceEquals(result, visited)) RewrittenNodes++;
        return result;
    }

    public override SyntaxNode? VisitCastExpression(CastExpressionSyntax node)
    {
        var visited = (CastExpressionSyntax)base.VisitCastExpression(node)!;
        return collectionStorage.IsBridged(node, model) || IsNativeDowncast(model.GetTypeInfo(node.Expression).Type, model.GetTypeInfo(node.Type).Type)
            ? CallHelper("Cast", visited.Type, visited.Expression, node)
            : visited;
    }

    public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        var visited = (BinaryExpressionSyntax)base.VisitBinaryExpression(node)!;
        if (map.NativeObjectBase is not null && model.GetOperation(node) is IBinaryOperation
            { OperatorMethod: null, OperatorKind: BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals } operation &&
            operation.LeftOperand.Type is { IsReferenceType: true, TypeKind: not TypeKind.Delegate, SpecialType: not SpecialType.System_String } &&
            operation.RightOperand.Type is { IsReferenceType: true, TypeKind: not TypeKind.Delegate, SpecialType: not SpecialType.System_String })
        {
            ExpressionSyntax equality = InvocationExpression(ParseExpression(NativeCastHelperSource.TypeName + ".Same"),
                ArgumentList(SeparatedList(new[] { Argument(visited.Left.WithoutTrivia()), Argument(visited.Right.WithoutTrivia()) })));
            if (operation.OperatorKind == BinaryOperatorKind.NotEquals)
                equality = PrefixUnaryExpression(SyntaxKind.LogicalNotExpression, equality);
            RewrittenNodes++;
            return equality.WithTriviaFrom(node);
        }
        string? helper = node.Kind() switch
        {
            SyntaxKind.AsExpression => "TryCast",
            SyntaxKind.IsExpression => "Is",
            _ => null,
        };
        return helper is not null &&
               (collectionStorage.IsBridged(node, model) || arrays.IsTypeTest(node) ||
                IsNativeDowncast(model.GetTypeInfo(node.Left).Type, model.GetTypeInfo(node.Right).Type))
            ? CallHelper(helper, visited.Right as TypeSyntax ?? ParseTypeName(visited.Right.ToString()), visited.Left, node)
            : visited;
    }

    public override SyntaxNode? VisitIsPatternExpression(IsPatternExpressionSyntax node)
    {
        if (TopLevelTypeTest(node.Pattern) is not { } test ||
            !(test.TypeExpression is TypeSyntax collectionType && collectionStorage.IsBridgedType(collectionType, model)) &&
            !IsNativeDowncast(model.GetTypeInfo(node.Expression).Type, BoundType(test.TypeExpression)))
        {
            return base.VisitIsPatternExpression(node);
        }

        // `TryCast<T>(input) is <pattern>` evaluates the input once, yields null for a failed native test, and
        // keeps the authored pattern so designations and definite assignment are unchanged.
        loweredPatterns.Add(test.Pattern);
        var visited = (IsPatternExpressionSyntax)base.VisitIsPatternExpression(node)!;
        ExpressionSyntax typeExpression = TopLevelTypeTest(visited.Pattern)!.Value.TypeExpression;
        TypeSyntax type = typeExpression as TypeSyntax ?? ParseTypeName(typeExpression.ToString());
        ExpressionSyntax input = CallHelper("TryCast", type, visited.Expression, node.Expression);
        return visited.WithExpression(input);
    }

    public override SyntaxNode? VisitDeclarationPattern(DeclarationPatternSyntax node)
    {
        CheckNestedPattern(node, node.Type);
        return base.VisitDeclarationPattern(node);
    }

    public override SyntaxNode? VisitCasePatternSwitchLabel(CasePatternSwitchLabelSyntax node)
    {
        if (node.Parent?.Parent is not SwitchStatementSyntax statement ||
            !NeedsSwitchRetype(statement.Expression, node.Pattern, out var test))
            return base.VisitCasePatternSwitchLabel(node);

        loweredPatterns.Add(test.Pattern);
        var visited = (CasePatternSwitchLabelSyntax)base.VisitCasePatternSwitchLabel(node)!;
        (PatternSyntax pattern, WhenClauseSyntax guard) = LowerSwitchPattern(node, visited.Pattern, visited.WhenClause);
        return visited.WithPattern(pattern).WithWhenClause(guard);
    }

    public override SyntaxNode? VisitSwitchExpressionArm(SwitchExpressionArmSyntax node)
    {
        if (node.Parent is not SwitchExpressionSyntax expression ||
            !NeedsSwitchRetype(expression.GoverningExpression, node.Pattern, out var test))
            return base.VisitSwitchExpressionArm(node);

        loweredPatterns.Add(test.Pattern);
        var visited = (SwitchExpressionArmSyntax)base.VisitSwitchExpressionArm(node)!;
        (PatternSyntax pattern, WhenClauseSyntax guard) = LowerSwitchPattern(node, visited.Pattern, visited.WhenClause);
        return visited.WithPattern(pattern).WithWhenClause(guard);
    }

    private bool NeedsSwitchRetype(ExpressionSyntax input, PatternSyntax pattern,
        out (PatternSyntax Pattern, ExpressionSyntax TypeExpression) test)
    {
        if (TopLevelTypeTest(pattern) is { } candidate &&
            (IsNativeDowncast(model.GetTypeInfo(input).Type, BoundType(candidate.TypeExpression)) ||
             candidate.TypeExpression is ArrayTypeSyntax array && collectionStorage.IsBridgedType(array, model)))
        {
            test = candidate;
            return true;
        }
        test = default;
        return false;
    }

    private (PatternSyntax, WhenClauseSyntax) LowerSwitchPattern(SyntaxNode original, PatternSyntax pattern, WhenClauseSyntax? guard)
    {
        string temporary = UniqueTemporary(original, "__s1interopSwitch");
        ExpressionSyntax typeExpression = TopLevelTypeTest(pattern)!.Value.TypeExpression;
        TypeSyntax type = typeExpression as TypeSyntax ?? ParseTypeName(typeExpression.ToString());
        ExpressionSyntax condition = IsPatternExpression(
            CallHelper("TryCast", type, IdentifierName(temporary), original).WithoutTrivia(),
            pattern.WithoutTrivia()).NormalizeWhitespace();
        if (guard is not null)
            condition = BinaryExpression(SyntaxKind.LogicalAndExpression,
                ParenthesizedExpression(condition), ParenthesizedExpression(guard.Condition.WithoutTrivia())).NormalizeWhitespace();
        RewrittenNodes++;
        return (VarPattern(SingleVariableDesignation(Identifier(temporary))).NormalizeWhitespace().WithTrailingTrivia(Space),
            WhenClause(condition).NormalizeWhitespace().WithTrailingTrivia(Space));
    }

    public override SyntaxNode? VisitTypePattern(TypePatternSyntax node)
    {
        CheckNestedPattern(node, node.Type);
        return base.VisitTypePattern(node);
    }

    public override SyntaxNode? VisitRecursivePattern(RecursivePatternSyntax node)
    {
        CheckNestedPattern(node, node.Type);
        return base.VisitRecursivePattern(node);
    }

    public override SyntaxNode? VisitConstantPattern(ConstantPatternSyntax node)
    {
        // The parser leaves `case Employee:` and `not Employee` as constant patterns; the binder decides they are types.
        if (model.GetSymbolInfo(node.Expression).Symbol is ITypeSymbol)
        {
            CheckNestedPattern(node, node.Expression);
        }

        return base.VisitConstantPattern(node);
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        ForEachStatementInfo info = model.GetForEachStatementInfo(node);
        var visited = (ForEachStatementSyntax)base.VisitForEachStatement(node)!;
        if (node.Type.IsVar || !IsNativeDowncast(info.ElementType, model.GetTypeInfo(node.Type).Type))
            return visited;

        string temporary = UniqueTemporary(node, "__s1interopItem");
        StatementSyntax cast = ParseStatement($"{visited.Type.WithoutTrivia()} {node.Identifier.Text} = {NativeCastHelperSource.TypeName}.Cast<{visited.Type.WithoutTrivia()}>({temporary});");
        BlockSyntax body = visited.Statement is BlockSyntax block
            ? block.WithStatements(block.Statements.Insert(0, cast))
            : Block(cast, visited.Statement);
        RewrittenNodes++;
        return visited.WithType(IdentifierName("var").WithTriviaFrom(visited.Type))
            .WithIdentifier(Identifier(temporary).WithTriviaFrom(visited.Identifier))
            .WithStatement(body);
    }

    private static string UniqueTemporary(SyntaxNode node, string prefix)
    {
        string temporary = prefix + node.SpanStart;
        var identifiers = node.SyntaxTree.GetRoot().DescendantTokens()
            .Where(token => token.IsKind(SyntaxKind.IdentifierToken)).Select(token => token.ValueText).ToHashSet();
        while (identifiers.Contains(temporary)) temporary += "_";
        return temporary;
    }

    private SyntaxNode? RewriteTypeName(ExpressionSyntax node, TypeArgumentListSyntax? typeArguments)
    {
        ISymbol? symbol = model.GetSymbolInfo(node).Symbol;
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor && node.Parent is AttributeSyntax)
        {
            symbol = constructor.ContainingType;
        }

        if (symbol is not INamedTypeSymbol type || IsQualifiedRightSide(node))
        {
            return null;
        }

        if (node is TypeSyntax scalarList && collectionStorage.IsBridgedType(scalarList, model) &&
            !(node is IdentifierNameSyntax { Identifier.ValueText: "var" }))
        {
            RewrittenNodes++;
            return ParseName(collectionStorage.Display(type)).WithTriviaFrom(node);
        }
        if (map.IsBridgedList(type) && !(node is IdentifierNameSyntax { Identifier.ValueText: "var" }))
        {
            RewrittenNodes++;
            return ParseName(map.TargetDisplay(type)).WithTriviaFrom(node);
        }

        if (node is SimpleNameSyntax simple)
        {
            // Excludes `var`, aliases, and keywords that merely bind to a type.
            string text = simple.Identifier.ValueText;
            if ((node is IdentifierNameSyntax identifier && model.GetAliasInfo(identifier) is not null) ||
                (text != type.Name && text + "Attribute" != type.Name))
            {
                return null;
            }
        }

        TypeMapping mapping = map.Resolve(type);
        if (mapping.Status is TypeMappingStatus.Missing)
        {
            Report(LoweringDiagnostics.MissingTargetType, node, type.ToDisplayString(), type.ContainingAssembly?.Name ?? "?", mapping.Detail);
            return null;
        }

        if (mapping.Status is TypeMappingStatus.Ambiguous)
        {
            Report(LoweringDiagnostics.AmbiguousTargetType, node, type.ToDisplayString(), mapping.Detail);
            return null;
        }

        INamedTypeSymbol definition = type.OriginalDefinition;
        if (map.TargetDisplay(definition) == definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
        {
            return null;
        }

        INamedTypeSymbol target = mapping.Target ?? definition;
        SimpleNameSyntax name = typeArguments is null
            ? IdentifierName(target.Name)
            : GenericName(Identifier(target.Name), (TypeArgumentListSyntax)VisitTypeArgumentList(typeArguments)!);
        NameSyntax result = map.TargetQualifier(type, target) is { } qualifier
            ? QualifiedName(ParseName(qualifier), name)
            : AliasQualifiedName(IdentifierName(Token(SyntaxKind.GlobalKeyword)), name);
        RewrittenNodes++;
        return result.WithTriviaFrom(node);
    }

    private bool IsNativeDowncast(ITypeSymbol? input, ITypeSymbol? narrowed)
    {
        if (input is null || narrowed is null || input.IsValueType || !(map.IsNative(narrowed) || map.IsBridgedList(narrowed)))
        {
            return false;
        }

        Conversion conversion = model.Compilation.ClassifyConversion(input, narrowed);
        return conversion.IsExplicit && conversion.IsReference;
    }

    private void CheckNestedPattern(PatternSyntax pattern, ExpressionSyntax? typeExpression)
    {
        if (typeExpression is null || loweredPatterns.Contains(pattern))
        {
            return;
        }

        ITypeSymbol? narrowed = BoundType(typeExpression);
        if (IsNativeDowncast(model.GetTypeInfo(pattern).Type, narrowed))
        {
            Report(LoweringDiagnostics.UnsupportedNativePattern, pattern, narrowed!.ToDisplayString());
        }
    }

    // Only a name that binds to a type is a type test; constant patterns like `is SomeConst` bind to fields.
    private ITypeSymbol? BoundType(ExpressionSyntax typeExpression) =>
        model.GetSymbolInfo(typeExpression).Symbol as ITypeSymbol;

    // The enclosing qualified name or member access owns the whole type reference.
    private static bool IsQualifiedRightSide(ExpressionSyntax node) => node.Parent switch
    {
        QualifiedNameSyntax qualified => qualified.Right == node,
        AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name == node,
        MemberAccessExpressionSyntax memberAccess => memberAccess.Name == node,
        _ => false,
    };

    private ExpressionSyntax CallHelper(string method, TypeSyntax type, ExpressionSyntax argument, SyntaxNode original)
    {
        if (type is NullableTypeSyntax nullable)
        {
            type = nullable.ElementType;
        }

        RewrittenNodes++;
        if (type.ToString().StartsWith(NativeListSource.TypeName + "<", StringComparison.Ordinal) ||
            type.ToString().StartsWith(NativeDictionarySource.TypeName + "<", StringComparison.Ordinal))
        {
            return InvocationExpression(MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                    ParseName(type.ToString()), IdentifierName(method)),
                ArgumentList(SingletonSeparatedList(Argument(argument.WithoutTrivia())))).WithTriviaFrom(original);
        }
        return InvocationExpression(
                MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    ParseName(NativeCastHelperSource.TypeName),
                    GenericName(Identifier(method), TypeArgumentList(SingletonSeparatedList(type.WithoutTrivia())))),
                ArgumentList(SingletonSeparatedList(Argument(argument.WithoutTrivia()))))
            .WithTriviaFrom(original);
    }

    private static (PatternSyntax Pattern, ExpressionSyntax TypeExpression)? TopLevelTypeTest(PatternSyntax pattern) =>
        pattern switch
        {
            DeclarationPatternSyntax declaration => (declaration, declaration.Type),
            TypePatternSyntax typePattern => (typePattern, typePattern.Type),
            RecursivePatternSyntax { Type: { } type } recursive => (recursive, type),
            ConstantPatternSyntax constant => (constant, constant.Expression),
            UnaryPatternSyntax { RawKind: (int)SyntaxKind.NotPattern, Pattern: TypePatternSyntax or ConstantPatternSyntax } not =>
                TopLevelTypeTest(not.Pattern),
            ParenthesizedPatternSyntax parenthesized => TopLevelTypeTest(parenthesized.Pattern),
            _ => null,
        };

    private void Report(DiagnosticDescriptor descriptor, SyntaxNode node, params object[] arguments) =>
        diagnostics.Add(Diagnostic.Create(descriptor, node.GetLocation(), arguments));
}
