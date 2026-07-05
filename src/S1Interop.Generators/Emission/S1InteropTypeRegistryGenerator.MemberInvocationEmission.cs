using System.Text;

namespace S1Interop.Generators;

public sealed partial class S1InteropTypeRegistryGenerator
{
    private static void GenerateMemberInvocationHelpers(StringBuilder builder)
    {
        builder.AppendLine("        public static object? Invoke(string ownerTypeName, string memberName, string[]? parameterTypeNames, object? instance, params object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            return Invoke(ResolveMethod(ownerTypeName, memberName, parameterTypeNames), instance, args);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static object? InvokeInstance(object? instance, string memberName, params object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            return InvokeInstance(instance, memberName, null, args);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public static object? InvokeInstance(object? instance, string memberName, string[]? parameterTypeNames, params object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (instance is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return Invoke(ResolveMemberCached(instance.GetType(), memberName, parameterTypeNames, S1InteropMemberKind.Method) as System.Reflection.MethodInfo, instance, args);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static object? Invoke(System.Reflection.MethodInfo? method, object? instance, params object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (method is null)");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            if (!TryConvertArguments(method.GetParameters(), args, out object?[] converted))");
        builder.AppendLine("            {");
        builder.AppendLine("                return null;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            object? result = method.Invoke(instance, converted);");
        builder.AppendLine("            CopyByRefArguments(method.GetParameters(), converted, args);");
        builder.AppendLine("            return result;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool TryConvertArguments(System.Reflection.ParameterInfo[] parameters, object?[] args, out object?[] converted)");
        builder.AppendLine("        {");
        builder.AppendLine("            converted = System.Array.Empty<object?>();");
        builder.AppendLine("            if (parameters.Length != args.Length)");
        builder.AppendLine("            {");
        builder.AppendLine("                return false;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            converted = new object?[args.Length];");
        builder.AppendLine("            for (int index = 0; index < args.Length; index++)");
        builder.AppendLine("            {");
        builder.AppendLine("                System.Type parameterType = parameters[index].ParameterType;");
        builder.AppendLine("                System.Type conversionType = parameterType.IsByRef && parameterType.GetElementType() is System.Type elementType");
        builder.AppendLine("                    ? elementType");
        builder.AppendLine("                    : parameterType;");
        builder.AppendLine("                if (!TryConvertValue(args[index], conversionType, out object? convertedValue))");
        builder.AppendLine("                {");
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                converted[index] = convertedValue;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return true;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static void CopyByRefArguments(System.Reflection.ParameterInfo[] parameters, object?[] converted, object?[] args)");
        builder.AppendLine("        {");
        builder.AppendLine("            int count = System.Math.Min(System.Math.Min(parameters.Length, converted.Length), args.Length);");
        builder.AppendLine("            for (int index = 0; index < count; index++)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (parameters[index].ParameterType.IsByRef)");
        builder.AppendLine("                {");
        builder.AppendLine("                    args[index] = ConvertBackValue(args[index], converted[index]);");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }
}