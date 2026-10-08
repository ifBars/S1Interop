using Mono.Cecil;
using Mono.Cecil.Cil;

namespace S1Interop.Compiler;

/// <summary>Metadata evidence for a generated wrapper that could not restore a managed method.</summary>
/// <param name="TargetMethod">Exact target signature.</param>
/// <param name="AuthorMethod">Unique matching author signature, if present.</param>
/// <param name="AuthorHasBody">Whether the original method supplies IL.</param>
/// <param name="EventName">Original event whose accessor owns the body, if any.</param>
/// <param name="StorageProperty">Matching readable/writable target property for the original backing field.</param>
/// <param name="UsesCompareExchange">Whether the original body calls Interlocked.CompareExchange.</param>
/// <param name="DirectCallers">Methods with direct call or delegate-function references to this stub.</param>
/// <param name="PotentialCallers">Same-module static reachability, including direct references. Branch execution, dynamic dispatch, reflection, and cross-assembly calls are not established.</param>
/// <param name="TargetModuleMvid">Module identity; display signatures alone may collide in generated metadata.</param>
/// <param name="TargetMetadataToken">Method token within that target module.</param>
/// <param name="HasCanonicalAtomicEventBody">Whether the complete original field-like event CAS loop matches a recognized pattern; no runtime repair is implied.</param>
public sealed record UnstrippedMethodEvidence(string TargetMethod, string? AuthorMethod, bool AuthorHasBody,
    string? EventName, string? StorageProperty, bool UsesCompareExchange, IReadOnlyList<string> DirectCallers,
    IReadOnlyList<string> PotentialCallers, Guid TargetModuleMvid, int TargetMetadataToken, bool HasCanonicalAtomicEventBody);

/// <summary>
/// Reads local assembly IL to identify failed unstripping and its source semantics. This is analysis only:
/// matching storage and a source body do not establish that a reconstruction is safe or implemented.
/// </summary>
public static class UnstrippedMethodAnalysis
{
    /// <summary>Inspects original Mono and generated target images without loading or executing either assembly.</summary>
    public static IReadOnlyList<UnstrippedMethodEvidence> Inspect(byte[] authorImage, byte[] targetImage)
    {
        ArgumentNullException.ThrowIfNull(authorImage);
        ArgumentNullException.ThrowIfNull(targetImage);
        using var authorStream = new MemoryStream(authorImage, writable: false);
        using var targetStream = new MemoryStream(targetImage, writable: false);
        using var author = AssemblyDefinition.ReadAssembly(authorStream);
        using var target = AssemblyDefinition.ReadAssembly(targetStream);
        if (author.Modules.Count != 1 || target.Modules.Count != 1)
            throw new NotSupportedException("Unstripping analysis currently requires single-module assemblies.");
        var authorMethods = author.Modules.SelectMany(Types).SelectMany(t => t.Methods)
            .GroupBy(m => Signature(m, false)).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var targetMethods = target.Modules.SelectMany(Types).SelectMany(t => t.Methods).ToArray();
        var callers = new Dictionary<MethodDefinition, HashSet<MethodDefinition>>();
        foreach (var module in target.Modules)
        {
            // Full signatures are scoped to their module; external methods with the same
            // spelling must never become edges in this module's call graph.
            var methods = Types(module).SelectMany(t => t.Methods).ToArray();
            var local = methods.GroupBy(m => m.FullName).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            foreach (var method in methods.Where(m => m.HasBody))
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt or Code.Newobj or Code.Ldftn or Code.Ldvirtftn) ||
                    instruction.Operand is not MethodReference called || !SameModule(called.DeclaringType, module)) continue;
                var definition = called is GenericInstanceMethod generic ? generic.ElementMethod : called;
                MethodDefinition? callee = definition is MethodDefinition exact && exact.Module == module ? exact :
                    local.TryGetValue(definition.FullName, out var candidates) &&
                    candidates.Where(c => c.HasThis == definition.HasThis && c.ExplicitThis == definition.ExplicitThis).ToArray() is [var unique]
                        ? unique : null;
                if (callee is null && definition.DeclaringType is GenericInstanceType declaring)
                {
                    var genericMatches = methods.Where(m => m.DeclaringType.FullName == declaring.ElementType.FullName &&
                        MethodShape(m, false) == MethodShape(definition, false)).ToArray();
                    if (genericMatches is [var genericMatch]) callee = genericMatch;
                }
                if (callee is null) continue;
                if (!callers.TryGetValue(callee, out var set)) callers.Add(callee, set = []);
                set.Add(method);
            }
        }

        var result = new List<UnstrippedMethodEvidence>();
        foreach (var method in targetMethods.Where(IsFailedUnstripping).OrderBy(m => m.FullName, StringComparer.Ordinal))
        {
            // Prefer exact source spelling. Normalize only the target's wrapper spelling,
            // never author names that genuinely begin with Il2Cpp.
            if (!authorMethods.TryGetValue(Signature(method, false), out var matches))
                authorMethods.TryGetValue(Signature(method, true), out matches);
            MethodDefinition? source = matches is [var match] ? match : null;
            EventDefinition? sourceEvent = source?.DeclaringType.Events.SingleOrDefault(e => e.AddMethod == source || e.RemoveMethod == source);
            var instructions = source?.HasBody == true ? source.Body.Instructions.ToArray() : [];
            var accessedFields = instructions.Select(i => i.Operand).OfType<FieldReference>()
                .Where(f => f.DeclaringType.FullName == source?.DeclaringType.FullName)
                .Select(f => f.FullName).ToHashSet(StringComparer.Ordinal);
            var backing = sourceEvent is null ? null : source!.DeclaringType.Fields.SingleOrDefault(f =>
                f.Name == sourceEvent.Name && f.FieldType.FullName == sourceEvent.EventType.FullName && accessedFields.Contains(f.FullName));
            var property = backing is null ? null : method.DeclaringType.Properties.SingleOrDefault(p =>
                p.Name == backing.Name && p.Parameters.Count == 0 &&
                (TypeName(p.PropertyType, false) == TypeName(backing.FieldType, false) || TypeName(p.PropertyType, true) == TypeName(backing.FieldType, false)) &&
                p.GetMethod is { } get && p.SetMethod is { } set && get.IsStatic == backing.IsStatic && set.IsStatic == backing.IsStatic);
            bool atomic = instructions.Select(i => i.Operand).OfType<MethodReference>().Any(m =>
                m.DeclaringType.FullName == "System.Threading.Interlocked" && m.Name == "CompareExchange");
            result.Add(new(method.FullName, source?.FullName, source?.HasBody == true, sourceEvent?.Name,
                property?.FullName, atomic, callers.TryGetValue(method, out var direct)
                    ? direct.Select(m => m.FullName).Order(StringComparer.Ordinal).ToArray() : [], ReachableCallers(method, callers),
                method.Module.Mvid, method.MetadataToken.ToInt32(), source is not null && sourceEvent is not null && backing is not null &&
                AtomicEventBody.Matches(source, sourceEvent, backing)));
        }
        return result;
    }

    private static string[] ReachableCallers(MethodDefinition stub, Dictionary<MethodDefinition, HashSet<MethodDefinition>> callers)
    {
        var seen = new HashSet<MethodDefinition> { stub };
        var pending = new Queue<MethodDefinition>();
        pending.Enqueue(stub);
        while (pending.TryDequeue(out var current))
            if (callers.TryGetValue(current, out var direct))
                foreach (var caller in direct)
                    if (seen.Add(caller)) pending.Enqueue(caller);
        seen.Remove(stub);
        return seen.Select(m => m.FullName).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool IsFailedUnstripping(MethodDefinition method)
    {
        if (!method.HasBody || method.Body.ExceptionHandlers.Count != 0) return false;
        var body = method.Body.Instructions.Where(i => i.OpCode.Code != Code.Nop).ToArray();
        // The generator appends an unreachable ret after its throwing fallback.
        if (body.Length == 4 && body[^1].OpCode.Code == Code.Ret) body = body[..^1];
        return body is [var message, var construct, var terminal] &&
            message.OpCode.Code == Code.Ldstr && message.Operand is "Method unstripping failed" &&
            construct.OpCode.Code == Code.Newobj && construct.Operand is MethodReference
            {
                Name: ".ctor", DeclaringType.FullName: "System.NotSupportedException", Parameters.Count: 1
            } constructor && constructor.Parameters[0].ParameterType.FullName == "System.String" &&
            terminal.OpCode.Code == Code.Throw;
    }

    private static IEnumerable<TypeDefinition> Types(ModuleDefinition module) => module.Types.SelectMany(Types);
    private static IEnumerable<TypeDefinition> Types(TypeDefinition type) => new[] { type }.Concat(type.NestedTypes.SelectMany(Types));

    private static bool SameModule(TypeReference type, ModuleDefinition module)
    {
        if (type is TypeSpecification specification) return SameModule(specification.ElementType, module);
        return type.Scope == module || type.Scope is AssemblyNameReference assembly && assembly.FullName == module.Assembly.Name.FullName;
    }

    private static string Signature(MethodReference method, bool target) => TypeName(method.DeclaringType, target) + "::" + MethodShape(method, target);

    private static string MethodShape(MethodReference method, bool target) =>
        method.Name + "`" + method.GenericParameters.Count +
        "(" + string.Join(",", method.Parameters.Select(p => TypeName(p.ParameterType, target))) + "):" +
        TypeName(method.ReturnType, target) + ":" + method.HasThis + ":" + method.ExplicitThis;

    private static string TypeName(TypeReference type, bool target) => type switch
    {
        GenericParameter parameter => (parameter.Type == GenericParameterType.Method ? "!!" : "!") + parameter.Position,
        ArrayType array => TypeName(array.ElementType, target) + "[" + new string(',', array.Rank - 1) + "]",
        ByReferenceType reference => TypeName(reference.ElementType, target) + "&",
        PointerType pointer => TypeName(pointer.ElementType, target) + "*",
        GenericInstanceType generic when target && generic.ElementType.FullName is
            "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1" or
            "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1" or
            "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase`1" => TypeName(generic.GenericArguments[0], true) + "[]",
        GenericInstanceType generic => TypeName(generic.ElementType, target) + "<" + string.Join(",", generic.GenericArguments.Select(t => TypeName(t, target))) + ">",
        _ when target && type.FullName == "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray" => "System.String[]",
        _ when type.DeclaringType is { } owner => TypeName(owner, target) + "/" + type.Name,
        _ => target && type.Namespace.Length > 0 && type.FullName.StartsWith("Il2Cpp", StringComparison.Ordinal)
            ? type.FullName[6..].TrimStart('.') : type.FullName
    };
}
