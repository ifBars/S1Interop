---
title: Earlier generator first mod
description: Set up MelonLoader, create a C# project, load your first Schedule I mod, and change its code.
uid: s1interop.legacy-first-mod
---

# Earlier generator first mod

You will build a mod that prints a message when Schedule I starts. No Unity project or previous modding experience is required. Begin with the game runtime you already have installed.

All terminal examples on this page use **PowerShell on Windows**. Run each command separately and stop on an error. Commands belong in PowerShell, C# belongs in `.cs` files, and XML belongs in `.props` or `.csproj` files.

## Before you start

1. Follow [Install S1Interop](getting-started.md), including its published-version notes. Check `s1interop --version`.
2. Install Schedule I and launch it once normally. Close the game.
3. Locate the game folder in Steam: right-click Schedule I and open **Manage > Browse local files**. Keep the folder path containing `Schedule I.exe`.
4. Install MelonLoader using its [official installer instructions](https://github.com/LavaGang/MelonLoader#how-to-use-the-installer). Select this game's executable and follow the loader's runtime prerequisites.
5. Launch the game with MelonLoader, wait for first-run setup, and close it. Confirm the `MelonLoader` and `Mods` folders exist. For IL2CPP, confirm `MelonLoader\Il2CppAssemblies` contains generated DLLs.

Use an editor that supports C#, or a plain text editor for this first exercise. You can open the generated `.sln` in an IDE later.

### Choose the runtime you have installed

Mono and IL2CPP are two ways Unity runs the game's code. They need different build references and different mod DLLs.

| Installed branch | First build | Expected references |
| --- | --- | --- |
| Public/default (`none` in Steam), or `beta` | `Debug Il2Cpp` | `MelonLoader\Il2CppAssemblies` |
| `alternate`, or `alternate-beta` | `Debug Mono` | `Schedule I_Data\Managed` |

Use your current branch for the first result. Steam replaces files when switching branches, so one folder cannot serve as both reference sets at once. See [Local game paths](local-paths.md) when you add the second runtime.

## 1. Create your project outside the game folder

Open PowerShell in a folder for your own projects, such as `Documents\Mods`. If using the candidate source installation, keep that terminal open and change folders with `Set-Location`.

Preview the files, then create them:

```powershell
s1interop new --legacy-generator .\MyFirstMod
s1interop new --legacy-generator .\MyFirstMod --apply
Set-Location .\MyFirstMod
```

The first command writes nothing. The second requires an empty or nonexistent target folder. The final command enters the new project; `.` in subsequent commands means this folder.

| File | Purpose |
| --- | --- |
| `ModCore.cs` | Your C# source and the message it logs |
| `MyFirstMod.csproj` | Compiler settings, dependencies, and build configurations |
| `MyFirstMod.sln` | The solution to open in an IDE |
| `local.build.props.example` | A template for paths on your computer |
| `.gitignore` | Keeps local paths and generated build files out of Git |

## 2. Point the project at your game

Replace the example path with the folder you found in Steam. For IL2CPP:

```powershell
s1interop doctor . --il2cpp-game-path "D:\Games\Schedule I"
s1interop setup . --il2cpp-game-path "D:\Games\Schedule I"
s1interop setup . --il2cpp-game-path "D:\Games\Schedule I" --apply
```

For Mono, use `--mono-game-path` in all three commands instead. Repeat the path on each command: `doctor` is read-only and does not save its arguments. Automatic detection is available by omitting the flag.

The candidate requires at least one ready runtime for the legacy generator scaffold. **Published alpha.1 requires Mono for automatic setup**, even for an IL2CPP build. An alpha.1 user with only IL2CPP should use the manual [local configuration](local-paths.md#configure-a-new-project).

Expect a `[ready]` check for your runtime. Fix missing game/loader references before continuing. `setup --apply` writes only the ignored `local.build.props`; it does not install software or overwrite existing configuration. Edit that file directly when a path changes.

## 3. Build the mod

With the game closed, run **one** command, matching your installed game:

```powershell
dotnet build -c "Debug Il2Cpp"
```

or:

```powershell
dotnet build -c "Debug Mono"
```

The first build restores packages from NuGet (or your candidate feed). Wait for `Build succeeded`. The build compiles `MyFirstMod.dll`, the file MelonLoader loads, and copies it into the matching install's `Mods` folder. The last output line shows where it went:

```text
MyFirstMod -> D:\Games\Schedule I\Mods\MyFirstMod.dll
```

Debug builds deploy this way; Release builds only write `bin\`. If the build fails, start with the first error in [Troubleshooting](troubleshooting.md).

## 4. Load it in Schedule I

Start the install you built for. `dotnet run` builds, deploys, and starts it in one step:

```powershell
dotnet run -c "Debug Il2Cpp"
```

Use `"Debug Mono"` for a Mono install. Starting the game from Steam works too.

Look in the MelonLoader console or `MelonLoader\Latest.log` for the line matching your runtime:

```text
MyFirstMod loaded on Mono.
MyFirstMod loaded on Il2Cpp.
```

MelonLoader adds timestamps and a mod-name prefix around the message. A successful build alone does not prove the mod loaded. Then load a save and press F8: the mod logs how many NPCs the game has registered.

If the line is missing, confirm the DLL is in this install's `Mods` folder and check loader errors. To uninstall, close the game and remove `Mods\MyFirstMod.dll` and `Mods\MyFirstMod.pdb`.

## 5. Make your first code change

Open `ModCore.cs`. The assembly attributes tell MelonLoader the mod name, version, author, and game. `ModCore` inherits `MelonMod`; MelonLoader calls `OnInitializeMelon` when it loads the mod and `OnUpdate` every frame.

The starter is equivalent to this complete example:

[!code-csharp[](../samples/first-mod/ModCore.cs)]

`NPCManager` lives in `ScheduleOne.NPCs` on Mono and `Il2CppScheduleOne.NPCs` on IL2CPP. The `S1InteropUsing` items in `MyFirstMod.csproj` import the right one for each build, so the same file compiles for both runtimes. [Write code for both runtimes](dual-runtime-code.md) covers the other differences.

Add this line inside `OnInitializeMelon`, below the existing log statement:

```csharp
LoggerInstance.Msg("My first code change is running!");
```

Save, close the game, and run `dotnet run -c "Debug Il2Cpp"` (or `"Debug Mono"`) again. Seeing the new message proves you completed the edit/build/deploy loop. Editing source alone does not update the installed DLL.

## Next steps

Read [Write code for both runtimes](dual-runtime-code.md) before touching more game code. Use [Common tasks](common-tasks.md) to add the other runtime or analyze source. Use [S1API and S1Interop](s1api-and-s1interop.md) to choose an API for a gameplay feature.

Before sharing a mod, follow [Test and distribute a mod](distributing-mods.md). The default two-build project is the recommended route. One-DLL backend-neutral facades remain [experimental](backend-neutral-sdk.md).
