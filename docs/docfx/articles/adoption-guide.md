# Choose an adoption path

Start with the smallest change that removes the problem you have. S1Interop can add diagnostics or a safe migration plan without converting the whole mod.

## Choose a path

| Your situation | Start with | Read next |
| --- | --- | --- |
| A new Schedule I mod | `s1interop new .\MyMod --apply` | [Build your first mod](first-mod.md) |
| An existing mod with manual Mono and IL2CPP branches | `s1interop analyze .`, then `s1interop lint .` | [Diagnostics](diagnostics.md) |
| A Mono mod that needs separate IL2CPP output | `s1interop migrate . --dual-runtime --dry-run` | [Migrate to dual-runtime](migrate-to-dual-runtime.md) |
| One direct game seam that you want to share | `s1interop init . --dry-run`, then `s1interop sdkgen . --dry-run` | [Migrate to backend-neutral](migrate-to-backend-neutral.md) |
| A migration you do not trust yet | `s1interop verify-migration . --dual-runtime --include-source-migrations` | [Migration overview](migrating-mono-mods.md) |
| A local game API exploration project | `s1interop sdkgen . --full-sdk --dry-run` | [SDK generation](sdk-generation.md) |

Review dry-run output before adding `--apply`. Applied migrations create backups and a manifest under `s1interop-runs/<run-id>/`.

## Keep the existing architecture

Use S1Interop at direct game-wrapper seams. Leave higher-level systems in the library that owns them.

| If the mod needs... | Keep using... | Use S1Interop for... |
| --- | --- | --- |
| Items, NPCs, shops, UI, saves, or gameplay lifecycle | S1API | A direct game call or patch outside the S1API surface. |
| Buildings, models, or mesh workflows | MAPI | Remaining direct Schedule One access. |
| Networking or dedicated-server behavior | Its networking or server framework | Runtime-specific game types, reflection bindings, and patch targets. |
| A direct MelonLoader patch | Your existing mod structure | Analysis, diagnostics, and a narrow generated binding where it removes duplicated runtime code. |

Do not start by changing content registration, save data, deployment scripts, or packaging. Start with the direct `ScheduleOne.*` and `Il2CppScheduleOne.*` code that creates the compatibility problem.

## Before you expand the scope

- Keep `local.build.props` local. It contains machine-specific game paths.
- Do not commit game assemblies, generated IL2CPP wrappers, decompiled output, or game assets.
- Treat a Mono build as Mono evidence only. Build and test the IL2CPP branch separately.
- Keep a dual-runtime fallback while backend-neutral facades are experimental.

For examples of small, mixed adoption, see [Ways to use S1Interop](use-cases.md). For the difference between S1API and low-level interop, see [S1API and S1Interop](s1api-and-s1interop.md).
