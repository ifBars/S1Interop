using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace S1Interop.Compiler.Tests;

internal static class PublicizerTests
{
    private const string Contract = """
        using System;
        namespace HiddenGame {
            internal class Secret {
                private int value;
                private Secret(int initial) { value = initial; }
                private int Read(int increment) => value + increment;
                public event Action Changed;
                public void Raise() => Changed?.Invoke();
                private class Token { internal int Value = 2; }
                private Token Make() => new Token();
            }
            public class Parent {
                protected virtual int Compute() => 2;
                public int Dispatch() => Compute();
            }
        }
        """;

    public static void RuntimeAccess()
    {
        byte[] original = CompilationSupport.Emit("PrivateGameContract", Contract);
        byte[] reference = GameReferencePublicizer.CreateReference(original);
        string source = ReferencePreparation.AccessSource(["PrivateGameContract"]) + """
            public sealed class Derived : HiddenGame.Parent { protected override int Compute() => 11; }
            public static class Probe {
                public static int Run() {
                    var secret = new HiddenGame.Secret(3);
                    secret.value = 7;
                    int calls = 0;
                    secret.Changed += () => calls++;
                    secret.Raise();
                    return secret.Read(1) + secret.Make().Value + calls + new Derived().Dispatch();
                }
            }
            """;
        var compilation = CompilationSupport.Create("Probe", source,
            CompilationSupport.PlatformReferences.Add(MetadataReference.CreateFromImage(reference)));
        using var emitted = new MemoryStream();
        var result = compilation.Emit(emitted);
        Require(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        var context = new AssemblyLoadContext("PublicizedContract", isCollectible: true);
        try
        {
            using var implementation = new MemoryStream(original);
            context.LoadFromStream(implementation);
            emitted.Position = 0;
            var assembly = context.LoadFromStream(emitted);
            Require((int)assembly.GetType("Probe")!.GetMethod("Run")!.Invoke(null, null)! == 22,
                "Private access, normal event subscription, or protected override behavior changed.");
        }
        finally { context.Unload(); }
    }

    public static void ReferenceShape()
    {
        byte[] original = CompilationSupport.Emit("PrivateGameContract", Contract);
        byte[] first = GameReferencePublicizer.CreateReference(original);
        Require(first.SequenceEqual(GameReferencePublicizer.CreateReference(original)), "Publicization is not deterministic.");
        using var originalStream = new MemoryStream(original);
        using var originalAssembly = AssemblyDefinition.ReadAssembly(originalStream);
        using var stream = new MemoryStream(first);
        using var assembly = AssemblyDefinition.ReadAssembly(stream);
        Require(assembly.Name.FullName == originalAssembly.Name.FullName, "Assembly identity changed.");
        Require(assembly.CustomAttributes.Any(attribute => attribute.AttributeType.FullName ==
            "System.Runtime.CompilerServices.ReferenceAssemblyAttribute"), "Missing reference-only marker.");
        foreach (var method in assembly.MainModule.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody))
            Require(method.Body.Instructions.Count == 2 && method.Body.Instructions[0].OpCode == OpCodes.Ldnull &&
                method.Body.Instructions[1].OpCode == OpCodes.Throw, "An executable game body was retained.");
        Require(assembly.MainModule.GetType("HiddenGame.Parent").Methods.Single(method => method.Name == "Compute").IsFamily,
            "Protected virtual accessibility changed.");
        var context = new AssemblyLoadContext("ReferenceOnly", isCollectible: true);
        try
        {
            using var image = new MemoryStream(first);
            try { context.LoadFromStream(image); }
            catch (BadImageFormatException) { return; }
            throw new InvalidOperationException("Compiler reference was accepted for execution.");
        }
        finally { context.Unload(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
