using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

/// <summary>Builds one shared, game-type-independent ABI for every lowered mod.</summary>
internal static class RuntimeSupportBuilder
{
    public const string AssemblyName = "S1Interop.Runtime";
    public const string FileName = AssemblyName + ".dll";

    public static (ImmutableArray<byte> Image, ImmutableArray<Diagnostic> Diagnostics, string Signature) Build(
        CSharpCompilation target, MetadataSymbolMap map, CancellationToken token)
    {
        // A fixed language and release configuration keep the support ABI independent of each mod's project options.
        var parse = new CSharpParseOptions(LanguageVersion.CSharp9);
        SyntaxTree[] trees =
        [
            NativeCastHelperSource.CreateTree(parse, token),
            NativeFieldInfoSource.CreateTree(parse, token),
            NativeDelegateCacheSource.CreateTree(parse, map, token),
            NativeListSource.CreateTree(parse, token),
            NativeEqualityComparerSource.CreateTree(parse, token),
            InjectionSupportSource.CreateTree(parse, map, token),
            CSharpSyntaxTree.ParseText("[assembly: System.Reflection.AssemblyVersion(\"0.1.0.0\")]", parse,
                "S1Interop.Runtime.Version.g.cs", System.Text.Encoding.UTF8, token)
        ];
        if (map.FindTargetType("Il2CppSystem.Collections.Generic.Dictionary`2") is not null)
            trees = [.. trees, NativeDictionarySource.CreateTree(parse, map, token)];
        if (map.SupportsNativeArrays)
            trees = [.. trees, NativeArraySource.CreateTree(parse, token)];
        if (map.SupportsNativeReferenceArrays)
            trees = [.. trees, NativeReferenceArraySource.CreateTree(parse, token)];
        if (map.FindTargetType(CoroutineSupportSource.AdapterType) is not null)
            trees = [.. trees, CoroutineSupportSource.CreateTree(parse, map, token)];
        string contract = string.Join("\n", trees.Select(tree => tree.ToString())) + "\n" +
            map.NativeObjectBase!.ContainingAssembly.Identity + "\n" +
            map.FindTargetType("Il2CppSystem.Collections.Generic.List`1")?.ContainingAssembly.Identity + "\n" +
            map.FindTargetType(CoroutineSupportSource.AdapterType)?.ContainingAssembly.Identity;
        string signature = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(contract)));
        var guard = CSharpSyntaxTree.ParseText($$"""
            namespace S1Interop.Compiler.Generated {
                public static class S1InteropRuntimeContract {
                    public static void Validate(string expected, string mod) {
                        if (expected != "{{signature}}")
                            throw new global::System.InvalidOperationException("S1Interop runtime mismatch for " + mod +
                                ". Rebuild the mod and deploy its matching S1Interop.Runtime.dll. Expected " + expected +
                                "; loaded {{signature}}.");
                    }
                }
            }
            """, parse, "S1Interop.Runtime.Contract.g.cs", Encoding.UTF8, token);
        var compilation = CSharpCompilation.Create(AssemblyName, trees.Append(guard), target.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
                deterministic: true, allowUnsafe: true));
        using var output = new MemoryStream();
        var emitted = compilation.Emit(output, cancellationToken: token);
        return (emitted.Success ? ImmutableArray.CreateRange(output.ToArray()) : [], emitted.Diagnostics, signature);
    }

    public static SyntaxTree CreateModGuard(string signature, string assemblyName, CSharpParseOptions options,
        CSharpCompilation target)
    {
        var dependencies = target.References.Select(target.GetAssemblyOrModuleSymbol).OfType<IAssemblySymbol>()
            .Where(assembly => assembly.GetAttributes().Any(attribute =>
                attribute.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyMetadataAttribute" &&
                attribute.ConstructorArguments.Length == 2 &&
                attribute.ConstructorArguments[0].Value as string == "S1Interop.AuthoringSha256"))
            .Select(assembly => assembly.Identity.ToString()).Distinct().OrderBy(name => name, StringComparer.Ordinal);
        string registrations = string.Join("\n", dependencies.Select(identity =>
            "global::MelonLoader.RegisterTypeInIl2Cpp.RegisterAssembly(global::System.Reflection.Assembly.Load(" +
            SyntaxFactory.Literal(identity) + "));"));
        return CSharpSyntaxTree.ParseText($$"""
            namespace S1Interop.Compiler.Generated {
                internal static class S1InteropRuntimeCheck {
                    [global::System.Runtime.CompilerServices.ModuleInitializer]
                    internal static void Initialize() {
                        S1InteropRuntimeContract.Validate("{{signature}}", {{SyntaxFactory.Literal(assemblyName)}});
                        {{registrations}}
                    }
                }
            }
            """, options, "S1Interop.Compiler.RuntimeCheck.g.cs", Encoding.UTF8);
    }
}
