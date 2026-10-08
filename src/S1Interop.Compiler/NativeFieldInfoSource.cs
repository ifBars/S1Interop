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
                private global::System.Type reflectedType;
                private global::System.Func<object, object> readValue;
                private global::System.Func<object, object> writeValue;
                private S1InteropNativeFieldInfo(global::System.Reflection.PropertyInfo property,
                    global::System.Reflection.FieldAttributes attributes) {
                    this.property = property;
                    this.attributes = attributes;
                    reflectedType = property.DeclaringType;
                }
                public static global::System.Reflection.FieldInfo Create(global::System.Type declaringType,
                    string name, global::System.Reflection.FieldAttributes attributes,
                    global::System.Func<object, object> readValue = null, global::System.Func<object, object> writeValue = null) {
                    var property = declaringType.GetProperty(name, global::System.Reflection.BindingFlags.Public |
                        global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance |
                        global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.DeclaredOnly);
                    if (property == null || property.GetIndexParameters().Length != 0)
                        throw new global::System.MissingFieldException(declaringType.FullName, name);
                    return new S1InteropNativeFieldInfo(property, attributes) { readValue = readValue, writeValue = writeValue };
                }
                public static global::System.Reflection.FieldInfo Lookup(global::System.Type reflectedType,
                    global::System.Type declaringType, string name, global::System.Reflection.FieldAttributes attributes,
                    global::System.Reflection.BindingFlags flags, string requestedName,
                    global::System.Func<object, object> readValue = null, global::System.Func<object, object> writeValue = null) {
                    if (!string.Equals(name, requestedName, (flags & global::System.Reflection.BindingFlags.IgnoreCase) != 0
                        ? global::System.StringComparison.OrdinalIgnoreCase : global::System.StringComparison.Ordinal)) return null;
                    bool inherited = reflectedType != declaringType;
                    var visibility = attributes & global::System.Reflection.FieldAttributes.FieldAccessMask;
                    bool isStatic = (attributes & global::System.Reflection.FieldAttributes.Static) != 0;
                    if (inherited && ((flags & global::System.Reflection.BindingFlags.DeclaredOnly) != 0 ||
                        visibility == global::System.Reflection.FieldAttributes.Private)) return null;
                    if (inherited && isStatic && (flags & global::System.Reflection.BindingFlags.FlattenHierarchy) == 0) return null;
                    if ((flags & (isStatic ? global::System.Reflection.BindingFlags.Static : global::System.Reflection.BindingFlags.Instance)) == 0) return null;
                    if ((flags & (visibility == global::System.Reflection.FieldAttributes.Public
                        ? global::System.Reflection.BindingFlags.Public : global::System.Reflection.BindingFlags.NonPublic)) == 0) return null;
                    var result = (S1InteropNativeFieldInfo)Create(declaringType, name, attributes, readValue, writeValue);
                    result.reflectedType = reflectedType;
                    return result;
                }
                public override string Name => property.Name;
                public override global::System.Type DeclaringType => property.DeclaringType;
                public override global::System.Type ReflectedType => reflectedType;
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
                public static T ReadTraverse<T>(object root, string name, bool typeRoot, global::System.Func<object, object> readValue = null) {
                    if (root == null) return default(T);
                    var wrapperType = typeRoot ? (global::System.Type)root : root.GetType();
                    var nativeRoot = root as global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                    var klass = typeRoot ? global::Il2CppInterop.Runtime.Il2CppClassPointerStore.GetNativeClassPointer(wrapperType)
                        : nativeRoot != null ? global::Il2CppInterop.Runtime.IL2CPP.il2cpp_object_get_class(nativeRoot.Pointer)
                        : global::System.IntPtr.Zero;
                    if (klass == global::System.IntPtr.Zero) throw new global::System.NotSupportedException("Native traversal root has no class metadata.");
                    var field = global::Il2CppInterop.Runtime.IL2CPP.il2cpp_class_get_field_from_name(klass, name);
                    var owner = field == global::System.IntPtr.Zero ? global::System.IntPtr.Zero
                        : global::Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_parent(field);
                    try {
                        for (var candidate = wrapperType; candidate != null && candidate != typeof(object); candidate = candidate.BaseType) {
                            var access = candidate.GetField(name, global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic |
                                global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.DeclaredOnly);
                            if (access == null && owner != global::System.IntPtr.Zero &&
                                global::Il2CppInterop.Runtime.Il2CppClassPointerStore.GetNativeClassPointer(candidate) == owner)
                                access = Create(candidate, name, (global::System.Reflection.FieldAttributes)global::Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_flags(field));
                            if (access == null) continue;
                            if (typeRoot && !access.IsStatic) return default(T);
                            object value = access.GetValue(typeRoot ? null : root);
                            if (value != null && readValue != null) value = readValue(value);
                            return value == null ? default(T) : (T)Rewrap(value, typeof(T));
                        }
                        if (field == global::System.IntPtr.Zero) return default(T);
                        throw new global::System.NotSupportedException("Native traversal field owner has no matching wrapper in the root type hierarchy.");
                    }
                    finally { global::System.GC.KeepAlive(root); }
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
                    var value = property.GetValue(CheckTarget(target), null);
                    return readValue == null ? value : readValue(value);
                }
                public override void SetValue(object target, object value, global::System.Reflection.BindingFlags invokeAttr,
                    global::System.Reflection.Binder binder, global::System.Globalization.CultureInfo culture) {
                    var instance = CheckTarget(target);
                    property.SetValue(instance, Rewrap(writeValue == null ? value : writeValue(value), FieldType), invokeAttr, binder, null, culture);
                }
            }
        }
        """, options, "S1Interop.Compiler/NativeFieldInfo.g.cs", System.Text.Encoding.UTF8, token);
}
