using System.Security.Cryptography;
using Mono.Cecil;

namespace S1Interop.Compiler;

/// <summary>A source-recognized event accessor, addressed by exact target module and token.</summary>
public sealed record AtomicEventRepair(int MetadataToken, string DeclaringType, string FieldName, string CallbackType, bool Add, bool IsStatic = false);

/// <summary>Content-bound reconstruction instructions; creating a plan does not modify or load either input.</summary>
public sealed record AtomicEventRepairPlan(string AuthorSha256, string TargetSha256, string TargetAssembly,
    Guid TargetModuleMvid, IReadOnlyList<AtomicEventRepair> Accessors)
{
    /// <summary>Plans only canonical source event loops with matching target storage and failed wrapper bodies.</summary>
    public static AtomicEventRepairPlan Create(byte[] authorImage, byte[] targetImage)
    {
        var findings = UnstrippedMethodAnalysis.Inspect(authorImage, targetImage);
        using var stream = new MemoryStream(targetImage, writable: false);
        using var target = AssemblyDefinition.ReadAssembly(stream);
        var repairs = new List<AtomicEventRepair>();
        foreach (var finding in findings.Where(f => f.HasCanonicalAtomicEventBody && f.StorageProperty is not null))
        {
            var method = target.MainModule.LookupToken(finding.TargetMetadataToken) as MethodDefinition;
            if (method is null || method.HasGenericParameters || method.DeclaringType.HasGenericParameters ||
                method.Parameters.Count != 1 || method.ReturnType.FullName != "System.Void") continue;
            var storage = method.DeclaringType.Properties.SingleOrDefault(p => p.FullName == finding.StorageProperty);
            if (storage is null || storage.Parameters.Count != 0 || storage.GetMethod is null || storage.SetMethod is null ||
                storage.GetMethod.IsStatic != method.IsStatic || storage.SetMethod.IsStatic != method.IsStatic ||
                storage.PropertyType.FullName != method.Parameters[0].ParameterType.FullName) continue;
            bool add = method.Name == "add_" + finding.EventName;
            if (!add && method.Name != "remove_" + finding.EventName) continue;
            repairs.Add(new(method.MetadataToken.ToInt32(), method.DeclaringType.FullName.Replace('/', '+'),
                storage.Name, storage.PropertyType.FullName.Replace('/', '+'), add, method.IsStatic));
        }
        return new(Convert.ToHexString(SHA256.HashData(authorImage)), Convert.ToHexString(SHA256.HashData(targetImage)),
            target.Name.FullName, target.MainModule.Mvid, repairs.OrderBy(r => r.MetadataToken).ToArray());
    }
}
