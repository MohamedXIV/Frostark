# FrostArk Session bridge — Unity Editor verification

This PR introduces a pure-C# `WorldSession` plus a thin VContainer session scope and MonoBehaviour presenter. The presenter shows a derived minute/revision status and sends commands through the same authoritative session; it never maintains a second supply total or drives simulation from `Update`/`FixedUpdate`.

## Actual evidence boundaries

GitHub Actions `core-checks.yml` runs the standalone NUnit G1 suite (including session commit/restore and procedural override retention) plus G0 Python tests. It **does not compile the Unity adapters or VContainer assemblies**.

G2 must be executed with licensed Unity 6000.6.0f1 and the pinned VContainer 1.19.0 package. From a trusted checkout that is not open in another Editor process, import/compile and run this separate Unity EditMode fixture:

```powershell
& 'F:\Program Files\Unity\6000.6.0f1\Editor\Unity.exe' -batchmode -nographics -projectPath 'F:\UnityProjects\Frostark' -runTests -testPlatform EditMode -testFilter FrostArk.Unity.Tests.WorldSessionBridgeTests -testResults 'F:\UnityProjects\Frostark\Logs\unity-session-bridge.xml' -logFile 'F:\UnityProjects\Frostark\Logs\unity-session-bridge.log'
```

For an explicit fixture scene only (no automatic scene creation):

```powershell
& 'F:\Program Files\Unity\6000.6.0f1\Editor\Unity.exe' -batchmode -nographics -quit -projectPath 'F:\UnityProjects\Frostark' -executeMethod FrostArk.Unity.Editor.FrostArkSessionFixture.CreateScene -logFile 'F:\UnityProjects\Frostark\Logs\unity-session-generate.log'
```

Review the generated scene file and metadata before committing. Both commands are **instructions, not executed evidence** for this PR. A passing G0/G1 workflow and correctly formed `.asmdef` files never establish Unity G2.

## Ownership

`WorldSessionScope` registers `WorldSession` at `Lifetime.Scoped` via a factory selecting the empty bootstrap fixture; production authored initial ship/crew data and loading saved sessions are explicit later composition requirements. `WorldSessionPresenter` receives it through VContainer `[Inject]`, subscribes to committed changes and unsubscribes in `OnDestroy`. No global `GameManager`, no presenter-owned fuel/crew totals.

`ISaveBlobStore` remains a platform adapter port. No real filesystem/WebGL atomic save implementation was added here.
