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

**Tomorrow's Unity check — not performed in this PR:**

1. Ensure the Editor is closed before switching branches. Preserve any
   uncommitted local work; never force-switch, reset or overwrite a dirty scene.
   Fetch and inspect this PR's exact head before testing; PR #56 is a separate
   branch and cannot be assumed present in the same checkout.
2. Run the authorized Unity import/compile gate on the PR #52 branch and
   inspect the actual Editor log for C# compile errors or package failures.
   The pinned engine is **6000.6.0f1**, VContainer **1.19.0**.
3. Execute the targeted Unity **EditMode**
   `FrostArk.Unity.Tests.WorldSessionBridgeTests` fixture using the commands
   above. Record exact SHA, test XML counts, process exit code and log path.
   `WorldSessionScope` must provide **one** scoped authority to the presenter,
   reject commands without mutating state, and dispose cleanly.
4. Explicitly generate `Assets/FrostArk/Scenes/SessionBootstrap.unity`, review
   the generated `.unity` and `.meta` files and test that the scope/presenter
   scene actually loads. Do not commit generated files without inspecting them.
5. **Only after G2 passes**, run a genuine Unity PlayMode scene smoke for G3:
   verify scoped presenter shows a minute/revision snapshot and that explicit
   session commands/time changes appear without a second source of truth.
   **Do not call this a playable shore/ship scene**: fixture session starts empty,
   and production starting world/weather composition and avatar control remain
   future integration work.
6. Record the exact engine results back on PR #52 and Issue #19, including
   failures. Then test the independent [PR #56 visual benchmark](https://github.com/MohamedXIV/Frostark/pull/56)
   branch separately for camera/fog generation and real graphic screenshots.
   Keep #19/#20 open and Draft unless their acceptance gates truly pass.

Stop and leave G2/G3 **UNVERIFIED** if the selected branch, pinned packages,
Unity license, available modules or Editor log cannot be confirmed. A passing
GitHub Actions status cannot replace an Editor or PlayMode result. G4 platform
validation remains separately pending.

## Ownership

`WorldSessionScope` registers `WorldSession` at `Lifetime.Scoped` via a factory selecting the empty bootstrap fixture; production authored initial ship/crew data and loading saved sessions are explicit later composition requirements. `WorldSessionPresenter` receives it through VContainer `[Inject]`, subscribes to committed changes and unsubscribes in `OnDestroy`. No global `GameManager`, no presenter-owned fuel/crew totals.

`ISaveBlobStore` remains a platform adapter port. No real filesystem/WebGL atomic save implementation was added here.
