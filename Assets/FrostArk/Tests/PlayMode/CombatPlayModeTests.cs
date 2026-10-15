using System;
using System.Collections;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    [TestFixture]
    public sealed class CombatPlayModeTests
    {
        private GameObject _sceneRoot;
        private Camera _camera;
        private TacticalActorProxy _protagonist;
        private TacticalCombatTargetProxy _enemy;
        private TacticalCommandController _commander;
        private WorldSessionScope _scope;
        private WorldSession _session;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var activeScene = SceneManager.GetActiveScene();
            foreach (var go in activeScene.GetRootGameObjects())
            {
                if (go.name == "Code-based tests runner" || go.name.Contains("tests runner"))
                    continue;
                UnityEngine.Object.Destroy(go);
            }
            yield return null;

            _sceneRoot = new GameObject("CombatPlayMode_Environment");

            // 1. Walkable ground
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "WalkableGround";
            ground.transform.SetParent(_sceneRoot.transform, false);
            ground.transform.position = new Vector3(0, -0.1f, 0);
            ground.transform.localScale = new Vector3(40, 0.2f, 40);

            // 2. Cover wall obstacle: spans X from -4 to +4 at Z = 3.0
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "CoverWall";
            wall.transform.SetParent(_sceneRoot.transform, false);
            wall.transform.position = new Vector3(0, 1.0f, 3.0f);
            wall.transform.localScale = new Vector3(8.0f, 2.0f, 1.0f);

            // 3. Bake NavMesh
            var navSurface = ground.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.All;
            navSurface.BuildNavMesh();

            // 4. Camera
            var camObj = new GameObject("TacticalCamera");
            camObj.transform.SetParent(_sceneRoot.transform, false);
            camObj.transform.position = new Vector3(0, 16.0f, -10.0f);
            camObj.transform.rotation = Quaternion.Euler(55, 0, 0);
            _camera = camObj.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 10.0f;
            camObj.tag = "MainCamera";

            // 5. Protagonist actor at (-2.5, 0, -2.0)
            _protagonist = CreateActor(_sceneRoot.transform, "Protagonist",
                new Vector3(-2.5f, 0.0f, -2.0f), EntityId.Parse("actor:protagonist"));
            _protagonist.SetWeapon(EntityId.Parse("item:weapon:rifle"), range: 15.0f, damage: 250);

            // 6. Hostile enemy sentry at (0, 0.0, 10.0)
            var enemyObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            enemyObj.name = "EnemySentry";
            enemyObj.transform.SetParent(_sceneRoot.transform, false);
            enemyObj.transform.position = new Vector3(0, 1.0f, 10.0f);
            enemyObj.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);
            _enemy = enemyObj.AddComponent<TacticalCombatTargetProxy>();
            _enemy.SetTargetId(EntityId.Parse("actor:enemy"));
            _enemy.SetHostile(true);
            _enemy.SetHealth(1000, 1000);

            // 7. Session Scope & DI
            var scopeObj = new GameObject("SessionScope");
            scopeObj.transform.SetParent(_sceneRoot.transform, false);
            _scope = scopeObj.AddComponent<WorldSessionScope>();
            _scope.SetInitialState(TacticalSliceFixtureState.CreateDefaultState());
            var tickRunner = scopeObj.AddComponent<CombatTickRunner>();

            var commanderObj = new GameObject("Commander");
            commanderObj.transform.SetParent(_sceneRoot.transform, false);
            _commander = commanderObj.AddComponent<TacticalCommandController>();
            _commander.SetCamera(_camera);

            _scope.Build();
            _session = _scope.Container.Resolve<WorldSession>();
            tickRunner.SetSession(_session);
            _commander.SetSession(_session);
            _commander.RefreshSceneRegistrations();
            _protagonist.SetSession(_session);

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_scope != null)
                _scope.DisposeCore();

            if (_sceneRoot != null)
                UnityEngine.Object.Destroy(_sceneRoot);

            yield return null;
        }

        private static TacticalActorProxy CreateActor(Transform parent, string name, Vector3 position, EntityId actorId)
        {
            var actorObj = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actorObj.name = name;
            actorObj.transform.SetParent(parent, false);
            actorObj.transform.position = position;
            actorObj.transform.localScale = new Vector3(0.8f, 1.0f, 0.8f);

            var agent = actorObj.AddComponent<NavMeshAgent>();
            agent.speed = 4.0f;
            agent.acceleration = 12.0f;
            agent.stoppingDistance = 0.1f;
            agent.radius = 0.4f;
            agent.height = 2.0f;

            var proxy = actorObj.AddComponent<TacticalActorProxy>();
            proxy.SetActorId(actorId);
            return proxy;
        }

        [UnityTest]
        public IEnumerator Test01_ClearShot_FiresAndInflictsAuthoritativeDamage()
        {
            // Position protagonist north of cover wall with direct clear line of fire to enemy
            _protagonist.transform.position = new Vector3(0, 0, 5.0f);
            _protagonist.Agent.Warp(new Vector3(0, 0, 5.0f));
            yield return null;

            _commander.ClearSelection();
            _commander.SelectActor(_protagonist.ActorId);

            long ammoBefore = _session.Capture().State.Inventory.GetAmount(
                InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId);
            Assert.That(ammoBefore, Is.EqualTo(20));
            Assert.That(_enemy.CurrentHealth, Is.EqualTo(1000));

            bool attackIssued = _commander.IssueAttackOrder(_enemy);
            Assert.That(attackIssued, Is.True);

            // Yield frames for raycast verification, fire command, and damage command
            yield return new WaitForSeconds(0.2f);

            long ammoAfter = _session.Capture().State.Inventory.GetAmount(
                InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId);
            Assert.That(ammoAfter, Is.EqualTo(19), "One carried ammo round must be consumed from pack.");

            Assert.That(_enemy.CurrentHealth, Is.EqualTo(750), "Enemy target health must drop to 750.");
            Assert.That(_session.Capture().State.CrewDuty.Get(_enemy.TargetId)!.Wounds, Is.EqualTo(250),
                "Authoritative wounds must be recorded in CrewDuty.");
            Assert.That(_session.Revision, Is.EqualTo(2),
                "Revision must advance by 2 (1 for weapon fired, 1 for damage inflicted).");
        }

        [UnityTest]
        public IEnumerator Test02_WallObstructed_ActorNavigatesAroundWallToEngage()
        {
            // Protagonist starts south of wall at (-2.5, 0, -2.0)
            // Enemy is at (0, 1.0, 10.0). Wall is at z=3 (x from -4 to +4)
            _protagonist.transform.position = new Vector3(-2.5f, 0, -2.0f);
            _protagonist.Agent.Warp(new Vector3(-2.5f, 0, -2.0f));
            yield return null;

            _commander.ClearSelection();
            _commander.SelectActor(_protagonist.ActorId);

            bool attackIssued = _commander.IssueAttackOrder(_enemy);
            Assert.That(attackIssued, Is.True);

            // Initial status must be Obstructed because wall is directly in line of fire
            Assert.That(_protagonist.LastTargetingStatus, Is.EqualTo(CombatTargetingStatus.Obstructed));

            // Wait for agent to pathfind around the wall and regain clear line of fire
            float timeout = 8.0f;
            while (_enemy.CurrentHealth == 1000 && timeout > 0)
            {
                yield return null;
                timeout -= Time.deltaTime;
            }

            Assert.That(_enemy.CurrentHealth, Is.LessThan(1000),
                "Actor must navigate around wall to clear line of fire and inflict damage.");
            Assert.That(_session.Capture().State.Inventory.GetAmount(
                InventoryContainer.Pack, TacticalSliceFixtureState.AmmoItemId), Is.EqualTo(19));
        }

        [UnityTest]
        public IEnumerator Test03_OutOfAmmo_FailsSafelyWithoutFakeDamage()
        {
            // Empty all ammo from pack in WorldSession
            var state = _session.Capture().State;
            var emptyPack = state.Inventory.TryConsumePack(TacticalSliceFixtureState.AmmoItemId, 20);
            Assert.That(emptyPack.Success, Is.True);

            var dryState = WorldState.Create(state.Minute,
                Array.Empty<SupplyStock>(),
                new[] { _protagonist.ActorId, EntityId.Parse("actor:companion"), _enemy.TargetId },
                inventory: emptyPack.State,
                crewDuty: state.CrewDuty,
                combat: state.Combat);

            _session = new WorldSession(dryState);
            _commander.SetSession(_session);

            _protagonist.transform.position = new Vector3(0, 0, 5.0f);
            _protagonist.Agent.Warp(new Vector3(0, 0, 5.0f));
            yield return null;

            _commander.ClearSelection();
            _commander.SelectActor(_protagonist.ActorId);

            string failReason = null;
            _protagonist.OnAttackFailed += (_, reason) => failReason = reason;

            _commander.IssueAttackOrder(_enemy);
            yield return new WaitForSeconds(0.2f);

            Assert.That(failReason, Is.EqualTo(CommandFailure.InsufficientInventory.ToString()),
                "Attack must fail due to InsufficientInventory.");
            Assert.That(_enemy.CurrentHealth, Is.EqualTo(1000), "No fake damage must be applied without ammo.");
            Assert.That(_protagonist.PendingTarget, Is.Null, "Combat order must cancel when out of ammo.");
        }

        [UnityTest]
        public IEnumerator Test04_EliminatingTarget_DisablesHitboxAndClearsOrder()
        {
            // Position protagonist in clear range
            _protagonist.transform.position = new Vector3(0, 0, 5.0f);
            _protagonist.Agent.Warp(new Vector3(0, 0, 5.0f));
            yield return null;

            // Authorize near-death condition in canonical WorldSession (750 wounds = 250 HP remaining)
            var preDmgCmd = new DamageCrewCommand(
                EntityId.Parse($"cmd:dmg:setup:{_session.Revision}:{Guid.NewGuid():N}"),
                _enemy.TargetId,
                750,
                _session.Revision);
            var preDmgResult = _session.Submit(preDmgCmd);
            Assert.That(preDmgResult.Success, Is.True);
            _enemy.SyncFromWounds(750);
            Assert.That(_enemy.CurrentHealth, Is.EqualTo(250), "Enemy health must be 250 after canonical 750 wounds.");

            _commander.ClearSelection();
            _commander.SelectActor(_protagonist.ActorId);

            _commander.IssueAttackOrder(_enemy);

            yield return new WaitForSeconds(0.25f);

            Assert.That(_enemy.CurrentHealth, Is.Zero, "Enemy health must reach zero.");
            Assert.That(_enemy.IsDead, Is.True, "Enemy must be marked dead.");
            Assert.That(_enemy.Hitbox.enabled, Is.False, "Hitbox collider must be disabled upon elimination.");
            Assert.That(_protagonist.PendingTarget, Is.Null, "Actor must clear pending target when eliminated.");
        }
    }
}
