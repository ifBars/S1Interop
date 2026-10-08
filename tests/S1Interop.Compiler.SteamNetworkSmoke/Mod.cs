using System;
using MelonLoader;
[assembly: MelonInfo(typeof(SteamProbe), "S1Interop SteamNetwork Runtime Smoke", "0.0.1", "S1Interop contributors")]
[assembly: MelonGame("TVGS", "Schedule I")]
public sealed class SteamProbe : MelonMod
{
    private bool ran;
    public override void OnSceneWasInitialized(int buildIndex, string sceneName)
    {
        if (ran || sceneName != "Menu") return;
        ran = true;
        string token = Environment.GetEnvironmentVariable("S1INTEROP_SMOKE_TOKEN") ?? "missing";
        try { int checks = RuntimeChecks.Run(); LoggerInstance.Msg($"S1Compiler|PASS|Token={token}|Scene={sceneName}|Checks={checks}|Case=SteamNetworkLibTransportOverride"); }
        catch (Exception e) { LoggerInstance.Error($"S1Compiler|FAIL|Token={token}|{e}"); }
    }
}
