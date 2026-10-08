using System;
using System.Linq;
using System.Collections.Generic;
using MelonLoader;
using ScheduleOne.NPCs;
using UnityEngine;
using UnityEngine.Events;
using S1Interop.CompilerSmoke.Library;

[assembly: MelonInfo(typeof(S1Interop.CompilerSmoke.Mod), "S1Interop Compiler Runtime Smoke", "0.0.1", "S1Interop contributors")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace S1Interop.CompilerSmoke;

public sealed class Mod : MelonMod
{
    private bool completed;

    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (completed || sceneName != "Menu") return;
        completed = true;
        string token = Environment.GetEnvironmentVariable("S1INTEROP_SMOKE_TOKEN") ?? "missing";
        GameObject? created = null;
        bool coroutineStarted = false;
        try
        {
            created = new GameObject("S1Interop compiler probe");
            var recyclerOwner = new GameObject("S1Interop reflection probe");
            recyclerOwner.SetActive(false);
            recyclerOwner.transform.SetParent(created.transform, false);
            var recycler = recyclerOwner.AddComponent<ScheduleOne.ObjectScripts.Recycler>();
            var collider = recyclerOwner.AddComponent<BoxCollider>();
            // The unchanged MoreXP Mono branch uses these exact two field lookups.
            var colliderField = HarmonyLib.AccessTools.Field(typeof(ScheduleOne.ObjectScripts.Recycler), "CheckCollider");
            var maskField = HarmonyLib.AccessTools.Field(typeof(ScheduleOne.ObjectScripts.Recycler), "DetectionMask");
            if (colliderField == null || maskField == null) throw new InvalidOperationException("MoreXP Recycler reflection lookups");
            colliderField.SetValue(recycler, collider);
            maskField.SetValue(recycler, (LayerMask)257);
            Require((colliderField.GetValue(recycler) as BoxCollider) == collider && recycler.CheckCollider == collider,
                "MoreXP reflection reads and writes native Unity object field");
            Require(((LayerMask)maskField.GetValue(recycler)!).value == 257 && recycler.DetectionMask.value == 257,
                "MoreXP reflection round-trips boxed LayerMask field");
            var vehicle = recyclerOwner.AddComponent<ScheduleOne.Vehicles.LandVehicle>();
            var wheel = recyclerOwner.AddComponent<WheelCollider>();
            var wheels = new[] { wheel };
            DynamicFieldProbe.WriteVehicle(vehicle, "driveWheels", wheels);
            var wheelAlias = DynamicFieldProbe.ReadVehicle<WheelCollider[]>(vehicle, "driveWheels");
            Require(wheelAlias[0] == wheel && vehicle.driveWheels[0] == wheel,
                "Forklift generic dynamic field lookup reads native reference array elements");
            wheelAlias[0] = null!;
            Require(wheels[0] == null && vehicle.driveWheels[0] == null,
                "Forklift dynamic array write retains source and native storage aliases");
            DynamicFieldProbe.WriteVehicle(vehicle, "maxSteeringAngle", 38f);
            Require(DynamicFieldProbe.ReadVehicle<float>(vehicle, "maxSteeringAngle") == 38f && vehicle.maxSteeringAngle == 38f,
                "Forklift generic dynamic field helper also preserves scalar values");
            var lobby = ScheduleOne.DevUtilities.Singleton<ScheduleOne.Networking.Lobby>.Instance;
            if (lobby == null) throw new InvalidOperationException("BiggerLobbies reflection probe requires the initialized menu lobby");
            var lobbyService = BiggerLobbiesReflectionProbe.GetLobbyService(lobby);
            Require(lobbyService != null && lobbyService == lobby._lobbyService,
                "BiggerLobbies Traverse read preserves native lobby service identity");
            Require(BiggerLobbiesReflectionProbe.GetLobbyService(null) == null,
                "BiggerLobbies Traverse read preserves null root behavior");
            var consoleReflection = new ConsoleReflectionProbe();
            var originalCommands = consoleReflection.Read();
            Require(originalCommands != null && ReferenceEquals(originalCommands, ScheduleOne.Console.commands),
                "ConsoleForAll cached dictionary reflection preserves the game table identity");
            try
            {
                var replacementCommands = GenericValueProbe.Make<Dictionary<string, ScheduleOne.Console.ConsoleCommand>>();
                consoleReflection.Write(replacementCommands);
                ScheduleOne.Console.commands = replacementCommands.Identity();
                var managedNumbers = GenericValueProbe.Make<List<int>>();
                Require(managedNumbers.GetType() == typeof(List<int>),
                    "generic factory retains independent CLR call storage");
                DynamicFieldProbe.WriteConsole("commands", replacementCommands);
                var dynamicCommands = DynamicFieldProbe.ReadConsole<Dictionary<string, ScheduleOne.Console.ConsoleCommand>>("commands");
                Require(ReferenceEquals(replacementCommands, dynamicCommands),
                    "dynamic generic dictionary lookup preserves replacement identity");
                dynamicCommands.Add("__s1interop_dynamic_probe__", null!);
                Require(ScheduleOne.Console.commands.ContainsKey("__s1interop_dynamic_probe__") && replacementCommands.ContainsKey("__s1interop_dynamic_probe__"),
                    "dynamic generic dictionary mutation reaches native game storage");
                var reflectedCommands = consoleReflection.Read()!;
                reflectedCommands.Add("__s1interop_probe__", null!);
                Require(ReferenceEquals(replacementCommands, reflectedCommands) && ScheduleOne.Console.commands.ContainsKey("__s1interop_probe__"),
                    "reflected dictionary replacement and mutation share native game storage");
                var traversedCommands = HarmonyLib.Traverse.Create(typeof(ScheduleOne.Console))
                    .Field("commands").GetValue<Dictionary<string, ScheduleOne.Console.ConsoleCommand>>();
                Require(ReferenceEquals(reflectedCommands, traversedCommands),
                    "Traverse collection read preserves reflected command table identity");
                traversedCommands.Add("__s1interop_traverse_probe__", null!);
                Require(replacementCommands.ContainsKey("__s1interop_traverse_probe__") &&
                    ScheduleOne.Console.commands.ContainsKey("__s1interop_traverse_probe__"),
                    "Traverse collection mutation shares native game storage");
                bool invalidTraversalCast = false;
                try
                {
                    List<int> wrongCommands = HarmonyLib.Traverse.Create(typeof(ScheduleOne.Console))
                        .Field("commands").GetValue<List<int>>();
                    _ = wrongCommands.Count;
                }
                catch (InvalidCastException) { invalidTraversalCast = true; }
                Require(invalidTraversalCast, "Traverse incompatible collection cast preserves InvalidCastException");


            }
            finally { consoleReflection.Write(originalCommands!); }
            Require(ReferenceEquals(originalCommands, consoleReflection.Read()) && ReferenceEquals(originalCommands, ScheduleOne.Console.commands),
                "reflected game command table is restored with its original identity");
            var serializedOwner = new GameObject("S1Interop serialized field probe");
            serializedOwner.transform.SetParent(created.transform, false);
            var serialized = serializedOwner.AddComponent<LibrarySerializedComponent>();
            var ownPayload = GenericValueProbe.Identity(new byte[] { 7 });
            serialized.Payload = ownPayload.Identity();
            serialized.Payload[0] = 19;
            Require(ownPayload[0] == 19,
                "generic identity and extension receivers preserve native array aliases across library fields");
            serialized.DecodePayload(" Q U J D\r\n");
            var payloadAlias = serialized.Payload;
            payloadAlias[1] = 90;
            GC.Collect();
            Require(serialized.ReadPayload(0, 3) == "AZC" && System.Text.Encoding.UTF8.GetString(payloadAlias) == "AZC",
                "Base64 buffer storage and UTF8 reads across compiler library retain aliases after collection");
            Func<byte[], string> encodePayload = values => { values[0] = 66; return Convert.ToBase64String(values); };
            Require(serialized.UsePayload(encodePayload) == "QlpD" && payloadAlias[0] == 66,
                "source delegate mutates shared native storage and encodes it across compiler library boundary");
            serialized.DecodePayload("");
            var firstEmptyPayload = serialized.Payload;
            serialized.DecodePayload("");
            Require(!object.ReferenceEquals(firstEmptyPayload, serialized.Payload) && serialized.ReadPayload(0, 0) == "",
                "Base64 empty buffers retain fresh identity and support empty UTF8 slices");
            serialized.SetBinaryPayload(0x12345678);
            var binaryAlias = serialized.Payload;
            Require(serialized.ReadBinaryPayload() == 0x12345678 && BitConverter.ToUInt32(binaryAlias, 0) == 0x12345678,
                "binary conversions read native component storage across compiler library boundary");
            binaryAlias[0] ^= 1;
            GC.Collect();
            Require(serialized.ReadBinaryPayload() == (0x12345678 ^ (BitConverter.IsLittleEndian ? 1 : 0x1000000)) &&
                object.ReferenceEquals(binaryAlias, serialized.Payload), "binary reads preserve mutated alias after collection");
            bool shortBinaryRejected = false;
            try { BitConverter.ToDouble(binaryAlias, 0); }
            catch (ArgumentException e) { shortBinaryRejected = e.ParamName == "value"; }
            Require(shortBinaryRejected, "binary read preserves insufficient-buffer exception parameter");
            Require(serialized.Number == 7 && serialized.Label == "before", "serialized component constructor initializers");
            var qualityAlias = serialized.Qualities;
            int pcmCalls = 0;
            AudioClip.PCMReaderCallback pcm = samples => { pcmCalls++; samples[0] = 0.25f; samples[1] = -0.5f; };
            float[] pcmSamples = new float[2];
            pcm(pcmSamples);
            Require(pcmCalls == 1 && pcmSamples[0] == 0.25f && pcmSamples[1] == -0.5f, "native PCM delegate writes through array storage");
            GC.Collect(); GC.WaitForPendingFinalizers();
            pcm(pcmSamples);
            Require(pcmCalls == 2 && pcmSamples[1] == -0.5f, "retained PCM callback survives managed collection");
            AudioClip.PCMReaderCallback explicitPcm = new AudioClip.PCMReaderCallback((float[] samples) => samples[0] = 0.75f);
            explicitPcm(pcmSamples);
            Require(pcmSamples[0] == 0.75f, "explicit PCM delegate construction preserves shared writes");
            int audioCalls = 0;
            AudioSettings.AudioConfigurationChangeHandler audioChanged = changed => audioCalls++;
            var audioField = typeof(AudioSettings).GetField(nameof(AudioSettings.OnAudioConfigurationChanged),
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
            var publicAudioField = typeof(AudioSettings).GetField(nameof(AudioSettings.OnAudioConfigurationChanged),
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            Require(publicAudioField == null, "reflection retains original private event visibility");
            var originalAudioCallbacks = audioField.GetValue(null);
            try
            {
                AudioSettings.OnAudioConfigurationChanged += audioChanged;
                AudioSettings.OnAudioConfigurationChanged += audioChanged;
                AudioSettings.InvokeOnAudioConfigurationChanged(false);
                Require(audioCalls == 2, "static native event dispatch preserves duplicate subscriptions");
                AudioSettings.OnAudioConfigurationChanged -= audioChanged;
                AudioSettings.InvokeOnAudioConfigurationChanged(false);
                Require(audioCalls == 3, "static native event removes one matching subscription");
                AudioSettings.OnAudioConfigurationChanged -= audioChanged;
                AudioSettings.InvokeOnAudioConfigurationChanged(false);
                Require(audioCalls == 3, "static native event no longer dispatches removed callback");
                AudioSettings.OnAudioConfigurationChanged -= audioChanged;
                AudioSettings.InvokeOnAudioConfigurationChanged(false);
                Require(audioCalls == 3, "static event removal of absent callback is harmless");
                audioField.SetValue(null, audioChanged);
                ((AudioSettings.AudioConfigurationChangeHandler)audioField.GetValue(null)!)(false);
                AudioSettings.InvokeOnAudioConfigurationChanged(false);
                Require(audioCalls == 5, "private event reflection writes callback storage used by native dispatch");
                audioField.SetValue(null, null);
                AudioSettings.InvokeOnAudioConfigurationChanged(false);
                Require(audioField.GetValue(null) == null && audioCalls == 5, "private event reflection clears native callback storage");
                var cachedAudio = new CachedAudioReflectionProbe();
                cachedAudio.Write(audioChanged);
                AudioSettings.InvokeOnAudioConfigurationChanged(false);
                Require(cachedAudio.Read() != null && audioCalls == 6, "cached private FieldInfo writes storage used by native dispatch");
                audioField.SetValue(null, null);
                Require(cachedAudio.Read() == null, "cached private FieldInfo observes external storage changes");
            }
            finally
            {
                AudioSettings.OnAudioConfigurationChanged -= audioChanged;
                AudioSettings.OnAudioConfigurationChanged -= audioChanged;
                audioField.SetValue(null, originalAudioCallbacks);
            }
            Require((int)serialized.Quality == 2 && (int)serialized.SavedQuality == 1, "scalar enum field initializers");
            serialized.Quality = (ScheduleOne.ItemFramework.EQuality)3;
            serialized.SaveQuality();
            Require((int)serialized.SavedQuality == 3, "scalar enum fields cross assembly access");
            string scalarEnumJson = JsonUtility.ToJson(serialized);
            Require(scalarEnumJson.Contains("\"Quality\":3") && scalarEnumJson.Contains("\"savedQuality\":3"), "Unity serializes public and private enum fields");
            JsonUtility.FromJsonOverwrite("{\"Quality\":1,\"savedQuality\":2}", serialized);
            Require((int)serialized.Quality == 1 && (int)serialized.SavedQuality == 2, "Unity overwrites scalar enum native storage");
            serialized.SetQuality(0, 3);
            GC.Collect();
            Require((int)qualityAlias[0] == 3 && object.ReferenceEquals(qualityAlias, serialized.Qualities),
                "mapped enum arrays share component storage across library and managed collection");
            var qualityCopy = (ScheduleOne.ItemFramework.EQuality[])qualityAlias.Clone();
            qualityCopy[0] = (ScheduleOne.ItemFramework.EQuality)1;
            Require((int)qualityAlias[0] == 3 && (int)qualityCopy[0] == 1 && !object.ReferenceEquals(qualityCopy, qualityAlias),
                "mapped enum array clone has independent storage");
            string enumJson = JsonUtility.ToJson(serialized);
            Require(enumJson.Contains("\"Qualities\":[3,2]"), "Unity serializes mapped enum array values");
            JsonUtility.FromJsonOverwrite("{\"Qualities\":[1,0,2]}", serialized);
            Require(serialized.Qualities.Length == 3 && (int)serialized.Qualities[0] == 1 && qualityAlias.Length == 2 && (int)qualityAlias[0] == 3,
                "enum array JSON replacement preserves retained old storage");
            int[] serializedArrayAlias = serialized.Numbers;
            serializedArrayAlias[1] = 11;
            Require(serialized.Numbers.Length == 2 && serialized.Numbers[0] == 3 && serialized.Numbers[1] == 11 && serialized.FirstFlag,
                "serialized array initializers and cross-library aliases");
            serialized.Number++;
            serialized.Weights[0] = 0.375f;
            serialized.Letters[0] = 'Q';
            serialized.WideValues[0] = 9007199254740997L;
            string serializedJson = JsonUtility.ToJson(serialized);
            Require(serializedJson.Contains("\"Number\":8") && serializedJson.Contains("\"label\":\"before\"") &&
                !serializedJson.Contains("RuntimeOnly"), "public and private component field serialization across library");
            Require(serializedJson.Contains("\"Numbers\":[3,11]") && serializedJson.Contains("\"flags\":[true,false]"),
                "public and private scalar array fields appear in Unity JSON");
            serialized.Weights[0] = 0;
            serialized.Letters[0] = '\0';
            serialized.WideValues[0] = 0;
            JsonUtility.FromJsonOverwrite(serializedJson, serialized);
            Require(serialized.Weights[0] == 0.375f && serialized.Letters[0] == 'Q' && serialized.WideValues[0] == 9007199254740997L,
                "float char and precise 64-bit array values round-trip through Unity JSON");
            JsonUtility.FromJsonOverwrite("{\"Number\":91,\"label\":\"after\"}", serialized);
            Require(serialized.Number == 91 && serialized.Label == "after", "native serialization updates author field access");
            JsonUtility.FromJsonOverwrite("{\"Numbers\":[4,9,16],\"flags\":[false,true]}", serialized);
            Require(serialized.Numbers.Length == 3 && serialized.Numbers[2] == 16 && !serialized.FirstFlag &&
                serializedArrayAlias.Length == 2 && serializedArrayAlias[1] == 11,
                "Unity array overwrite replaces native storage without changing old aliases");
            serialized.SetNumbers(null!);
            Require(serialized.Numbers is null, "serialized array field accepts null");
            JsonUtility.FromJsonOverwrite("{\"Numbers\":[4,9,16]}", serialized);
            Require(serialized.Numbers is { Length: 3 } && serialized.Numbers[0] == 4,
                "Unity JSON restores a serialized array after null assignment");
            serialized.Linked = serializedOwner;
            serialized.SetAnchor(serializedOwner.transform);
            var linkedArray = serialized.LinkedObjects;
            linkedArray[0] = serializedOwner;
            serialized.SetFirstAnchor(serializedOwner.transform);
            Require(serialized.LinkedObjects.Length == 1 && serialized.LinkedObjects[0] == serializedOwner &&
                serialized.FirstAnchor == serializedOwner.transform, "serialized public and private reference array initializers and aliases");
            Require(object.ReferenceEquals(serialized.Linked, serializedOwner) && serialized.Anchor == serializedOwner.transform,
                "cross-library native object field identity");
            string objectJson = JsonUtility.ToJson(serialized);
            Require(objectJson.Contains("\"LinkedObjects\":[") && objectJson.Contains("\"anchors\":["),
                "public and private reference arrays appear in Unity JSON");
            serialized.Linked = null!;
            serialized.SetAnchor(null!);
            serialized.LinkedObjects = null!;
            serialized.SetFirstAnchor(null!);
            JsonUtility.FromJsonOverwrite(objectJson, serialized);
            Require(serialized.LinkedObjects is { Length: 1 } && serialized.LinkedObjects[0] == serializedOwner &&
                serialized.FirstAnchor == serializedOwner.transform && linkedArray[0] == serializedOwner,
                "Unity JSON restores reference arrays after null assignment without mutating old aliases");
            Require(serialized.Linked == serializedOwner && serialized.Anchor == serializedOwner.transform,
                "serialized native object references restore after null assignment");
            var serializedClone = UnityEngine.Object.Instantiate(serializedOwner);
            try
            {
                var clonedFields = serializedClone.GetComponent<LibrarySerializedComponent>();
                Require(clonedFields.Number == 91 && clonedFields.Label == "after", "serialized component runtime cloning");
                Require(clonedFields.Linked == serializedClone && clonedFields.Anchor == serializedClone.transform &&
                    serialized.Linked == serializedOwner, "cloned object fields remap internal references");
                Require(clonedFields.LinkedObjects[0] == serializedClone && clonedFields.FirstAnchor == serializedClone.transform &&
                    !object.ReferenceEquals(clonedFields.LinkedObjects, serialized.LinkedObjects),
                    "cloned reference arrays remap objects and own independent storage");
                clonedFields.LinkedObjects[0] = null!;
                clonedFields.SetFirstAnchor(null!);
                Require(serialized.LinkedObjects[0] == serializedOwner && serialized.FirstAnchor == serializedOwner.transform,
                    "cloned reference array mutations leave original fields unchanged");
                Require(clonedFields.Numbers.Length == 3 && clonedFields.Numbers[0] == 4 && !clonedFields.FirstFlag &&
                    !object.ReferenceEquals(clonedFields.Numbers, serialized.Numbers), "cloned scalar arrays have independent storage");
                clonedFields.Numbers[0] = 77;
                Require(serialized.Numbers![0] == 4 && clonedFields.Numbers[0] == 77, "cloned scalar array mutations remain independent");
                Require(clonedFields.Weights[0] == 0.375f && clonedFields.Letters[0] == 'Q' && clonedFields.WideValues[0] == 9007199254740997L,
                    "cloned float char and 64-bit arrays preserve serialized values");
            }
            finally { UnityEngine.Object.Destroy(serializedClone); }
            var report = new ScheduleOne.Reporting.ReportSubmission();
            var initialMetadata = new Dictionary<string, string> { ["first"] = "value" };
            SharedProbe.ReplaceMetadata(report, initialMetadata);
            Dictionary<string, string> metadata = SharedProbe.Metadata(report);
            Dictionary<string, string>.KeyCollection metadataKeys = metadata.Keys;
            Dictionary<string, string>.Enumerator metadataEnumerator = metadata.GetEnumerator();
            var metadataCursorCopy = metadataEnumerator;
            Require(metadataEnumerator.MoveNext() && metadataCursorCopy.MoveNext() &&
                metadataEnumerator.Current.Key == metadataCursorCopy.Current.Key, "dictionary cursor copies are independent");
            metadata["second"] = "next";
            Require(report.metadata.Count == 2 && metadataKeys.Count == 2 &&
                object.ReferenceEquals(metadata, initialMetadata), "cross-library dictionary live storage and key view");
            bool dictionaryInvalidated = false;
            try { metadataEnumerator.MoveNext(); } catch (InvalidOperationException) { dictionaryInvalidated = true; }
            Require(dictionaryInvalidated && metadata.OrderBy(pair => pair.Key).Last().Value == "next",
                "dictionary mutation invalidation and managed LINQ");
            var metadataCopy = new Dictionary<string, string>(metadata);
            metadataCopy.Remove("first");
            var localDictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["KEY"] = "local" };
            Require(report.metadata.ContainsKey("first") && localDictionary.ContainsKey("key") &&
                localDictionary.GetType() == typeof(Dictionary<string, string>), "dictionary copies and unrelated managed comparers");
            var foldedMetadata = new[] { "KEY" }.ToDictionary(key => key, key => "folded", StringComparer.OrdinalIgnoreCase);
            SharedProbe.ReplaceMetadata(report, foldedMetadata);
            Require(report.metadata.ContainsKey("key") && SharedProbe.Metadata(report).Comparer.Equals("KEY", "key"),
                "ToDictionary preserves ordinal-ignore-case comparison in native storage");
            var customComparer = new KeyLengthComparer();
            var customMetadata = new Dictionary<string, string>(customComparer) { ["one"] = "custom" };
            SharedProbe.ReplaceMetadata(report, customMetadata);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Require(report.metadata.ContainsKey("two") && report.metadata["two"] == "custom" &&
                object.ReferenceEquals(SharedProbe.Metadata(report).Comparer, customComparer),
                "custom comparer callbacks and identity through native library storage after collection");
            var settings = ScriptableObject.CreateInstance<DeveloperSettings>();
            try
            {
                List<string> properties = SharedProbe.Properties(settings);
                properties.Add("compiler probe");
                Require(settings.OwnedProperties.Count == 1 && object.ReferenceEquals(properties, settings.OwnedProperties),
                    "cross-library live string list");
                var unrelated = new List<string> { "managed" };
                Require(unrelated.GetType() == typeof(List<string>), "unrelated scalar list retains CLR type");
            }
            finally { UnityEngine.Object.Destroy(settings); }
            var vertices = new UnityEngine.UI.VertexHelper();
            try
            {
                vertices.AddTriangle(0, 1, 2);
                List<int> indices = SharedProbe.Indices(vertices);
                indices.Add(3);
                Require(vertices.currentIndexCount == 4 && object.ReferenceEquals(indices, vertices.m_Indices),
                    "cross-library live numeric list");
            }
            finally { vertices.Dispose(); }
            var trashData = new ScheduleOne.Persistence.TrashContentData();
            int[] trashCounts = { 3, 4, 5 };
            SharedProbe.ReplaceTrashCounts(trashData, trashCounts);
            trashCounts[0] = 9;
            int[] nativeCounts = SharedProbe.TrashCounts(trashData);
            nativeCounts[1]++;
            Require(trashData.TrashQuantities[0] == 9 && trashCounts[1] == 5 &&
                object.ReferenceEquals(nativeCounts, trashCounts), "cross-library scalar array storage and identity");
            Array.Copy(trashCounts, 0, trashCounts, 1, 2);
            Require(trashData.TrashQuantities[1] == 9 && trashData.TrashQuantities[2] == 5,
                "native scalar array overlapping copy");
            int[] copiedCounts = (int[])trashCounts.Clone();
            SharedProbe.ReplaceTrashCounts(trashData, copiedCounts);
            Array.Resize(ref copiedCounts, 5);
            copiedCounts[0] = 12;
            Require(copiedCounts.Length == 5 && copiedCounts[4] == 0 && trashData.TrashQuantities[0] == 9 &&
                trashCounts[0] == 9, "native scalar array clone and resize preserve independent storage");
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Require(nativeCounts[2] == 5 && copiedCounts[0] == 12, "native scalar arrays retained through managed collection");
            var npcValue = new ScheduleOne.Persistence.Datas.DynamicSaveData(new ScheduleOne.Persistence.Datas.NPCData("compiler-array-probe"));
            ScheduleOne.Persistence.Datas.DynamicSaveData[] npcValues = { npcValue, null! };
            var npcData = new ScheduleOne.Persistence.Datas.NPCCollectionData(npcValues);
            var npcAlias = SharedProbe.NpcData(npcData);
            npcAlias[1] = npcValue;
            Require(object.ReferenceEquals(npcValues, npcAlias) && object.ReferenceEquals(npcValues[1], npcValue),
                "cross-library native reference array aliases and element assignment");
            ScheduleOne.Persistence.Datas.SaveData[] covariant = npcValues;
            bool rejectedReference = false;
            try { covariant[0] = new ScheduleOne.Persistence.Datas.NPCData("invalid-array-element"); }
            catch (ArrayTypeMismatchException) { rejectedReference = true; }
            Require(rejectedReference && object.ReferenceEquals(npcValues[0], npcValue),
                "native reference array rejects incompatible covariant store");
            var npcClone = (ScheduleOne.Persistence.Datas.SaveData[])covariant.Clone();
            bool rejectedClone = false;
            try { npcClone[0] = new ScheduleOne.Persistence.Datas.NPCData("invalid-clone-element"); }
            catch (ArrayTypeMismatchException) { rejectedClone = true; }
            Require(rejectedClone && !object.ReferenceEquals(npcClone, covariant) && object.ReferenceEquals(npcClone[0], npcValue),
                "native reference array clone preserves runtime element type");
            Array.Resize(ref covariant, 3);
            covariant[2] = new ScheduleOne.Persistence.Datas.NPCData("valid-resized-element");
            Require(covariant.Length == 3 && npcValues.Length == 2 && covariant[2] is ScheduleOne.Persistence.Datas.NPCData,
                "native reference array resize allocates static element type");
            npcAlias[0] = null!;
            Require(npcData.NPCs[0] == null && npcValues[0] == null,
                "native reference array null store remains shared");
            SharedProbe.WriteNpcData(npcData, npcValue);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Require(object.ReferenceEquals(npcData.NPCs[0], npcValue) && npcData.NPCs[1].BaseData.Contains("compiler-array-probe"),
                "native reference array contents retained through managed collection");
            var mixedSource = SharedProbe.CreateNpcReferences("mixed-reference-copy");
            var mixedDestination = new ScheduleOne.Persistence.Datas.DynamicSaveData[1];
            ScheduleOne.Persistence.Datas.SaveData[] mixedView = mixedDestination;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Array.Copy(mixedSource, mixedView, 1);
            Require(mixedDestination.GetType() == typeof(ScheduleOne.Persistence.Datas.DynamicSaveData[]) &&
                mixedDestination[0].BaseData.Contains("mixed-reference-copy") && object.ReferenceEquals(mixedDestination[0], mixedSource[0]),
                "native reference copy retypes elements for covariant managed destination");
            var iteratorObject = new GameObject("S1Interop native iterator probe");
            iteratorObject.transform.SetParent(created.transform, false);
            iteratorObject.SetActive(false);
            var lightning = iteratorObject.AddComponent<LightningLightController>();
            lightning.lightEntries = new LightningLightController.LightEntry[0];
            System.Collections.IEnumerator nativeRoutine = lightning.DoStrikeRoutine();
            Require(nativeRoutine.MoveNext() && nativeRoutine.Current == null && nativeRoutine.MoveNext(),
                "native game iterator through managed IEnumerator");
            Require(nativeRoutine is IDisposable, "native iterator exposes managed disposal");
            ((IDisposable)nativeRoutine).Dispose();
            created.AddComponent<BoxCollider>();
            // GetComponent<Collider> returns a base proxy on IL2CPP. CLR casts alone cannot retype it.
            Component component = created.GetComponent<Collider>();
            Require(component is BoxCollider box && box != null, "declaration pattern");
            Require(component as BoxCollider != null, "as cast");
            Require((BoxCollider)component != null, "explicit cast");
            object? absent = null;
            Require((BoxCollider?)absent is null, "null cast");
            Require(!(component is Light), "negative type check");
            Require(component switch { Light => false, BoxCollider => true, _ => false }, "native switch dispatch");

            var counter = created.AddComponent<CompilerCounter>();
            LibraryCounterBase inherited = created.AddComponent<InheritedCounter>();
            Require(inherited.Read() == 7 && inherited.BaseAwakeCalls == 1,
                "cross-library abstract base construction and Awake dispatch");
            var libraryComponent = created.AddComponent<LibraryComponent>();
            Func<System.Collections.IEnumerator> libraryStart = libraryComponent.Start;
            var libraryIterator = libraryStart();
            Require(libraryIterator.MoveNext() && !libraryIterator.MoveNext() && nameof(libraryComponent.Start) == "Start",
                "cross-library coroutine lifecycle method group");
            Require(counter.AwakeCalls == 1, "injected Awake callback");
            Require(counter.Value == 7 && counter.Values.Count == 1, "injected constructor and managed helper");
            var rediscovered = created.GetComponent<MonoBehaviour>() as CompilerCounter;
            Require(object.ReferenceEquals(counter, rediscovered), "injected managed identity");
            Require(object.ReferenceEquals(component, (BoxCollider)component), "native reference identity through downcast");

            var button = created.AddComponent<UnityEngine.UI.Button>();
            UnityEngine.EventSystems.IPointerClickHandler handler = button;
            Require(handler != null && ((UnityEngine.EventSystems.IPointerClickHandler)(object)button) != null,
                "native interface implicit and explicit conversions");

            var registry = NPCManager.NPCRegistry;
            int before = registry.Count;
            var liveQuery = registry.Where(npc => npc == null);
            int absentBefore = liveQuery.Count();
            registry.Add(null!);
            try
            {
                Require(NPCManager.NPCRegistry.Count == before + 1, "live list identity");
                Require(liveQuery.Count() == absentBefore + 1, "lazy native collection LINQ");
            }
            finally { registry.RemoveAt(before); }
            Require(registry.Count == before, "list mutation restoration");
            List<NPC> declared = NPCManager.NPCRegistry;
            Require(object.ReferenceEquals(declared, registry), "declared live List identity");
            var readOnly = declared.AsReadOnly();
            var enumerator = declared.GetEnumerator();
            declared.Add(null!);
            try
            {
                Require(readOnly.Count == before + 1 && NPCManager.NPCRegistry.Count == before + 1, "live read-only list view");
                bool invalidated = false;
                try { enumerator.MoveNext(); }
                catch (InvalidOperationException) { invalidated = true; }
                Require(invalidated, "managed enumeration invalidation exception");
            }
            finally { declared.RemoveAt(before); }
            List<NPC> copied = declared.Where(npc => npc != null).ToList();
            copied.Add(null!);
            Require(declared.Count == before, "ToList owns independent storage");
            var components = new List<Component> { component };
            Require(components.Contains((BoxCollider)component), "native List equality through retyped element");
            components.AddRange(components);
            components.RemoveAll(value => value == component);
            Require(components.Count == 0, "list self-add and managed predicate");
            var unique = new HashSet<Component> { component, (BoxCollider)component };
            Require(unique.Count == 1, "native default hash equality");
            var keyed = new Dictionary<Component, int> { [component] = 7 };
            Require(keyed[(BoxCollider)component] == 7, "native dictionary lookup through retyped key");
            Require(new Component[] { component, (BoxCollider)component }.Distinct().Count() == 1,
                "native LINQ default equality");

            var callbacks = new CallbackProbe();
            var signal = new UnityEvent();
            signal.AddListener(callbacks.Increment);
            signal.Invoke();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            signal.RemoveListener(callbacks.Increment);
            signal.Invoke();
            Require(callbacks.Calls == 1, "method-group removal after managed collection");
            UnityAction callback = () => callbacks.Increment();
            callback();
            signal.AddListener(callback);
            signal.Invoke();
            signal.RemoveListener(callback);
            signal.Invoke();
            Require(callbacks.Calls == 3, "delegate variable invocation and removal");
            Require(object.ReferenceEquals(SharedProbe.Registry, declared), "cross-mod list identity");
            Action sharedCallback = callbacks.Increment;
            SharedProbe.AddListener(signal, sharedCallback);
            signal.Invoke();
            signal.RemoveListener(sharedCallback.Invoke);
            signal.Invoke();
            Require(callbacks.Calls == 4, "cross-mod native callback removal");
            signal.AddListener(sharedCallback.Invoke);
            SharedProbe.RemoveListener(signal, sharedCallback);
            signal.Invoke();
            Require(callbacks.Calls == 4, "cross-mod reverse callback removal");
            var authoredSignal = new LibrarySignal();
            authoredSignal.Changed += callbacks.Increment;
            authoredSignal.Changed += callbacks.Increment;
            authoredSignal.Raise();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            authoredSignal.Changed -= callbacks.Increment;
            authoredSignal.Raise();
            authoredSignal.Changed -= callbacks.Increment;
            authoredSignal.Raise();
            Require(callbacks.Calls == 7, "cross-library authored multicast event and removal after collection");
            Require(new object[] { component, null! }.OfType<BoxCollider>().Single() != null, "native LINQ type filter");
            Require(Quaternion.IsEqualUsingDot(1f), "private engine method access");
            Require(Quaternion.identityQuaternion.w == 1f, "private readonly engine field access");
            Require(object.ReferenceEquals(ScheduleOne.DevUtilities.NetworkSingleton<NPCManager>.instance, NPCManager.Instance),
                "private generic game field access");
            counter.StartCoroutine(CompleteProbe(counter, created, token, sceneName));
            coroutineStarted = true;
        }
        catch (Exception exception)
        {
            LoggerInstance.Error($"S1Compiler|FAIL|Token={token}|{exception}");
        }
        finally
        {
            if (!coroutineStarted)
            {
                if (created != null) UnityEngine.Object.Destroy(created);
                Application.Quit();
            }
        }
    }

    private System.Collections.IEnumerator CompleteProbe(CompilerCounter owner, GameObject created, string token, string sceneName)
    {
        var checks = CoroutineChecks(owner);
        try
        {
            while (true)
            {
                bool more = false;
                object? current = null;
                Exception? failure = null;
                try { more = checks.MoveNext(); if (more) current = checks.Current; }
                catch (Exception exception) { failure = exception; }
                if (failure != null)
                {
                    LoggerInstance.Error($"S1Compiler|FAIL|Token={token}|{failure}");
                    yield break;
                }
                if (!more) break;
                yield return current;
            }
            LoggerInstance.Msg($"S1Compiler|PASS|Token={token}|Version={Application.version}|Scene={sceneName}|Checks={passedChecks}");
        }
        finally
        {
            (checks as IDisposable)?.Dispose();
            UnityEngine.Object.Destroy(created);
            Application.Quit();
        }
    }

    private static System.Collections.IEnumerator CoroutineChecks(CompilerCounter owner)
    {
        int calls = 0;
        var routine = Nested(() => calls++);
        var started = owner.StartCoroutine(routine);
        Require(started != null && calls == 1, "managed coroutine starts immediately");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        owner.StopCoroutine(routine);
        yield return null;
        Require(calls == 1, "same iterator stops the native coroutine");
        yield return Nested(() => calls++);
        Require(calls == 3, "nested managed iterator resumes parent");
        float before = Time.time;
        yield return new WaitForSeconds(0.02f);
        Require(Time.time > before, "native yield instruction resumes managed iterator");
        Require(owner.StartCalls == 2, "automatic IEnumerator Start lifecycle");
        Require(owner.GetComponent<InheritedCounter>().InheritedStartCalls == 2,
            "cross-library abstract IEnumerator Start override");
    }

    private static System.Collections.IEnumerator Nested(Action step)
    {
        step();
        yield return null;
        step();
    }

    private static int passedChecks;

    private static void Require(bool condition, string scenario)
    {
        if (!condition) throw new InvalidOperationException(scenario);
        passedChecks++;
    }
}

public sealed class CallbackProbe
{
    public int Calls;
    public void Increment() { Calls++; }
}

public sealed class KeyLengthComparer : IEqualityComparer<string>
{
    public bool Equals(string? left, string? right) => left?.Length == right?.Length;
    public int GetHashCode(string value) => value.Length;
}

public sealed class InheritedCounter : LibraryCounterBase
{
    public InheritedCounter() { value += 2; }
    protected override void Awake() { base.Awake(); }
    public override int Read() => value;
    protected override System.Collections.IEnumerator Start()
    {
        InheritedStartCalls++;
        yield return null;
        InheritedStartCalls++;
    }
}

public sealed class CompilerCounter : MonoBehaviour
{
    [NonSerialized]
    public int AwakeCalls;
    [NonSerialized]
    public int StartCalls;
    [NonSerialized]
    public int Value = 4;

    public CompilerCounter() { Value += 3; }

    private void Awake() { AwakeCalls++; }

    private System.Collections.IEnumerator Start()
    {
        StartCalls++;
        yield return null;
        StartCalls++;
    }

    public System.Collections.Generic.List<string> Values => new System.Collections.Generic.List<string> { "managed" };
}
