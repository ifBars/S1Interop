using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

/// <summary>
/// Emits the helper that lowered casts, <c>as</c>, and <c>is</c> call. Written in C# 7.3 so it compiles under
/// any language version a mod targets.
/// </summary>
internal static class NativeCastHelperSource
{
    public const string TypeName = "global::S1Interop.Compiler.Generated.S1InteropNativeCast";
    public const string FilePath = "S1Interop.Compiler/S1InteropNativeCast.g.cs";

    private const string Body = """
        namespace S1Interop.Compiler.Generated
        {
            public interface IS1InteropNativeView
            {
                global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase NativeObject { get; }
            }
            public static class S1InteropNativeCast
            {
                public static bool Same(object left, object right)
                {
                    if (object.ReferenceEquals(left, right)) return true;
                    var nativeLeft = left as global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase ?? (left as IS1InteropNativeView)?.NativeObject;
                    var nativeRight = right as global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase ?? (right as IS1InteropNativeView)?.NativeObject;
                    return !(nativeLeft is null) && !(nativeRight is null) && nativeLeft.Pointer == nativeRight.Pointer;
                }

                public static T TryCast<T>(object value) where T : global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
                {
                    if (value is null) return null;
                    // A proxy already of type T (including CLR mod subclasses) keeps its identity.
                    if (value is T managed) return managed;
                    var native = value as global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase ??
                        (value as IS1InteropNativeView)?.NativeObject;
                    return native is null ? null : native.TryCast<T>();
                }

                public static T Cast<T>(object value) where T : global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
                {
                    if (value is null) return null;
                    T result = TryCast<T>(value);
                    if (result is null)
                    {
                        throw new global::System.InvalidCastException(
                            "Unable to cast object of type '" + value.GetType().FullName + "' to type '" + typeof(T).FullName + "'.");
                    }

                    return result;
                }

                public static bool Is<T>(object value) where T : global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase =>
                    !(TryCast<T>(value) is null);

                public static bool TryValue<T>(object value, out T result)
                {
                    if (value is T typed) { result = typed; return true; }
                    result = default(T);
                    if (value is null) return default(T) is null;
                    var convert = ValueCaster<T>.Native;
                    if (convert is null) return false;
                    result = convert(value);
                    return !(result is null);
                }

                private static class ValueCaster<T>
                {
                    internal static readonly global::System.Func<object, T> Native = Create();
                    private static global::System.Func<object, T> Create()
                    {
                        if (!typeof(global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase).IsAssignableFrom(typeof(T))) return null;
                        return (global::System.Func<object, T>)typeof(S1InteropNativeCast).GetMethod(nameof(TryCast))
                            .MakeGenericMethod(typeof(T)).CreateDelegate(typeof(global::System.Func<object, T>));
                    }
                }

                public static global::System.Collections.Generic.IEnumerable<T> CastSequence<T>(global::System.Collections.IEnumerable source)
                    where T : global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
                {
                    if (source is null) throw new global::System.ArgumentNullException(nameof(source));
                    return CastIterator<T>(source);
                }

                private static global::System.Collections.Generic.IEnumerable<T> CastIterator<T>(global::System.Collections.IEnumerable source)
                    where T : global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
                {
                    foreach (object value in source) yield return Cast<T>(value);
                }

                public static global::System.Collections.Generic.IEnumerable<T> OfTypeSequence<T>(global::System.Collections.IEnumerable source)
                    where T : global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
                {
                    if (source is null) throw new global::System.ArgumentNullException(nameof(source));
                    return OfTypeIterator<T>(source);
                }

                private static global::System.Collections.Generic.IEnumerable<T> OfTypeIterator<T>(global::System.Collections.IEnumerable source)
                    where T : global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
                {
                    foreach (object value in source)
                    {
                        T result = TryCast<T>(value);
                        if (!(result is null)) yield return result;
                    }
                }
            }
        }
        """;

    public static SyntaxTree CreateTree(CSharpParseOptions options, CancellationToken cancellationToken)
    {
        string nullable = options.LanguageVersion >= LanguageVersion.CSharp8 ? "#nullable disable\n" : string.Empty;
        return CSharpSyntaxTree.ParseText(
            "// <auto-generated/>\n" + nullable + Body,
            options,
            FilePath,
            System.Text.Encoding.UTF8,
            cancellationToken);
    }
}
