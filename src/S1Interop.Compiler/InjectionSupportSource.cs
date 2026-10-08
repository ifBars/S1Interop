using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

/// <summary>
/// Maintains native class metadata for injected components using Il2CppInterop's version-specific class accessor, and
/// provides the lazy native field storage that lowered serialized scalar fields use.
/// </summary>
/// <remarks>
/// Stock <c>ClassInjector</c> leaves <c>HasReferences</c> false, which makes Unity allocate injected objects from the
/// pointer-free GC pool; a string held by a native field would then be collected while its component is alive. The
/// loader does not inherit the flag, so every injected class sets it on its own native class right after registration
/// and before any instance is allocated.
/// </remarks>
internal static class InjectionSupportSource
{
    public const string Helper = "global::S1Interop.Compiler.Generated.S1InteropInjection";

    public static SyntaxTree CreateTree(CSharpParseOptions options, MetadataSymbolMap map, CancellationToken token)
    {
        // The fake runtime used by portable contract tests has no native class accessor; every native branch is omitted there.
        bool native = map.FindTargetType("Il2CppInterop.Runtime.Runtime.UnityVersionHandler") is not null;
        string flags = !native ? "" : """
            var pointer = global::Il2CppInterop.Runtime.Il2CppClassPointerStore<T>.NativeClassPtr;
            if (pointer == global::System.IntPtr.Zero) throw new global::System.InvalidOperationException("Abstract component registration produced no native class.");
            var native = global::Il2CppInterop.Runtime.Runtime.UnityVersionHandler.Wrap(
                (global::Il2CppInterop.Runtime.Runtime.Il2CppClass*)pointer);
            native.Flags |= global::Il2CppInterop.Runtime.Runtime.Il2CppClassAttributes.TYPE_ATTRIBUTE_ABSTRACT;
            """;
        string register = !native ? "global::Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<T>();" : """
            var pointer = global::Il2CppInterop.Runtime.Il2CppClassPointerStore<T>.NativeClassPtr;
            if (pointer == global::System.IntPtr.Zero)
            {
                global::Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<T>();
                pointer = global::Il2CppInterop.Runtime.Il2CppClassPointerStore<T>.NativeClassPtr;
            }
            if (pointer == global::System.IntPtr.Zero) throw new global::System.InvalidOperationException("Component registration produced no native class for " + typeof(T).FullName + ".");
            PrepareNative(typeof(T), pointer);
            """;
        string prepare = !native ? "" : """
            private static unsafe void PrepareNative(global::System.Type type, global::System.IntPtr pointer) {
                object native = global::Il2CppInterop.Runtime.Runtime.UnityVersionHandler.Wrap(
                    (global::Il2CppInterop.Runtime.Runtime.Il2CppClass*)pointer);
                var references = native.GetType().GetProperty("HasReferences",
                    global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public);
                if (references == null || references.PropertyType != typeof(bool) || !references.CanRead || !references.CanWrite ||
                    references.GetIndexParameters().Length != 0)
                    throw new global::System.NotSupportedException("The installed Il2CppInterop native class accessor " + native.GetType().FullName +
                        " has no readable and writable bool HasReferences property, so strings stored in injected fields would not be visible to the IL2CPP garbage collector.");
                references.SetValue(native, true);
                if (!(bool)references.GetValue(native))
                    throw new global::System.InvalidOperationException("HasReferences did not stick on the native class of " + type.FullName + ".");
                ValidateLayout(type, pointer, native);
            }
            private static void ValidateLayout(global::System.Type type, global::System.IntPtr pointer, object native) {
                bool hasNativeFields = false;
                foreach (var field in type.GetFields(global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public |
                    global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.DeclaredOnly)) {
                    var definition = field.FieldType.IsGenericType ? field.FieldType.GetGenericTypeDefinition() : field.FieldType;
                    bool isString = definition.FullName == "Il2CppInterop.Runtime.InteropTypes.Fields.Il2CppStringField";
                    bool isReference = definition.FullName == "Il2CppInterop.Runtime.InteropTypes.Fields.Il2CppReferenceField`1";
                    if (!isString && !isReference && definition.FullName != "Il2CppInterop.Runtime.InteropTypes.Fields.Il2CppValueField`1") continue;
                    hasNativeFields = true;
                    if (!isString && !isReference) continue;
                    // The GC scans injected objects conservatively only when pointer slots are word aligned.
                    var handle = global::Il2CppInterop.Runtime.IL2CPP.GetIl2CppField(pointer, field.Name);
                    if (handle == global::System.IntPtr.Zero)
                        throw new global::System.InvalidOperationException("Native class of " + type.FullName + " has no field '" + field.Name + "'.");
                    long offset = (long)global::Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(handle);
                    if (offset % 8 != 0)
                        throw new global::System.InvalidOperationException("Native reference field '" + field.Name + "' of " + type.FullName + " is at unaligned offset " + offset + ".");
                }
                if (!hasNativeFields) return;
                var size = native.GetType().GetProperty("InstanceSize", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public);
                if (size != null && size.CanRead && size.GetIndexParameters().Length == 0) {
                    long value = global::System.Convert.ToInt64(size.GetValue(native));
                    if (value % 8 != 0)
                        throw new global::System.InvalidOperationException("Native instance size " + value + " of " + type.FullName + " is not word aligned.");
                }
            }
            """;
        string referenceWrite = !native ? "" : """
            if (typeof(TValue) == typeof(string) || typeof(global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).IsAssignableFrom(typeof(TValue))) {
                var pointer = global::Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtrNotNull(owner);
                var field = global::Il2CppInterop.Runtime.IL2CPP.GetIl2CppField(owner.ObjectClass, name);
                var address = pointer + checked((int)global::Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_offset(field));
                var reference = typeof(TValue) == typeof(string)
                    ? global::Il2CppInterop.Runtime.IL2CPP.ManagedStringToIl2Cpp((string)(object)value)
                    : global::Il2CppInterop.Runtime.IL2CPP.Il2CppObjectBaseToPtr(
                        (global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)(object)value);
                global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gc_wbarrier_set_field(pointer, address, reference);
                global::System.GC.KeepAlive(value);
                global::System.GC.KeepAlive(owner);
                return;
            }
            """;
        return CSharpSyntaxTree.ParseText($$"""
            namespace S1Interop.Compiler.Generated {
                [global::System.AttributeUsage(global::System.AttributeTargets.Property, Inherited = false)]
                public sealed class S1InteropFieldAccessorAttribute : global::System.Attribute {
                    public S1InteropFieldAccessorAttribute(string fieldName) { FieldName = fieldName; }
                    public string FieldName { get; }
                }
                public static class S1InteropInjection {
                    private static class WeakOwnership {
                        internal static readonly global::System.Reflection.FieldInfo Handle = Resolve();
                        private static global::System.Reflection.FieldInfo Resolve() {
                            var field = typeof(global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).GetField(
                                "myGcHandle", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic);
                            if (field == null || field.FieldType != typeof({{NativeDelegateCacheSource.ResolveHandleType(map)}}))
                                throw new global::System.NotSupportedException("The installed Il2CppInterop GC handle layout cannot support weak adapter ownership.");
                            return field;
                        }
                    }
                    private static class ComponentState<T> { internal static bool Prepared; }
                    private static class FieldAccess<TField, TValue> where TField : class {
                        internal static readonly global::System.Reflection.ConstructorInfo Constructor = ResolveConstructor();
                        internal static readonly global::System.Action<TField, TValue> Writer = ResolveWriter();
                        private static global::System.Reflection.ConstructorInfo ResolveConstructor() {
                            var constructor = typeof(TField).GetConstructor(
                                global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.NonPublic,
                                null, new[] { typeof(global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase), typeof(string) }, null);
                            if (constructor == null)
                                throw new global::System.NotSupportedException("The installed Il2CppInterop field wrapper " + typeof(TField) + " has no (Il2CppObjectBase, string) constructor.");
                            return constructor;
                        }
                        private static global::System.Action<TField, TValue> ResolveWriter() {
                            var property = typeof(TField).GetProperty("Value", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.Public);
                            var setter = property == null ? null : property.GetSetMethod();
                            if (setter == null || property.PropertyType != typeof(TValue) || setter.GetParameters().Length != 1)
                                throw new global::System.NotSupportedException("The installed Il2CppInterop field wrapper " + typeof(TField) + " has no public settable " + typeof(TValue) + " Value property.");
                            return (global::System.Action<TField, TValue>)global::System.Delegate.CreateDelegate(typeof(global::System.Action<TField, TValue>), setter);
                        }
                    }
                    public static void ValidateWeakOwnership() { var handle = WeakOwnership.Handle; }
                    public static void WeakenNativeReference(
                        global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase adapter,
                        global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase strongOwner) {
                        if (global::System.Object.ReferenceEquals(adapter, strongOwner) || adapter.Pointer != strongOwner.Pointer)
                            throw new global::System.ArgumentException("A separate strong wrapper must retain the same native object.", nameof(strongOwner));
                        var field = WeakOwnership.Handle;
                        var previous = ({{NativeDelegateCacheSource.ResolveHandleType(map)}})field.GetValue(adapter);
                        var weak = global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_new_weakref(adapter.Pointer, false);
                        if (weak == default({{NativeDelegateCacheSource.ResolveHandleType(map)}}))
                            throw new global::System.InvalidOperationException("Native weak ownership handle allocation failed.");
                        try { field.SetValue(adapter, weak); }
                        catch { global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_free(weak); throw; }
                        global::Il2CppInterop.Runtime.IL2CPP.il2cpp_gchandle_free(previous);
                        global::System.GC.KeepAlive(strongOwner);
                    }
                    // Returns the native field wrapper for a lowered serialized scalar, creating it on first use and
                    // copying the authored (staged) value into native storage exactly once.
                    public static TField EnsureField<TField, TValue>(
                        global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase owner, ref TField wrapper, ref bool ready, ref TValue staged, string name)
                        where TField : class {
                        if (owner == null) throw new global::System.ArgumentNullException(nameof(owner));
                        if (wrapper == null) {
                            try { wrapper = (TField)FieldAccess<TField, TValue>.Constructor.Invoke(new object[] { owner, name }); }
                            catch (global::System.Reflection.TargetInvocationException exception) when (exception.InnerException != null) {
                                global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                                throw;
                            }
                        }
                        if (!ready) {
                            SetField<TField, TValue>(owner, wrapper, name, staged);
                            ready = true;
                            staged = default;
                        }
                        return wrapper;
                    }
                    public static void SetField<TField, TValue>(
                        global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase owner, TField wrapper, string name, TValue value)
                        where TField : class {
                        {{referenceWrite}}
                        FieldAccess<TField, TValue>.Writer(wrapper, value);
                    }
                    public static unsafe void RegisterAbstract<T>() where T : class {
                        if (ComponentState<T>.Prepared) return;
                        ComponentState<T>.Prepared = true;
                        try {
                            global::Il2CppInterop.Runtime.Injection.ClassInjector.RegisterTypeInIl2Cpp<T>();
                            {{flags}}
                            {{(native ? "PrepareNative(typeof(T), pointer);" : "")}}
                        } catch { ComponentState<T>.Prepared = false; throw; }
                    }
                    public static unsafe void RegisterComponent<T>() where T : class {
                        if (ComponentState<T>.Prepared) return;
                        ComponentState<T>.Prepared = true;
                        try {
                            {{register}}
                        } catch { ComponentState<T>.Prepared = false; throw; }
                    }
                    {{prepare}}
                }
            }
            """, options, "S1Interop.Compiler.Injection.g.cs", System.Text.Encoding.UTF8, token);
    }
}
