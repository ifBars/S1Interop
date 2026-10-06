using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

internal static class NativeFieldInfoSource
{
    public const string TypeName = "global::S1Interop.Compiler.Generated.S1InteropNativeFieldInfo";

    public static SyntaxTree CreateTree(CSharpParseOptions options, CancellationToken token) => CSharpSyntaxTree.ParseText("""
        namespace S1Interop.Compiler.Generated {
            public sealed class S1InteropNativeFieldInfo : global::System.Reflection.FieldInfo {
                private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type,
                    global::System.Func<object, global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase>> casters = new();
                private readonly global::System.Reflection.PropertyInfo property;
                private readonly global::System.Reflection.FieldAttributes attributes;
                private S1InteropNativeFieldInfo(global::System.Reflection.PropertyInfo property,
                    global::System.Reflection.FieldAttributes attributes) {
                    this.property = property;
                    this.attributes = attributes;
                }
                public static global::System.Reflection.FieldInfo Create(global::System.Type declaringType,
                    string name, global::System.Reflection.FieldAttributes attributes) {
                    var property = declaringType.GetProperty(name, global::System.Reflection.BindingFlags.Public |
                        global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance |
                        global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.DeclaredOnly);
                    if (property == null || property.GetIndexParameters().Length != 0)
                        throw new global::System.MissingFieldException(declaringType.FullName, name);
                    return new S1InteropNativeFieldInfo(property, attributes);
                }
                public override string Name => property.Name;
                public override global::System.Type DeclaringType => property.DeclaringType;
                public override global::System.Type ReflectedType => property.DeclaringType;
                public override global::System.Type FieldType => property.PropertyType;
                public override global::System.Reflection.FieldAttributes Attributes => attributes;
                public override global::System.RuntimeFieldHandle FieldHandle => throw UnsupportedMetadata();
                public override object[] GetCustomAttributes(bool inherit) => throw UnsupportedMetadata();
                public override object[] GetCustomAttributes(global::System.Type type, bool inherit) => throw UnsupportedMetadata();
                public override bool IsDefined(global::System.Type type, bool inherit) => throw UnsupportedMetadata();
                private static global::System.NotSupportedException UnsupportedMetadata() =>
                    new global::System.NotSupportedException("Native field handles and custom-attribute metadata are not yet represented by the source compiler.");
                private static object Rewrap(object value, global::System.Type type) {
                    if (value == null || type.IsInstanceOfType(value) ||
                        !typeof(global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).IsAssignableFrom(type)) return value;
                    var cast = casters.GetOrAdd(type, key =>
                        (global::System.Func<object, global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase>)
                        typeof(S1InteropNativeCast).GetMethod("TryCast").MakeGenericMethod(key).CreateDelegate(
                            typeof(global::System.Func<object, global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase>)));
                    return cast(value) ?? value;
                }
                private object CheckTarget(object target) {
                    if (IsStatic) return null;
                    if (target == null) throw new global::System.Reflection.TargetException("Non-static field requires a target.");
                    target = Rewrap(target, DeclaringType);
                    if (!DeclaringType.IsInstanceOfType(target))
                        throw new global::System.ArgumentException("Object does not match the field's declaring type.");
                    return target;
                }
                public override object GetValue(object target) {
                    return property.GetValue(CheckTarget(target), null);
                }
                public override void SetValue(object target, object value, global::System.Reflection.BindingFlags invokeAttr,
                    global::System.Reflection.Binder binder, global::System.Globalization.CultureInfo culture) {
                    property.SetValue(CheckTarget(target), Rewrap(value, FieldType), invokeAttr, binder, null, culture);
                }
            }
        }
        """, options, "S1Interop.Compiler/NativeFieldInfo.g.cs", System.Text.Encoding.UTF8, token);
}
