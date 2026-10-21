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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    [TestFixture]
    public sealed class TacticalPlayModeTests
    {
        private GameObject _sceneRoot;
        private Camera _camera;
        private TacticalActorProxy _protagonist;
        private TacticalActorProxy _companion;
        private TacticalInteractableProxy _salvage;
        private TacticalCommandController _commander;
        private WorldSessionPresenter _presenter;
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

            _sceneRoot = new GameObject("PlayModeTestEnvironment");

            // 1. Walkable Ground
            var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "WalkableGround";
            ground.transform.SetParent(_sceneRoot.transform, false);
            ground.transform.position = new Vector3(0, -0.1f, 0);
            ground.transform.localScale = new Vector3(40, 0.2f, 40);

            // 2. Cover Wall Obstacle
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "CoverWall";
            wall.transform.SetParent(_sceneRoot.transform, false);
            wall.transform.position = new Vector3(0, 1.0f, 3.0f);
            wall.transform.localScale = new Vector3(8.0f, 2.0f, 1.0f);

            // 3. Bake NavMeshSurface across ground and wall obstacle
            var navSurface = ground.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.All;
            navSurface.BuildNavMesh();

            // 4. Tactical Camera
            var camObj = new GameObject("TacticalCamera");
            camObj.transform.SetParent(_sceneRoot.transform, false);
            camObj.transform.position = new Vector3(0, 16.0f, -10.0f);
            camObj.transform.rotation = Quaternion.Euler(55, 0, 0);
            _camera = camObj.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 10.0f;
            _camera.nearClipPlane = 0.3f;
            _camera.farClipPlane = 100.0f;
            camObj.tag = "MainCamera";

            // 5. Destination feedback marker
            var markerObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            markerObj.name = "DestinationMarker";
            markerObj.transform.SetParent(_sceneRoot.transform, false);
            markerObj.transform.localScale = new Vector3(0.8f, 0.02f, 0.8f);
            var markerCollider = markerObj.GetComponent<Collider>();
            if (markerCollider != null) UnityEngine.Object.DestroyImmediate(markerCollider);
            var destMarker = markerObj.AddComponent<TacticalDestinationMarker>();
            markerObj.SetActive(false);

            // 6. Protagonist
            _protagonist = CreateActor(_sceneRoot.transform, "Protagonist",
                new Vector3(-2.5f, 0.0f, -2.0f), EntityId.Parse("actor:protagonist"));

            // 7. Companion
            _companion = CreateActor(_sceneRoot.transform, "Companion",
                new Vector3(2.5f, 0.0f, -2.0f), EntityId.Parse("actor:companion"));

            // 8. Salvage Interactable (supply_cache)
            var crateObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crateObj.name = "SalvageCrate";
            crateObj.transform.SetParent(_sceneRoot.transform, false);
            crateObj.transform.position = new Vector3(0, 0.5f, 7.0f);
            crateObj.transform.localScale = new Vector3(1.2f, 1.0f, 1.2f);
            _salvage = crateObj.AddComponent<TacticalInteractableProxy>();
            _salvage.SetTargetId(EntityId.Parse("salvage:supply_cache"));
            _salvage.SetInteractionRange(2.5f);

            // 9. Session Scope & DI
            var scopeObj = new GameObject("SessionScope");
            scopeObj.transform.SetParent(_sceneRoot.transform, false);

            var presenterObj = new GameObject("SessionPresenter");
            presenterObj.transform.SetParent(scopeObj.transform, false);
            _presenter = presenterObj.AddComponent<WorldSessionPresenter>();

            _scope = scopeObj.AddComponent<WorldSessionScope>();
            _scope.SetInitialState(TacticalSliceFixtureState.CreateDefaultState());

            var commanderObj = new GameObject("Commander");
            commanderObj.transform.SetParent(_sceneRoot.transform, false);
            _commander = commanderObj.AddComponent<TacticalCommandController>();
            _commander.SetCamera(_camera);
            _commander.SetDestinationMarker(destMarker);

            _scope.Build();
            _session = _scope.Container.Resolve<WorldSession>();
            _commander.SetSession(_session);
            _commander.RefreshSceneRegistrations();

            // Wait 1 frame for physics and NavMesh agent initialization
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

        private static float FlatDistance(Vector3 a, Vector3 b)
        {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
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

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SelectionRing";
            ring.transform.SetParent(actorObj.transform, false);
            ring.transform.localPosition = new Vector3(0, -0.95f, 0);
            ring.transform.localScale = new Vector3(1.4f, 0.02f, 1.4f);
            var ringCol = ring.GetComponent<Collider>();
            if (ringCol != null) UnityEngine.Object.DestroyImmediate(ringCol);
            ring.SetActive(false);

            var proxy = actorObj.AddComponent<TacticalActorProxy>();
            proxy.SetActorId(actorId);
            proxy.SetSelectionMarker(ring);
            return proxy;
        }

        [UnityTest]
        public IEnumerator Test01_PointerClick_SelectsProtagonist_AndCompanionToggleWorks()
        {
            _commander.ClearSelection();
            Assert.That(_commander.Selection.Count, Is.Zero);

            // 1. Click protagonist screen position
            Vector2 screenPos = _camera.WorldToScreenPoint(_protagonist.transform.position);
            bool clicked = _commander.ProcessPointerClick(screenPos, isAdditive: false);

            Assert.That(clicked, Is.True);
            Assert.That(_commander.Selection.Count, Is.EqualTo(1));
            Assert.That(_commander.Selection.IsSelected(_protagonist.ActorId), Is.True);
            Assert.That(_protagonist.IsSelected, Is.True);
            Assert.That(_companion.IsSelected, Is.False);

            // 2. Additive click companion
            Vector2 compScreenPos = _camera.WorldToScreenPoint(_companion.transform.position);
            bool compClicked = _commander.ProcessPointerClick(compScreenPos, isAdditive: true);

            Assert.That(compClicked, Is.True);
            Assert.That(_commander.Selection.Count, Is.EqualTo(2));
            Assert.That(_commander.Selection.IsSelected(_protagonist.ActorId), Is.True);
            Assert.That(_commander.Selection.IsSelected(_companion.ActorId), Is.True);
            Assert.That(_protagonist.IsSelected, Is.True);
            Assert.That(_companion.IsSelected, Is.True);

            yield return null;
        }

        [UnityTest]
        public IEnumerator Test02_ClickOnGround_WalksOverMultipleFrames_WithoutTeleporting()
        {
            _commander.ClearSelection();
            _commander.SelectActor(_protagonist.ActorId);

            Vector3 startPos = _protagonist.transform.position;
            Vector3 destination = new Vector3(-2.5f, 0.0f, 4.0f);

            Assert.That(_protagonist.Agent.isOnNavMesh, Is.True, "Protagonist NavMeshAgent must be placed on NavMesh.");

            bool moveIssued = _commander.IssueMoveOrder(destination);
            Assert.That(moveIssued, Is.True);

            // Yield a few frames: agent should be moving continuously, not instant teleport
            yield return new WaitForSeconds(0.15f);

            float distFromStart = FlatDistance(_protagonist.transform.position, startPos);
            float distToDestination = FlatDistance(_protagonist.transform.position, destination);

            Assert.That(distFromStart, Is.GreaterThan(0.05f), "Protagonist must have moved from start position.");
            Assert.That(distToDestination, Is.GreaterThan(0.5f), "Protagonist must NOT teleport instantly to destination.");

            // Wait for agent to arrive at destination
            float timeout = 4.0f;
            while (FlatDistance(_protagonist.transform.position, destination) > 0.3f && timeout > 0)
            {
                yield return null;
                timeout -= Time.deltaTime;
            }

            Assert.That(FlatDistance(_protagonist.transform.position, destination), Is.LessThan(0.4f),
                "Protagonist must reach destination via NavMesh navigation.");
        }

        [UnityTest]
        public IEnumerator Test03_NavMeshAgent_NavigatesAroundCoverWall()
        {
            _commander.ClearSelection();
            _commander.SelectActor(_protagonist.ActorId);

            // Position protagonist south of the cover wall
            _protagonist.transform.position = new Vector3(0, 0, 0);
            _protagonist.Agent.Warp(new Vector3(0, 0, 0));

            // Destination is north of cover wall (which is at z=3, spanning x from -4 to +4)
            Vector3 destinationBehindWall = new Vector3(0, 0, 6.0f);

            bool moveIssued = _commander.IssueMoveOrder(destinationBehindWall);
            Assert.That(moveIssued, Is.True);

            // Let agent calculate and start walking
            yield return new WaitForSeconds(0.2f);
            Assert.That(_protagonist.Agent.hasPath, Is.True);

            // Monitor trajectory over time to verify agent paths around the wall (x deviates around the obstacle)
            float maxAbsX = 0f;
            float timeout = 6.0f;
            while (FlatDistance(_protagonist.transform.position, destinationBehindWall) > 0.4f && timeout > 0)
            {
                maxAbsX = Mathf.Max(maxAbsX, Mathf.Abs(_protagonist.transform.position.x));
                yield return null;
                timeout -= Time.deltaTime;
            }

            // Wall half-width is 4.0; agent pathing around it must reach around abs(x) > 3.0
            Assert.That(maxAbsX, Is.GreaterThan(2.5f), "Agent path must navigate around the cover wall obstacle.");
            Assert.That(FlatDistance(_protagonist.transform.position, destinationBehindWall), Is.LessThan(0.5f),
                "Agent must reach destination behind wall.");
        }

        [UnityTest]
        public IEnumerator Test04_InvalidDestination_FailsSafely()
        {
            _commander.ClearSelection();
            _commander.SelectActor(_protagonist.ActorId);

            Vector3 startPos = _protagonist.transform.position;
            Vector3 offMeshDestination = new Vector3(1000f, 100f, 1000f);

            bool moveResult = _commander.IssueMoveOrder(offMeshDestination);
            Assert.That(moveResult, Is.False, "IssueMoveOrder must return false when NavMesh destination is invalid/unreachable.");
            Assert.That(_commander.LastIssuedIntent, Is.Null, "LastIssuedIntent must NOT be set when move order is rejected.");

            yield return new WaitForSeconds(0.1f);

            // Agent must not teleport or jump to the invalid coordinate
            Assert.That(Vector3.Distance(_protagonist.transform.position, startPos), Is.LessThan(0.5f));
            Assert.That(_protagonist.Agent.isOnNavMesh, Is.True);
        }

        [UnityTest]
        public IEnumerator Test05_ClickSalvage_Approaches_RangeValidates_AndCollectsAuthoritatively()
        {
            _commander.ClearSelection();
            _commander.SelectActor(_protagonist.ActorId);

            // Initial authoritative state checks
            Assert.That(_session.Revision, Is.Zero);
            Assert.That(_salvage.IsDepleted, Is.False);
            Assert.That(_session.Capture().State.Inventory.IsCollected(_salvage.TargetId), Is.False);
            Assert.That(_session.Capture().State.Inventory.GetAmount(InventoryContainer.Pack, EntityId.Parse("item:scrap_metal")), Is.Zero);

            // Issue contextual interaction order
            bool interactIssued = _commander.IssueInteractOrder(_salvage);
            Assert.That(interactIssued, Is.True);

            // Protagonist must approach salvage crate over multiple frames
            float timeout = 5.0f;
            while (!_salvage.IsDepleted && timeout > 0)
            {
                yield return null;
                timeout -= Time.deltaTime;
            }

            // Authoritative state must now reflect exactly one committed collection
            Assert.That(_salvage.IsDepleted, Is.True, "Salvage crate must be depleted after collection.");
            Assert.That(_session.Revision, Is.EqualTo(1), "WorldSession revision must increment exactly once.");
            Assert.That(_session.Capture().State.Inventory.IsCollected(_salvage.TargetId), Is.True,
                "Pickup must be marked collected in authoritative InventoryState.");
            Assert.That(_session.Capture().State.Inventory.GetAmount(InventoryContainer.Pack, EntityId.Parse("item:scrap_metal")), Is.EqualTo(5),
                "Collected quantity must be credited to pack inventory.");
        }

        [UnityTest]
        public IEnumerator Test06_RepeatedOrRejectedSalvage_DoesNotDuplicateOrChangeRevision()
        {
            // First, perform valid collection
            _commander.ClearSelection();
            _commander.SelectActor(_protagonist.ActorId);
            _commander.IssueInteractOrder(_salvage);

            float timeout = 5.0f;
            while (!_salvage.IsDepleted && timeout > 0)
            {
                yield return null;
                timeout -= Time.deltaTime;
            }
            Assert.That(_salvage.IsDepleted, Is.True);
            Assert.That(_session.Revision, Is.EqualTo(1));

            // Attempt repeated interaction with already depleted crate
            bool repeatAttempt = _salvage.Interact(_protagonist);
            Assert.That(repeatAttempt, Is.False, "Interact on depleted crate must be rejected.");

            // Directly submitting duplicate CollectSalvageCommand to session must also be rejected
            var duplicateCmd = new CollectSalvageCommand(
                EntityId.Parse("cmd:salvage:repeat:01"),
                _salvage.TargetId,
                _session.Revision);
            var result = _session.Submit(duplicateCmd);
            Assert.That(result.Success, Is.False, "Duplicate collection must be rejected by WorldCommandBus.");
            Assert.That(result.Failure, Is.EqualTo(CommandFailure.AlreadyCollected), "Expected collection rejection.");
            Assert.That(_session.Revision, Is.EqualTo(1), "Revision must remain unchanged after rejected duplicate.");
            Assert.That(_session.Capture().State.Inventory.GetAmount(InventoryContainer.Pack, EntityId.Parse("item:scrap_metal")), Is.EqualTo(5),
                "Inventory items must not be duplicated.");
        }

        [UnityTest]
        public IEnumerator Test07_MultiUnitMoveOrder_DispatchesToBothActors()
        {
            _commander.ClearSelection();
            _commander.SelectActor(_protagonist.ActorId);
            _commander.SelectActor(_companion.ActorId, addToSelection: true);

            Assert.That(_commander.Selection.Count, Is.EqualTo(2));

            Vector3 groupDestination = new Vector3(0, 0, -4.0f);
            Vector3 protStart = _protagonist.transform.position;
            Vector3 compStart = _companion.transform.position;

            bool moveIssued = _commander.IssueMoveOrder(groupDestination);
            Assert.That(moveIssued, Is.True);

            yield return new WaitForSeconds(0.3f);

            Assert.That(Vector3.Distance(_protagonist.transform.position, protStart), Is.GreaterThan(0.05f),
                "Protagonist must move in group order.");
            Assert.That(Vector3.Distance(_companion.transform.position, compStart), Is.GreaterThan(0.05f),
                "Companion must move in group order.");
        }

        [UnityTest]
        public IEnumerator Test08_RealInputSystem_PointerClickInjected_DispatchesThroughUpdate()
        {
            _commander.ClearSelection();
            Assert.That(_commander.Selection.Count, Is.Zero);

            // Compute screen position of protagonist from camera
            Vector2 screenPos = _camera.WorldToScreenPoint(_protagonist.transform.position);

            var prevBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            var prevBackground = InputSystem.settings.backgroundBehavior;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;

            try
            {
                // Ensure InputSystem Mouse device exists
                var mouse = Mouse.current ?? InputSystem.AddDevice<Mouse>();
                Assert.That(mouse, Is.Not.Null, "Input System Mouse device must be present.");

                // Queue pointer position update
                using (DeltaStateEvent.From(mouse.position, out var posEvent))
                {
                    mouse.position.WriteValueIntoEvent(screenPos, posEvent);
                    InputSystem.QueueEvent(posEvent);
                }

                // Queue left mouse button press
                using (DeltaStateEvent.From(mouse.leftButton, out var pressEvent))
                {
                    mouse.leftButton.WriteValueIntoEvent(1f, pressEvent);
                    InputSystem.QueueEvent(pressEvent);
                }

                // Yield a frame so InputSystem processes queued events and Update() executes
                yield return null;

                // Queue left mouse button release
                using (DeltaStateEvent.From(mouse.leftButton, out var releaseEvent))
                {
                    mouse.leftButton.WriteValueIntoEvent(0f, releaseEvent);
                    InputSystem.QueueEvent(releaseEvent);
                }
                yield return null;

                // Protagonist must be selected through the real Update() -> Input System path
                Assert.That(_commander.Selection.Count, Is.EqualTo(1), "Real pointer press must select protagonist via Update().");
                Assert.That(_commander.Selection.IsSelected(_protagonist.ActorId), Is.True);
                Assert.That(_protagonist.IsSelected, Is.True);
            }
            finally
            {
                InputSystem.settings.editorInputBehaviorInPlayMode = prevBehavior;
                InputSystem.settings.backgroundBehavior = prevBackground;
            }
        }

        [UnityTest]
        public IEnumerator Test09_SavedVerticalSliceScene_LoadsAndExecutesSmoke()
        {
            // Nullify programmatic fixture references as Single scene load unloads them
            _scope = null;
            _sceneRoot = null;

            // Load the actual saved TacticalVerticalSlice scene
            var loadOp = SceneManager.LoadSceneAsync("TacticalVerticalSlice", LoadSceneMode.Single);
            Assert.That(loadOp, Is.Not.Null, "TacticalVerticalSlice scene must be registered in build settings and loadable.");

            while (!loadOp.isDone)
            {
                yield return null;
            }

            // Yield an extra frame for scene Awake / Start / VContainer resolution
            yield return null;

            // Locate scene components
            var commander = UnityEngine.Object.FindFirstObjectByType<TacticalCommandController>();
            Assert.That(commander, Is.Not.Null, "TacticalCommandController must exist in saved scene.");

            var presenter = UnityEngine.Object.FindFirstObjectByType<WorldSessionPresenter>();
            Assert.That(presenter, Is.Not.Null, "WorldSessionPresenter must exist in saved scene.");
            Assert.That(presenter.IsBound, Is.True, "Presenter must be bound to authoritative WorldSession.");
            Assert.That(presenter.Read().Revision, Is.Zero, "Session must initialize at revision 0.");

            var actors = UnityEngine.Object.FindObjectsByType<TacticalActorProxy>(FindObjectsSortMode.None);
            Assert.That(actors.Length, Is.GreaterThanOrEqualTo(2), "Saved scene must contain protagonist and companion.");

            var protagonist = System.Array.Find(actors, a => a.ActorId == EntityId.Parse("actor:protagonist"));
            Assert.That(protagonist, Is.Not.Null, "Protagonist proxy must exist in saved scene.");
            Assert.That(protagonist.Agent.isOnNavMesh, Is.True, "Protagonist must be placed on baked NavMesh.");

            var salvage = UnityEngine.Object.FindFirstObjectByType<TacticalInteractableProxy>();
            Assert.That(salvage, Is.Not.Null, "Salvage crate must exist in saved scene.");
            Assert.That(salvage.IsDepleted, Is.False, "Salvage crate must be intact initially.");

            // Select protagonist and issue move order on baked NavMesh
            commander.SelectActor(protagonist.ActorId);
            Assert.That(protagonist.IsSelected, Is.True);

            Vector3 moveTarget = new Vector3(-2.5f, 0.0f, 2.0f);
            bool moveIssued = commander.IssueMoveOrder(moveTarget);
            Assert.That(moveIssued, Is.True, "Move order on baked NavMesh in saved scene must be accepted.");

            // Let agent walk towards destination over several frames
            yield return new WaitForSeconds(0.25f);
            Assert.That(protagonist.Agent.hasPath, Is.True, "Agent must have active path in saved scene.");

            // Order contextual interaction with salvage crate
            bool interactIssued = commander.IssueInteractOrder(salvage);
            Assert.That(interactIssued, Is.True, "Interact order in saved scene must be accepted.");

            float timeout = 5.0f;
            while (!salvage.IsDepleted && timeout > 0)
            {
                yield return null;
                timeout -= Time.deltaTime;
            }

            Assert.That(salvage.IsDepleted, Is.True, "Salvage in saved scene must be depleted after collection.");
            Assert.That(presenter.Read().Revision, Is.EqualTo(1), "WorldSession revision must advance in saved scene.");
        }

        [UnityTest]
        public IEnumerator Test10_PrimaryEmptyClick_DeselectsWithoutCancellingNavMeshOrder()
        {
            _commander.ClearSelection();
            _commander.SelectActor(_protagonist.ActorId);
            var destination = new Vector3(-2.5f, 0f, 4f);
            Assert.That(_commander.IssueMoveOrder(destination), Is.True);
            var issued = _commander.LastIssuedIntent;
            var from = _protagonist.transform.position;

            var blank = _camera.WorldToScreenPoint(new Vector3(0f, 0f, -6f));
            Assert.That(_commander.ProcessPointerClick(blank), Is.False);
            Assert.That(_commander.Selection.Count, Is.Zero);
            Assert.That(_commander.LastIssuedIntent, Is.SameAs(issued),
                "Deselect must not issue/erase a persistent player command.");

            yield return new WaitForSeconds(0.25f);
            Assert.That(FlatDistance(_protagonist.transform.position, from),
                Is.GreaterThan(0.05f),
                "The NavMesh order must continue after deselection.");
            Assert.That(_protagonist.IsSelected, Is.False);
        }

        [UnityTest]
        public IEnumerator Test11_BoxSelection_AndContextGroupMove_ReuseNavMeshAgents()
        {
            _commander.ClearSelection();
            var a = (Vector2)_camera.WorldToScreenPoint(
                _protagonist.GetComponent<Collider>().bounds.center);
            var b = (Vector2)_camera.WorldToScreenPoint(
                _companion.GetComponent<Collider>().bounds.center);
            var lo = Vector2.Min(a, b) - new Vector2(60f, 60f);
            var hi = Vector2.Max(a, b) + new Vector2(60f, 60f);
            Assert.That(_commander.ProcessBoxSelection(lo, hi), Is.EqualTo(2));
            Assert.That(_protagonist.IsSelected && _companion.IsSelected, Is.True);
            var startHero = _protagonist.transform.position;
            var startCompanion = _companion.transform.position;

            var target = _camera.WorldToScreenPoint(new Vector3(0f, 0f, -4f));
            Assert.That(_commander.ProcessContextClick(target), Is.True,
                "Right-click ground must command the selected NavMesh group.");
            yield return new WaitForSeconds(0.25f);
            Assert.That(FlatDistance(startHero, _protagonist.transform.position),
                Is.GreaterThan(0.05f));
            Assert.That(FlatDistance(startCompanion, _companion.transform.position),
                Is.GreaterThan(0.05f));
        }

        [UnityTest]
        public IEnumerator Test12_EmptyBox_ReleasesOnlySelection_NotExistingOrders()
        {
            _commander.SelectActor(_protagonist.ActorId);
            Assert.That(_commander.IssueMoveOrder(new Vector3(-2.5f, 0f, 4f)),
                Is.True);
            var issued = _commander.LastIssuedIntent;
            // An empty viewport rectangle is deliberately not a move command.
            var count = _commander.ProcessBoxSelection(
                new Vector2(-100f, -100f), new Vector2(-10f, -10f));
            Assert.That(count, Is.Zero);
            Assert.That(_commander.LastIssuedIntent, Is.SameAs(issued));
            Assert.That(_protagonist.IsSelected, Is.False);
            yield return new WaitForSeconds(0.25f);
            Assert.That(_protagonist.Agent.hasPath, Is.True);
        }

        [UnityTest]
        public IEnumerator Test13_PrimarySelection_RejectsActorHiddenBySolidObstacle()
        {
            _commander.ClearSelection();
            var collider = _protagonist.GetComponent<Collider>();
            var center = collider.bounds.center;
            var screenPoint = _camera.WorldToScreenPoint(center);
            var ray = _camera.ScreenPointToRay(screenPoint);
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = "TemporaryOccluder";
            obstacle.transform.SetParent(_sceneRoot.transform, false);
            obstacle.transform.position =
                ray.GetPoint(Vector3.Distance(ray.origin, center) * 0.5f);
            obstacle.transform.localScale = new Vector3(2f, 2f, 0.4f);
            Physics.SyncTransforms();

            Assert.That(_commander.ProcessPointerClick(screenPoint), Is.False);
            Assert.That(_commander.Selection.Count, Is.Zero,
                "Unit selection must not raycast through a solid wall.");
            UnityEngine.Object.Destroy(obstacle);
            yield return null;
        }

    }
}
