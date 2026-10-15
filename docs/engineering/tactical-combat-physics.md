# Tactical Combat and Physics Adapter — FrostArk (Issue #27)

**Status:** Authored Core damage commands, Unity combat targeting proxies, ballistic raycasting with confirmed hitbox contact, combat tick progression, and comprehensive EditMode and PlayMode test suites. Verified G0, G1 (.NET Core: 218/218 passed), G2 (Unity EditMode: 29/29 passed), and G3 (Unity PlayMode: 16/16 passed across combat integration, tactical navigation, and ship/shore transit suites).

---

## 1. Domain & Application Contracts (Pure C#)

### 1.1 Authoritative Damage Execution
- `DamageCrewCommand(EntityId id, EntityId crewId, int woundPermille, long expectedRevision)`:
  - Submitted via `WorldSession.Submit()`.
  - Executed authoritatively by `WorldCommandBus`.
  - Validates `crewId` in `CrewDuty`.
  - If crew member already incapacitated (`Wounds >= 1000`), returns `CommandFailure.CrewAlreadyIncapacitated`.
  - Computes `clamped = Math.Min(1000, current.Wounds + woundPermille)`.
  - Updates `CrewDuty` state via immutable `WithCrewWound`.
  - Emits `WorldEventKind.CrewWounded` with payload `(woundPermille << 16) | clamped`.
  - Increments session revision monotonically.
  - Stale revisions and duplicate command IDs are rejected with zero mutation.
  - Preserved across `SaveCodec` v13 serialization and restore.

### 1.2 Combat Tick Progression
- `WorldSession.AdvanceCombatTick(long tick)`:
  - Advances `CombatState.Tick` monotonically (`Math.Max(current, tick)`).
  - Explicitly decouples weapon cooldown evaluation from authoritative world calendar progression (`WorldMinute`).
  - Weapons in `CombatState` track `NextReadyTick = currentTick + CooldownTicks`.

---

## 2. Unity Combat & Physics Architecture

### 2.1 Tactical Combat Target Proxy (`TacticalCombatTargetProxy`)
- Attached to attackable scene entities (e.g., `actor:enemy`).
- Holds persistent `EntityId TargetId`, `bool IsHostile`, `Collider Hitbox`, and `Transform AimPoint`.
- Manages health representation synced with authoritative `CrewMember.Wounds`.
- When eliminated (`CurrentHealth <= 0`), disables `Hitbox` collider and raises `OnTargetDied`.

### 2.2 Ballistic Raycast & Line of Fire (`TacticalCombatRaycast`)
- Evaluates line-of-fire from weapon muzzle to target aim point.
- Returns `CombatTargetingStatus`:
  - `Clear`: Direct, uninhibited sightline with verified hit on target collider.
  - `Missed`: Raycast along aim line completed within range but did not contact target collider.
  - `OutOfRange`: Distance exceeds weapon range.
  - `Obstructed`: Raycast hit an intermediate collider (terrain, cover wall, or friendly actor).
  - `InvalidTarget`: Target is null, dead, friendly, or lacks an active hitbox collider.
- Ignores triggers (`QueryTriggerInteraction.Ignore`).
- Excludes shooter's own collider (`shooterCollider`) to eliminate self-collision artifacts.

### 2.3 Combat Tick Runner (`CombatTickRunner`)
- MonoBehaviour running at fixed cadence (default 10 Hz) in `FixedUpdate`.
- Directly advances `WorldSession.AdvanceCombatTick`.
- Registered in `WorldSessionScope` for dependency injection.

### 2.4 Tactical Actor Combat (`TacticalActorProxy`)
- Consumes `TacticalOrderIntent.AttackTarget`.
- Evaluates line of fire via `TacticalCombatRaycast`.
- If obstructed, missed, or out of range, engages NavMesh navigation towards target until clear line of fire is acquired.
- When clear, faces target and executes `TryExecuteShot()`:
  - Verifies weapon cooldown in `CombatState`.
  - Submits authoritative `FireWeaponCommand` consuming ammo from `InventoryContainer.Pack`.
  - Re-evaluates physical line of fire at moment of discharge to confirm hitbox contact.
  - On confirmed ballistic hit, submits authoritative `DamageCrewCommand`.
  - Updates target health solely from confirmed authoritative damage; failed damage commands never reduce health or fabricate hit events.
  - Raises `OnShotFired` / `OnTargetHit`.

### 2.5 Tactical Command Controller (`TacticalCommandController`)
- Click-to-attack targeting from existing pointer selection model.
- Clicking a hostile target while player actors are selected issues `TacticalOrderIntent.AttackTarget`.
- Respects multi-selection, deselection, and target elimination.

---

## 3. Evidence Ladder

| Gate | Status | Details |
|---|---|---|
| **G0 (Schema & Meta)** | **PASS** | Valid `.meta` GUID pairs for all newly authored scripts and tests. |
| **G1 (Pure C# Tests)** | **PASS** | `dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj`<br>Total: 218, Passed: 218, Failed: 0, Duration: 683 ms. |
| **G2 (Unity EditMode)** | **PASS** | `tools/Validate-Unity.ps1 -Mode EditMode`<br>Total: 29, Passed: 29, Failed: 0, Duration: 0.81s. |
| **G3 (Unity PlayMode)** | **PASS** | `tools/Validate-Unity.ps1 -Mode PlayMode`<br>Total: 16, Passed: 16, Failed: 0, Duration: 5.76s.<br>Suites: `CombatPlayModeTests` (4/4), `TacticalPlayModeTests` (9/9), `ShoreSceneTransitPlayModeTests` (2/2), `ShoreSceneRestorePlayModeTests` (1/1). |
| **G4 (Platform & Builds)** | **NOT RUN** | Out of scope for this slice. |
