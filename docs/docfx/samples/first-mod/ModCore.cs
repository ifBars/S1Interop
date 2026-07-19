using MelonLoader;
using S1Interop.Generated;

[assembly: MelonInfo(typeof(MyFirstMod.ModCore), "MyFirstMod", "0.1.0", "YourName")]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace MyFirstMod;

public sealed class ModCore : MelonMod
{
    public override void OnInitializeMelon()
    {
        LoggerInstance.Msg($"MyFirstMod loaded on {S1InteropRuntime.Backend}.");
    }
}
