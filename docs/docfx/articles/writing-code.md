---
title: Write game code
description: Read game objects and add a visible feature using ordinary Schedule One and Unity C#.
uid: s1interop.writing-code
---

# Write game code

Your mod calls the game's C# API directly. Use `ScheduleOne.*` for game systems, `UnityEngine` for objects and components, and `MelonLoader` for your mod's entry point. S1Interop handles supported Mono/IL2CPP differences when you build.

Start with the project from [Build your first mod](first-mod.md). Open its `.csproj` in your C# editor and edit `Mod.cs`.

## Read the game and add a player light

Replace `Mod.cs` with the code below. Keep your own mod name in `MelonInfo` if you chose something other than `MyMod`.

- **F8** prints your player's position and the names of registered NPCs to the MelonLoader console.
- **F9** toggles a light attached to your player. Try it somewhere dark.

```csharp
using MelonLoader;
using ScheduleOne.NPCs;
using ScheduleOne.PlayerScripts;
using UnityEngine;

[assembly: MelonInfo(typeof(Entry), "MyMod", "0.1.0", "Mod author")]
[assembly: MelonGame("TVGS", "Schedule I")]

public sealed class Entry : MelonMod
{
    private Light? playerLight;

    public override void OnInitializeMelon() => LoggerInstance.Msg("MyMod loaded.");

    public override void OnUpdate()
    {
        bool report = Input.GetKeyDown(KeyCode.F8);
        bool toggleLight = Input.GetKeyDown(KeyCode.F9);
        if (!report && !toggleLight)
            return;

        var player = Player.Local;
        if (player == null)
        {
            LoggerInstance.Msg("Load a save before using this mod.");
            return;
        }

        if (report)
        {
            LoggerInstance.Msg($"Your position: {player.transform.position}");
            LoggerInstance.Msg($"Registered NPCs: {NPCManager.NPCRegistry.Count}");
            foreach (var npc in NPCManager.NPCRegistry)
            {
                if (npc != null)
                    LoggerInstance.Msg(npc.FirstName);
            }
        }

        if (toggleLight)
        {
            if (playerLight == null)
            {
                var lightObject = new GameObject("MyMod player light");
                lightObject.transform.SetParent(player.transform, false);
                lightObject.transform.localPosition = Vector3.up * 1.5f;
                playerLight = lightObject.AddComponent<Light>();
                playerLight.range = 10f;
                playerLight.intensity = 2f;
                playerLight.enabled = false;
            }

            playerLight.enabled = !playerLight.enabled;
            LoggerInstance.Msg($"Player light enabled: {playerLight.enabled}");
        }
    }

    public override void OnDeinitializeMelon()
    {
        if (playerLight != null)
            Object.Destroy(playerLight.gameObject);
    }
}
```

Build and deploy using the [first-mod instructions](first-mod.md#build-both-runtimes), then load a save. Press F8 and check the log. Press F9 twice to turn the light on and off. Test both runtimes before sharing the mod.

The light is a local visual effect. This example doesn't save it or synchronize it with other players.

## How the code reaches the game

`Player.Local` gets the local player's existing game object. Its `transform.position` is a Unity property. `NPCManager.NPCRegistry` gets the game's NPC collection; each entry has game members such as `FirstName`. These are game APIs, not S1Interop wrappers.

`new GameObject` creates an object owned by the mod. `SetParent` makes it follow the player, and `AddComponent<Light>()` adds a built-in Unity component. Assigning `range`, `intensity`, and `enabled` changes that component just as it would in ordinary Unity C#.

`OnInitializeMelon` runs when the mod loads, before a save's objects are necessarily available. `OnUpdate` runs every frame; this example only accesses the game when you press a key. The `Player.Local` null check handles the main menu and loading transitions.

The light is created once and reused. Parenting it to the player ties its lifetime to that player object. Unity's null check also detects a destroyed component, so a later key press can create it again after changing saves. `OnDeinitializeMelon` removes it when MelonLoader deinitializes the mod.

## Find the API for your feature

Use completion and Go to Definition on game types in your configured project. Start with an object you have, such as `Player.Local`, and inspect its members. When signatures aren't enough, inspect your local Mono `Schedule I_Data/Managed/Assembly-CSharp.dll` in a .NET decompiler to see how the game uses that member.

Write the same `ScheduleOne.*` imports for both builds. You don't need an `Il2CppScheduleOne` import, `#if IL2CPP`, or a wrapper declaration for this example. Keep your code in your own source files; files under `obj` are build output.

An accessible member isn't automatically safe to change. Check how the game manages its lifetime, saves its value, or synchronizes it before modifying gameplay state. [Compatibility](compatibility.md) lists compiler gaps; [Everyday development](common-tasks.md) covers the edit/build/test loop.
