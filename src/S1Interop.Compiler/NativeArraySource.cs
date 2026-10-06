using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

/// <summary>
/// Emits the helpers lowered scalar arrays call. Arrays connected to a native slot are real
/// <c>Il2CppStructArray&lt;T&gt;</c> instances: fresh arrays allocate native storage and every operation reads or writes it,
/// so aliases are shared with the game. Written in C# 7.3 so it compiles under any language version a mod targets.
/// </summary>
internal static class NativeArraySource
{
    public const string TypeName = "global::S1Interop.Compiler.Generated.S1InteropArray";
    public const string RefTypeName = "global::S1Interop.Compiler.Generated.S1InteropArrayRef";
    public const string StructArrayMetadataName = "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1";
    public const string StructArrayName = "global::Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray";
    public const string FilePath = "S1Interop.Compiler/S1InteropArray.g.cs";

    public static bool IsSupportedArray(ITypeSymbol? type) => type is IArrayTypeSymbol { IsSZArray: true } array &&
        array.ElementType.SpecialType is SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or
            SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
            SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or
            SpecialType.System_Char;

    // Parameter names mirror System.Array so named arguments in author source keep binding after lowering.
    private const string Body = """
        namespace S1Interop.Compiler.Generated
        {
            using NA = global::Il2CppInterop.Runtime.InteropTypes.Arrays;

            /// <summary>A CLR or IL2CPP scalar array, so mixed copy operands keep the managed exception contract.</summary>
            public readonly struct S1InteropArrayRef<T> where T : unmanaged
            {
                private readonly T[] managed;
                private readonly NA.Il2CppStructArray<T> native;

                private S1InteropArrayRef(T[] managed, NA.Il2CppStructArray<T> native)
                {
                    this.managed = managed;
                    this.native = native;
                }

                public bool IsNull { get { return managed is null && native is null; } }

                public int Length { get { return managed is null ? native.Length : managed.Length; } }

                public global::System.Span<T> Span { get { return managed is null ? native.AsSpan() : new global::System.Span<T>(managed); } }

                internal void KeepAlive() { global::System.GC.KeepAlive(native); }

                public static implicit operator S1InteropArrayRef<T>(T[] array) { return new S1InteropArrayRef<T>(array, null); }

                public static implicit operator S1InteropArrayRef<T>(NA.Il2CppStructArray<T> array) { return new S1InteropArrayRef<T>(null, array); }
            }

            public static class S1InteropArray
            {
                private const int MaxLength = 0x7FFFFFC7;
                private const string Huge = "Arrays larger than 2GB are not supported.";
                private const string NonNegative = "Non-negative number required.";

                public static NA.Il2CppStructArray<T> Create<T>(int length) where T : unmanaged { return Create<T>((long)length); }

                public static NA.Il2CppStructArray<T> Create<T>(uint length) where T : unmanaged { return Create<T>((long)length); }

                public static NA.Il2CppStructArray<T> Create<T>(ulong length) where T : unmanaged
                {
                    if (length > MaxLength) throw new global::System.OutOfMemoryException("Array dimensions exceeded supported range.");
                    return Create<T>((long)length);
                }

                public static NA.Il2CppStructArray<T> Create<T>(long length) where T : unmanaged
                {
                    if (length < 0) throw new global::System.OverflowException("Arithmetic operation resulted in an overflow.");
                    if (length > MaxLength) throw new global::System.OutOfMemoryException("Array dimensions exceeded supported range.");
                    return new NA.Il2CppStructArray<T>(length);
                }

                // The params array is a compiler-created temporary that nothing else references, so copying it keeps no alias apart.
                public static NA.Il2CppStructArray<T> Of<T>(params T[] values) where T : unmanaged
                {
                    var array = Create<T>((long)values.Length);
                    new global::System.Span<T>(values).CopyTo(array.AsSpan());
                    return array;
                }

                public static string Utf8String(global::System.Text.Encoding encoding, NA.Il2CppStructArray<byte> bytes)
                {
                    if (bytes is null) throw new global::System.ArgumentNullException("bytes");
                    return Utf8String(encoding, bytes, 0, bytes.Length);
                }

                public static string ToBase64String(NA.Il2CppStructArray<byte> inArray) =>
                    ToBase64String(inArray, global::System.Base64FormattingOptions.None);

                public static string ToBase64String(NA.Il2CppStructArray<byte> inArray, global::System.Base64FormattingOptions options)
                {
                    if (inArray is null) throw new global::System.ArgumentNullException("inArray");
                    return ToBase64String(inArray, 0, inArray.Length, options);
                }

                public static string ToBase64String(NA.Il2CppStructArray<byte> inArray, int offset, int length) =>
                    ToBase64String(inArray, offset, length, global::System.Base64FormattingOptions.None);

                public static string ToBase64String(NA.Il2CppStructArray<byte> inArray, int offset, int length, global::System.Base64FormattingOptions options)
                {
                    if (inArray is null) throw new global::System.ArgumentNullException("inArray");
                    if (length < 0) throw new global::System.ArgumentOutOfRangeException("length");
                    if (offset < 0 || offset > inArray.Length - length) throw new global::System.ArgumentOutOfRangeException("offset");
                    try { return global::System.Convert.ToBase64String(inArray.AsSpan().Slice(offset, length), options); }
                    finally { global::System.GC.KeepAlive(inArray); }
                }

                public static string Utf8String(global::System.Text.Encoding encoding, NA.Il2CppStructArray<byte> bytes, int index, int count)
                {
                    if (bytes is null) throw new global::System.ArgumentNullException("bytes");
                    if (index < 0) throw new global::System.ArgumentOutOfRangeException("index");
                    if (count < 0) throw new global::System.ArgumentOutOfRangeException("count");
                    if (bytes.Length - index < count) throw new global::System.ArgumentOutOfRangeException("bytes");
                    // The compiler admits the built-in Encoding.UTF8 receiver only. A custom
                    // Encoding can override its array and span overloads with different behavior.
                    try { return encoding.GetString(bytes.AsSpan().Slice(index, count)); }
                    finally { global::System.GC.KeepAlive(bytes); }
                }

                // Maps every CLR index type onto the int indexer; anything outside int range is out of bounds on a CLR array too.
                public static int Index(uint index) { return index > int.MaxValue ? -1 : (int)index; }

                public static int Index(long index) { return index < 0 || index > int.MaxValue ? -1 : (int)index; }

                public static int Index(ulong index)
                {
                    if (index > long.MaxValue) throw new global::System.OverflowException("Arithmetic operation resulted in an overflow.");
                    return index > int.MaxValue ? -1 : (int)index;
                }

                public static long LongLength<T>(NA.Il2CppStructArray<T> array) where T : unmanaged
                {
                    Require(array);
                    return array.Length;
                }

                public static bool InstanceEquals(object left, object right)
                {
                    Require(left);
                    return IsNativeArray(left) ? S1InteropNativeCast.Same(left, right) : left.Equals(right);
                }

                public static bool ObjectEquals(object left, object right)
                {
                    return object.ReferenceEquals(left, right) || (!(left is null) && !(right is null) && InstanceEquals(left, right));
                }

                public static int IdentityHash(object value)
                {
                    Require(value);
                    return IsNativeArray(value)
                        ? ((global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)value).Pointer.GetHashCode()
                        : value.GetHashCode();
                }

                // Reference arrays are matched by name: a target without Il2CppReferenceArray must still compile these helpers.
                private static bool IsNativeArray(object value)
                {
                    for (var type = value.GetType(); !(type is null); type = type.BaseType)
                    {
                        if (!type.IsGenericType) continue;
                        var definition = type.GetGenericTypeDefinition();
                        if (definition == typeof(NA.Il2CppStructArray<>) ||
                            definition.FullName == "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1") return true;
                    }
                    return false;
                }

                public static int Rank<T>(NA.Il2CppStructArray<T> array) where T : unmanaged
                {
                    Require(array);
                    return 1;
                }

                public static int GetLength<T>(NA.Il2CppStructArray<T> array, int dimension) where T : unmanaged
                {
                    Require(array);
                    CheckDimension(dimension);
                    return array.Length;
                }

                public static int GetLowerBound<T>(NA.Il2CppStructArray<T> array, int dimension) where T : unmanaged
                {
                    Require(array);
                    CheckDimension(dimension);
                    return 0;
                }

                public static int GetUpperBound<T>(NA.Il2CppStructArray<T> array, int dimension) where T : unmanaged
                {
                    Require(array);
                    CheckDimension(dimension);
                    return array.Length - 1;
                }

                public static object Clone<T>(NA.Il2CppStructArray<T> array) where T : unmanaged
                {
                    Require(array);
                    var copy = Create<T>((long)array.Length);
                    array.AsSpan().CopyTo(copy.AsSpan());
                    global::System.GC.KeepAlive(array);
                    return copy;
                }

                public static void Clear<T>(NA.Il2CppStructArray<T> array) where T : unmanaged
                {
                    if (array is null) throw new global::System.ArgumentNullException("array");
                    array.AsSpan().Clear();
                    global::System.GC.KeepAlive(array);
                }

                public static void Clear<T>(NA.Il2CppStructArray<T> array, int index, int length) where T : unmanaged
                {
                    if (array is null) throw new global::System.ArgumentNullException("array");
                    if (index < 0 || length < 0 || (uint)(index + length) > (uint)array.Length) throw new global::System.IndexOutOfRangeException();
                    array.AsSpan().Slice(index, length).Clear();
                    global::System.GC.KeepAlive(array);
                }

                public static void Resize<T>(ref NA.Il2CppStructArray<T> array, int newSize) where T : unmanaged
                {
                    if (newSize < 0) throw new global::System.ArgumentOutOfRangeException("newSize", NonNegative);
                    var current = array;
                    if (current is null)
                    {
                        array = Create<T>((long)newSize);
                        return;
                    }

                    // Like the CLR, an equal size keeps the same array so existing aliases stay attached.
                    if (current.Length == newSize) return;
                    var resized = Create<T>((long)newSize);
                    current.AsSpan().Slice(0, global::System.Math.Min(current.Length, newSize)).CopyTo(resized.AsSpan());
                    global::System.GC.KeepAlive(current);
                    array = resized;
                }

                // Check order follows Array.Copy; Span.CopyTo is overlap-safe, so same-array copies behave as if buffered.
                public static void Copy<T>(S1InteropArrayRef<T> sourceArray, int sourceIndex, S1InteropArrayRef<T> destinationArray, int destinationIndex, int length) where T : unmanaged
                {
                    if (sourceArray.IsNull) throw new global::System.ArgumentNullException("sourceArray");
                    if (destinationArray.IsNull) throw new global::System.ArgumentNullException("destinationArray");
                    if (length < 0) throw new global::System.ArgumentOutOfRangeException("length", NonNegative);
                    if (sourceIndex < 0) throw new global::System.ArgumentOutOfRangeException("sourceIndex", "Number was less than the array's lower bounds.");
                    if (destinationIndex < 0) throw new global::System.ArgumentOutOfRangeException("destinationIndex", "Number was less than the array's lower bounds.");
                    if (length > sourceArray.Length - sourceIndex)
                        throw new global::System.ArgumentException("Source array was not long enough. Check the source index, length, and the array's lower bounds.", "sourceArray");
                    if (length > destinationArray.Length - destinationIndex)
                        throw new global::System.ArgumentException("Destination array was not long enough. Check the destination index, length, and the array's lower bounds.", "destinationArray");
                    sourceArray.Span.Slice(sourceIndex, length).CopyTo(destinationArray.Span.Slice(destinationIndex, length));
                    sourceArray.KeepAlive();
                    destinationArray.KeepAlive();
                }

                public static void Copy<T>(S1InteropArrayRef<T> sourceArray, S1InteropArrayRef<T> destinationArray, int length) where T : unmanaged
                {
                    Copy<T>(sourceArray, 0, destinationArray, 0, length);
                }

                public static void Copy<T>(S1InteropArrayRef<T> sourceArray, long sourceIndex, S1InteropArrayRef<T> destinationArray, long destinationIndex, long length) where T : unmanaged
                {
                    int source = (int)sourceIndex;
                    int destination = (int)destinationIndex;
                    int count = (int)length;
                    if (sourceIndex != source) throw new global::System.ArgumentOutOfRangeException("sourceIndex", Huge);
                    if (destinationIndex != destination) throw new global::System.ArgumentOutOfRangeException("destinationIndex", Huge);
                    if (length != count) throw new global::System.ArgumentOutOfRangeException("length", Huge);
                    Copy<T>(sourceArray, source, destinationArray, destination, count);
                }

                public static void Copy<T>(S1InteropArrayRef<T> sourceArray, S1InteropArrayRef<T> destinationArray, long length) where T : unmanaged
                {
                    int count = (int)length;
                    if (length != count) throw new global::System.ArgumentOutOfRangeException("length", Huge);
                    Copy<T>(sourceArray, destinationArray, count);
                }

                public static void CopyTo<T>(NA.Il2CppStructArray<T> source, S1InteropArrayRef<T> array, int index) where T : unmanaged
                {
                    Require(source);
                    Copy<T>(source, 0, array, index, source.Length);
                }

                public static void CopyTo<T>(NA.Il2CppStructArray<T> source, S1InteropArrayRef<T> array, long index) where T : unmanaged
                {
                    Require(source);
                    int narrowed = (int)index;
                    if (index != narrowed) throw new global::System.ArgumentOutOfRangeException("index", Huge);
                    CopyTo<T>(source, array, narrowed);
                }

                private static void Require(object array)
                {
                    if (array is null) throw new global::System.NullReferenceException();
                }

                private static void CheckDimension(int dimension)
                {
                    if (dimension != 0) throw new global::System.IndexOutOfRangeException("Array does not have that many dimensions.");
                }
            }
        }
        """;

    public static SyntaxTree CreateTree(CSharpParseOptions options, CancellationToken token)
    {
        string nullable = options.LanguageVersion >= LanguageVersion.CSharp8 ? "#nullable disable\n" : string.Empty;
        return CSharpSyntaxTree.ParseText("// <auto-generated/>\n" + nullable + Body, options, FilePath,
            System.Text.Encoding.UTF8, token);
    }

    /// <summary>
    /// Gets whether the target exposes the <c>Il2CppStructArray&lt;T&gt;</c> members the helpers call. Without them arrays keep
    /// today's behavior: no lowering, and the post-binding guard rejects snapshot conversions.
    /// </summary>
    public static bool HasSurface(MetadataSymbolMap map)
    {
        if (map.NativeObjectBase is null || map.FindTargetType(StructArrayMetadataName) is not { Arity: 1 } array) return false;
        ITypeParameterSymbol element = array.TypeParameters[0];
        ISymbol[] members = Chain(array).SelectMany(type => type.GetMembers()).ToArray();
        return (element.HasUnmanagedTypeConstraint || element.HasValueTypeConstraint) &&
            array.InstanceConstructors.Any(constructor => constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters is [{ Type.SpecialType: SpecialType.System_Int64 }]) &&
            members.OfType<IMethodSymbol>().Any(method => method is { Name: "AsSpan", IsStatic: false, Parameters.Length: 0,
                ReturnType: INamedTypeSymbol { Name: "Span", Arity: 1 } }) &&
            members.OfType<IPropertySymbol>().Any(property => property is { Name: "Length", Type.SpecialType: SpecialType.System_Int32 }) &&
            members.OfType<IPropertySymbol>().Any(property => property.IsIndexer &&
                property.Parameters is [{ Type.SpecialType: SpecialType.System_Int32 }]);
    }

    private static IEnumerable<INamedTypeSymbol> Chain(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType) yield return current;
    }
}
