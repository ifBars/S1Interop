---
title: Report a problem
description: Share enough code, version information, and output to investigate an S1Interop issue.
uid: s1interop.reporting-issues
---

# Report a problem

[Open a bug report](https://github.com/ifBars/S1Interop/issues/new?template=bug_report.yml) when setup, compilation, or your mod's in-game behavior goes wrong. You do not need to prove that S1Interop caused it, test both runtimes, or make a complete sample project first. Fill in what you know and mark anything you could not check.

For a new capability or an easier workflow, [request an improvement](https://github.com/ifBars/S1Interop/issues/new?template=feature_request.yml).

## Include the versions you used

Run these commands from your mod's folder in Command Prompt, PowerShell, or Windows Terminal:

```console
dotnet tool run s1interop -- --version
dotnet --version
```

The first command reports the project's pinned tool. A globally installed `s1interop` can be a different version. If the local tool cannot run, include the error and the version from `.config/dotnet-tools.json`. For an installation failure before you have a project, include the install command and requested version instead. Source builds should include their Git commit.

Also include the exact game version and Steam branch for each installation involved, your operating system, MelonLoader version, and relevant mod dependencies. You can find game and loader versions in the MelonLoader startup log. If you only tested one runtime, say which one.

## Show the failure

For **setup or build failures**, copy the exact command and its output, starting with the first error. Keep diagnostic codes such as `S1IC001` and the source location. Later errors may follow from the first one.

For **a mod that fails to load or behaves incorrectly**, include the first relevant exception and stack trace from `MelonLoader/Latest.log` in that game's installation. Copy the log after the failing session, before launching again. If there is no exception, describe the expected behavior and what actually happened. Include the steps, whether a save was loaded, and whether you were the multiplayer host or a client.

For IL2CPP loading failures, mention where you placed the mod DLL and `S1Interop.Runtime.dll`, and whether both came from the same build. See [Test and distribute](distributing-mods.md) for deployment instructions.

A successful build and a successful in-game test are different results. Report each separately. Do not feel obliged to repeat an action that could damage a save just to collect another log.

## Share a small example when you can

Paste the relevant mod code, link a repository with its commit, or attach a small project containing your own source. Include the project settings and package versions needed to build it. Keep the original code that fails so we can investigate the compiler behavior.

If a full mod is difficult to reduce, start with the failing method and its callers. For example, a collection problem may depend on how a list was obtained from the game, so include that part too. Private source does not need to become public; describe the operation and share a reduced example if possible.

## Review attachments before uploading

GitHub issues are public. Share relevant text output instead of screenshots of compiler errors. Screenshots are useful for visual problems.

Remove private paths, usernames, player identifiers, and other personal information from logs. Do not upload game DLLs, generated game reference assemblies, decompiled game source, saves, or whole game folders. A source sample should leave out `bin`, `obj`, and local game-path configuration. Maintainers can use their own game installations to supply references.
