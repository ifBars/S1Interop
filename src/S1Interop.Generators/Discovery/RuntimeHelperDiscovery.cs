using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace S1Interop.Generators.Discovery;

/// <summary>
/// Reads the compilation facts that decide which runtime helpers and injected-type constructors can be generated.
/// </summary>
internal static class RuntimeHelperDiscovery
{
    public const string RegisterTypeInIl2CppMetadataName = "MelonLoader.RegisterTypeInIl2Cpp";
    public const string RegisterTypeInIl2CppWithInterfacesMetadataName = "MelonLoader.RegisterTypeInIl2CppWithInterfaces";

    private const string Il2CppObjectBaseMetadataName = "Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase";
    private const string ClassInjectorMetadataName = "Il2CppInterop.Runtime.Injection.ClassInjector";
    private const string MelonInfoMetadataName = "MelonLoader.MelonInfoAttribute";
    private const string MelonPlatformDomainMetadataName = "MelonLoader.MelonPlatformDomainAttribute";

    public static RuntimeHelperCapabilities GetCapabilities(Compilation compilation)
    {
        INamedTypeSymbol? melonInfo = compilation.GetTypeByMetadataName(MelonInfoMetadataName);
        INamedTypeSymbol? platformDomain = compilation.GetTypeByMetadataName(MelonPlatformDomainMetadataName);
        ImmutableArray<AttributeData> assemblyAttributes = compilation.Assembly.GetAttributes();
        bool isMelon = melonInfo is not null &&
            assemblyAttributes.Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, melonInfo));
        bool declaresPlatformDomain = platformDomain is not null &&
            assemblyAttributes.Any(attribute => SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, platformDomain));

        INamedTypeSymbol? unityAction = compilation.GetTypeByMetadataName("UnityEngine.Events.UnityAction");
        return new RuntimeHelperCapabilities(
            IsUsable(compilation.GetTypeByMetadataName(Il2CppObjectBaseMetadataName)),
            IsUsable(compilation.GetTypeByMetadataName("Il2CppSystem.Collections.Generic.List`1")),
            IsUsable(compilation.GetTypeByMetadataName("Il2CppSystem.Collections.Generic.Dictionary`2")),
            IsUsable(compilation.GetTypeByMetadataName("UnityEngine.Events.UnityEvent")) &&
                IsUsable(compilation.GetTypeByMetadataName("UnityEngine.Events.UnityEvent`1")) &&
                IsUsable(unityAction),
            unityAction?.TypeKind == TypeKind.Delegate,
            compilation.GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.NotNullWhenAttribute") is not null,
            isMelon && platformDomain is not null && !declaresPlatformDomain);
    }

    public static Il2CppInjectedTypeEntry? ReadInjectedType(GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol type ||
            type.TypeKind != TypeKind.Class ||
            context.TargetNode is not ClassDeclarationSyntax declaration)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var containingDeclarations = new List<string>();
        bool containersCanExtend = true;
        for (INamedTypeSymbol? container = type.ContainingType; container is not null; container = container.ContainingType)
        {
            containersCanExtend &= container.TypeKind == TypeKind.Class && IsPartial(container) && !container.IsGenericType;
            containingDeclarations.Insert(0, $"partial class {container.Name}");
        }

        bool hasIntPtrConstructor = type.InstanceConstructors.Any(IsIntPtrConstructor);
        bool declaresConstructor = type.InstanceConstructors.Any(constructor => !constructor.IsImplicitlyDeclared);
        bool isComponent = DerivesFrom(type, "UnityEngine", "Component");
        Compilation compilation = context.SemanticModel.Compilation;

        return new Il2CppInjectedTypeEntry(
            GetMetadataName(type),
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            containingDeclarations.ToImmutableArray(),
            type.Name,
            containersCanExtend && declaration.Modifiers.Any(SyntaxKind.PartialKeyword) && !type.IsGenericType,
            hasIntPtrConstructor,
            !declaresConstructor && !isComponent && !type.IsAbstract &&
                compilation.GetTypeByMetadataName(ClassInjectorMetadataName) is not null,
            BaseHasIntPtrConstructor(type),
            declaration.Identifier.GetLocation());
    }

    /// <summary>
    /// A referenced type is only usable when its whole base chain resolves. IL2CPP wrappers derive from
    /// Il2Cppmscorlib types, which narrow reference sets often leave out; generated code touching such a type
    /// would fail with CS0012.
    /// </summary>
    private static bool IsUsable(INamedTypeSymbol? type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.TypeKind == TypeKind.Error)
            {
                return false;
            }
        }

        return type is not null;
    }

    private static bool BaseHasIntPtrConstructor(INamedTypeSymbol type)
    {
        INamedTypeSymbol? baseType = type.BaseType;
        if (baseType is null)
        {
            return false;
        }

        // A partial injected base gets its constructor from this generator, which this compilation cannot see yet.
        return baseType.InstanceConstructors.Any(constructor =>
                IsIntPtrConstructor(constructor) && constructor.DeclaredAccessibility is not Accessibility.Private) ||
            baseType.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() is RegisterTypeInIl2CppMetadataName or RegisterTypeInIl2CppWithInterfacesMetadataName);
    }

    private static bool IsIntPtrConstructor(IMethodSymbol constructor) =>
        constructor.Parameters.Length == 1 &&
        constructor.Parameters[0].Type.SpecialType == SpecialType.System_IntPtr;

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is TypeDeclarationSyntax declaration &&
            declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static bool DerivesFrom(INamedTypeSymbol type, string namespaceName, string typeName)
    {
        for (INamedTypeSymbol? current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.Name == typeName && current.ContainingNamespace.ToDisplayString() == namespaceName)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetMetadataName(INamedTypeSymbol type)
    {
        string name = type.MetadataName;
        for (INamedTypeSymbol? container = type.ContainingType; container is not null; container = container.ContainingType)
        {
            name = $"{container.MetadataName}+{name}";
        }

        return type.ContainingNamespace.IsGlobalNamespace
            ? name
            : $"{type.ContainingNamespace.ToDisplayString()}.{name}";
    }
}
