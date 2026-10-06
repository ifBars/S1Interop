using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Fields;
using MelonLoader;
using UnityEngine;
using S1Interop.CompilerSmoke.Library;

[assembly: MelonInfo(typeof(S1Interop.CompilerSmoke.NativeSerialization.Mod), "S1Interop Compiler Runtime Smoke (Native Serialization)", "0.0.1", "S1Interop contributors")]
[assembly: MelonGame("TVGS", "Schedule I")]
[assembly: MelonPlatformDomain(MelonPlatformDomainAttribute.CompatibleDomains.IL2CPP)]

namespace S1Interop.CompilerSmoke.NativeSerialization;

// Direct loader instrumentation; this is not a compiler-transformed authoring example.
public sealed class SerializationProbe : MonoBehaviour
{
    public Il2CppValueField<int> Number = null!;
    [NonSerialized] public Il2CppValueField<int> NumberPadding = null!;
    public Il2CppStringField Text = null!;
    public Il2CppValueField<bool> Flag = null!;
    public Il2CppValueField<byte> Small = null!;
    public Il2CppValueField<long> Wide = null!;
    public Il2CppValueField<double> Fraction = null!;
    public Il2CppValueField<char> Character = null!;
    [NonSerialized] public Il2CppValueField<int> RuntimeOnly = null!;
    // The generated Unity SerializeField proxy is not a CLR Attribute (CS0616).
    private Il2CppValueField<int> secret = null!;
    public static bool ConstructorSawInitializedFields;
    public static int ConstructorNativeNumber;
    public static int ConstructorCalls;

    public int Secret { get => secret.Value; set => secret.Value = value; }

    public SerializationProbe(IntPtr pointer) : base(pointer)
    {
        ConstructorCalls++;
        ConstructorSawInitializedFields = Number != null && Text != null;
        Number = CreateField<Il2CppValueField<int>>(nameof(Number));
        Text = CreateField<Il2CppStringField>(nameof(Text));
        ConstructorNativeNumber = Number.Value;
        Number.Value = 11;
        Text.Value = "initializer";
    }

    private T CreateField<T>(string name) => (T)Activator.CreateInstance(typeof(T),
        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null,
        new object[] { this, name }, null)!;
}

public sealed class Mod : MelonMod
{
    private bool ran;
    private GameObject? retainedOwner;
    private SerializationProbe? retainedComponent;
    private nint retainedString;
    private nint unrootedString;
    private LibrarySerializedComponent? retainedLibrary;
    private nint libraryString;
    private nint retainedObject;
    private nint unrootedObject;
    private Il2CppScheduleOne.Persistence.TrashContentData? retainedArrayOwner;
    private nint retainedArray;
    private nint unrootedArray;
    private nint serializedArray;
    private nint serializedReferenceArray;
    private nint serializedReferenceElement;
    private Il2CppScheduleOne.Persistence.Datas.NPCCollectionData? retainedReferenceOwner;
    private nint retainedArrayElement;
    private nint unrootedArrayElement;
    private AsyncOperation? unloadUnused;
    private int unloadFrames;
    private int frames;
    private string runToken = "missing";

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (ran || sceneName != "Menu") return;
        ran = true;
        string token = Environment.GetEnvironmentVariable("S1INTEROP_SMOKE_TOKEN") ?? "missing";
        runToken = token;
        GameObject? owner = null;
        GameObject? clone = null;
        try
        {
            ClassInjector.RegisterTypeInIl2Cpp<SerializationProbe>();
            unsafe
            {
                var nativeClass = Il2CppInterop.Runtime.Runtime.UnityVersionHandler.Wrap(
                    (Il2CppInterop.Runtime.Runtime.Il2CppClass*)Il2CppClassPointerStore<SerializationProbe>.NativeClassPtr);
                var references = nativeClass.GetType().GetProperty("HasReferences") ??
                    throw new NotSupportedException("Installed native class accessor has no HasReferences property.");
                LoggerInstance.Msg($"S1Compiler|OBSERVATION|Token={token}|OriginalHasReferences={references.GetValue(nativeClass)}");
                references.SetValue(nativeClass, true);
            }
            owner = new GameObject("S1Interop serialization diagnostic");
            var component = owner.AddComponent<SerializationProbe>();
            if (component.Number.Value != 11 || component.Text.Value != "initializer")
                throw new InvalidOperationException("Early native field initialization did not survive loader construction.");
            component.Number.Value = 37;
            component.Text.Value = "before";
            component.RuntimeOnly.Value = 23;
            component.Secret = 41;
            component.Flag.Value = true;
            component.Small.Value = 251;
            component.Wide.Value = 9007199254740993L;
            component.Fraction.Value = 0.125;
            component.Character.Value = 'Z';
            if (component.Number.Value != 37 || component.Text.Value != "before")
                throw new InvalidOperationException("Native field read/write failed.");
            string json = JsonUtility.ToJson(component);
            component.Flag.Value = false;
            component.Small.Value = 0;
            component.Wide.Value = 0;
            component.Fraction.Value = 0;
            component.Character.Value = '\0';
            JsonUtility.FromJsonOverwrite(json, component);
            if (!component.Flag.Value || component.Small.Value != 251 || component.Wide.Value != 9007199254740993L ||
                component.Fraction.Value != 0.125 || component.Character.Value != 'Z')
                throw new InvalidOperationException("Mixed-size native scalar field round trip failed.");
            JsonUtility.FromJsonOverwrite("{\"Number\":91,\"Text\":\"after\"}", component);
            bool wrote = json.Contains("\"Number\":37") && json.Contains("\"Text\":\"before\"");
            bool read = component.Number.Value == 91 && component.Text.Value == "after";
            bool privateSerialized = json.Contains("\"secret\":41");
            bool runtimeOnlySerialized = json.Contains("\"RuntimeOnly\"");
            clone = UnityEngine.Object.Instantiate(owner);
            var cloned = clone.GetComponent<SerializationProbe>();
            bool clonedPublicFields = cloned.Number.Value == 91 && cloned.Text.Value == "after" &&
                cloned.Flag.Value && cloned.Small.Value == 251 && cloned.Wide.Value == 9007199254740993L &&
                cloned.Fraction.Value == 0.125 && cloned.Character.Value == 'Z';
            LoggerInstance.Msg($"S1Compiler|OBSERVATION|Token={token}|Json={json}|Serialized={wrote}|Deserialized={read}|PrivateSerialized={privateSerialized}|RuntimeOnlySerialized={runtimeOnlySerialized}|ClonedPublicFields={clonedPublicFields}|ConstructorFieldsReady={SerializationProbe.ConstructorSawInitializedFields}|CloneConstructorNativeNumber={SerializationProbe.ConstructorNativeNumber}");
            if (!wrote || !read)
                throw new InvalidOperationException("Injected native fields did not complete the Unity JsonUtility round trip.");
            if (runtimeOnlySerialized || !clonedPublicFields)
                throw new InvalidOperationException("Native field exclusion or runtime cloning failed.");
            cloned.Number.Value = 123;
            if (cloned.Pointer == component.Pointer || component.Number.Value != 91 ||
                SerializationProbe.ConstructorCalls != 2 || cloned.RuntimeOnly.Value != 0)
                throw new InvalidOperationException("Clone identity, construction, or runtime-only state was not independent.");
            retainedOwner = owner;
            retainedComponent = component;
            retainedString = RetainString(component);
            unrootedString = UnrootedString();
            ClassInjector.RegisterTypeInIl2Cpp<LibrarySerializedComponent>();
            retainedLibrary = owner.AddComponent<LibrarySerializedComponent>();
            libraryString = RetainLibraryString(retainedLibrary);
            CreateObjectReferences(retainedLibrary);
            CreateArrayReferences();
            CreateReferenceArrayElements();
            unloadUnused = Resources.UnloadUnusedAssets();
            LoggerInstance.Msg($"S1Compiler|OBSERVATION|Token={token}|StringOffset={StringOffset(component)}|NativeGcFrames=8");
        }
        catch (Exception exception)
        {
            LoggerInstance.Error($"S1Compiler|FAIL|Token={token}|{exception}");
            if (retainedOwner != null) { Cleanup(); owner = null; }
        }
        finally
        {
            if (clone != null) UnityEngine.Object.Destroy(clone);
            if (retainedOwner == null)
            {
                if (owner != null) UnityEngine.Object.Destroy(owner);
                Application.Quit();
            }
        }
    }

    public override void OnUpdate()
    {
        if (retainedOwner == null) return;
        try
        {
            if (unloadUnused != null && !unloadUnused.isDone)
            {
                if (++unloadFrames > 240) throw new InvalidOperationException("Unused-resource unloading exceeded the frame bound.");
                return;
            }
            ScrubStack();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            IL2CPP.il2cpp_gc_collect(0);
            if (IL2CPP.il2cpp_gchandle_get_target(retainedString) == IntPtr.Zero)
                throw new InvalidOperationException("A string stored in an injected field was reclaimed while its component was retained.");
            if (IL2CPP.il2cpp_gchandle_get_target(libraryString) == IntPtr.Zero)
                throw new InvalidOperationException("A compiler-lowered serialized string was reclaimed while its component was retained.");
            if (IL2CPP.il2cpp_gchandle_get_target(retainedObject) == IntPtr.Zero)
                throw new InvalidOperationException("A compiler-lowered serialized object reference was reclaimed while its component was retained.");
            if (IL2CPP.il2cpp_gchandle_get_target(retainedArray) == IntPtr.Zero)
                throw new InvalidOperationException("An array stored through a compiler-built library was reclaimed while its game owner was retained.");
            if (IL2CPP.il2cpp_gchandle_get_target(serializedArray) == IntPtr.Zero)
                throw new InvalidOperationException("An array stored in an injected component was reclaimed while its component was retained.");
            if (IL2CPP.il2cpp_gchandle_get_target(serializedReferenceArray) == IntPtr.Zero ||
                IL2CPP.il2cpp_gchandle_get_target(serializedReferenceElement) == IntPtr.Zero)
                throw new InvalidOperationException("A serialized reference array or its sole-owned resource was reclaimed while its component was retained.");
            if (IL2CPP.il2cpp_gchandle_get_target(retainedArrayElement) == IntPtr.Zero)
                throw new InvalidOperationException("An element written through compiler-built array access was reclaimed while its array owner was retained.");
            if (++frames < 8) return;
            if (IL2CPP.il2cpp_gchandle_get_target(unrootedString) != IntPtr.Zero)
                throw new InvalidOperationException("The unrooted control string was not reclaimed; native GC evidence is inconclusive.");
            if (IL2CPP.il2cpp_gchandle_get_target(unrootedObject) != IntPtr.Zero)
                throw new InvalidOperationException("The unrooted control object was not reclaimed; native object GC evidence is inconclusive.");
            if (IL2CPP.il2cpp_gchandle_get_target(unrootedArray) != IntPtr.Zero)
                throw new InvalidOperationException("The unrooted control array was not reclaimed; native array GC evidence is inconclusive.");
            if (IL2CPP.il2cpp_gchandle_get_target(unrootedArrayElement) != IntPtr.Zero)
                throw new InvalidOperationException("The unrooted control element was not reclaimed; native element GC evidence is inconclusive.");
            if (retainedComponent!.Text.Value != "retained-native-string-" + runToken)
                throw new InvalidOperationException("Retained string content changed.");
            if (retainedLibrary!.Label != "compiler-native-string-" + runToken)
                throw new InvalidOperationException("Compiler-lowered retained string content changed.");
            if (retainedLibrary.Resource.Value.name != "retained-reference-" + runToken)
                throw new InvalidOperationException("Retained object reference changed.");
            if (SharedProbe.TrashCounts(retainedArrayOwner!)[1] != 42)
                throw new InvalidOperationException("Retained native array content changed.");
            var rediscovered = retainedOwner!.GetComponent<LibrarySerializedComponent>();
            if (rediscovered.Numbers.Value.Length != 2 || rediscovered.Numbers.Value[1] != 53)
                throw new InvalidOperationException("Rediscovered component did not retain its serialized array after collection.");
            if (!retainedReferenceOwner!.NPCs[0].BaseData.Contains("retained-array-element-" + runToken))
                throw new InvalidOperationException("Retained reference array element content changed.");
            if (rediscovered.Resources.Value[0].name != "serialized-array-resource-" + runToken)
                throw new InvalidOperationException("Rediscovered reference array resource changed.");
            LoggerInstance.Msg($"S1Compiler|PASS|Token={runToken}|Version={Application.version}|Scene=Menu|Checks=17|NativeGcFrames={frames}|UnusedResourceWaitFrames={unloadFrames}");
        }
        catch (Exception exception)
        {
            LoggerInstance.Error($"S1Compiler|FAIL|Token={runToken}|{exception}");
        }
        Cleanup();
        Application.Quit();
    }

    private void Cleanup()
    {
        if (serializedReferenceElement != 0 && IL2CPP.il2cpp_gchandle_get_target(serializedReferenceElement) != IntPtr.Zero &&
            serializedReferenceArray != 0 && IL2CPP.il2cpp_gchandle_get_target(serializedReferenceArray) != IntPtr.Zero && retainedLibrary != null)
            UnityEngine.Object.Destroy(retainedLibrary.Resources.Value[0]);
        if (serializedReferenceArray != 0) IL2CPP.il2cpp_gchandle_free(serializedReferenceArray);
        if (serializedReferenceElement != 0) IL2CPP.il2cpp_gchandle_free(serializedReferenceElement);
        serializedReferenceArray = serializedReferenceElement = 0;
        if (retainedObject != 0 && IL2CPP.il2cpp_gchandle_get_target(retainedObject) != IntPtr.Zero && retainedLibrary != null &&
            retainedLibrary.Resource.Value != null) UnityEngine.Object.Destroy(retainedLibrary.Resource.Value);
        if (retainedString != 0) IL2CPP.il2cpp_gchandle_free(retainedString);
        if (unrootedString != 0) IL2CPP.il2cpp_gchandle_free(unrootedString);
        if (libraryString != 0) IL2CPP.il2cpp_gchandle_free(libraryString);
        if (retainedObject != 0) IL2CPP.il2cpp_gchandle_free(retainedObject);
        if (unrootedObject != 0) IL2CPP.il2cpp_gchandle_free(unrootedObject);
        if (retainedArray != 0) IL2CPP.il2cpp_gchandle_free(retainedArray);
        if (unrootedArray != 0) IL2CPP.il2cpp_gchandle_free(unrootedArray);
        if (serializedArray != 0) IL2CPP.il2cpp_gchandle_free(serializedArray);
        if (retainedArrayElement != 0) IL2CPP.il2cpp_gchandle_free(retainedArrayElement);
        if (unrootedArrayElement != 0) IL2CPP.il2cpp_gchandle_free(unrootedArrayElement);
        retainedArrayElement = unrootedArrayElement = 0;
        retainedReferenceOwner = null;
        retainedString = unrootedString = libraryString = 0;
        retainedObject = unrootedObject = 0;
        retainedArray = unrootedArray = 0;
        serializedArray = 0;
        retainedArrayOwner = null;
        if (retainedOwner != null) UnityEngine.Object.Destroy(retainedOwner);
        retainedOwner = null;
        retainedComponent = null;
        retainedLibrary = null;
        unloadUnused = null;
    }

    private static int StringOffset(SerializationProbe component) =>
        (int)IL2CPP.il2cpp_field_get_offset(IL2CPP.GetIl2CppField(component.ObjectClass, "Text"));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private nint RetainString(SerializationProbe component)
    {
        component.Text.Value = "retained-native-string-" + runToken;
        return IL2CPP.il2cpp_gchandle_new_weakref(Marshal.ReadIntPtr(component.Pointer + StringOffset(component)), false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private nint RetainLibraryString(LibrarySerializedComponent component)
    {
        component.SetLabel("compiler-native-string-" + runToken);
        var field = IL2CPP.GetIl2CppField(component.ObjectClass, "label");
        int offset = (int)IL2CPP.il2cpp_field_get_offset(field);
        if (offset % IntPtr.Size != 0 || !IL2CPP.il2cpp_class_has_references(component.ObjectClass))
            throw new InvalidOperationException("Compiler-emitted component has invalid field alignment or GC metadata.");
        return IL2CPP.il2cpp_gchandle_new_weakref(Marshal.ReadIntPtr(component.Pointer + offset), false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CreateObjectReferences(LibrarySerializedComponent component)
    {
        var resource = ScriptableObject.CreateInstance<ScriptableObject>();
        resource.name = "retained-reference-" + runToken;
        component.SetResource(resource);
        retainedObject = IL2CPP.il2cpp_gchandle_new_weakref(resource.Pointer, false);
        var control = ScriptableObject.CreateInstance<ScriptableObject>();
        control.name = "unrooted-reference-" + runToken;
        unrootedObject = IL2CPP.il2cpp_gchandle_new_weakref(control.Pointer, false);
        var arrayResource = ScriptableObject.CreateInstance<ScriptableObject>();
        arrayResource.name = "serialized-array-resource-" + runToken;
        component.SetResourceElement(arrayResource);
        serializedReferenceArray = IL2CPP.il2cpp_gchandle_new_weakref(component.Resources.Value.Pointer, false);
        serializedReferenceElement = IL2CPP.il2cpp_gchandle_new_weakref(arrayResource.Pointer, false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CreateArrayReferences()
    {
        retainedArrayOwner = new Il2CppScheduleOne.Persistence.TrashContentData();
        var values = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(3);
        values[1] = 42;
        SharedProbe.ReplaceTrashCounts(retainedArrayOwner, values);
        retainedArray = IL2CPP.il2cpp_gchandle_new_weakref(values.Pointer, false);
        var control = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(3);
        unrootedArray = IL2CPP.il2cpp_gchandle_new_weakref(control.Pointer, false);
        var componentValues = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<int>(2);
        componentValues[1] = 53;
        retainedLibrary!.SetNumbers(componentValues);
        serializedArray = IL2CPP.il2cpp_gchandle_new_weakref(componentValues.Pointer, false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void CreateReferenceArrayElements()
    {
        var values = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Il2CppScheduleOne.Persistence.Datas.DynamicSaveData>(1);
        retainedReferenceOwner = new Il2CppScheduleOne.Persistence.Datas.NPCCollectionData(values);
        var value = new Il2CppScheduleOne.Persistence.Datas.DynamicSaveData(
            new Il2CppScheduleOne.Persistence.Datas.NPCData("retained-array-element-" + runToken));
        SharedProbe.WriteNpcData(retainedReferenceOwner, value);
        retainedArrayElement = IL2CPP.il2cpp_gchandle_new_weakref(value.Pointer, false);
        var control = new Il2CppScheduleOne.Persistence.Datas.DynamicSaveData(
            new Il2CppScheduleOne.Persistence.Datas.NPCData("unrooted-array-element-" + runToken));
        unrootedArrayElement = IL2CPP.il2cpp_gchandle_new_weakref(control.Pointer, false);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static nint UnrootedString() =>
        IL2CPP.il2cpp_gchandle_new_weakref(IL2CPP.ManagedStringToIl2Cpp(Guid.NewGuid().ToString()), false);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void ScrubStack()
    {
        byte* bytes = stackalloc byte[32768];
        for (int index = 0; index < 32768; index++) bytes[index] = 0;
    }
}
