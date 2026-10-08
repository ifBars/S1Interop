using HarmonyLib;
using ScheduleOne.Networking;

namespace S1Interop.CompilerSmoke;

internal static class BiggerLobbiesReflectionProbe
{
    // The unchanged Mono expression from BiggerLobbies/Integrations/HarmonyPatches.cs.
    // The full original mod is also compiled through the controlled corpus harness.
    internal static ILobbyService? GetLobbyService(Lobby? lobby)
    {
        return Traverse.Create(lobby).Field("_lobbyService").GetValue<ILobbyService>();
    }
}
