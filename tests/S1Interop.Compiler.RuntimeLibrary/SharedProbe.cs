using System;
using System.Collections.Generic;
using ScheduleOne.NPCs;
using UnityEngine.Events;

namespace S1Interop.CompilerSmoke.Library;

public sealed class LibrarySignal
{
    // Private members are absent from the authoring reference: consumers must read accessor metadata.
    private void __S1InteropEvent_add_Changed() { }
    public event UnityAction Changed = null!;
    public void Raise() => Changed?.Invoke();
}

public abstract class LibraryCounterBase : UnityEngine.MonoBehaviour
{
    [NonSerialized] public int BaseAwakeCalls;
    [NonSerialized] public int InheritedStartCalls;
    [NonSerialized] protected int value = 2;
    protected LibraryCounterBase() { value += 3; }
    protected virtual void Awake() { BaseAwakeCalls++; }
    public abstract int Read();
    protected abstract System.Collections.IEnumerator Start();
}

public sealed class LibraryComponent : UnityEngine.MonoBehaviour
{
    public System.Collections.IEnumerator Start() { yield return null; }
}

public abstract class LibrarySerializedBase : UnityEngine.MonoBehaviour
{
    public int Number = 3;
    [UnityEngine.SerializeField] private string label = "before";
    [NonSerialized] public int RuntimeOnly = 23;
    public UnityEngine.GameObject Linked = null!;
    public UnityEngine.Object Resource = null!;
    public UnityEngine.GameObject[] LinkedObjects = [null!];
    public UnityEngine.Object[] Resources = [null!];
    [UnityEngine.SerializeField] private UnityEngine.Transform[] anchors = { null! };
    public UnityEngine.Transform FirstAnchor => anchors[0];
    public void SetFirstAnchor(UnityEngine.Transform value) { anchors[0] = value; }
    public void SetResources(UnityEngine.Object[] values) { Resources = values; }
    public void SetResourceElement(UnityEngine.Object value) { Resources[0] = value; }
    public int[] Numbers = [3, 7];
    public ScheduleOne.ItemFramework.EQuality Quality = (ScheduleOne.ItemFramework.EQuality)2;
    [UnityEngine.SerializeField] private ScheduleOne.ItemFramework.EQuality savedQuality = (ScheduleOne.ItemFramework.EQuality)1;
    public ScheduleOne.ItemFramework.EQuality SavedQuality => savedQuality;
    public void SaveQuality() { savedQuality = Quality; }
    public ScheduleOne.ItemFramework.EQuality[] Qualities = [(ScheduleOne.ItemFramework.EQuality)1, (ScheduleOne.ItemFramework.EQuality)2];
    public void SetQuality(int index, int value) { Qualities[index] = (ScheduleOne.ItemFramework.EQuality)value; }
    public byte[] Payload = [65, 66, 67];
    public void DecodePayload(string value) { Payload = Convert.FromBase64String(value); }
    public void SetBinaryPayload(int value) { Payload = BitConverter.GetBytes(value); }
    public int ReadBinaryPayload() => BitConverter.ToInt32(Payload, 0);
    public string ReadPayload(int index, int count) => System.Text.Encoding.UTF8.GetString(Payload, index, count);
    public string UsePayload(Func<byte[], string> callback) => callback(Payload);
    public float[] Weights = [0.125f];
    public char[] Letters = ['Z'];
    public long[] WideValues = [9007199254740993L];
    [UnityEngine.SerializeField] private bool[] flags = { true, false };
    [UnityEngine.SerializeField] private UnityEngine.Transform anchor = null!;
    public string Label => label;
    public void SetLabel(string value) { label = value; }
    public UnityEngine.Transform Anchor => anchor;
    public void SetAnchor(UnityEngine.Transform value) { anchor = value; }
    public void SetResource(UnityEngine.Object value) { Resource = value; }
    public void SetNumbers(int[] value) { Numbers = value; }
    public bool FirstFlag => flags[0];
    protected LibrarySerializedBase() { Number += 4; }
}

// The concrete type declares no fields: it must still inherit working native storage and GC scanning.
public sealed class LibrarySerializedComponent : LibrarySerializedBase { }

public static class SharedProbe
{
    public static ScheduleOne.Persistence.Datas.DynamicSaveData[] NpcData(ScheduleOne.Persistence.Datas.NPCCollectionData data) => data.NPCs;
    public static void ReplaceNpcData(ScheduleOne.Persistence.Datas.NPCCollectionData data, ScheduleOne.Persistence.Datas.DynamicSaveData[] values) => data.NPCs = values;
    public static void WriteNpcData(ScheduleOne.Persistence.Datas.NPCCollectionData data, ScheduleOne.Persistence.Datas.DynamicSaveData value) => data.NPCs[0] = value;
    public static ScheduleOne.Persistence.Datas.SaveData[] CreateNpcReferences(string id)
    {
        ScheduleOne.Persistence.Datas.DynamicSaveData[] values = { new(new ScheduleOne.Persistence.Datas.NPCData(id)) };
        return new ScheduleOne.Persistence.Datas.NPCCollectionData(values).NPCs;
    }
    public static int[] TrashCounts(ScheduleOne.Persistence.TrashContentData data) => data.TrashQuantities;
    public static void ReplaceTrashCounts(ScheduleOne.Persistence.TrashContentData data, int[] values) => data.TrashQuantities = ThroughGeneric<string>(values);
    private static int[] ThroughGeneric<T>(int[] values) => values;
    public static Dictionary<string, string> Metadata(ScheduleOne.Reporting.ReportSubmission report) => report.metadata;
    public static void ReplaceMetadata(ScheduleOne.Reporting.ReportSubmission report, Dictionary<string, string> metadata) => report.metadata = metadata;
    public static List<string> Properties(DeveloperSettings settings) => settings.OwnedProperties;
    public static List<int> Indices(UnityEngine.UI.VertexHelper helper) => helper.m_Indices;
    public static List<NPC> Registry => NPCManager.NPCRegistry;
    public static void AddListener(UnityEvent signal, Action callback) => signal.AddListener(callback.Invoke);
    public static void RemoveListener(UnityEvent signal, Action callback) => signal.RemoveListener(callback.Invoke);
}
