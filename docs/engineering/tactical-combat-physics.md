# Tactical Combat and Physics Adapter — FrostArk (Issue #27)

**Status:** Authored Core damage commands, weapon reload and magazine tracking, armor mitigation calculations, cold combat modifiers, tactical retreat intent, Unity combat targeting proxies, ballistic raycasting with confirmed hitbox contact, combat tick progression, and comprehensive EditMode and PlayMode test suites. Verified G0, G1 (.NET Core: 232/232 passed), G2 (Unity EditMode: 34/34 passed), and G3 (Unity PlayMode: 19/19 passed across combat integration, tactical navigation, and ship/shore transit suites).

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

### 1.2 Magazine Accounting and Reload
- `WeaponDefinition`:
  - `MagazineCapacity`: Maximum rounds contained in magazine (0 indicates direct pack-fed firing for backwards compatibility).
  - `ReloadTicks`: Duration in combat ticks required to reload the magazine.
- `WeaponRuntimeState`:
  - `MagazineRounds`: Authoritative current rounds inside weapon magazine.
- `FireWeaponCommand`:
  - When `MagazineCapacity > 0`: Deducts ammunition from `MagazineRounds` via `WithWeaponShotFromMagazine`. If `MagazineRounds < AmmoPerShot`, rejects with `CommandFailure.MagazineEmpty`.
  - When `MagazineCapacity == 0`: Deducts ammunition directly from `InventoryContainer.Pack`.
- `ReloadWeaponCommand(EntityId id, EntityId weaponId, long expectedRevision)`:
  - Validates weapon is carried in `InventoryContainer.Pack` and is not cooling down.
  - Rejects if `MagazineRounds >= MagazineCapacity` (`MagazineAlreadyFull`) or if weapon cannot reload (`WeaponCannotReload`).
  - Atomically transfers up to needed rounds from `InventoryContainer.Pack` to `MagazineRounds`.
  - Sets weapon cooldown to `currentTick + ReloadTicks`.
  - Emits `WorldEventKind.WeaponReloaded`.

### 1.3 Armor Mitigation
- `CombatMitigation.CalculateMitigatedWounds(int rawWounds, int armorPermille)`:
  - Pure C# domain function evaluating physical damage reduction.
  - `armorPermille` in [0, 1000]; 1000 represents complete deflection (0 wounds).
  - Formula: `mitigated = rawWounds - (rawWounds * armorPermille + 500) / 1000`.
  - When armor permille is 1000 or raw wounds reduce to zero, returns 0. For partial damage with armor < 1000, clamped to at least 1 wound.

### 1.4 Cold Exposure Combat Modifiers
- `ColdCombatModifiers`:
  - `ApplyCooldownModifier(baseCooldownTicks, coldPermille)`: Increases shot cooldown by up to +50% under extreme hypothermia.
  - `ApplyReloadModifier(baseReloadTicks, coldPermille)`: Increases reload duration by up to +100% when fingers are freezing.
  - `CalculateSpeedMultiplier(coldPermille)`: Scales movement speed down by up to -40% based on cold exposure permille.

### 1.5 Combat Tick Progression
- `WorldSession.AdvanceCombatTick(long tick)`:
  - Advances `CombatState.Tick` monotonically (`Math.Max(current, tick)`).
  - Explicitly decouples weapon cooldown evaluation from authoritative world calendar progression (`WorldMinute`).
  - Weapons in `CombatState` track `NextReadyTick = currentTick + CooldownTicks` (or `ReloadTicks`).

---

## 2. Unity Combat & Physics Architecture

### 2.1 Tactical Combat Target Proxy (`TacticalCombatTargetProxy`)
- Attached to attackable scene entities (e.g., `actor:enemy`).
- Holds persistent `EntityId TargetId`, `bool IsHostile`, `Collider Hitbox`, `Transform AimPoint`, and `int ArmorPermille`.
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
- Consumes `TacticalOrderIntent.AttackTarget` and `TacticalOrderIntent.RetreatTo`.
- Evaluates line of fire via `TacticalCombatRaycast`.
- If obstructed, missed, or out of range, engages NavMesh navigation towards target until clear line of fire is acquired.
- When clear, faces target and executes `TryExecuteShot()`:
  - Verifies weapon cooldown in `CombatState`.
  - If magazine is depleted, auto-attempts `TryExecuteReload()`.
  - Submits authoritative `FireWeaponCommand` consuming ammo from magazine.
  - Re-evaluates physical line of fire at moment of discharge to confirm hitbox contact.
  - Evaluates armor mitigation via `CombatMitigation.CalculateMitigatedWounds`.
  - If fully absorbed (0 damage), raises `OnAttackFailed("ArmorAbsorbed")` and `OnTargetHit(..., 0)` without submitting damage command.
  - On effective damage (> 0), submits authoritative `DamageCrewCommand`.
  - Updates target health solely from confirmed authoritative damage; failed damage commands never reduce health or fabricate hit events.
  - Raises `OnShotFired` / `OnTargetHit`.
- Locomotion adjustments:
  - Applies `ColdExposurePermille` scaling to NavMesh movement speed.
  - On retreat, disengages pending combat, applies sprint speed multiplier (1.25x), and raises `OnRetreatStarted`.

### 2.5 Tactical Command Controller (`TacticalCommandController`)
- Click-to-attack targeting from existing pointer selection model.
- Contextual commands: `IssueMoveOrder`, `IssueInteractOrder`, `IssueAttackOrder`, and `IssueRetreatOrder`.
- Respects multi-selection, deselection, target elimination, and order preemption.

---

## 3. Evidence Ladder

| Gate | Status | Details |
|---|---|---|
| **G0 (Schema & Meta)** | **PASS** | Valid `.meta` GUID pairs for all newly authored scripts and tests. |
| **G1 (Pure C# Tests)** | **PASS** | `dotnet test tests/FrostArk.Core.Tests/FrostArk.Core.Tests.csproj`<br>Total: 232, Passed: 232, Failed: 0, Duration: 1.0s. |
| **G2 (Unity EditMode)** | **PASS** | `tools/Validate-Unity.ps1 -Mode EditMode`<br>Total: 34, Passed: 34, Failed: 0, Duration: 0.87s. |
| **G3 (Unity PlayMode)** | **PASS** | `tools/Validate-Unity.ps1 -Mode PlayMode`<br>Total: 19, Passed: 19, Failed: 0, Duration: 23.05s.<br>Suites: `CombatPlayModeTests` (7/7), `TacticalPlayModeTests` (9/9), `ShoreSceneTransitPlayModeTests` (2/2), `ShoreSceneRestorePlayModeTests` (1/1). |
| **G4 (Platform & Builds)** | **NOT RUN** | Out of scope for this slice. |

