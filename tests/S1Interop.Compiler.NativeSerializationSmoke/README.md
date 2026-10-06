# Native field serialization diagnostic

This direct IL2CPP probe tests the installed loader's field injection separately from compiler lowering. It is not an example of backend-neutral mod source and is not part of the portable solution.

Run against an isolated, marked test installation with empty Mods, Plugins, and UserLibs. Build the compiler RuntimeLibrary first; the existing smoke runner requires its adjacent runtime support files.

```powershell
dotnet build tests/S1Interop.Compiler.NativeSerializationSmoke -c Release -p:Il2CppGamePath=<isolated-install>
./tests/Run-CompilerRuntimeSmoke.ps1 -GamePath <isolated-install> -ModPath tests/S1Interop.Compiler.NativeSerializationSmoke/bin/Release/net6.0/S1Interop.Compiler.NativeSerializationSmoke.dll -Runtime Il2Cpp
```

At the menu, the probe creates an isolated GameObject and injected component. It checks native field read/write, JsonUtility serialization and overwrite, NonSerialized exclusion, and Object.Instantiate preservation and independence of public fields. It then forces native and managed collection over eight frames, checking a field-owned string against an unrooted string control. It destroys both objects and quits without loading a save.

On the verified 0.4.7f9 / MelonLoader 0.7.3 installation:

- Public native `int`, `string`, `bool`, `byte`, `long`, `double`, and `char` fields serialized, deserialized, and survived runtime cloning. The long value exceeded the exact-integer range of double precision.
- The public NonSerialized field and unannotated private field were excluded.
- Native field wrappers were still null in the managed pointer constructor.
- Early wrapper creation and initializer writes in that constructor survived loader construction; Unity applied cloned values after the constructor. Clone mutation left the original unchanged.
- Applying the generated UnityEngine.SerializeField proxy directly as a CLR attribute failed compilation with CS0616. This probe therefore does **not** test a private SerializeField round trip.

The stock loader's layout and GC metadata were **insufficient**: the field-owned native string was reclaimed while its component remained retained. Padding alone failed, and setting `HasReferences` alone failed. The passing probe pads the string slot to pointer alignment and sets the versioned class accessor's `HasReferences` property before allocating instances. With both changes, the retained string survived eight collection frames and the unrooted control was reclaimed. The probe deliberately uses the accessor exposed by the installed runtime rather than guessing a struct offset.

The probe also retains a compiler-built component whose serialized fields belong to an abstract base class. Its generated setters write a distinct string and a ScriptableObject reference. Both must survive unused-resource unloading and the subsequent native collections. An unreferenced ScriptableObject control must be reclaimed: native GC alone retained that control and was inconclusive, while unused-resource unloading distinguished ownership. The final probe has thirteen checks across eight collection frames, after waiting for the unload operation. Generated reference setters use Unity's write barrier; the direct loader fixture retains its raw wrapper writes to distinguish that fixture from compiler support.

These results do not establish prefab/AssetBundle script binding, SerializeReference, authored component reference cycles, nested collections, arbitrary layout correctness, or concurrent/incremental GC. The unchanged dual-runtime authoring probe supplies separate evidence for field lowering, JSON overwrite, null assignment, and clone remapping of GameObject/Transform references. All resources belong to the isolated probe process, which exits afterward.

The scalar-array ownership check stores an integer array through the compiler-built library into a game-owned data field, then drops temporary wrappers. Across the same eight native-GC frames, the field-owned array stays live with its content intact while an unrooted control array is reclaimed. A second integer array is retained only through a compiler-lowered injected component field and must also survive collection and component rediscovery. The dual-runtime author probe separately checks Unity JSON and cloning of supported scalar array fields; prefab/AssetBundle script binding remains unverified.
