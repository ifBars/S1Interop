using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace S1Interop.Generators;

public sealed partial class S1InteropTypeRegistryGenerator
{
#pragma warning disable RS2008
    private static readonly DiagnosticDescriptor HarmonyTranspilerDiagnostic = new(
        "S1I004",
        "Harmony transpiler is not IL2CPP portable",
        "Harmony transpiler '{0}' is not portable to IL2CPP builds; use prefix/postfix/finalizer patches or runtime-specific patch registration",
        "S1Interop",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ManagedCollectionBoundaryDiagnostic = new(
        "S1I005",
        "Managed collection signature crosses an IL2CPP boundary",
        "Method '{0}' uses managed collection parameter '{1}' in an IL2CPP-facing signature; use the IL2CPP wrapper type or a generated S1Interop facade boundary",
        "S1Interop",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor ManagedByteBufferBoundaryDiagnostic = new(
        "S1I006",
        "Managed byte buffer crosses an IL2CPP boundary",
        "Invocation '{0}' passes managed byte[] argument '{1}' to an IL2CPP/native-facing buffer API; use Il2CppStructArray<byte> at the boundary and copy to managed byte[] after the call",
        "S1Interop",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor Il2CppObjectCastBoundaryDiagnostic = new(
        "S1I007",
        "Plain C# cast crosses an IL2CPP object boundary",
        "Expression '{0}' casts or pattern-matches an object/proxy value to '{1}' directly; use S1Interop.Generated.S1InteropObjectCast for backend-neutral IL2CPP proxy unwrapping",
        "S1Interop",
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
#pragma warning restore RS2008

    private static readonly string[] Il2CppBoundaryNamespacePrefixes =
    [
        "Il2Cpp",
        "Il2CppSystem",
        "ScheduleOne",
        "Steamworks"
    ];

    private static void ReportIl2CppSourceDiagnostics(SourceProductionContext context, Compilation compilation)
    {
        if (!ShouldReportIl2CppSourceDiagnostics(compilation))
        {
            return;
        }

        foreach (SyntaxTree syntaxTree in compilation.SyntaxTrees)
        {
            if (IsGeneratedOrToolSyntaxTree(syntaxTree))
            {
                continue;
            }

            SemanticModel semanticModel = compilation.GetSemanticModel(syntaxTree);
            SyntaxNode root = syntaxTree.GetRoot(context.CancellationToken);
            foreach (MethodDeclarationSyntax method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
            {
                ReportHarmonyTranspilerDiagnostic(context, method);
                ReportManagedCollectionBoundaryDiagnostics(context, method);
            }

            foreach (InvocationExpressionSyntax invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
            {
                ReportManagedByteBufferBoundaryDiagnostics(context, semanticModel, invocation);
            }

            foreach (IsPatternExpressionSyntax pattern in root.DescendantNodes().OfType<IsPatternExpressionSyntax>())
            {
                ReportIl2CppObjectPatternDiagnostics(context, semanticModel, pattern);
            }

            foreach (BinaryExpressionSyntax binaryExpression in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
            {
                ReportIl2CppObjectAsCastDiagnostics(context, semanticModel, binaryExpression);
            }
        }
    }

    private static bool ShouldReportIl2CppSourceDiagnostics(Compilation compilation) =>
        HasIl2CppPreprocessorSymbol(compilation) ||
        HasRuntimeSurface(compilation, RuntimeBackend.Il2Cpp);

    private static bool HasIl2CppPreprocessorSymbol(Compilation compilation) =>
        compilation.SyntaxTrees.Any(tree =>
            tree.Options is CSharpParseOptions options &&
            options.PreprocessorSymbolNames.Contains("IL2CPP", StringComparer.OrdinalIgnoreCase));

    private static bool IsGeneratedOrToolSyntaxTree(SyntaxTree syntaxTree)
    {
        string path = syntaxTree.FilePath ?? string.Empty;
        if (path.Length == 0)
        {
            return false;
        }

        return path.Contains($"{System.IO.Path.DirectorySeparatorChar}obj{System.IO.Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains($"{System.IO.Path.AltDirectorySeparatorChar}obj{System.IO.Path.AltDirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) ||
               path.Contains("S1Interop.Generated", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
               path.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static void ReportHarmonyTranspilerDiagnostic(SourceProductionContext context, MethodDeclarationSyntax method)
    {
        if (!IsHarmonyTranspiler(method))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            HarmonyTranspilerDiagnostic,
            method.Identifier.GetLocation(),
            method.Identifier.ValueText));
    }

    private static bool IsHarmonyTranspiler(MethodDeclarationSyntax method)
    {
        if (method.AttributeLists
            .SelectMany(attributeList => attributeList.Attributes)
            .Any(attribute => AttributeNameContains(attribute.Name, "HarmonyTranspiler")))
        {
            return true;
        }

        string returnType = method.ReturnType.ToString();
        return method.Identifier.ValueText.Contains("Transpiler", StringComparison.Ordinal) &&
               returnType.Contains("CodeInstruction", StringComparison.Ordinal);
    }

    private static bool AttributeNameContains(NameSyntax name, string value) =>
        name.ToString().Contains(value, StringComparison.Ordinal);

    private static void ReportManagedCollectionBoundaryDiagnostics(SourceProductionContext context, MethodDeclarationSyntax method)
    {
        if (!IsIl2CppFacingSignatureContext(method))
        {
            return;
        }

        foreach (ParameterSyntax parameter in method.ParameterList.Parameters)
        {
            if (parameter.Type is null || !IsManagedCollectionTypeText(parameter.Type.ToString()))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                ManagedCollectionBoundaryDiagnostic,
                parameter.GetLocation(),
                method.Identifier.ValueText,
                parameter.Identifier.ValueText));
        }
    }

    private static bool IsIl2CppFacingSignatureContext(MethodDeclarationSyntax method)
    {
        string methodName = method.Identifier.ValueText;
        if (methodName.Contains("Prefix", StringComparison.Ordinal) ||
            methodName.Contains("Postfix", StringComparison.Ordinal) ||
            methodName.Contains("Finalizer", StringComparison.Ordinal) ||
            methodName.Contains("BindInternal", StringComparison.Ordinal) ||
            methodName.Contains("Config", StringComparison.Ordinal) ||
            methodName.Contains("Panel", StringComparison.Ordinal))
        {
            return true;
        }

        if (method.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText.StartsWith("__", StringComparison.Ordinal)))
        {
            return true;
        }

        SyntaxNode? parent = method.Parent;
        return parent is TypeDeclarationSyntax typeDeclaration &&
               typeDeclaration.AttributeLists
                   .SelectMany(attributeList => attributeList.Attributes)
                   .Any(attribute => AttributeNameContains(attribute.Name, "RegisterTypeInIl2Cpp"));
    }

    private static bool IsManagedCollectionTypeText(string typeText)
    {
        string normalized = typeText.Replace("global::", string.Empty).Replace(" ", string.Empty);
        if (normalized.Contains("Il2CppSystem.Collections.Generic.", StringComparison.Ordinal))
        {
            return false;
        }

        return normalized.Contains("List<", StringComparison.Ordinal) ||
               normalized.Contains("IList<", StringComparison.Ordinal) ||
               normalized.Contains("IReadOnlyList<", StringComparison.Ordinal) ||
               normalized.Contains("ICollection<", StringComparison.Ordinal) ||
               normalized.Contains("IReadOnlyCollection<", StringComparison.Ordinal) ||
               normalized.Contains("Dictionary<", StringComparison.Ordinal) ||
               normalized.Contains("IDictionary<", StringComparison.Ordinal) ||
               normalized.Contains("HashSet<", StringComparison.Ordinal);
    }

    private static void ReportManagedByteBufferBoundaryDiagnostics(
        SourceProductionContext context,
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation)
    {
        if (!IsIl2CppByteBufferBoundaryInvocation(semanticModel, invocation))
        {
            return;
        }

        foreach (ArgumentSyntax argument in invocation.ArgumentList.Arguments)
        {
            if (!IsManagedByteArrayExpression(semanticModel, argument.Expression))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                ManagedByteBufferBoundaryDiagnostic,
                argument.GetLocation(),
                GetInvocationDisplayName(invocation),
                argument.Expression.ToString()));
        }
    }

    private static bool IsIl2CppByteBufferBoundaryInvocation(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        string expressionText = invocation.Expression.ToString();
        if (expressionText.Contains("Il2CppStructArray", StringComparison.Ordinal) ||
            expressionText.StartsWith("System.IO.File.", StringComparison.Ordinal) ||
            expressionText.StartsWith("File.", StringComparison.Ordinal) ||
            expressionText.Contains("Encoding.", StringComparison.Ordinal) ||
            expressionText.Contains("MemoryStream", StringComparison.Ordinal))
        {
            return false;
        }

        if (expressionText.Contains("SteamNetworking.ReadP2PPacket", StringComparison.Ordinal))
        {
            return true;
        }

        if (!LooksLikeBufferFillName(GetInvocationSimpleName(invocation)))
        {
            return false;
        }

        IMethodSymbol? method = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (method is null)
        {
            return false;
        }

        return IsIl2CppBoundarySymbol(method);
    }

    private static bool LooksLikeBufferFillName(string methodName) =>
        methodName.Contains("Read", StringComparison.Ordinal) ||
        methodName.Contains("Receive", StringComparison.Ordinal) ||
        methodName.Contains("Recv", StringComparison.Ordinal) ||
        methodName.Contains("Fill", StringComparison.Ordinal);

    private static string GetInvocationSimpleName(InvocationExpressionSyntax invocation) =>
        invocation.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => invocation.Expression.ToString()
        };

    private static string GetInvocationDisplayName(InvocationExpressionSyntax invocation) =>
        invocation.Expression.ToString();

    private static bool IsIl2CppBoundarySymbol(IMethodSymbol method)
    {
        string containingType = method.ContainingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty;
        string containingNamespace = method.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return Il2CppBoundaryNamespacePrefixes.Any(prefix =>
            containingType.StartsWith("global::" + prefix, StringComparison.Ordinal) ||
            containingNamespace.StartsWith(prefix, StringComparison.Ordinal));
    }

    private static bool IsManagedByteArrayExpression(SemanticModel semanticModel, ExpressionSyntax expression)
    {
        ITypeSymbol? type = semanticModel.GetTypeInfo(expression).Type;
        return type is IArrayTypeSymbol
        {
            ElementType.SpecialType: SpecialType.System_Byte,
            Rank: 1
        };
    }

    private static void ReportIl2CppObjectPatternDiagnostics(
        SourceProductionContext context,
        SemanticModel semanticModel,
        IsPatternExpressionSyntax patternExpression)
    {
        if (patternExpression.Pattern is not DeclarationPatternSyntax declarationPattern ||
            !IsIl2CppObjectCastSource(semanticModel, patternExpression.Expression) ||
            !IsIl2CppObjectCastTarget(declarationPattern.Type))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Il2CppObjectCastBoundaryDiagnostic,
            patternExpression.GetLocation(),
            patternExpression.Expression.ToString(),
            declarationPattern.Type.ToString()));
    }

    private static void ReportIl2CppObjectAsCastDiagnostics(
        SourceProductionContext context,
        SemanticModel semanticModel,
        BinaryExpressionSyntax binaryExpression)
    {
        if (!binaryExpression.IsKind(SyntaxKind.AsExpression) ||
            !IsIl2CppObjectCastSource(semanticModel, binaryExpression.Left) ||
            binaryExpression.Right is not TypeSyntax targetType ||
            !IsIl2CppObjectCastTarget(targetType))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(
            Il2CppObjectCastBoundaryDiagnostic,
            binaryExpression.GetLocation(),
            binaryExpression.Left.ToString(),
            targetType.ToString()));
    }

    private static bool IsIl2CppObjectCastSource(SemanticModel semanticModel, ExpressionSyntax expression)
    {
        ITypeSymbol? type = semanticModel.GetTypeInfo(expression).Type;
        if (type is null)
        {
            return false;
        }

        string typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return type.SpecialType == SpecialType.System_Object ||
               typeName.Contains("Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase", StringComparison.Ordinal);
    }

    private static bool IsIl2CppObjectCastTarget(TypeSyntax targetType)
    {
        string typeText = targetType.ToString();
        return typeText.Contains("UnityEngine.Object", StringComparison.Ordinal) ||
               typeText.Contains("Component", StringComparison.Ordinal) ||
               typeText.Contains("MonoBehaviour", StringComparison.Ordinal) ||
               typeText.Contains("UniversalRenderPipelineAsset", StringComparison.Ordinal);
    }
}

internal static class S1InteropStringCompatibilityExtensions
{
    public static bool Contains(this string source, string value, StringComparison comparisonType) =>
        source.IndexOf(value, comparisonType) >= 0;
}
