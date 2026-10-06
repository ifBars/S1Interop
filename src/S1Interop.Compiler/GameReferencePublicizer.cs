using Mono.Cecil;
using Mono.Cecil.Cil;

namespace S1Interop.Compiler;

/// <summary>
/// Produces metadata-only, publicized compile references from local game assemblies so mod code can be compiled
/// against nonpublic game surface without redistributing or executing game code.
/// </summary>
/// <remarks>
/// <para>
/// The output keeps the input assembly identity (name, version, culture, public key) but is never re-signed. Every
/// nonpublic type becomes public, as do fields and nonvirtual methods and constructors. Virtual methods keep their
/// original accessibility because overrides must match the access of the slot they override.
/// </para>
/// <para>
/// A field keeps its original accessibility when it shares its name with an event or property on the same type, so
/// compilers do not see an ambiguous member.
/// </para>
/// <para>
/// Every IL method body becomes <c>ldnull; throw</c>. The writer strips embedded resources, debug metadata, and the
/// entry point, then marks the assembly with
/// <see cref="System.Runtime.CompilerServices.ReferenceAssemblyAttribute"/> so runtimes refuse to load it for
/// execution.
/// </para>
/// </remarks>
public static class GameReferencePublicizer
{
    /// <summary>
    /// Version of the publicized output shape. Increment it whenever the transformation changes so callers can
    /// invalidate cached references.
    /// </summary>
    public const string FormatVersion = "2";

    private const string ReferenceAssemblyAttributeNamespace = "System.Runtime.CompilerServices";
    private const string ReferenceAssemblyAttributeName = "ReferenceAssemblyAttribute";

    /// <summary>
    /// Creates a publicized, metadata-only reference assembly from a managed assembly image.
    /// </summary>
    /// <param name="image">The raw bytes of a managed assembly. It is read as metadata only and never loaded or run.</param>
    /// <param name="resolutionDirectories">Local directories used only to read dependency metadata when Cecil needs it.</param>
    /// <returns>The bytes of the publicized reference assembly. Identical input always produces identical output.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <see langword="null"/>.</exception>
    /// <exception cref="BadImageFormatException"><paramref name="image"/> is not a valid managed assembly.</exception>
    public static byte[] CreateReference(byte[] image, IEnumerable<string>? resolutionDirectories = null)
    {
        ArgumentNullException.ThrowIfNull(image);

        using var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
        foreach (string directory in resolutionDirectories ?? []) resolver.AddSearchDirectory(directory);
        // Keep untouched custom-attribute blobs raw; dependency resolution remains metadata-only.
        var readerParameters = new ReaderParameters(ReadingMode.Deferred)
        {
            InMemory = true,
            ReadSymbols = false,
            ReadWrite = false,
            AssemblyResolver = resolver,
        };

        using var input = new MemoryStream(image, writable: false);
        using var assembly = AssemblyDefinition.ReadAssembly(input, readerParameters);

        foreach (var module in assembly.Modules)
        {
            TransformModule(module);
        }

        AddReferenceAssemblyAttribute(assembly);

        var writerParameters = new WriterParameters
        {
            DeterministicMvid = true,
            WriteSymbols = false,
        };

        using var output = new MemoryStream();
        assembly.Write(output, writerParameters);
        return output.ToArray();
    }

    private static void TransformModule(ModuleDefinition module)
    {
        module.EntryPoint = null;
        if (module.Kind is ModuleKind.Console or ModuleKind.Windows)
        {
            module.Kind = ModuleKind.Dll;
        }

        // The identity's public key is preserved, but the output is not signed; do not claim a valid signature.
        module.Attributes &= ~ModuleAttributes.StrongNameSigned;

        for (var i = module.Resources.Count - 1; i >= 0; i--)
        {
            if (module.Resources[i] is EmbeddedResource)
            {
                module.Resources.RemoveAt(i);
            }
        }

        foreach (var type in module.GetTypes())
        {
            TransformType(type);
        }
    }

    private static void TransformType(TypeDefinition type)
    {
        PublicizeType(type);

        var accessorNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var evt in type.Events)
        {
            accessorNames.Add(evt.Name);
        }

        foreach (var property in type.Properties)
        {
            accessorNames.Add(property.Name);
        }

        foreach (var field in type.Fields)
        {
            if (!accessorNames.Contains(field.Name))
            {
                field.Attributes = (field.Attributes & ~FieldAttributes.FieldAccessMask) | FieldAttributes.Public;
            }
        }

        foreach (var method in type.Methods)
        {
            if (!method.IsVirtual && !method.IsConstructor || method.IsConstructor && !method.IsStatic)
            {
                method.Attributes = (method.Attributes & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Public;
            }

            if (method.HasBody)
            {
                StubBody(method);
            }
        }
    }

    private static void PublicizeType(TypeDefinition type)
    {
        if (type.Name == "<Module>") return;
        var visibility = type.IsNested ? TypeAttributes.NestedPublic : TypeAttributes.Public;
        type.Attributes = (type.Attributes & ~TypeAttributes.VisibilityMask) | visibility;
    }

    private static void StubBody(MethodDefinition method)
    {
        var body = new MethodBody(method);
        var il = body.GetILProcessor();
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Throw);
        method.Body = body;
        method.DebugInformation.Scope = null;
        method.DebugInformation.SequencePoints.Clear();
        method.CustomDebugInformations.Clear();
    }

    private static void AddReferenceAssemblyAttribute(AssemblyDefinition assembly)
    {
        foreach (var attribute in assembly.CustomAttributes)
        {
            if (attribute.AttributeType.Namespace == ReferenceAssemblyAttributeNamespace
                && attribute.AttributeType.Name == ReferenceAssemblyAttributeName)
            {
                return;
            }
        }

        var module = assembly.MainModule;
        var attributeType = new TypeReference(
            ReferenceAssemblyAttributeNamespace,
            ReferenceAssemblyAttributeName,
            module,
            module.TypeSystem.CoreLibrary);
        var constructor = new MethodReference(".ctor", module.TypeSystem.Void, attributeType) { HasThis = true };
        assembly.CustomAttributes.Add(new CustomAttribute(constructor));
    }
}
