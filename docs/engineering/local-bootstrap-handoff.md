# Local bootstrap handoff — 2026-10-10

## Delivery and scope

Base main: `d988a9c738826ccb5bd8d2440f9fbe8e66e07047`. Cleanup commit: `3cfa5e3d865cfd2f9c456f71efe67c12d1068514`.
The PR records the exact delivery head SHA; verification below applies to the bootstrap source and pinned dependencies in that delivery.
No gameplay architecture, GitHub Actions, additional platform modules, Android builds, or player builds were introduced.
Local Input System/AI Navigation updates were preserved. Main was fast-forwarded before implementation; no work was reset or discarded.

## Installed dependencies

| Package | Verified version | Source |
|---|---|---|
| VContainer | 1.19.0 | UPM Git tag; resolved commit `5401e5a7ebc4980a2b82141ffc26391a6547edd7` |
| Addressables | 2.11.2 | Unity Registry |
| Cinemachine 3 | 6.6.0 | Editor built-in core package |
| Universal Render Pipeline | 17.6.0 | Editor built-in |
| Input System | 1.20.1 | Unity Registry |
| AI Navigation | 2.0.16 | Unity Registry |
| Unity Test Framework | 1.8.0 | Editor built-in |

Packages were installed through the running Editor's Pipeline package commands, backed by UPM; manifest and lockfile are committed together. Cinemachine was physically present but neither registered nor loaded initially. Activating its dependency resolved to `source: builtin`, not a second Registry copy.
Addressables has only its generated default settings and a `BootstrapFixtures` group containing one small text asset. No production catalog was designed. Smoke loading uses AssetDatabase Fast Mode, not built AssetBundles or a remote catalog.

Version references: [VContainer installation](https://vcontainer.hadashikick.jp/getting-started/installation), [Unity 6.6 Addressables](https://docs.unity.com/en-us/engine/6000.6/manual/packages-list/packages-all/pack-safe/com-unity-addressables), [Cinemachine core change](https://docs.unity.com/en-us/engine/6000.6/manual/upgrade-guides/upgrade-guide-unity66).
The selected packages compile in this Windows Editor. No incompatibility preventing the bootstrap was observed. Android/WebGL IL2CPP, stripping, rendering and runtime compatibility remain unverified until actual target builds/tests; this is not a blanket cross-platform certification.

## Local execution and evidence

- Unity CLI `1.0.0-beta.13`; Editor `6000.6.0f1`, executable `F:\Program Files\Unity\6000.6.0f1\Editor\Unity.exe`.
- `unity status --format json`: one Frostark instance, `ready`; Pipeline command discovery responded successfully.
- `unity license status --format json`: `active=true`, `signedIn=true`, OAuth. No license files or credentials were copied.
- The sandbox could not read the user-owned discovery file. Specific local CLI calls were run with the approved execution context; this does not give cloud agents remote access.
- GUI Editor had no dirty scenes before it was closed for headless validation. It remains closed after validation.
- Exact headless import smoke: `Unity.exe -batchmode -nographics -quit -projectPath "F:\UnityProjects\Frostark" -logFile "F:\UnityProjects\Frostark\Logs\bootstrap-import.log"`: exit **0**, no C# compiler/package-resolution errors.
- Reusable import gate: `./tools/Validate-Unity.ps1 -Mode Import`; additionally invokes `FrostArk.Bootstrap.BootstrapSetup.ReportReadiness` to verify required package versions and record native module availability.
- `./tools/Validate-Unity.ps1 -Mode EditMode`: exit **0**, **2 tests passed, 0 failed**. LifetimeScope scoped registration/resolution/disposal and configured Addressables TextAsset loading/releasing actually executed.
- First test attempt: 1 passed / 1 failed, Unity exit 2, because the test used delayed `Destroy` via `LifetimeScope.Dispose()` in EditMode. Fixed the test to use `DisposeCore()` + `DestroyImmediate`; the subsequent run passed. Package source was not modified and the error was not suppressed.
- Evidence stays in ignored `Logs/bootstrap-import.log`, `Logs/bootstrap-readiness.json`, `Logs/bootstrap-editmode.log`, `Logs/bootstrap-editmode.xml`. Do not upload entire logs containing local paths or licensing diagnostics without review.
- .NET SDKs available: 7.0.403, 7.0.410, 8.0.425, 10.0.112. Standalone Core harness: **NOT AVAILABLE**; no `dotnet test` verdict claimed. Its implementation remains Issue #15.
- Player/platform builds: **NOT RUN**. Android build explicitly excluded by the owner. PlayMode, graphics and device acceptance: **NOT RUN**.

## Platform modules

- Android: `BuildPipeline.IsBuildTargetSupported(Android)=true`; AndroidPlayer, embedded SDK, NDK and OpenJDK are present in this exact Editor installation.
- SDK platforms present: android-34, android-36, android-37.0; NDK `27.2.12479018` (r27c), OpenJDK `17.0.18`. Installation evidence only; an Android build was not executed.
- WebGL: `BuildPipeline.IsBuildTargetSupported(WebGL)=false`; no WebGL playback engine directory in this Editor installation. This contradicts the earlier owner expectation and is a named missing capability, not a passing check. Nothing was reinstalled or downloaded.

## ShellWarden — blocked

Installed broker: `F:\Development\Tools\ShellWarden\execution\shellwarden-broker.exe`.
Its documented JSON-lines protocol was exercised directly:

1. `health` returned `coreVersion=1.1.12`, `allowedCommands=["git"]`.
2. `execute` with argv `["F:\\Program Files\\Unity\\6000.6.0f1\\Editor\\Unity.exe", "-version"]`, directory `F:\UnityProjects\Frostark`, timeout 30 returned **denied**: `bootstrap policy does not allow executable: ...Unity.exe`.

The broker process exited 0, but the execution response was `ok=false`; this is **not** a successful Unity invocation. The installed broker matches the documented git-only bootstrap policy. [Execution-core documentation](https://github.com/MohamedXIV/shellwarden/blob/main/docs/EXECUTION_CORE.md) and [broker source](https://github.com/MohamedXIV/shellwarden/blob/main/execution/broker.py) show a hard-coded git allowlist and removal of environment overrides.
No supported configuration for authorizing Unity at that layer was found. No SQLite grants, packaged binaries or security policy were modified. ShellWarden's MCP connector is not exposed to this Codex session; broker verification is not proof of end-to-end remote MCP execution.

Required follow-up in ShellWarden: implement a supported exact-executable/project/argv/timeout allow rule, preserve both broker and approval boundaries, then validate through the real remote connector. Do not grant generic PowerShell/cmd or route Unity through an allowed executable.

## Git and GitHub

- Removed disposable TutorialInfo, its metas, template Readme and the already self-deleted HubForceResolve script. The latter resolves packages once and deliberately deletes itself; it is not application infrastructure.
- Baseline main's URP.png was a raw Git blob despite an LFS attribute. Cleanup removed this disposable asset; existing history was preserved. After cleanup: `git lfs fsck` **OK**.
- `Library`, `Temp`, `Logs`, `UserSettings` ignored; none tracked. Source/config asset files keep Unity-generated meta pairs.
- Native GitHub graph: **31 parent/sub-issue links** and **64 documented blocked-by links** verified through supported REST APIs. Documented graph cycle check passed. Descriptions, priorities and states were preserved.
- `tools/Sync-IssueRelationships.ps1` defaults to read-only plan; `-Apply` is an explicit external mutation and must only be used with user authorization. It fails on API errors or conflicting parentage.
- Project #5 Ready queue remains #13 and #14; neither has prerequisites. Other dependent tasks stay Backlog until their prerequisites close. Issue #14 remains open while remote execution is blocked.

## Remaining gates for online agents

1. ShellWarden requires a supported broker-policy change before it can run Unity remotely. The local CLI runner is ready, but the autonomous remote-validation definition of done is **not yet met**.
2. WebGL support for this exact Editor installation is missing; acquire/enable it only when authorized and needed.
3. Implement Core/.NET harness under Issue #15; install compatible .NET SDK in the cloud environment as needed.
4. Run actual player builds, PlayMode and hardware/graphics tests at the appropriate milestones. Addressables packed-content loading and IL2CPP stripping need separate evidence.
5. Review/merge the bootstrap PR before cloud agents treat its dependencies and runner as main's baseline.
