using System;
using FrostArk.Core.Application;
using FrostArk.Core.Combat;
using FrostArk.Core.Commands;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    [TestFixture]
    public sealed class CombatRaycastEditModeTests
    {
        private GameObject _testRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = new GameObject("CombatRaycastTests_Root");
            Physics.SyncTransforms();
        }

        [TearDown]
        public void TearDown()
        {
            if (_testRoot != null)
                UnityEngine.Object.DestroyImmediate(_testRoot);
        }

        private TacticalCombatTargetProxy CreateTarget(Vector3 position, string id = "actor:enemy", bool hostile = true, int health = 1000)
        {
            var targetObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            targetObj.name = "Target_" + id;
            targetObj.transform.SetParent(_testRoot.transform);
            targetObj.transform.position = position;
            targetObj.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);

            var proxy = targetObj.AddComponent<TacticalCombatTargetProxy>();
            proxy.SetTargetId(EntityId.Parse(id));
            proxy.SetHostile(hostile);
            proxy.SetHealth(health, 1000);
            Physics.SyncTransforms();
            return proxy;
        }

        [Test]
        public void LineOfFire_ClearSightToEnemy_ReturnsClear()
        {
            var target = CreateTarget(new Vector3(0, 1.0f, 6.0f));
            Vector3 muzzle = new Vector3(0, 1.2f, 0);

            var status = TacticalCombatRaycast.EvaluateLineOfFire(
                muzzle, target, maxRange: 15.0f, blockingMask: ~0, shooterCollider: null, out var hit);

            Assert.That(status, Is.EqualTo(CombatTargetingStatus.Clear));
            Assert.That(hit.collider, Is.EqualTo(target.Hitbox));
        }

        [Test]
        public void LineOfFire_EnemyBehindSolidWall_ReturnsObstructed()
        {
            var target = CreateTarget(new Vector3(0, 1.0f, 6.0f));
            Vector3 muzzle = new Vector3(0, 1.2f, 0);

            // Solid wall directly in between muzzle and target
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "SolidWall";
            wall.transform.SetParent(_testRoot.transform);
            wall.transform.position = new Vector3(0, 1.0f, 3.0f);
            wall.transform.localScale = new Vector3(4.0f, 2.0f, 1.0f);
            Physics.SyncTransforms();

            var status = TacticalCombatRaycast.EvaluateLineOfFire(
                muzzle, target, maxRange: 15.0f, blockingMask: ~0, shooterCollider: null, out var hit);

            Assert.That(status, Is.EqualTo(CombatTargetingStatus.Obstructed));
            Assert.That(hit.collider, Is.EqualTo(wall.GetComponent<Collider>()));
        }

        [Test]
        public void LineOfFire_EnemyPartlyVisibleAroundCover_ReturnsClearWhenLaneIsOpen()
        {
            // Wall covers X from -1 to +1 at Z = 3.0
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "CoverBlock";
            wall.transform.SetParent(_testRoot.transform);
            wall.transform.position = new Vector3(0, 1.0f, 3.0f);
            wall.transform.localScale = new Vector3(2.0f, 2.0f, 1.0f);

            // Enemy shifted to X = 2.5 (past the wall's edge)
            var target = CreateTarget(new Vector3(2.5f, 1.0f, 6.0f));
            Vector3 muzzle = new Vector3(0, 1.2f, 0);
            Physics.SyncTransforms();

            var status = TacticalCombatRaycast.EvaluateLineOfFire(
                muzzle, target, maxRange: 15.0f, blockingMask: ~0, shooterCollider: null, out var hit);

            Assert.That(status, Is.EqualTo(CombatTargetingStatus.Clear));
            Assert.That(hit.collider, Is.EqualTo(target.Hitbox));
        }

        [Test]
        public void LineOfFire_TargetOutsideWeaponRange_ReturnsOutOfRange()
        {
            var target = CreateTarget(new Vector3(0, 1.0f, 20.0f));
            Vector3 muzzle = new Vector3(0, 1.2f, 0);

            var status = TacticalCombatRaycast.EvaluateLineOfFire(
                muzzle, target, maxRange: 10.0f, blockingMask: ~0, shooterCollider: null, out _);

            Assert.That(status, Is.EqualTo(CombatTargetingStatus.OutOfRange));
        }

        [Test]
        public void LineOfFire_FriendlyActorObstructingFiringLane_ReturnsObstructed()
        {
            var target = CreateTarget(new Vector3(0, 1.0f, 6.0f));
            Vector3 muzzle = new Vector3(0, 1.2f, 0);

            // Friendly companion standing directly in line of fire
            var friendlyObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            friendlyObj.name = "FriendlyCompanion";
            friendlyObj.transform.SetParent(_testRoot.transform);
            friendlyObj.transform.position = new Vector3(0, 1.0f, 2.5f);
            var friendlyProxy = friendlyObj.AddComponent<TacticalActorProxy>();
            friendlyProxy.SetActorId(EntityId.Parse("actor:companion"));
            Physics.SyncTransforms();

            var status = TacticalCombatRaycast.EvaluateLineOfFire(
                muzzle, target, maxRange: 15.0f, blockingMask: ~0, shooterCollider: null, out var hit);

            Assert.That(status, Is.EqualTo(CombatTargetingStatus.Obstructed));
            Assert.That(hit.collider, Is.EqualTo(friendlyObj.GetComponent<Collider>()));
        }

        [Test]
        public void LineOfFire_ShooterSelfCollider_IsIgnoredAndDoesNotCorruptSightline()
        {
            var target = CreateTarget(new Vector3(0, 1.0f, 6.0f));

            var shooterObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            shooterObj.name = "Shooter";
            shooterObj.transform.SetParent(_testRoot.transform);
            shooterObj.transform.position = Vector3.zero;
            var shooterCollider = shooterObj.GetComponent<Collider>();
            Physics.SyncTransforms();

            // Muzzle is inside/at edge of shooter's bounding volume
            Vector3 muzzle = shooterObj.transform.position + Vector3.up * 1.2f;

            var status = TacticalCombatRaycast.EvaluateLineOfFire(
                muzzle, target, maxRange: 15.0f, blockingMask: ~0, shooterCollider: shooterCollider, out var hit);

            Assert.That(status, Is.EqualTo(CombatTargetingStatus.Clear));
            Assert.That(hit.collider, Is.EqualTo(target.Hitbox));
        }

        [Test]
        public void LineOfFire_DeadOrFriendlyTarget_ReturnsInvalidTarget()
        {
            var deadTarget = CreateTarget(new Vector3(0, 1.0f, 5.0f), health: 0);
            var statusDead = TacticalCombatRaycast.EvaluateLineOfFire(
                Vector3.zero, deadTarget, 15f, ~0, null, out _);
            Assert.That(statusDead, Is.EqualTo(CombatTargetingStatus.InvalidTarget));

            var friendlyTarget = CreateTarget(new Vector3(0, 1.0f, 5.0f), hostile: false);
            var statusFriendly = TacticalCombatRaycast.EvaluateLineOfFire(
                Vector3.zero, friendlyTarget, 15f, ~0, null, out _);
            Assert.That(statusFriendly, Is.EqualTo(CombatTargetingStatus.InvalidTarget));
        }

        [Test]
        public void CommandController_IssueAttackOrder_BuildsIntentAndConsumesCarriedAmmo()
        {
            var actorObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actorObj.name = "Protagonist";
            actorObj.transform.SetParent(_testRoot.transform);
            actorObj.transform.position = Vector3.zero;
            var actor = actorObj.AddComponent<TacticalActorProxy>();
            actor.SetActorId(EntityId.Parse("actor:protagonist"));
            actor.SetWeapon(EntityId.Parse("item:weapon:rifle"), range: 15.0f, damage: 250);

            var enemyTarget = CreateTarget(new Vector3(0, 1.0f, 5.0f), id: "actor:enemy");

            var cmdObj = new GameObject("Commander");
            cmdObj.transform.SetParent(_testRoot.transform);
            var commander = cmdObj.AddComponent<TacticalCommandController>();
            commander.RegisterActor(actor);
            commander.RegisterCombatTarget(enemyTarget);

            Physics.SyncTransforms();

            var session = new WorldSession(TacticalSliceFixtureState.CreateDefaultState());
            commander.SetSession(session);

            var stateBefore = session.Capture().State;
            long ammoBefore = stateBefore.Inventory.GetAmount(InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId);
            Assert.That(ammoBefore, Is.EqualTo(20));

            commander.SelectActor(actor.ActorId);
            bool ordered = commander.IssueAttackOrder(enemyTarget);

            Assert.That(ordered, Is.True);
            Assert.That(commander.LastIssuedIntent, Is.Not.Null);
            Assert.That(commander.LastIssuedIntent.Kind, Is.EqualTo(TacticalOrderKind.Attack));
            Assert.That(commander.LastIssuedIntent.TargetId, Is.EqualTo(enemyTarget.TargetId));

            // Verify authoritative execution
            var stateAfter = session.Capture().State;
            long ammoAfter = stateAfter.Inventory.GetAmount(InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId);
            Assert.That(ammoAfter, Is.EqualTo(19), "One round of carried ammo must be consumed from Pack.");

            // Verify damage consequence on crew and proxy
            int enemyWounds = stateAfter.CrewDuty.Get(enemyTarget.TargetId)!.Wounds;
            Assert.That(enemyWounds, Is.EqualTo(250), "Authoritative wounds must be recorded in CrewDuty.");
            Assert.That(enemyTarget.CurrentHealth, Is.EqualTo(750), "Target proxy health must reflect authoritative damage.");
        }

        [Test]
        public void Actor_WeaponCooldown_PreventsPrematureSecondShotUntilTickAdvances()
        {
            var actorObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actorObj.name = "Protagonist";
            actorObj.transform.SetParent(_testRoot.transform);
            actorObj.transform.position = Vector3.zero;
            var actor = actorObj.AddComponent<TacticalActorProxy>();
            actor.SetActorId(EntityId.Parse("actor:protagonist"));
            actor.SetWeapon(EntityId.Parse("item:weapon:rifle"), range: 15.0f, damage: 250);

            var enemyTarget = CreateTarget(new Vector3(0, 1.0f, 5.0f), id: "actor:enemy");
            Physics.SyncTransforms();

            var session = new WorldSession(TacticalSliceFixtureState.CreateDefaultState());
            actor.SetSession(session);

            // First shot executes via AttackTarget at tick 0
            bool targeted = actor.AttackTarget(enemyTarget, session);
            Assert.That(targeted, Is.True);
            Assert.That(session.Capture().State.Inventory.GetAmount(InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId), Is.EqualTo(19));
            Assert.That(enemyTarget.CurrentHealth, Is.EqualTo(750));

            // Immediate second shot at same tick must fail (cooldown = 3 ticks)
            bool shot2 = actor.TryExecuteShot();
            Assert.That(shot2, Is.False, "Shot during weapon cooldown must fail.");
            Assert.That(session.Capture().State.Inventory.GetAmount(InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId), Is.EqualTo(19));

            // Advance combat ticks to 3
            session.AdvanceCombatTick(3);

            // Now second shot succeeds
            bool shot3 = actor.TryExecuteShot();
            Assert.That(shot3, Is.True, "Shot after cooldown expiration must succeed.");
            Assert.That(session.Capture().State.Inventory.GetAmount(InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId), Is.EqualTo(18));
            Assert.That(enemyTarget.CurrentHealth, Is.EqualTo(500));
        }

        [Test]
        public void CombatTickRunner_AdvancesWorldSessionTicksAtCadence()
        {
            var session = new WorldSession(TacticalSliceFixtureState.CreateDefaultState());
            var runnerObj = new GameObject("Runner");
            runnerObj.transform.SetParent(_testRoot.transform);
            var runner = runnerObj.AddComponent<CombatTickRunner>();
            runner.SetSession(session);

            Assert.That(runner.CurrentTick, Is.EqualTo(0));
            Assert.That(session.Capture().State.Combat.Tick, Is.EqualTo(0));

            runner.Step(5);
            Assert.That(runner.CurrentTick, Is.EqualTo(5));
            Assert.That(session.Capture().State.Combat.Tick, Is.EqualTo(5));
        }

        [Test]
        public void LineOfFire_MissingOrDisabledHitbox_ReturnsInvalidTarget()
        {
            var target = CreateTarget(new Vector3(0, 1.0f, 6.0f));
            target.Hitbox.enabled = false;
            Physics.SyncTransforms();

            Vector3 muzzle = new Vector3(0, 1.2f, 0);
            var status = TacticalCombatRaycast.EvaluateLineOfFire(
                muzzle, target, maxRange: 15.0f, blockingMask: ~0, shooterCollider: null, out var hit);

            Assert.That(status, Is.EqualTo(CombatTargetingStatus.InvalidTarget));
            Assert.That(hit.collider, Is.Null);

            bool verified = TacticalCombatRaycast.TryVerifyProjectileHit(
                muzzle, target, maxRange: 15.0f, blockingMask: ~0, shooterCollider: null, out _);
            Assert.That(verified, Is.False);
        }

        [Test]
        public void LineOfFire_AimPointOutsideCollider_ReturnsMissed_AndDistinguishesSightline()
        {
            var target = CreateTarget(new Vector3(0, 1.0f, 6.0f));

            // Aim point placed far above the capsule collider
            var highAim = new GameObject("HighAimPoint");
            highAim.transform.SetParent(target.transform);
            highAim.transform.position = target.transform.position + Vector3.up * 8.0f;
            target.SetAimPoint(highAim.transform);
            Physics.SyncTransforms();

            Vector3 muzzle = new Vector3(0, 1.2f, 0);

            // Sightline to the high point is free of walls/obstacles
            bool hasLos = TacticalCombatRaycast.HasClearLineOfSight(
                muzzle, target, maxRange: 15.0f, blockingMask: ~0, shooterCollider: null);
            Assert.That(hasLos, Is.True, "Unobstructed LOS must be true when no obstacles block the lane.");

            // But the ballistic projectile misses the actual target collider
            var status = TacticalCombatRaycast.EvaluateLineOfFire(
                muzzle, target, maxRange: 15.0f, blockingMask: ~0, shooterCollider: null, out var hit);
            Assert.That(status, Is.EqualTo(CombatTargetingStatus.Missed), "Empty raycast missing collider must report Missed.");
            Assert.That(hit.collider, Is.Null);

            bool verified = TacticalCombatRaycast.TryVerifyProjectileHit(
                muzzle, target, maxRange: 15.0f, blockingMask: ~0, shooterCollider: null, out _);
            Assert.That(verified, Is.False, "Missed projectile must NOT be authorized as a confirmed hit.");
        }

        [Test]
        public void Actor_RejectedDamage_DoesNotReduceHealthOrEmitTargetHit()
        {
            var actorObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actorObj.name = "Protagonist";
            actorObj.transform.SetParent(_testRoot.transform);
            actorObj.transform.position = Vector3.zero;
            var actor = actorObj.AddComponent<TacticalActorProxy>();
            actor.SetActorId(EntityId.Parse("actor:protagonist"));
            actor.SetWeapon(EntityId.Parse("item:weapon:rifle"), range: 15.0f, damage: 250);

            var enemyTarget = CreateTarget(new Vector3(0, 1.0f, 5.0f), id: "actor:enemy");
            Physics.SyncTransforms();

            var session = new WorldSession(TacticalSliceFixtureState.CreateDefaultState());

            // Fully incapacitate enemy in canonical session (1000 wounds)
            var incapacitateCmd = new DamageCrewCommand(
                EntityId.Parse($"cmd:dmg:incap:{session.Revision}"),
                enemyTarget.TargetId,
                1000,
                session.Revision);
            var incapResult = session.Submit(incapacitateCmd);
            Assert.That(incapResult.Success, Is.True);

            // Keep enemyTarget proxy at non-zero local health to test that rejected damage doesn't touch it
            enemyTarget.SetHealth(500, 1000);
            int healthBefore = enemyTarget.CurrentHealth;

            bool targetHitRaised = false;
            string attackFailReason = null;
            actor.OnTargetHit += (_, _, _) => targetHitRaised = true;
            actor.OnAttackFailed += (_, reason) => attackFailReason = reason;

            actor.SetSession(session);
            bool attackStarted = actor.AttackTarget(enemyTarget, session);

            Assert.That(attackStarted, Is.True, "Attack target accepted order.");
            Assert.That(session.Capture().State.Inventory.GetAmount(InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId), Is.EqualTo(19), "Shot fired and paid.");
            Assert.That(targetHitRaised, Is.False, "OnTargetHit MUST NEVER be raised when damage command is rejected.");
            Assert.That(enemyTarget.CurrentHealth, Is.EqualTo(healthBefore), "Enemy health MUST NOT decrease on rejected damage.");
            Assert.That(attackFailReason, Is.EqualTo(CommandFailure.CrewAlreadyIncapacitated.ToString()));
        }

        [Test]
        public void Actor_UnpaidShot_DoesNotInflictDamageOrMutateState()
        {
            var actorObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actorObj.name = "Protagonist";
            actorObj.transform.SetParent(_testRoot.transform);
            actorObj.transform.position = Vector3.zero;
            var actor = actorObj.AddComponent<TacticalActorProxy>();
            actor.SetActorId(EntityId.Parse("actor:protagonist"));
            actor.SetWeapon(EntityId.Parse("item:weapon:rifle"), range: 15.0f, damage: 250);

            var enemyTarget = CreateTarget(new Vector3(0, 1.0f, 5.0f), id: "actor:enemy");
            Physics.SyncTransforms();

            var defaultState = TacticalSliceFixtureState.CreateDefaultState();
            // Consume all 20 rifle rounds from pack
            var consumed = defaultState.Inventory.TryConsumePack(TacticalSliceFixtureState.AmmoItemId, 20);
            Assert.That(consumed.Success, Is.True);

            var dryState = WorldState.Create(
                defaultState.Minute,
                Array.Empty<SupplyStock>(),
                new[] { actor.ActorId, EntityId.Parse("actor:companion"), enemyTarget.TargetId },
                inventory: consumed.State,
                crewDuty: defaultState.CrewDuty,
                combat: defaultState.Combat);

            var session = new WorldSession(dryState);
            actor.SetSession(session);

            bool targetHitRaised = false;
            string attackFailReason = null;
            actor.OnTargetHit += (_, _, _) => targetHitRaised = true;
            actor.OnAttackFailed += (_, reason) => attackFailReason = reason;

            actor.AttackTarget(enemyTarget, session);
            bool shotResult = actor.TryExecuteShot();

            Assert.That(shotResult, Is.False, "Shot without ammunition must return false.");
            Assert.That(targetHitRaised, Is.False, "OnTargetHit must not be raised for unpaid shot.");
            Assert.That(enemyTarget.CurrentHealth, Is.EqualTo(1000), "Target health must remain untouched.");
            Assert.That(session.Revision, Is.Zero, "Session revision must not advance for unpaid shot.");
            Assert.That(attackFailReason, Is.EqualTo(CommandFailure.InsufficientInventory.ToString()));
        }

        [Test]
        public void CombatTicks_SingleAuthoritativeRunnerPerScope()
        {
            var scopeObj = new GameObject("SessionScope");
            scopeObj.transform.SetParent(_testRoot.transform);
            var scope = scopeObj.AddComponent<WorldSessionScope>();
            scope.SetInitialState(TacticalSliceFixtureState.CreateDefaultState());
            var runner = scopeObj.AddComponent<CombatTickRunner>();

            scope.Build();
            var resolvedSession = scope.Container.Resolve<WorldSession>();
            var resolvedRunner = scope.Container.Resolve<CombatTickRunner>();

            Assert.That(resolvedRunner, Is.SameAs(runner), "Scope container must resolve exactly the single configured tick runner.");
            Assert.That(resolvedSession.Capture().State.Combat.Tick, Is.EqualTo(0));

            resolvedRunner.Step(3);
            Assert.That(resolvedRunner.CurrentTick, Is.EqualTo(3));
            Assert.That(resolvedSession.Capture().State.Combat.Tick, Is.EqualTo(3),
                "Combat ticks must advance authoritative WorldSession without parallel clocks.");

            scope.DisposeCore();
        }

        [Test]
        public void CombatDamage_SaveLoadConsistency()
        {
            var session = new WorldSession(TacticalSliceFixtureState.CreateDefaultState());
            var enemyId = EntityId.Parse("actor:enemy");

            var dmgCmd = new DamageCrewCommand(
                EntityId.Parse($"cmd:dmg:{session.Revision}"),
                enemyId,
                250,
                session.Revision);
            var result = session.Submit(dmgCmd);
            Assert.That(result.Success, Is.True);
            Assert.That(result.State.CrewDuty.Get(enemyId)!.Wounds, Is.EqualTo(250));

            // Serialize to v13 binary
            var snapshot = session.Capture();
            byte[] encoded = SaveCodec.Encode(snapshot);
            Assert.That(encoded, Is.Not.Null);

            // Restore from bytes
            var decodeResult = SaveCodec.TryDecode(encoded);
            Assert.That(decodeResult.Success, Is.True);
            var restoredSnapshot = decodeResult.Value;
            Assert.That(restoredSnapshot, Is.Not.Null);
            Assert.That(restoredSnapshot.State.Combat.Tick, Is.EqualTo(result.State.Combat.Tick));
            Assert.That(restoredSnapshot.State.CrewDuty.Get(enemyId)!.Wounds, Is.EqualTo(250));

            // Validate proxy representation after sync
            var target = CreateTarget(new Vector3(0, 1.0f, 5.0f), id: "actor:enemy");
            target.SyncFromWounds(restoredSnapshot.State.CrewDuty.Get(enemyId)!.Wounds);
            Assert.That(target.CurrentHealth, Is.EqualTo(750), "Target proxy must project exact same health after restore.");
        }

        [Test]
        public void ArmorMitigation_PartialDamage_MitigatesAndAppliesToHealth()
        {
            var actorObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actorObj.name = "Protagonist";
            actorObj.transform.SetParent(_testRoot.transform);
            actorObj.transform.position = Vector3.zero;
            var actor = actorObj.AddComponent<TacticalActorProxy>();
            actor.SetActorId(EntityId.Parse("actor:protagonist"));
            actor.SetWeapon(EntityId.Parse("item:weapon:rifle"), range: 15.0f, damage: 250);

            var enemyTarget = CreateTarget(new Vector3(0, 1.0f, 5.0f), id: "actor:enemy");
            enemyTarget.SetArmorPermille(500); // 50% mitigation
            Physics.SyncTransforms();

            var session = new WorldSession(TacticalSliceFixtureState.CreateDefaultState());
            actor.SetSession(session);

            int reportedDamage = -1;
            actor.OnTargetHit += (_, _, dmg) => reportedDamage = dmg;

            bool attackStarted = actor.AttackTarget(enemyTarget, session);

            Assert.That(attackStarted, Is.True);
            Assert.That(reportedDamage, Is.EqualTo(125), "250 damage with 500 permille armor must result in 125 effective wounds.");
            Assert.That(enemyTarget.CurrentHealth, Is.EqualTo(875), "Target health must decrease by exactly 125.");
        }

        [Test]
        public void ArmorMitigation_FullAbsorption_EmitsZeroDamageAndArmorAbsorbed()
        {
            var actorObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actorObj.name = "Protagonist";
            actorObj.transform.SetParent(_testRoot.transform);
            actorObj.transform.position = Vector3.zero;
            var actor = actorObj.AddComponent<TacticalActorProxy>();
            actor.SetActorId(EntityId.Parse("actor:protagonist"));
            actor.SetWeapon(EntityId.Parse("item:weapon:rifle"), range: 15.0f, damage: 250);

            var enemyTarget = CreateTarget(new Vector3(0, 1.0f, 5.0f), id: "actor:enemy");
            enemyTarget.SetArmorPermille(1000); // 100% deflection
            Physics.SyncTransforms();

            var session = new WorldSession(TacticalSliceFixtureState.CreateDefaultState());
            actor.SetSession(session);

            int reportedDamage = -1;
            string failReason = null;
            actor.OnTargetHit += (_, _, dmg) => reportedDamage = dmg;
            actor.OnAttackFailed += (_, reason) => failReason = reason;

            bool attackStarted = actor.AttackTarget(enemyTarget, session);

            Assert.That(attackStarted, Is.True);
            Assert.That(reportedDamage, Is.EqualTo(0), "1000 permille armor must deflect all damage.");
            Assert.That(failReason, Is.EqualTo("ArmorAbsorbed"));
            Assert.That(enemyTarget.CurrentHealth, Is.EqualTo(1000), "Target health must remain 1000.");
            Assert.That(session.Capture().State.CrewDuty.Get(enemyTarget.TargetId)!.Wounds, Is.Zero);
        }

        [Test]
        public void WeaponMagazine_DischargeDepletesMagazine_AutoReloadsFromPack()
        {
            var actorObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actorObj.name = "Protagonist";
            actorObj.transform.SetParent(_testRoot.transform);
            actorObj.transform.position = Vector3.zero;
            var actor = actorObj.AddComponent<TacticalActorProxy>();
            actor.SetActorId(EntityId.Parse("actor:protagonist"));
            actor.SetWeapon(EntityId.Parse("item:weapon:rifle"), range: 15.0f, damage: 250);

            var enemyTarget = CreateTarget(new Vector3(0, 1.0f, 5.0f), id: "actor:enemy");
            Physics.SyncTransforms();

            // Magazine capacity 2, reload takes 4 ticks, cooldown 3 ticks
            var session = new WorldSession(TacticalSliceFixtureState.CreateStateWithMagazineWeapon(magazineCapacity: 2, reloadTicks: 4));
            actor.SetSession(session);

            long reloadRounds = -1;
            actor.OnWeaponReloaded += (_, count) => reloadRounds = count;

            // AttackTarget fires Shot 1 from magazine (2 -> 1)
            bool attackStarted = actor.AttackTarget(enemyTarget, session);
            Assert.That(attackStarted, Is.True);
            var state = session.Capture().State;
            Assert.That(state.Combat.Get(actor.WeaponId)!.MagazineRounds, Is.EqualTo(1));
            Assert.That(state.Inventory.GetAmount(InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId), Is.EqualTo(20));

            // Advance combat ticks beyond cooldown
            session.AdvanceCombatTick(4);

            // Shot 2: fires from magazine (1 -> 0)
            Assert.That(actor.TryExecuteShot(), Is.True);
            state = session.Capture().State;
            Assert.That(state.Combat.Get(actor.WeaponId)!.MagazineRounds, Is.EqualTo(0));
            Assert.That(state.Inventory.GetAmount(InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId), Is.EqualTo(20));

            // Advance combat ticks beyond cooldown
            session.AdvanceCombatTick(8);

            // Shot 3: Magazine is empty! Auto-reloads from pack
            bool shotFired = actor.TryExecuteShot();
            Assert.That(shotFired, Is.False, "Shot execution must yield during reload initiation.");
            Assert.That(reloadRounds, Is.EqualTo(2), "Reload must load 2 rounds into magazine.");

            state = session.Capture().State;
            Assert.That(state.Combat.Get(actor.WeaponId)!.MagazineRounds, Is.EqualTo(2));
            Assert.That(state.Inventory.GetAmount(InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId), Is.EqualTo(18), "Pack ammo must be reduced by 2.");
        }

        [Test]
        public void ColdExposure_ReducesMovementSpeed()
        {
            var actorObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actorObj.name = "Protagonist";
            actorObj.transform.SetParent(_testRoot.transform);
            var actor = actorObj.AddComponent<TacticalActorProxy>();
            actor.SetActorId(EntityId.Parse("actor:protagonist"));

            float baseSpeed = actor.Agent.speed;
            Assert.That(baseSpeed, Is.GreaterThan(0));

            actor.SetColdExposurePermille(500);
            Assert.That(actor.ColdExposurePermille, Is.EqualTo(500));
            float expected500 = baseSpeed * ColdCombatModifiers.CalculateSpeedMultiplier(500);
            Assert.That(actor.Agent.speed, Is.EqualTo(expected500).Within(0.001f));

            actor.SetColdExposurePermille(1000);
            float expected1000 = baseSpeed * ColdCombatModifiers.CalculateSpeedMultiplier(1000);
            Assert.That(actor.Agent.speed, Is.EqualTo(expected1000).Within(0.001f));

            actor.SetColdExposurePermille(0);
            Assert.That(actor.Agent.speed, Is.EqualTo(baseSpeed).Within(0.001f));
        }

        [Test]
        public void TacticalRetreat_IncreasesSpeedAndSetsRetreatState()
        {
            var actorObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actorObj.name = "Protagonist";
            actorObj.transform.SetParent(_testRoot.transform);
            var actor = actorObj.AddComponent<TacticalActorProxy>();
            actor.SetActorId(EntityId.Parse("actor:protagonist"));

            float baseSpeed = actor.Agent.speed;

            bool retreatStarted = false;
            actor.OnRetreatStarted += _ => retreatStarted = true;

            actor.RetreatTo(new Vector3(10, 0, 10));
            Assert.That(actor.IsRetreating, Is.True);
            Assert.That(actor.Agent.speed, Is.EqualTo(baseSpeed * 1.25f).Within(0.001f));

            actor.MoveTo(new Vector3(5, 0, 5));
            Assert.That(actor.IsRetreating, Is.False);
            Assert.That(actor.Agent.speed, Is.EqualTo(baseSpeed).Within(0.001f));
        }
    }
}
