using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Text;

namespace S1Interop.Generators;

public sealed partial class S1InteropTypeRegistryGenerator
{
    private static void GenerateObjectHandle(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("    internal readonly struct S1InteropObject<TTag>");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly object? instance;");
        builder.AppendLine();
        builder.AppendLine("        public S1InteropObject(object? instance)");
        builder.AppendLine("        {");
        builder.AppendLine("            this.instance = instance;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public object? Instance => instance;");
        builder.AppendLine("        public bool HasValue => instance is not null;");
        builder.AppendLine("        public override string ToString() => instance?.ToString() ?? string.Empty;");
        builder.AppendLine("    }");
    }

    private static void GenerateObjectCastRegistry(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("    internal static class S1InteropObjectCast");
        builder.AppendLine("    {");
        builder.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo?> TryCastCache = new System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo?>(System.StringComparer.Ordinal);");
        builder.AppendLine();
        builder.AppendLine("        public static bool Is<T>(object? value, out T? result) where T : class");
        builder.AppendLine("        {");
        builder.AppendLine("            result = As<T>(value);");
        builder.AppendLine("            return result is not null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static T? As<T>(object? value) where T : class");
        builder.AppendLine("        {");
        builder.AppendLine("            if (value is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (value is T typed)");
        builder.AppendLine("            {");
        builder.AppendLine("                return typed;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Type valueType = value.GetType();");
        builder.AppendLine("            if (!IsIl2CppObjectBase(valueType))");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Reflection.MethodInfo? tryCast = ResolveTryCastMethod(valueType, typeof(T));");
        builder.AppendLine("            if (tryCast is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                return tryCast.Invoke(value, null) as T;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MethodInfo? ResolveTryCastMethod(System.Type valueType, System.Type targetType)");
        builder.AppendLine("        {");
        builder.AppendLine("            string cacheKey = (valueType.AssemblyQualifiedName ?? valueType.FullName ?? valueType.Name) + \"|\" + (targetType.AssemblyQualifiedName ?? targetType.FullName ?? targetType.Name);");
        builder.AppendLine("            if (!TryCastCache.TryGetValue(cacheKey, out System.Reflection.MethodInfo? method))");
        builder.AppendLine("            {");
        builder.AppendLine("                method = FindTryCastMethod(valueType, targetType);");
        builder.AppendLine("                TryCastCache[cacheKey] = method;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return method;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MethodInfo? FindTryCastMethod(System.Type valueType, System.Type targetType)");
        builder.AppendLine("        {");
        builder.AppendLine("            for (System.Type? current = valueType; current is not null; current = current.BaseType)");
        builder.AppendLine("            {");
        builder.AppendLine("                foreach (System.Reflection.MethodInfo method in current.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (!method.Name.Equals(\"TryCast\", System.StringComparison.Ordinal) ||");
        builder.AppendLine("                        !method.IsGenericMethodDefinition ||");
        builder.AppendLine("                        method.GetGenericArguments().Length != 1 ||");
        builder.AppendLine("                        method.GetParameters().Length != 0)");
        builder.AppendLine("                    {");
        builder.AppendLine("                        continue;");
        builder.AppendLine("                    }");
        builder.AppendLine();
        builder.AppendLine("                    return method.MakeGenericMethod(targetType);");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool IsIl2CppObjectBase(System.Type valueType)");
        builder.AppendLine("        {");
        builder.AppendLine("            for (System.Type? current = valueType; current is not null; current = current.BaseType)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (string.Equals(current.FullName, \"Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase\", System.StringComparison.Ordinal))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return true;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static string SanitizeLocalName(string value)
    {
        var builder = new StringBuilder();
        foreach (char character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '_');
        }

        return builder.ToString();
    }

    private static void GenerateDelegateBridge(StringBuilder builder)
    {
        builder.AppendLine();
        builder.AppendLine("    internal static class S1InteropDelegateBridge");
        builder.AppendLine("    {");
        builder.AppendLine("        private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo?> ConvertDelegateCache = new System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo?>(System.StringComparer.Ordinal);");
        builder.AppendLine();
        builder.AppendLine("        public static TDelegate? Convert<TDelegate>(TDelegate? listener) where TDelegate : class");
        builder.AppendLine("        {");
        builder.AppendLine("            if (listener is null || !S1InteropRuntime.IsIl2Cpp || listener is not System.Delegate delegateValue)");
        builder.AppendLine("            {");
        builder.AppendLine("                return listener;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            System.Reflection.MethodInfo? convertDelegate = ResolveConvertDelegate(typeof(TDelegate));");
        builder.AppendLine("            if (convertDelegate is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return listener;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                return convertDelegate.Invoke(null, new object[] { delegateValue }) as TDelegate ?? listener;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch");
        builder.AppendLine("            {");
        builder.AppendLine("                return listener;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MethodInfo? ResolveConvertDelegate(System.Type delegateType)");
        builder.AppendLine("        {");
        builder.AppendLine("            string cacheKey = delegateType.AssemblyQualifiedName ?? delegateType.FullName ?? delegateType.Name;");
        builder.AppendLine("            if (!ConvertDelegateCache.TryGetValue(cacheKey, out System.Reflection.MethodInfo? method))");
        builder.AppendLine("            {");
        builder.AppendLine("                method = FindConvertDelegateMethod(delegateType);");
        builder.AppendLine("                ConvertDelegateCache[cacheKey] = method;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return method;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Reflection.MethodInfo? FindConvertDelegateMethod(System.Type delegateType)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Type? delegateSupport = ResolveType(\"Il2CppInterop.Runtime.DelegateSupport\");");
        builder.AppendLine("            if (delegateSupport is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            foreach (System.Reflection.MethodInfo method in delegateSupport.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))");
        builder.AppendLine("            {");
        builder.AppendLine("                if (!method.Name.Equals(\"ConvertDelegate\", System.StringComparison.Ordinal) ||");
        builder.AppendLine("                    !method.IsGenericMethodDefinition ||");
        builder.AppendLine("                    method.GetGenericArguments().Length != 1 ||");
        builder.AppendLine("                    method.GetParameters().Length != 1)");
        builder.AppendLine("                {");
        builder.AppendLine("                    continue;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                return method.MakeGenericMethod(delegateType);");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static System.Type? ResolveType(string typeName)");
        builder.AppendLine("        {");
        builder.AppendLine("            System.Type? type = System.Type.GetType(typeName, throwOnError: false);");
        builder.AppendLine("            if (type is not null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return type;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            foreach (System.Reflection.Assembly assembly in System.AppDomain.CurrentDomain.GetAssemblies())");
        builder.AppendLine("            {");
        builder.AppendLine("                type = assembly.GetType(typeName, throwOnError: false);");
        builder.AppendLine("                if (type is not null)");
        builder.AppendLine("                {");
        builder.AppendLine("                    return type;");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return null;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }
}
