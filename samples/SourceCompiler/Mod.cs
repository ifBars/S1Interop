using MelonLoader;
using ScheduleOne.NPCs;
using ScheduleOne.PlayerScripts;
using UnityEngine;

[assembly: MelonInfo(typeof(SourceCompilerSample.Mod), "S1Interop Source Compiler Sample", "0.0.1", "S1Interop contributors")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace SourceCompilerSample;

public sealed class Mod : MelonMod
{
    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        LoggerInstance.Msg($"Source compiler: {sceneName}; {Player.PlayerList.Count} players, {NPCManager.NPCRegistry.Count} NPCs.");
    }

    // This ordinary C# cast is lowered to native proxy retyping on IL2CPP.
    public static Player? AsPlayer(Object candidate) => candidate as Player;
}
