using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace S1Interop.Compiler;

internal static partial class NativeReflectionVerifier
{
    private static (IInvocationOperation Invocation, bool Direct, INamedTypeSymbol Source)? ResolveDynamicLookup(
        InvocationExpressionSyntax syntax, SemanticModel model, MetadataSymbolMap map)
    {
        if (map.NativeObjectBase is null || model.GetOperation(syntax) is not IInvocationOperation invocation) return null;
        bool direct = invocation.TargetMethod.ContainingType.ToDisplayString() == "System.Type" && invocation.TargetMethod.Name == "GetField";
        if (!direct && !(invocation.TargetMethod.ContainingType.ToDisplayString() == "HarmonyLib.AccessTools" && invocation.TargetMethod.Name == "Field")) return null;
        var name = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == (direct ? 0 : 1));
        IOperation? type = direct ? invocation.Instance : invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0)?.Value;
        while (type is IConversionOperation conversion) type = conversion.Operand;
        if (name?.Value.ConstantValue.HasValue != false || name.Syntax is not ArgumentSyntax ||
            type is not ITypeOfOperation { TypeOperand: INamedTypeSymbol source } || map.IsAuthorType(source) ||
            map.Resolve(source).Status != TypeMappingStatus.Mapped || !HasOnlyValueAccess(syntax, model)) return null;

        return (invocation, direct, source);
    }

    private static ExpressionSyntax? RewriteDynamic(InvocationExpressionSyntax syntax, InvocationExpressionSyntax visited,
        SemanticModel model, MetadataSymbolMap map)
    {
        if (ResolveDynamicLookup(syntax, model, map) is not { } resolved) return null;
        var (invocation, direct, source) = resolved;
        var entries = new List<string>();
        for (INamedTypeSymbol? owner = source; owner is not null && map.Resolve(owner).Target is { } target; owner = owner.BaseType)
        {
            foreach (string fieldName in map.ReferenceFields.FieldNames(owner).Order(StringComparer.Ordinal))
            {
                if (!map.ReferenceFields.TryGetAttributes(owner, fieldName, out var attributes)) continue;
                string ownerType = map.TargetDisplay(owner);
                string quotedName = SymbolDisplay.FormatLiteral(fieldName, true);
                string factory = "null";
                var fieldType = map.ReferenceFields.FieldType(owner, fieldName);
                var properties = target.GetMembers(fieldName).OfType<IPropertySymbol>().Where(member => !member.IsIndexer).ToArray();
                var property = properties.Length == 1 ? properties[0] : null;
                if (fieldType is not null && property is not null &&
                    (CanAdaptValue(new(owner, fieldName, fieldType, attributes), property, map) ||
                     fieldType is IArrayTypeSymbol array && property.GetMethod is not null && property.SetMethod is not null &&
                     (attributes & (System.Reflection.FieldAttributes.Literal | System.Reflection.FieldAttributes.InitOnly)) == 0 &&
                     attributes.HasFlag(System.Reflection.FieldAttributes.Static) == property.IsStatic &&
                     MatchesDynamicArray(array, property.Type, map)))
                {
                    string adapters = "";
                    if (CollectionAdapter(fieldType, property.Type, map) is { } adapter)
                    {
                        string native = property.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        adapters = $", static value => {adapter}.FromNative(({native})value), static value => value == null || value is {adapter} ? {adapter}.ToNative(({adapter})value!) : throw new global::System.ArgumentException(\"Value does not match the field type.\")";
                    }
                    factory = $"static () => {NativeFieldInfoSource.TypeName}.Create(typeof({ownerType}), {quotedName}, (global::System.Reflection.FieldAttributes){(int)attributes}{adapters})";
                }
                else if (fieldType is not null && target.GetMembers(fieldName).OfType<IFieldSymbol>().Any(nativeField =>
                    (map.TargetDisplay(fieldType) == nativeField.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ||
                     fieldType is IArrayTypeSymbol array && MatchesDynamicArray(array, nativeField.Type, map))))
                    factory = $"static () => typeof({ownerType}).GetField({quotedName}, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.DeclaredOnly)";
                entries.Add($"new {NativeFieldInfoSource.TypeName}.Candidate(typeof({ownerType}), {quotedName}, (global::System.Reflection.FieldAttributes){(int)attributes}, {factory})");
            }
        }
        // Preserve written argument order, including reordered named arguments with side effects.
        var arguments = new List<ArgumentSyntax> { Argument(TypeOfExpression(ParseTypeName(map.TargetDisplay(source)))) };
        bool hasFlags = false;
        foreach (var argument in syntax.ArgumentList.Arguments)
        {
            var operation = invocation.Arguments.FirstOrDefault(item => item.Syntax == argument);
            string? parameter = operation?.Parameter?.Ordinal switch {
                0 when direct => "name",
                1 when direct => "flags",
                1 when !direct => "name",
                _ => null
            };
            if (parameter is null) continue;
            if (parameter == "flags") hasFlags = true;
            arguments.Add(Argument(visited.ArgumentList.Arguments[syntax.ArgumentList.Arguments.IndexOf(argument)].Expression)
                .WithNameColon(NameColon(IdentifierName(parameter))));
        }
        if (!hasFlags)
            arguments.Add(Argument(ParseExpression("global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Static"))
                .WithNameColon(NameColon(IdentifierName("flags"))));
        arguments.Add(Argument(LiteralExpression(direct ? SyntaxKind.FalseLiteralExpression : SyntaxKind.TrueLiteralExpression))
            .WithNameColon(NameColon(IdentifierName("harmony"))));
        arguments.Add(Argument(ParseExpression($"static () => new {NativeFieldInfoSource.TypeName}.Candidate[] {{ {string.Join(", ", entries)} }}"))
            .WithNameColon(NameColon(IdentifierName("createCandidates"))));
        return InvocationExpression(ParseExpression(NativeFieldInfoSource.TypeName + ".LookupCandidates"), ArgumentList(SeparatedList(arguments)))
            .WithTriviaFrom(syntax);
    }
    private static bool MatchesDynamicArray(IArrayTypeSymbol array, ITypeSymbol target, MetadataSymbolMap map) =>
        (map.SupportsNativeArrays && NativeArraySource.IsSupportedArray(map, array) ||
         map.SupportsNativeReferenceArrays && NativeReferenceArraySource.IsMappedReferenceArray(map, array)) &&
        NativeArrayLowering.Display(map, array) == target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

}
