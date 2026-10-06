using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace S1Interop.Compiler;

/// <summary>
/// Emits the helpers lowered reference arrays call. Arrays of mapped native reference classes connected to a native slot are
/// real <c>Il2CppReferenceArray&lt;T&gt;</c> instances. Loads and stores go through a checked element adaptor that follows the
/// CLR contract (null receiver, index range, then the array's runtime element class), and every store is performed by the
/// runtime's own <c>Array.SetValue</c> so the native write barrier runs. The surface this needs is gated separately from the
/// scalar helpers in <see cref="NativeArraySource"/>. Written in C# 7.3 so it compiles under any language version a mod targets.
/// </summary>
internal static class NativeReferenceArraySource
{
    public const string TypeName = "global::S1Interop.Compiler.Generated.S1InteropReferenceArray";
    public const string RefTypeName = "global::S1Interop.Compiler.Generated.S1InteropReferenceArrayRef";
    public const string ReferenceArrayMetadataName = "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray`1";
    public const string ReferenceArrayName = "global::Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray";
    public const string FilePath = "S1Interop.Compiler/S1InteropReferenceArray.g.cs";

    private const string NativeArrayMetadataName = "Il2CppSystem.Array";
    private const string NativeObjectMetadataName = "Il2CppSystem.Object";
    private const string RuntimeMetadataName = "Il2CppInterop.Runtime.IL2CPP";

    /// <summary>
    /// Gets whether the type is a rank-1 array of a mapped native reference class: not a source struct, enum, string, interface,
    /// delegate, generic type, or authored (component) class, which have no native array element representation here.
    /// </summary>
    public static bool IsMappedReferenceArray(MetadataSymbolMap map, ITypeSymbol? type) =>
        type is IArrayTypeSymbol { IsSZArray: true, ElementType: INamedTypeSymbol { TypeKind: TypeKind.Class, SpecialType: SpecialType.None,
            IsGenericType: false, IsTupleType: false } element } &&
        !map.IsAuthorType(element) && map.IsNative(element) &&
        map.Resolve(element) is { Status: TypeMappingStatus.Mapped, Target: { TypeKind: TypeKind.Class, IsGenericType: false } };

    // Parameter names mirror System.Array so named arguments in author source keep binding after lowering.
    private const string Body = """
        namespace S1Interop.Compiler.Generated
        {
            using NA = global::Il2CppInterop.Runtime.InteropTypes.Arrays;
            using Base = global::Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
            using Native = global::Il2CppInterop.Runtime.IL2CPP;

            /// <summary>
            /// The checked element adaptor. `Elements(array)[index]` evaluates the receiver, the index, and the assigned value in
            /// source order, and only the indexer accessors touch the array, so compound and null-coalescing assignment work.
            /// </summary>
            public readonly struct S1InteropReferenceElements<T> where T : Base
            {
                private readonly NA.Il2CppReferenceArray<T> array;

                internal S1InteropReferenceElements(NA.Il2CppReferenceArray<T> array) { this.array = array; }

                public int Length { get { return S1InteropReferenceArray.Count(array); } }

                public T this[int index]
                {
                    get { return S1InteropReferenceArray.Load(array, index); }
                    set { S1InteropReferenceArray.Store(array, index, value); }
                }
            }

            /// <summary>A CLR or IL2CPP reference array, so mixed copy operands keep the managed exception contract.</summary>
            public readonly struct S1InteropReferenceArrayRef<T> where T : Base
            {
                private readonly T[] managed;
                private readonly NA.Il2CppReferenceArray<T> native;

                private S1InteropReferenceArrayRef(T[] managed, NA.Il2CppReferenceArray<T> native)
                {
                    this.managed = managed;
                    this.native = native;
                }

                public bool IsNull { get { return managed is null && native is null; } }

                public bool IsManaged { get { return !(managed is null); } }

                public int Length { get { return managed is null ? native.Length : managed.Length; } }

                public T[] Managed { get { return managed; } }

                public NA.Il2CppReferenceArray<T> NativeArray { get { return native; } }

                internal void KeepAlive() { global::System.GC.KeepAlive(native); }

                public static implicit operator S1InteropReferenceArrayRef<T>(T[] array) { return new S1InteropReferenceArrayRef<T>(array, null); }

                public static implicit operator S1InteropReferenceArrayRef<T>(NA.Il2CppReferenceArray<T> array) { return new S1InteropReferenceArrayRef<T>(null, array); }
            }

            public static class S1InteropReferenceArray
            {
                private const int MaxLength = 0x7FFFFFC7;
                private const string Huge = "Arrays larger than 2GB are not supported.";
                private const string NonNegative = "Non-negative number required.";

                public static NA.Il2CppReferenceArray<T> Create<T>(int length) where T : Base { return Create<T>((long)length); }

                public static NA.Il2CppReferenceArray<T> Create<T>(uint length) where T : Base { return Create<T>((long)length); }

                public static NA.Il2CppReferenceArray<T> Create<T>(ulong length) where T : Base
                {
                    if (length > MaxLength) throw new global::System.OutOfMemoryException("Array dimensions exceeded supported range.");
                    return Create<T>((long)length);
                }

                public static NA.Il2CppReferenceArray<T> Create<T>(long length) where T : Base
                {
                    if (length < 0) throw new global::System.OverflowException("Arithmetic operation resulted in an overflow.");
                    if (length > MaxLength) throw new global::System.OutOfMemoryException("Array dimensions exceeded supported range.");
                    return new NA.Il2CppReferenceArray<T>(length);
                }

                // The values array is a compiler-created temporary that nothing else references. The stock Il2CppReferenceArray<T>(T[])
                // constructor writes element slots without a runtime check, so the fresh array is populated through the checked store.
                public static NA.Il2CppReferenceArray<T> Of<T>(T[] values) where T : Base
                {
                    var array = Create<T>((long)values.Length);
                    for (int i = 0; i < values.Length; i++) Store(array, i, values[i]);
                    return array;
                }

                // The adaptor never throws: a CLR array access evaluates its index before it faults on a null array.
                public static S1InteropReferenceElements<T> Elements<T>(NA.Il2CppReferenceArray<T> array) where T : Base
                {
                    return new S1InteropReferenceElements<T>(array);
                }

                public static S1InteropReferenceElements<T>? NullableElements<T>(NA.Il2CppReferenceArray<T> array) where T : Base
                {
                    return array is null ? (S1InteropReferenceElements<T>?)null : Elements(array);
                }

                public static NA.Il2CppReferenceArray<T> FromNative<T>(Base array) where T : Base
                {
                    if (array is null) return null;
                    if (array is NA.Il2CppReferenceArray<T> existing) return existing;
                    var result = new NA.Il2CppReferenceArray<T>(array.Pointer);
                    global::System.GC.KeepAlive(array);
                    return result;
                }

                internal static int Count<T>(NA.Il2CppReferenceArray<T> array) where T : Base
                {
                    if (array is null) throw new global::System.NullReferenceException();
                    return array.Length;
                }

                internal static T Load<T>(NA.Il2CppReferenceArray<T> array, int index) where T : Base
                {
                    CheckIndex(array, index);
                    T value = array[index];
                    global::System.GC.KeepAlive(array);
                    return value;
                }

                internal static void Store<T>(NA.Il2CppReferenceArray<T> array, int index, T value) where T : Base
                {
                    CheckIndex(array, index);
                    // Like stelem.ref, the check uses the array's actual runtime element class, which can be narrower than T.
                    if (!(value is null) && !Native.il2cpp_class_is_assignable_from(ElementClassOf(array), ClassOf(value)))
                        throw new global::System.ArrayTypeMismatchException();
                    Write(array, index, value);
                }

                private static void CheckIndex<T>(NA.Il2CppReferenceArray<T> array, int index) where T : Base
                {
                    if (array is null) throw new global::System.NullReferenceException();
                    if ((uint)index >= (uint)array.Length) throw new global::System.IndexOutOfRangeException();
                }

                private static global::System.IntPtr ClassOf(Base value)
                {
                    return Native.il2cpp_object_get_class(value.Pointer);
                }

                private static global::System.IntPtr ElementClassOf(Base array)
                {
                    global::System.IntPtr element = Native.il2cpp_class_get_element_class(ClassOf(array));
                    if (element == global::System.IntPtr.Zero)
                        throw new global::System.InvalidOperationException("The native array's element class is unavailable.");
                    return element;
                }

                // Array.SetValue performs the reference store through the runtime (including its write barrier); the element slot is
                // never written directly. Every wrapper involved stays alive until the call returns.
                private static void Write(Base array, int index, Base value)
                {
                    var target = new global::Il2CppSystem.Array(array.Pointer);
                    global::Il2CppSystem.Object boxed = value is null ? null : new global::Il2CppSystem.Object(value.Pointer);
                    target.SetValue(boxed, index);
                    global::System.GC.KeepAlive(boxed);
                    global::System.GC.KeepAlive(target);
                    global::System.GC.KeepAlive(value);
                    global::System.GC.KeepAlive(array);
                }

                // A covariant source conversion is a second wrapper over the same native array, so identity and the actual runtime
                // element class are unchanged and no element is copied.
                public static NA.Il2CppReferenceArray<TTarget> Covariant<TTarget, TSource>(NA.Il2CppReferenceArray<TSource> source)
                    where TTarget : Base where TSource : Base
                {
                    if (source is null) return null;
                    var view = new NA.Il2CppReferenceArray<TTarget>(source.Pointer);
                    global::System.GC.KeepAlive(source);
                    return view;
                }

                // Maps every CLR index type onto the int indexer; anything outside int range is out of bounds on a CLR array too.
                public static int Index(uint index) { return index > int.MaxValue ? -1 : (int)index; }

                public static int Index(long index) { return index < 0 || index > int.MaxValue ? -1 : (int)index; }

                public static int Index(ulong index)
                {
                    if (index > long.MaxValue) throw new global::System.OverflowException("Arithmetic operation resulted in an overflow.");
                    return index > int.MaxValue ? -1 : (int)index;
                }

                public static long LongLength<T>(NA.Il2CppReferenceArray<T> array) where T : Base
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
                    return IsNativeArray(value) ? ((Base)value).Pointer.GetHashCode() : value.GetHashCode();
                }

                private static bool IsNativeArray(object value)
                {
                    for (var type = value.GetType(); !(type is null); type = type.BaseType)
                    {
                        if (!type.IsGenericType) continue;
                        var definition = type.GetGenericTypeDefinition();
                        if (definition == typeof(NA.Il2CppReferenceArray<>) ||
                            definition.FullName == "Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray`1") return true;
                    }
                    return false;
                }

                public static int Rank<T>(NA.Il2CppReferenceArray<T> array) where T : Base
                {
                    Require(array);
                    return 1;
                }

                public static int GetLength<T>(NA.Il2CppReferenceArray<T> array, int dimension) where T : Base
                {
                    Require(array);
                    CheckDimension(dimension);
                    return array.Length;
                }

                public static int GetLowerBound<T>(NA.Il2CppReferenceArray<T> array, int dimension) where T : Base
                {
                    Require(array);
                    CheckDimension(dimension);
                    return 0;
                }

                public static int GetUpperBound<T>(NA.Il2CppReferenceArray<T> array, int dimension) where T : Base
                {
                    Require(array);
                    CheckDimension(dimension);
                    return array.Length - 1;
                }

                // A clone keeps the source's actual runtime element class, which can be narrower than T, so the storage is allocated
                // from that class instead of from T.
                public static object Clone<T>(NA.Il2CppReferenceArray<T> array) where T : Base
                {
                    Require(array);
                    int length = array.Length;
                    global::System.IntPtr pointer = Native.il2cpp_array_new(ElementClassOf(array), (ulong)length);
                    if (pointer == global::System.IntPtr.Zero) throw new global::System.OutOfMemoryException();
                    var copy = new NA.Il2CppReferenceArray<T>(pointer);
                    for (int i = 0; i < length; i++)
                    {
                        T value = array[i];
                        if (!(value is null)) Write(copy, i, value);
                    }
                    global::System.GC.KeepAlive(array);
                    return copy;
                }

                public static void Clear<T>(NA.Il2CppReferenceArray<T> array) where T : Base
                {
                    if (array is null) throw new global::System.ArgumentNullException("array");
                    int length = array.Length;
                    for (int i = 0; i < length; i++) Write(array, i, null);
                    global::System.GC.KeepAlive(array);
                }

                public static void Clear<T>(NA.Il2CppReferenceArray<T> array, int index, int length) where T : Base
                {
                    if (array is null) throw new global::System.ArgumentNullException("array");
                    if (index < 0 || length < 0 || (long)index + length > array.Length) throw new global::System.IndexOutOfRangeException();
                    for (int i = 0; i < length; i++) Write(array, index + i, null);
                    global::System.GC.KeepAlive(array);
                }

                // Like Array.Resize<T>, the new array uses the static element type T, never the source's runtime element class.
                public static void Resize<T>(ref NA.Il2CppReferenceArray<T> array, int newSize) where T : Base
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
                    int count = global::System.Math.Min(current.Length, newSize);
                    for (int i = 0; i < count; i++) Store(resized, i, current[i]);
                    global::System.GC.KeepAlive(current);
                    array = resized;
                }

                // Check order follows Array.Copy. Pure CLR pairs use Array.Copy itself. Otherwise elements move one at a time through
                // the checked store, backwards when a same-array copy overlaps, so the result is as if the source were buffered.
                public static void Copy<TSource, TDestination>(S1InteropReferenceArrayRef<TSource> sourceArray, int sourceIndex, S1InteropReferenceArrayRef<TDestination> destinationArray, int destinationIndex, int length)
                    where TSource : Base where TDestination : Base
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
                    if (sourceArray.IsManaged && destinationArray.IsManaged)
                    {
                        global::System.Array.Copy(sourceArray.Managed, sourceIndex, destinationArray.Managed, destinationIndex, length);
                        return;
                    }

                    global::System.Type managedDestinationType = destinationArray.IsManaged ? destinationArray.Managed.GetType().GetElementType() : null;
                    global::System.IntPtr sourceClass = sourceArray.IsManaged
                        ? global::Il2CppInterop.Runtime.Il2CppClassPointerStore.GetNativeClassPointer(sourceArray.Managed.GetType().GetElementType())
                        : ElementClassOf(sourceArray.NativeArray);
                    global::System.IntPtr destinationClass = destinationArray.IsManaged
                        ? global::Il2CppInterop.Runtime.Il2CppClassPointerStore.GetNativeClassPointer(managedDestinationType)
                        : ElementClassOf(destinationArray.NativeArray);
                    if (sourceClass == global::System.IntPtr.Zero || destinationClass == global::System.IntPtr.Zero)
                        throw new global::System.InvalidOperationException("The array element class is unavailable.");
                    // Even a zero-length copy checks incompatible array classes before inspecting individual elements.
                    if (!Native.il2cpp_class_is_assignable_from(destinationClass, sourceClass) &&
                        !Native.il2cpp_class_is_assignable_from(sourceClass, destinationClass))
                        throw new global::System.ArrayTypeMismatchException();

                    bool backwards = sourceIndex < destinationIndex && !sourceArray.IsManaged && !destinationArray.IsManaged &&
                        sourceArray.NativeArray.Pointer == destinationArray.NativeArray.Pointer;
                    for (int step = 0; step < length; step++)
                    {
                        int offset = backwards ? length - 1 - step : step;
                        Base value = sourceArray.IsManaged ? (Base)sourceArray.Managed[sourceIndex + offset] : Load(sourceArray.NativeArray, sourceIndex + offset);
                        if (destinationArray.IsManaged)
                        {
                            destinationArray.Managed.SetValue(ToManaged(value, managedDestinationType), destinationIndex + offset);
                        }
                        else
                        {
                            if (!(value is null) && !Native.il2cpp_class_is_assignable_from(destinationClass, ClassOf(value)))
                                throw new global::System.InvalidCastException("At least one element in the source array could not be cast down to the destination array type.");
                            Write(destinationArray.NativeArray, destinationIndex + offset, value);
                        }
                    }
                    sourceArray.KeepAlive();
                    destinationArray.KeepAlive();
                }

                public static void Copy<TSource, TDestination>(S1InteropReferenceArrayRef<TSource> sourceArray, S1InteropReferenceArrayRef<TDestination> destinationArray, int length)
                    where TSource : Base where TDestination : Base
                {
                    Copy<TSource, TDestination>(sourceArray, 0, destinationArray, 0, length);
                }

                public static void Copy<TSource, TDestination>(S1InteropReferenceArrayRef<TSource> sourceArray, long sourceIndex, S1InteropReferenceArrayRef<TDestination> destinationArray, long destinationIndex, long length)
                    where TSource : Base where TDestination : Base
                {
                    int source = (int)sourceIndex;
                    int destination = (int)destinationIndex;
                    int count = (int)length;
                    if (sourceIndex != source) throw new global::System.ArgumentOutOfRangeException("sourceIndex", Huge);
                    if (destinationIndex != destination) throw new global::System.ArgumentOutOfRangeException("destinationIndex", Huge);
                    if (length != count) throw new global::System.ArgumentOutOfRangeException("length", Huge);
                    Copy<TSource, TDestination>(sourceArray, source, destinationArray, destination, count);
                }

                public static void Copy<TSource, TDestination>(S1InteropReferenceArrayRef<TSource> sourceArray, S1InteropReferenceArrayRef<TDestination> destinationArray, long length)
                    where TSource : Base where TDestination : Base
                {
                    int count = (int)length;
                    if (length != count) throw new global::System.ArgumentOutOfRangeException("length", Huge);
                    Copy<TSource, TDestination>(sourceArray, destinationArray, count);
                }

                public static void CopyTo<TSource, TDestination>(NA.Il2CppReferenceArray<TSource> source, S1InteropReferenceArrayRef<TDestination> array, int index)
                    where TSource : Base where TDestination : Base
                {
                    Require(source);
                    Copy<TSource, TDestination>(source, 0, array, index, source.Length);
                }

                public static void CopyTo<TSource, TDestination>(NA.Il2CppReferenceArray<TSource> source, S1InteropReferenceArrayRef<TDestination> array, long index)
                    where TSource : Base where TDestination : Base
                {
                    Require(source);
                    int narrowed = (int)index;
                    if (index != narrowed) throw new global::System.ArgumentOutOfRangeException("index", Huge);
                    CopyTo<TSource, TDestination>(source, array, narrowed);
                }

                private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::System.Func<object, Base>> ManagedCasters =
                    new global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Type, global::System.Func<object, Base>>();

                // A covariant CLR destination can be narrower than its declared type. Construct a wrapper assignable to that
                // actual element type, rather than storing a base proxy for an otherwise compatible native object.
                private static Base ToManaged(Base value, global::System.Type elementType)
                {
                    if (value is null) return null;
                    if (elementType.IsInstanceOfType(value)) return value;
                    var cast = ManagedCasters.GetOrAdd(elementType, type =>
                        (global::System.Func<object, Base>)typeof(S1InteropNativeCast).GetMethod("TryCast").MakeGenericMethod(type)
                            .CreateDelegate(typeof(global::System.Func<object, Base>)));
                    Base result = cast(value);
                    if (result is null)
                        throw new global::System.InvalidCastException("At least one element in the source array could not be cast down to the destination array type.");
                    return result;
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
    /// Gets whether the target exposes every member the reference helpers call: <c>Il2CppReferenceArray&lt;T&gt;</c>, the runtime
    /// <c>Array.SetValue</c> wrapper used for stores, and the class queries used for the element check. Independent of the
    /// scalar surface; without it reference arrays keep today's behavior (no lowering, and the post-binding guard applies).
    /// </summary>
    public static bool HasSurface(MetadataSymbolMap map)
    {
        if (map.NativeObjectBase is not { } objectBase ||
            map.FindTargetType(ReferenceArrayMetadataName) is not { Arity: 1 } array ||
            map.FindTargetType(NativeArrayMetadataName) is not { } nativeArray ||
            map.FindTargetType(NativeObjectMetadataName) is not { } nativeObject ||
            map.FindTargetType(RuntimeMetadataName) is not { } runtime ||
            map.FindTargetType("Il2CppInterop.Runtime.Il2CppClassPointerStore") is not { } pointerStore)
            return false;

        ITypeParameterSymbol element = array.TypeParameters[0];
        if (!element.HasReferenceTypeConstraint && !element.ConstraintTypes.Any(type => Derives(type, objectBase))) return false;

        ISymbol[] members = Chain(array).SelectMany(type => type.GetMembers()).ToArray();
        return array.InstanceConstructors.Any(constructor => IsPublicConstructor(constructor, SpecialType.System_Int64)) &&
            array.InstanceConstructors.Any(constructor => IsPublicConstructor(constructor, SpecialType.System_IntPtr)) &&
            IsPublicConstructor(nativeArray, SpecialType.System_IntPtr) && IsPublicConstructor(nativeObject, SpecialType.System_IntPtr) &&
            members.OfType<IPropertySymbol>().Any(property => property is { Name: "Length", Type.SpecialType: SpecialType.System_Int32 }) &&
            members.OfType<IPropertySymbol>().Any(property => property.IsIndexer && property.GetMethod is { DeclaredAccessibility: Accessibility.Public } &&
                property.Parameters is [{ Type.SpecialType: SpecialType.System_Int32 }]) &&
            Chain(objectBase).SelectMany(type => type.GetMembers("Pointer")).OfType<IPropertySymbol>().Any(property =>
                property is { IsStatic: false, DeclaredAccessibility: Accessibility.Public, Type.SpecialType: SpecialType.System_IntPtr }) &&
            Chain(nativeArray).SelectMany(type => type.GetMembers("SetValue")).OfType<IMethodSymbol>().Any(method =>
                method is { IsStatic: false, DeclaredAccessibility: Accessibility.Public, Parameters: [{ } value, { Type.SpecialType: SpecialType.System_Int32 }] } &&
                SymbolEqualityComparer.Default.Equals(value.Type, nativeObject)) &&
            HasRuntime(runtime, "il2cpp_object_get_class", SpecialType.System_IntPtr, SpecialType.System_IntPtr) &&
            HasRuntime(runtime, "il2cpp_class_get_element_class", SpecialType.System_IntPtr, SpecialType.System_IntPtr) &&
            HasRuntime(runtime, "il2cpp_class_is_assignable_from", SpecialType.System_Boolean, SpecialType.System_IntPtr, SpecialType.System_IntPtr) &&
            HasRuntime(runtime, "il2cpp_array_new", SpecialType.System_IntPtr, SpecialType.System_IntPtr, SpecialType.System_UInt64) &&
            pointerStore.GetMembers("GetNativeClassPointer").OfType<IMethodSymbol>().Any(method =>
                method is { IsStatic: true, DeclaredAccessibility: Accessibility.Public, ReturnType.SpecialType: SpecialType.System_IntPtr,
                    Parameters: [{ Type: { } type }] } && type.ToDisplayString() == "System.Type");
    }

    private static bool IsPublicConstructor(INamedTypeSymbol type, SpecialType parameter) =>
        type.InstanceConstructors.Any(constructor => IsPublicConstructor(constructor, parameter));

    private static bool IsPublicConstructor(IMethodSymbol constructor, SpecialType parameter) =>
        constructor.DeclaredAccessibility == Accessibility.Public && constructor.Parameters is [{ } only] &&
        only.Type.SpecialType == parameter;

    private static bool HasRuntime(INamedTypeSymbol runtime, string name, SpecialType result, params SpecialType[] parameters) =>
        runtime.GetMembers(name).OfType<IMethodSymbol>().Any(method => method.IsStatic &&
            method.DeclaredAccessibility == Accessibility.Public && method.ReturnType.SpecialType == result &&
            method.Parameters.Length == parameters.Length &&
            method.Parameters.Select(parameter => parameter.Type.SpecialType).SequenceEqual(parameters));

    private static bool Derives(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType)) return true;
        return false;
    }

    private static IEnumerable<INamedTypeSymbol> Chain(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType) yield return current;
    }
}
