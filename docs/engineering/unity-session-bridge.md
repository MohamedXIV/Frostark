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

## Canonical world-minute integration (G1 verified, Unity G2 pending)

The scoped `WorldSession` now wires the existing Core reducers against its **single**
`WorldState` pointer, in the scheduler's deterministic phases:

1. Crew duties and paid repairs (`ShipCrewMinuteSystem`).
2. Thermal fuel/heat and authoritative minute commit (`ShipThermalMinuteSystem`).
3. Workshop job completion and real inventory output (`WorkshopMinuteSystem`).
4. Ship travel and real fuel/hull effects (`ShipTravelMinuteSystem`).

The scheduler itself never owns independent inventory or fuel totals. An authored
thermal ship (`Thermal.Rooms.Count > 0`) requires an explicit
`ambientMilliC: Func<WorldMinute,long>` provider when constructing **or restoring**
a session. The empty bootstrap fixture can omit the provider because it has
no physical rooms; this does **not** establish a production weather or climate source.
Unity integration must supply the appropriate zone/weather-derived temperature
before launching or restoring a real ship run.

G1 regression `SessionThermalAdvanceConsumesRealFuelAndReplaysAfterRestore` checks
5 paid simulation minutes (400 → 395 fuel), save/reload continuation and
byte-stable world fingerprint after 10 total minutes (390 fuel).
A RED compile failure is recorded at Actions run 38066611106; the GREEN
full suite at run 38066703294 passed **186/186 NUnit** and **11/11 G0 Python**
on head `72c7fe6777b312d7fec2f270f879fa51cbf41fbf`.
These runs **do not** execute Unity Editor import, VContainer component injection,
renderer screenshots, input, PlayMode, or target builds. PR stays Draft.

## Final editor-preflight: order, evidence and stop conditions

G1-only integration preflight on code commit `71efcfc7383e2035a987edf5e367c88dd0ba89b6`:
[Actions run 38069243742](https://github.com/MohamedXIV/Frostark/actions/runs/38069243742)
**188/188 standalone NUnit tests PASS**, **11/11 G0 structural tests PASS**.
The two new `WorldSessionTests` cover (a) paid workshop work paused by a
sleep-interrupting raid, restored via save and completed once with byte-equal
state, and (b) one shared fuel unit consumed by heat before travel and
rejected implicit climate defaults on new/restored thermal ships. These
are integration tests of *existing* canonical minute systems: no separate
workshop or navigation authority was introduced. They do **not** compile or
run Unity assemblies.

## Unity Editor verification (G2 and G3 executed locally — 2026-10-13)

- Local environment: Unity 6000.6.0f1, VContainer 1.19.0.
- Resolved compile issues: added missing `VContainer` reference to `FrostArk.Unity.Editor.asmdef`; disambiguated `EntityId` in `WorldSessionBridgeTests.cs`; handled untitled active scenes in `FrostArkSessionFixture.CreateScene`.
- Recompilation: `unity recompile` completed with exit code 0; `recompile_status: completed`.
- EditMode test run: `unity command run_tests --mode EditMode --filter FrostArk.Unity.Tests.WorldSessionBridgeTests`: **1/1 passed (5.77s)**.
- Scene fixture generation: `FrostArk/Fixtures/Rebuild Session Bootstrap Scene` executed successfully, generating `Assets/FrostArk/Scenes/SessionBootstrap.unity` and `.meta`.
- Scene load & PlayMode smoke:
  1. Opened `Assets/FrostArk/Scenes/SessionBootstrap.unity`.
  2. Entered PlayMode (`editor_play`). `runtime_status` confirmed `IsPlaying=true`, `LoadedLevelName="SessionBootstrap"`.
  3. `WorldSessionPresenter` bound to scoped `WorldSession` via VContainer injection (`IsBound=true`).
  4. Initial presentation projection verified: `"FrostArk | minute 0 | revision 0"`.
  5. Invalid command rejection verified: rejected with `Success=False`, revision remained 0.
  6. Advancing session by 15 simulation minutes dynamically triggered `OnSessionChanged` and updated `WorldSessionPresenter.StatusText` to `"FrostArk | minute 15 | revision 0"`.
  7. Exited PlayMode cleanly (`editor_stop`).
- Acceptance criteria for Issue #19 are verified in Unity Editor. G4 target platform validation remains separately pending for future release stages.

## Ownership

`WorldSessionScope` registers `WorldSession` at `Lifetime.Scoped` via a factory selecting the empty bootstrap fixture; production authored initial ship/crew data and loading saved sessions are explicit later composition requirements. `WorldSessionPresenter` receives it through VContainer `[Inject]`, subscribes to committed changes and unsubscribes in `OnDestroy`. No global `GameManager`, no presenter-owned fuel/crew totals.

`ISaveBlobStore` remains a platform adapter port. No real filesystem/WebGL atomic save implementation was added here.
