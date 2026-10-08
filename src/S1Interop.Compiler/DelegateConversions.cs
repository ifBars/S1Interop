using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace S1Interop.Compiler;

/// <summary>Finds native delegate slots from the bound member signature, without game-specific API names.</summary>
internal sealed class DelegateConversions(SemanticModel model, MetadataSymbolMap map,
    ImmutableArray<Diagnostic>.Builder diagnostics)
{
    private static readonly DiagnosticDescriptor UnsupportedSignature = new("S1IC030", "Delegate signature cannot be bridged",
        "Delegate '{0}' cannot be automatically bridged: {1}", "S1Interop.Compiler", DiagnosticSeverity.Error, true);

    public bool Used { get; private set; }

    public bool IsNativeExpression(ExpressionSyntax expression) =>
        InferExpression(expression, new HashSet<ISymbol>(SymbolEqualityComparer.Default)) is { } type && IsNativeDelegate(type);

    public INamedTypeSymbol? InferredTargetType(ExpressionSyntax expression) =>
        InferExpression(expression, new HashSet<ISymbol>(SymbolEqualityComparer.Default));

    public INamedTypeSymbol? ExpectedTargetType(ExpressionSyntax expression) => ExpectedSlot(expression);

    public ExpressionSyntax Rewrite(ExpressionSyntax original, ExpressionSyntax visited)
    {
        // Convert an explicit delegate creation as a whole; converting its method-group
        // argument first would construct a native wrapper around another native wrapper.
        if (original.Parent is ArgumentSyntax { Parent: ArgumentListSyntax { Parent: BaseObjectCreationExpressionSyntax creation } } &&
            model.GetOperation(creation) is IDelegateCreationOperation)
            return visited;
        if (original.Parent is MemberAccessExpressionSyntax member && member.Name == original ||
            original.Parent is MemberBindingExpressionSyntax or QualifiedNameSyntax or AliasQualifiedNameSyntax)
            return visited;
        if (map.NativeObjectBase is null || original is TypeSyntax && model.GetSymbolInfo(original).Symbol is ITypeSymbol ||
            original.IsKind(SyntaxKind.NullLiteralExpression) ||
            model.GetTypeInfo(original).ConvertedType is not INamedTypeSymbol { TypeKind: TypeKind.Delegate } sourceDelegate)
            return visited;

        INamedTypeSymbol? targetDelegate = NativeDelegate(sourceDelegate) ?? ExpectedSlot(original);
        if (targetDelegate is null || !IsNativeDelegate(targetDelegate)) return visited;

        bool explicitCreation = original is BaseObjectCreationExpressionSyntax && model.GetOperation(original) is IDelegateCreationOperation;
        bool createsDelegate = explicitCreation || original is AnonymousFunctionExpressionSyntax ||
            original is IdentifierNameSyntax or MemberAccessExpressionSyntax &&
            model.GetSymbolInfo(original).Symbol is IMethodSymbol && model.GetTypeInfo(original).Type is null;
        if (!createsDelegate && InferExpression(original, new HashSet<ISymbol>(SymbolEqualityComparer.Default)) is { } expressionType &&
            IsNativeDelegate(expressionType))
            return visited;
        if (!createsDelegate && model.GetTypeInfo(original).Type?.TypeKind != TypeKind.Delegate) return visited;

        IMethodSymbol signature = sourceDelegate.DelegateInvokeMethod!;
        if (signature.RefKind != RefKind.None || signature.Parameters.Any(parameter => parameter.RefKind != RefKind.None) ||
            signature.Parameters.Length > 16 || signature.Parameters.Any(parameter => parameter.Type.IsRefLikeType))
        {
            diagnostics.Add(Diagnostic.Create(UnsupportedSignature, original.GetLocation(), sourceDelegate.ToDisplayString(),
                "by-reference, ref-like, and more than 16 callback parameters are not supported"));
            return visited;
        }

        string targetName = NativeDelegate(sourceDelegate) is not null
            ? map.TargetDisplay(sourceDelegate)
            : RenderSlot(targetDelegate, sourceDelegate);
        string managedType = "global::System.Action";
        var nativeSignature = InvokeSignature(targetDelegate);
        string CallbackType(ITypeSymbol source, ITypeSymbol? target)
        {
            if (source is IArrayTypeSymbol && target is INamedTypeSymbol array && IsNativeArray(array))
            {
                if (CollectionStorageAnalysis.SupportsNativeArraySlot(map, array, source) && array.Name != "Il2CppArrayBase")
                    return target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                diagnostics.Add(Diagnostic.Create(UnsupportedSignature, original.GetLocation(), sourceDelegate.ToDisplayString(),
                    $"array slot '{source}' -> '{target}' does not have a supported concrete shared-storage representation"));
            }
            return map.TargetDisplay(source);
        }
        var arguments = signature.Parameters.Select(parameter => CallbackType(parameter.Type,
            nativeSignature is not null && parameter.Ordinal < nativeSignature.Parameters.Length
                ? nativeSignature.Parameters[parameter.Ordinal].Type : null)).ToList();
        if (!signature.ReturnsVoid)
        {
            managedType = "global::System.Func";
            arguments.Add(CallbackType(signature.ReturnType, nativeSignature?.ReturnType));
        }
        if (arguments.Count > 0) managedType += "<" + string.Join(", ", arguments) + ">";
        ExpressionSyntax callbackExpression = explicitCreation && visited is BaseObjectCreationExpressionSyntax { ArgumentList.Arguments: [var callbackArgument] }
            ? callbackArgument.Expression : visited;
        ExpressionSyntax callback = createsDelegate
            ? CastExpression(ParseTypeName(managedType), ParenthesizedExpression(callbackExpression.WithoutTrivia()))
            : visited.WithoutTrivia();
        Used = true;
        return InvocationExpression(ParseExpression(NativeDelegateCacheSource.TypeName + ".Convert<" + targetName + ">"),
                ArgumentList(SingletonSeparatedList(Argument(callback))))
            .WithTriviaFrom(original);
    }

    private INamedTypeSymbol? ExpectedSlot(ExpressionSyntax expression) => expression.Parent switch
    {
        AssignmentExpressionSyntax assignment when assignment.Right == expression =>
            InferExpression(assignment.Left, new HashSet<ISymbol>(SymbolEqualityComparer.Default)),
        ArgumentSyntax argument when model.GetOperation(argument) is IArgumentOperation { Parameter: { } parameter } =>
            TargetParameter(parameter),
        EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable } =>
            model.GetDeclaredSymbol(variable) switch
            {
                ILocalSymbol local => NativeDelegate(local.Type),
                IFieldSymbol field => NativeDelegate(field.Type),
                _ => null
            },
        _ => null
    };

    private INamedTypeSymbol? TargetParameter(IParameterSymbol source)
    {
        if (source.ContainingSymbol is not IMethodSymbol method) return null;
        var candidates = TargetMembers(method).OfType<IMethodSymbol>()
            .Where(candidate => candidate.Parameters.Length == method.Parameters.Length && candidate.Arity == method.Arity)
            .Where(candidate => candidate.OriginalDefinition.Parameters.Zip(method.OriginalDefinition.Parameters)
                .All(pair => Shape(pair.First.Type) == Shape(pair.Second.Type) && pair.First.RefKind == pair.Second.RefKind))
            .ToArray();
        return candidates.Length == 1 ? ConstructMethod(candidates[0], method).Parameters[source.Ordinal].Type as INamedTypeSymbol : null;
    }

    private INamedTypeSymbol? InferExpression(ExpressionSyntax expression, HashSet<ISymbol> seen)
    {
        if (expression is ParenthesizedExpressionSyntax parenthesized) return InferExpression(parenthesized.Expression, seen);
        if (expression is ConditionalAccessExpressionSyntax conditional) return InferExpression(conditional.WhenNotNull, seen);
        ISymbol? symbol = model.GetSymbolInfo(expression).Symbol;
        if (symbol is null || !seen.Add(symbol)) return NativeDelegate(model.GetTypeInfo(expression).Type);
        switch (symbol)
        {
            case ILocalSymbol local:
                // Native iterator reads are lowered at their source, including a var initializer.
                // The local therefore holds the managed view, not the inferred native return type.
                if (local.Type.ToDisplayString() == "System.Collections.IEnumerator") return null;
                if (NativeDelegate(local.Type) is { } localDelegate) return localDelegate;
                if (local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax() is VariableDeclaratorSyntax
                    { Parent: VariableDeclarationSyntax { Type.IsVar: true }, Initializer.Value: { } initializer })
                    return InferExpression(initializer, seen);
                return null;
            case IFieldSymbol or IPropertySymbol or IEventSymbol:
                var members = TargetMembers(symbol).Select(MemberType).OfType<INamedTypeSymbol>().ToArray();
                if (members.Length == 1) return members[0];
                if (symbol is IFieldSymbol { Type: INamedTypeSymbol { IsGenericType: false } fieldType } && map.IsNative(fieldType))
                    return map.Resolve(fieldType).Target;
                return NativeDelegate(MemberType(symbol));
            case IMethodSymbol method:
                var methods = TargetMembers(method).OfType<IMethodSymbol>()
                    .Where(candidate => candidate.Parameters.Length == method.Parameters.Length && candidate.Arity == method.Arity)
                    .Where(candidate => candidate.OriginalDefinition.Parameters.Zip(method.OriginalDefinition.Parameters)
                        .All(pair => Shape(pair.First.Type) == Shape(pair.Second.Type))).ToArray();
                return methods.Length == 1 ? ConstructMethod(methods[0], method).ReturnType as INamedTypeSymbol : NativeDelegate(method.ReturnType);
            default:
                return NativeDelegate(model.GetTypeInfo(expression).Type);
        }
    }

    private IMethodSymbol ConstructMethod(IMethodSymbol target, IMethodSymbol source)
    {
        if (source.Arity == 0) return target;
        ITypeSymbol MapArgument(ITypeSymbol argument)
        {
            if (argument is ITypeParameterSymbol { TypeParameterKind: TypeParameterKind.Method } parameter)
                return target.TypeParameters[parameter.Ordinal];
            if (argument is not INamedTypeSymbol named || map.Resolve(named) is not { Status: TypeMappingStatus.Mapped, Target: { } mapped })
                return argument;
            return named.Arity == 0 ? mapped : mapped.Construct(named.TypeArguments.Select(MapArgument).ToArray());
        }
        return target.Construct(source.TypeArguments.Select(MapArgument).ToArray());
    }

    private IEnumerable<ISymbol> TargetMembers(ISymbol source)
    {
        if (source.ContainingType is not { } owner || map.Resolve(owner) is not { Status: TypeMappingStatus.Mapped, Target: { } target })
            return [];
        if (source is IFieldSymbol && ComponentFieldLowering.FindAccessor(target, source.Name) is { } accessor)
            return [accessor];
        return target.GetMembers(source.Name);
    }

    private INamedTypeSymbol? NativeDelegate(ITypeSymbol? source) =>
        source is INamedTypeSymbol { TypeKind: TypeKind.Delegate } named &&
        map.Resolve(named) is { Status: TypeMappingStatus.Mapped, Target: { } target } && IsNativeDelegate(target)
            ? target : null;

    internal static IMethodSymbol? InvokeSignature(INamedTypeSymbol type) => type.DelegateInvokeMethod ??
        (IsNativeDelegate(type) && type.GetMembers("Invoke").OfType<IMethodSymbol>().ToArray() is [var invoke] ? invoke : null);

    internal static bool IsNativeDelegate(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
            if (current.ToDisplayString() == "Il2CppSystem.Delegate") return true;
        return false;
    }

    private string RenderSlot(INamedTypeSymbol target, INamedTypeSymbol source)
    {
        string name = "global::" + target.ContainingNamespace.ToDisplayString() + "." + target.Name;
        if (target.Arity != source.Arity)
        {
            // The signature matcher normally makes this impossible; preserve a compile-time diagnostic if metadata differs.
            return target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }
        return target.Arity == 0 ? name : name + "<" + string.Join(", ", source.TypeArguments.Select(map.TargetDisplay)) + ">";
    }

    private static ITypeSymbol? MemberType(ISymbol symbol) => symbol switch
    {
        IFieldSymbol field => field.Type,
        IPropertySymbol property => property.Type,
        IEventSymbol @event => @event.Type,
        _ => null
    };

    private static string Shape(ITypeSymbol type) => type switch
    {
        ITypeParameterSymbol parameter => "!" + parameter.TypeParameterKind + parameter.Ordinal,
        IArrayTypeSymbol array => Shape(array.ElementType) + "[" + array.Rank + "]",
        INamedTypeSymbol named when IsNativeArray(named) && named.Arity == 1 => Shape(named.TypeArguments[0]) + "[1]",
        INamedTypeSymbol named when IsNativeArray(named) && named.Name == "Il2CppStringArray" => "System.String<>[1]",
        INamedTypeSymbol named => Normalize((named.ContainingNamespace.IsGlobalNamespace ? "" : named.ContainingNamespace.ToDisplayString() + ".") + named.MetadataName) +
            "<" + string.Join(",", named.TypeArguments.Select(Shape)) + ">",
        _ => type.ToDisplayString()
    };

    internal static bool IsNativeArray(INamedTypeSymbol? type) => type is not null &&
        type.ContainingNamespace.ToDisplayString() == "Il2CppInterop.Runtime.InteropTypes.Arrays" &&
        type.Name is "Il2CppArrayBase" or "Il2CppStructArray" or "Il2CppReferenceArray" or "Il2CppStringArray";

    private static string Normalize(string name) => name switch
    {
        "S1Interop.Compiler.Generated.S1InteropList`1" => "System.Collections.Generic.List`1",
        "S1Interop.Compiler.Generated.S1InteropDictionary`2" => "System.Collections.Generic.Dictionary`2",
        _ => name.StartsWith("Il2Cpp", StringComparison.Ordinal) ? name[6..].TrimStart('.') : name
    };
}
