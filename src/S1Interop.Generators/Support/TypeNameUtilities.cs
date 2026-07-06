using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace S1Interop.Generators.Support;

internal static class TypeNameUtilities
{
    public static string ToIl2CppTypeName(string monoTypeName)
    {
        if (string.Equals(monoTypeName, "System.Guid", StringComparison.Ordinal) ||
            string.Equals(monoTypeName, "Guid", StringComparison.Ordinal))
        {
            return "Il2CppSystem.Guid";
        }

        const string listPrefix = "System.Collections.Generic.List<";
        if (monoTypeName.StartsWith(listPrefix, StringComparison.Ordinal) &&
            monoTypeName.EndsWith(">", StringComparison.Ordinal))
        {
            string elementTypeName = monoTypeName.Substring(listPrefix.Length, monoTypeName.Length - listPrefix.Length - 1);
            return $"Il2CppSystem.Collections.Generic.List<{ToIl2CppTypeName(elementTypeName)}>";
        }

        const string hashSetPrefix = "System.Collections.Generic.HashSet<";
        if (monoTypeName.StartsWith(hashSetPrefix, StringComparison.Ordinal) &&
            monoTypeName.EndsWith(">", StringComparison.Ordinal))
        {
            string elementTypeName = monoTypeName.Substring(hashSetPrefix.Length, monoTypeName.Length - hashSetPrefix.Length - 1);
            return $"Il2CppSystem.Collections.Generic.HashSet<{ToIl2CppTypeName(elementTypeName)}>";
        }

        const string dictionaryPrefix = "System.Collections.Generic.Dictionary<";
        if (monoTypeName.StartsWith(dictionaryPrefix, StringComparison.Ordinal) &&
            monoTypeName.EndsWith(">", StringComparison.Ordinal))
        {
            string argumentsText = monoTypeName.Substring(dictionaryPrefix.Length, monoTypeName.Length - dictionaryPrefix.Length - 1);
            string[] arguments = SplitTopLevelGenericArguments(argumentsText);
            if (arguments.Length == 2)
            {
                return $"Il2CppSystem.Collections.Generic.Dictionary<{ToIl2CppTypeName(arguments[0])}, {ToIl2CppTypeName(arguments[1])}>";
            }
        }

        if (monoTypeName.EndsWith("[]", StringComparison.Ordinal))
        {
            string elementTypeName = monoTypeName.Substring(0, monoTypeName.Length - 2);
            string arrayTypeName = IsKnownValueTypeName(elementTypeName)
                ? "Il2CppStructArray"
                : "Il2CppReferenceArray";
            return $"Il2CppInterop.Runtime.InteropTypes.Arrays.{arrayTypeName}<{ToIl2CppTypeName(elementTypeName)}>";
        }

        if (monoTypeName.StartsWith("ScheduleOne.", StringComparison.Ordinal))
        {
            return "Il2Cpp" + monoTypeName;
        }

        return monoTypeName;
    }

    public static string GetSimpleName(string typeName)
    {
        int separator = typeName.LastIndexOf('.');
        return separator < 0 ? typeName : typeName.Substring(separator + 1);
    }

    public static TypeFacadeName GetTypeFacadeName(S1InteropTypeEntry entry)
    {
        string[] parts = entry.MonoTypeName
            .Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(SanitizeIdentifier)
            .ToArray();
        if (parts.Length == 0)
        {
            return new TypeFacadeName("S1Interop", entry.Alias);
        }

        string typeName = parts[parts.Length - 1];
        IEnumerable<string> namespaceParts = parts.Take(parts.Length - 1);
        return new TypeFacadeName(BuildTypeFacadeNamespace(namespaceParts), typeName);
    }

    public static string GetHandleTypeName(S1InteropTypeEntry entry) =>
        $"S1Interop.Generated.S1InteropObject<S1Interop.Generated.S1InteropTypeRegistry.{entry.Alias}Tag>";

    public static string ToPascalIdentifier(string value)
    {
        string sanitized = SanitizeIdentifier(value);
        if (sanitized.Length == 0)
        {
            return "Member";
        }

        if (sanitized.Length == 1)
        {
            return sanitized.ToUpperInvariant();
        }

        return char.ToUpperInvariant(sanitized[0]) + sanitized.Substring(1);
    }

    public static string SanitizeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "RuntimeType";
        }

        var builder = new StringBuilder();
        foreach (char character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
        }

        if (builder.Length == 0 || char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    public static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public static string ToCSharpBoolean(bool value) => value ? "true" : "false";

    public static string NormalizeComparableTypeName(string typeName) =>
        typeName
            .Replace("global::", string.Empty)
            .Replace("System.Boolean", "bool")
            .Replace("System.Byte", "byte")
            .Replace("System.Char", "char")
            .Replace("System.Decimal", "decimal")
            .Replace("System.Double", "double")
            .Replace("System.Int16", "short")
            .Replace("System.Int32", "int")
            .Replace("System.Int64", "long")
            .Replace("System.Object", "object")
            .Replace("System.SByte", "sbyte")
            .Replace("System.Single", "float")
            .Replace("System.String", "string")
            .Replace("System.UInt16", "ushort")
            .Replace("System.UInt32", "uint")
            .Replace("System.UInt64", "ulong")
            .Replace("System.Void", "void")
            .Replace(" ", string.Empty)
            .Trim();

    private static bool IsKnownValueTypeName(string typeName) =>
        typeName is "bool" or "byte" or "char" or "double" or "float" or "int" or "long" or "short" or "uint" or "ulong" or "System.Guid" or "Guid";

    private static string[] SplitTopLevelGenericArguments(string argumentsText)
    {
        var arguments = new List<string>();
        int depth = 0;
        int start = 0;
        for (int index = 0; index < argumentsText.Length; index++)
        {
            char character = argumentsText[index];
            if (character == '<')
            {
                depth++;
            }
            else if (character == '>')
            {
                depth--;
            }
            else if (character == ',' && depth == 0)
            {
                arguments.Add(argumentsText.Substring(start, index - start).Trim());
                start = index + 1;
            }
        }

        arguments.Add(argumentsText.Substring(start).Trim());
        return arguments.ToArray();
    }

    private static string BuildTypeFacadeNamespace(IEnumerable<string> namespaceParts)
    {
        string namespaceSuffix = string.Join(".", namespaceParts);
        return string.IsNullOrWhiteSpace(namespaceSuffix)
            ? "S1Interop"
            : "S1Interop." + namespaceSuffix;
    }
}
