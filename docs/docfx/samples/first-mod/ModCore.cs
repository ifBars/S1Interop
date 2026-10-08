using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(MyFirstMod.ModCore), "MyFirstMod", "0.1.0", "YourName")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace MyFirstMod;

public sealed class ModCore : MelonMod
{
    public const string ModName = "MyFirstMod";

    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg($"{ModName} loaded on {S1Interop.Generated.S1InteropRuntime.Backend}.");
    }

    public override void OnUpdate()
    {
        // NPCManager is ScheduleOne.NPCs.NPCManager on Mono and Il2CppScheduleOne.NPCs.NPCManager on IL2CPP.
        // The S1InteropUsing items in the .csproj import the right namespace, so this file needs no #if blocks.
        if (Input.GetKeyDown(KeyCode.F8))
        {
            LoggerInstance.Msg($"{NPCManager.NPCRegistry.Count} NPCs are registered.");
        }
    }
}
