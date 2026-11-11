using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using VContainer;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// One persistent run root owns exactly one ship, one camera/light/input rig
    /// and one physical GameObject per crew member. Environment scenes may load
    /// additively around this root; they must not own a session, camera or actors.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class PersistentWorldRuntime : MonoBehaviour, IInitialWorldStateProvider
    {
        public const string ShipSceneId = "scene:ship:persistent";
        public const string ShipAnchorId = "anchor:ship:start";
        public const string BridgeRoomId = "room:ship:bridge";
        public const string EngineRoomId = "room:ship:engineering";
        public const string DoorId = "door:ship:bulkhead";
        public const string CaptainId = "crew:ship:captain";
        public const string EngineerId = "crew:ship:engineer";
        public const string ScoutId = "crew:ship:scout";
        public const string MedicId = "crew:ship:medic";
        public const string BlueprintId = "blueprint:ship:machine";
        public const string EngineBlueprintId = "blueprint:ship:engine";
        public const string BridgeConsoleId = "module:ship:bridge-console";
        public const string EnginePlantId = "module:ship:engine-plant";
        public const string WorkSlotId = "slot:ship:work";
        public const string WatchWorkSlotId = "slot:ship:watch";
        public const string RestSlotId = "slot:ship:rest";
        public const string DrinkSlotId = "slot:ship:drink";
        public const string RepairJobId = "repair:ship:hull";
        public const string WaterItemId = "item:ship:water";
        public const string WaterPickupId = "pickup:ship:water";
        private const string ShipPrefabResourcePath = "E12PersistentShip";
        public const string ShoreSceneName = "E12ShoreEnvironment";
        public const string ShoreSceneId = "scene:shore:training-floe";
        public const string ShoreSiteId = "island:training-floe";
        public const string ShoreAnchorId = "anchor:shore:gangway";
        public static readonly Vector3 ShoreEntryPoint = new Vector3(21f, 0f, 3f);
        private static readonly Vector2Int[] ShipGangwayQueue =
        {
            new Vector2Int(4, 2),
            new Vector2Int(3, 2),
            new Vector2Int(4, 1),
            new Vector2Int(4, 3),
            new Vector2Int(3, 1),
            new Vector2Int(3, 3)
        };

        private static readonly EntityId[] CrewIds =
        {
            EntityId.Parse(CaptainId),
            EntityId.Parse(EngineerId),
            EntityId.Parse(ScoutId),
            EntityId.Parse(MedicId)
        };

        private static readonly Vector2Int[] StartCells =
        {
            new Vector2Int(1, 1),
            new Vector2Int(1, 3),
            new Vector2Int(2, 2),
            new Vector2Int(3, 3)
        };

        private static readonly Color[] CrewColors =
        {
            new Color(0.95f, 0.76f, 0.45f),
            new Color(0.42f, 0.78f, 0.92f),
            new Color(0.76f, 0.85f, 0.56f),
            new Color(0.88f, 0.56f, 0.70f)
        };

        private readonly List<PersistentCrewActor> _crewActors =
            new List<PersistentCrewActor>();

        private WorldSession _session;
        private Transform _shipRoot;
        private ShipTacticalSceneBootstrap _bootstrap;
        private TacticalCommandController _commander;
        private Camera _camera;
        private bool _ready;
        private bool _shoreReady;
        private bool _crossing;
        private bool _queueRunning;
        private bool _stepSucceeded;
        private bool _restoring;
        private float _lastReconcileTime;
        private string _initializationError;
        private string _shoreError;
        private string _lastTransitStatus;
        private string _lastRestoreError;

        public bool IsReady => _ready;
        public bool IsShoreReady => _shoreReady;
        public bool IsCrossing => _crossing;
        public bool IsTransitBusy => _queueRunning || _crossing;
        public bool IsRestoring => _restoring;
        public string LastRestoreError => _lastRestoreError;
        public WorldSession Session => _session;
        public Transform ShipRoot => _shipRoot;
        public ShipTacticalSceneBootstrap Bootstrap => _bootstrap;
        public TacticalCommandController Commander => _commander;
        public Camera GameplayCamera => _camera;
        public string InitializationError => _initializationError;
        public string ShoreError => _shoreError;
        public string LastTransitStatus => _lastTransitStatus;
        public IReadOnlyList<PersistentCrewActor> CrewActors => _crewActors;

        public static ShipGridCell GangwayCell =>
            new ShipGridCell(EntityId.Parse(EngineRoomId), 5, 2);

        public WorldState ProvideInitialState() => CreateDefaultState();

        /// <summary>
        /// Capture canonical world truth plus the derived physical positions of
        /// the one actor object per crew. Safe points only; callers must not save
        /// midway through a gangway crossing.
        /// </summary>
        public SaveGameDto CaptureSnapshot()
        {
            if (_session == null)
                throw new InvalidOperationException("Persistent world has no session.");
            if (IsTransitBusy)
                throw new InvalidOperationException(
                    "Cannot capture the world while transit is in progress.");
            var locations = new List<PhysicalActorLocation>(_crewActors.Count);
            foreach (var actor in _crewActors)
            {
                if (actor == null)
                    continue;
                if (actor.IsOnShip && actor.ShipMotor != null &&
                    actor.ShipMotor.IsConfigured)
                    locations.Add(PhysicalActorLocation.Aboard(actor.ActorId,
                        actor.ShipMotor.LastReachedCell));
                else if (!actor.IsOnShip)
                    locations.Add(PhysicalActorLocation.Ashore(actor.ActorId,
                        actor.transform.position.x, actor.transform.position.z));
            }
            return _session.Capture(locations);
        }

        [Inject]
        public void Construct(WorldSession session)
        {
            _session = session;
        }

        private void Start()
        {
            if (_ready)
                return;

            if (_session == null)
            {
                var scope = GetComponent<WorldSessionScope>();
                if (scope != null && scope.Container != null)
                    _session = scope.Container.Resolve<WorldSession>();
            }

            if (_session == null)
            {
                _initializationError = "Persistent world has no WorldSession.";
                return;
            }

            try
            {
                BuildWorld(_session);
            }
            catch (Exception ex)
            {
                _initializationError = ex.Message;
                Debug.LogException(ex, this);
            }
        }

        /// <summary>Idempotent composition entry point for tests and production.</summary>
        public void BuildWorld(WorldSession session)
        {
            if (_ready)
                return;
            if (session == null)
                throw new ArgumentNullException(nameof(session));

            _session = session;
            if (Application.isPlaying)
                DontDestroyOnLoad(gameObject);

            if (session.SceneCheckpoint == null)
            {
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    EntityId.Parse(ShipSceneId), EntityId.Parse(ShipAnchorId)));
            }
            if (session.SceneCheckpoint.Theater != PhysicalTheater.Ship &&
                session.SceneCheckpoint.Theater != PhysicalTheater.Shore)
                throw new InvalidOperationException(
                    "Persistent world requires a ship or shore physical checkpoint.");
            ValidateCheckpoint(session.SceneCheckpoint);

            var shipPrefab = Resources.Load<GameObject>(ShipPrefabResourcePath);
            if (shipPrefab == null)
                throw new InvalidOperationException(
                    "Missing persistent ship prefab at Resources/" +
                    ShipPrefabResourcePath + ".");
            var shipObject = Instantiate(shipPrefab, transform);
            shipObject.name = "ShipWorld | persistent";
            _shipRoot = shipObject.transform;

            var geometry = ResolveShipGeometry(_shipRoot);

            var commanderObject = new GameObject("TacticalCommandController");
            commanderObject.transform.SetParent(transform, false);
            _commander = commanderObject.AddComponent<TacticalCommandController>();
            _commander.SetPersistentWorld(this);

            var cameraObject = new GameObject("GameplayCamera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.position = new Vector3(16f, 30f, -16f);
            cameraObject.transform.LookAt(new Vector3(16f, 0f, 3f));
            cameraObject.tag = "MainCamera";
            _camera = cameraObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 16f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 150f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.10f, 0.14f, 0.20f);
            if (Application.isPlaying)
                cameraObject.AddComponent<AudioListener>();
            _commander.SetCamera(_camera);

            var lightObject = new GameObject("WorldKeyLight");
            lightObject.transform.SetParent(transform, false);
            lightObject.transform.rotation = Quaternion.Euler(54f, -25f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.color = new Color(0.77f, 0.86f, 0.95f);

            var actorParent = new GameObject("PersistentCrew");
            actorParent.transform.SetParent(transform, false);

            var bindings = new List<ShipTacticalSceneBootstrap.CrewBinding>();
            var state = session.Capture().State;
            var savedShipCells = new Dictionary<EntityId, ShipGridCell>();
            foreach (var location in session.PhysicalActorLocations)
                if (location.Theater == PhysicalTheater.Ship)
                    savedShipCells[location.CrewId] = location.ShipCell;
            for (int i = 0; i < CrewIds.Length; i++)
            {
                var member = state.CrewDuty.Get(CrewIds[i]);
                if (member == null)
                    throw new InvalidOperationException(
                        "Persistent state is missing crew " + CrewIds[i].Value);
                var cell = new ShipGridCell(
                    EntityId.Parse(BridgeRoomId), StartCells[i].x, StartCells[i].y);
                // Restore an exact authored ship cell when the saved layout
                // still contains its room; fall back to the authored spawn.
                if (savedShipCells.TryGetValue(CrewIds[i], out var saved) &&
                    geometry.RoomOrigins.ContainsKey(saved.RoomId))
                    cell = saved;
                var actor = CreateCrewActor(actorParent.transform, CrewIds[i], cell,
                    CrewColors[i], _shipRoot, geometry.RoomOrigins);
                _crewActors.Add(actor);
                bindings.Add(new ShipTacticalSceneBootstrap.CrewBinding
                {
                    Actor = actor.ShipSelection,
                    StartingRoomId = cell.RoomId.Value,
                    StartingCell = new Vector2Int(cell.X, cell.Y)
                });
            }

            var bootstrapObject = new GameObject("ShipTacticalSceneBootstrap");
            bootstrapObject.transform.SetParent(transform, false);
            _bootstrap = bootstrapObject.AddComponent<ShipTacticalSceneBootstrap>();
            _bootstrap.ConfigureShipSceneId(EntityId.Parse(ShipSceneId));
            _bootstrap.Configure(_shipRoot, _commander,
                geometry.Rooms, geometry.Doors, bindings.ToArray());
            EnsureDefaultDutyAssignment(session);
            _bootstrap.Initialize(session);
            _commander.RefreshSceneRegistrations();
            CreateGangway();
            Physics.SyncTransforms();
            _ready = true;
            if (Application.isPlaying)
            {
                var save = GetComponent<PersistentWorldSaveCoordinator>();
                if (save == null)
                    save = gameObject.AddComponent<PersistentWorldSaveCoordinator>();
                save.Bind(this);
                StartCoroutine(LoadShoreEnvironment());
            }
        }

        /// <summary>
        /// This slice owns exactly one ship and one shore. A save from another
        /// physical scene must fail loudly instead of silently loading this one.
        /// </summary>
        private static void ValidateCheckpoint(PhysicalSceneCheckpoint checkpoint)
        {
            if (checkpoint.Theater == PhysicalTheater.Ship)
            {
                if (checkpoint.SceneId != EntityId.Parse(ShipSceneId) ||
                    checkpoint.SpawnAnchorId != EntityId.Parse(ShipAnchorId))
                    throw new InvalidOperationException(
                        "UnsupportedShipCheckpoint:" + checkpoint.SceneId.Value);
                return;
            }
            if (checkpoint.SceneId != EntityId.Parse(ShoreSceneId) ||
                checkpoint.SiteId != EntityId.Parse(ShoreSiteId) ||
                checkpoint.SpawnAnchorId != EntityId.Parse(ShoreAnchorId))
                throw new InvalidOperationException(
                    "UnsupportedShoreCheckpoint:" + checkpoint.SceneId.Value);
        }

        /// <summary>
        /// In-place F9 restore: rebuild the generated ship/actor/camera graph
        /// from the restored session while preserving the one run root/session.
        /// </summary>
        public bool RestoreFromSnapshot(SaveGameDto snapshot)
        {
            if (snapshot == null || _restoring || _session == null)
                return false;
            if (IsTransitBusy)
            {
                _lastRestoreError = "RestoreRejected: transit in progress.";
                return false;
            }
            var preflight = PreflightRestore(snapshot);
            if (preflight != null)
            {
                _lastRestoreError = "RestorePreflightFailed: " + preflight;
                return false;
            }
            SaveGameDto rollback;
            try
            {
                rollback = CaptureSnapshot();
            }
            catch (Exception ex)
            {
                _lastRestoreError =
                    "RestorePreflightFailed: rollback capture: " + ex.Message;
                return false;
            }
            _restoring = true;
            _lastRestoreError = null;
            StartCoroutine(RestoreRoutine(snapshot, rollback));
            return true;
        }

        private string PreflightRestore(SaveGameDto snapshot)
        {
            if (!_ready || !_shoreReady)
                return "WorldNotReady";
            var checkpoint = snapshot.SceneCheckpoint;
            if (checkpoint == null)
                return "CheckpointMissing";
            try
            {
                ValidateCheckpoint(checkpoint);
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            if (!Application.CanStreamedLevelBeLoaded(ShoreSceneName))
                return "ShoreSceneNotInBuildSettings";
            var shoreScene = SceneManager.GetSceneByName(ShoreSceneName);
            if (!shoreScene.IsValid() || !shoreScene.isLoaded)
                return "ShoreSceneNotLoaded";
            ShoreEnvironmentRuntime environment = null;
            foreach (var root in shoreScene.GetRootGameObjects())
            {
                if (root == null) continue;
                environment = root.GetComponentInChildren<ShoreEnvironmentRuntime>(true);
                if (environment != null) break;
            }
            if (environment == null)
                return "ShoreEnvironmentMissing";
            if (!environment.IsReady)
                return "ShoreEnvironmentNotReady:" +
                    (environment.Error ?? "unknown");

            var prefab = Resources.Load<GameObject>(ShipPrefabResourcePath);
            if (prefab == null)
                return "ShipPrefabMissing";
            var authoring = prefab.GetComponent<PersistentShipGeometry>();
            if (authoring == null || authoring.Rooms == null ||
                authoring.Rooms.Length == 0 || authoring.Doors == null)
                return "ShipPrefabGeometryInvalid";
            var prefabRoomIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var binding in authoring.Rooms)
            {
                if (binding == null || binding.Origin == null ||
                    binding.Floor == null || string.IsNullOrEmpty(binding.RoomId))
                    return "ShipPrefabRoomInvalid";
                if (!prefabRoomIds.Add(binding.RoomId))
                    return "ShipPrefabRoomDuplicate:" + binding.RoomId;
            }

            var state = snapshot.State;
            foreach (var id in CrewIds)
                if (!state.HasCrew(id) || state.CrewDuty.Get(id) == null)
                    return "MissingCanonicalCrew:" + id.Value;
            if (state.CrewDuty.Members.Any(m => !CrewIds.Contains(m.Id)))
                return "ForeignCrewRoster";
            var layout = state.Layout;
            if (layout.Rooms.Count != prefabRoomIds.Count)
                return "PrefabLayoutRoomCountMismatch";
            foreach (var room in layout.Rooms)
                if (!prefabRoomIds.Contains(room.Id.Value))
                    return "LayoutRoomNotInPrefab:" + room.Id.Value;

            var portals = new List<ShipRoomPortal>();
            foreach (var door in authoring.Doors)
            {
                if (door == null)
                    return "ShipPrefabDoorInvalid";
                try
                {
                    portals.Add(new ShipRoomPortal(EntityId.Parse(door.DoorId),
                        new ShipGridCell(EntityId.Parse(door.FromRoomId),
                            door.FromCell.x, door.FromCell.y),
                        new ShipGridCell(EntityId.Parse(door.ToRoomId),
                            door.ToCell.x, door.ToCell.y),
                        door.Open));
                }
                catch (Exception ex)
                {
                    return "ShipPrefabDoorInvalid:" + ex.Message;
                }
            }

            ShipSpatialMap map;
            try
            {
                map = ShipSpatialMap.Derive(layout,
                    Array.Empty<ShipModuleSpatialProfile>(), portals);
            }
            catch (Exception ex)
            {
                return "ShipLayoutInvalid:" + ex.Message;
            }

            var savedShip = new HashSet<EntityId>();
            foreach (var location in snapshot.ActorLocations)
            {
                if (location.Theater != PhysicalTheater.Ship)
                    continue;
                savedShip.Add(location.CrewId);
                if (!map.Contains(location.ShipCell) ||
                    !map.Walkable(location.ShipCell))
                    return "SavedShipPlacementInvalid:" + location.CrewId.Value;
            }
            for (int i = 0; i < CrewIds.Length; i++)
            {
                if (savedShip.Contains(CrewIds[i]))
                    continue;
                var member = state.CrewDuty.Get(CrewIds[i]);
                if (member == null || member.IsAway)
                    continue;
                var start = new ShipGridCell(EntityId.Parse(BridgeRoomId),
                    StartCells[i].x, StartCells[i].y);
                if (!map.Walkable(start) || map.IsPortalEndpoint(start))
                    return "DefaultShipSpawnInvalid:" + CrewIds[i].Value;
            }
            foreach (var move in snapshot.PendingCrewMoves)
                if (!map.Contains(move.ShipGoal) ||
                    !map.Walkable(move.ShipGoal) ||
                    map.IsPortalEndpoint(move.ShipGoal))
                    return "PendingMoveInvalid:" + move.CrewId.Value;

            foreach (var location in snapshot.ActorLocations)
            {
                if (location.Theater != PhysicalTheater.Shore)
                    continue;
                var point = new Vector3(location.ShoreX, 0f, location.ShoreZ);
                if (!NavMesh.SamplePosition(point, out var hit, 0.5f,
                        NavMesh.AllAreas) ||
                    (hit.position - point).sqrMagnitude > 0.25f * 0.25f)
                    return "SavedShorePlacementInvalid:" + location.CrewId.Value;
            }
            return null;
        }

        private IEnumerator RestoreRoutine(SaveGameDto snapshot, SaveGameDto rollback)
        {
            string error = null;
            try
            {
                _session.RestoreSnapshot(snapshot);
                ResetWorldForRebuild();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Debug.LogException(ex, this);
            }

            if (error != null)
            {
                yield return RollbackRestore(rollback,
                    "RestoreFailed:" + error);
                yield break;
            }

            yield return null;
            try
            {
                BuildWorld(_session);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Debug.LogException(ex, this);
            }
            if (error != null)
            {
                yield return RollbackRestore(rollback,
                    "RestoreBuildFailed:" + error);
                yield break;
            }

            float wait = 8f;
            while ((!_ready || !_shoreReady) && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            if (!_ready || !_shoreReady)
            {
                yield return RollbackRestore(rollback,
                    "RestoreIncomplete:" + (_shoreError ?? "not ready"));
                yield break;
            }
            _restoring = false;
        }

        private void ResetWorldForRebuild()
        {
            _ready = false;
            _shoreReady = false;
            _crossing = false;
            _queueRunning = false;
            _stepSucceeded = false;
            _lastReconcileTime = 0f;
            _initializationError = null;
            _lastTransitStatus = null;
            _crewActors.Clear();
            _shipRoot = null;
            _bootstrap = null;
            _commander = null;
            _camera = null;
            foreach (Transform child in transform)
                if (child != null)
                    Destroy(child.gameObject);
        }

        private IEnumerator RollbackRestore(SaveGameDto rollback, string failure)
        {
            _lastRestoreError = failure + " | restoring previous world";
            if (rollback == null)
            {
                _restoring = false;
                yield break;
            }

            string error = null;
            try
            {
                _session.RestoreSnapshot(rollback);
                ResetWorldForRebuild();
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Debug.LogException(ex, this);
            }
            if (error != null)
            {
                _lastRestoreError =
                    failure + " | RollbackSessionFailed:" + error;
                _restoring = false;
                yield break;
            }

            yield return null;
            try
            {
                BuildWorld(_session);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Debug.LogException(ex, this);
            }
            if (error != null)
            {
                _lastRestoreError =
                    failure + " | RollbackBuildFailed:" + error;
                _restoring = false;
                yield break;
            }

            float wait = 8f;
            while ((!_ready || !_shoreReady) && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            _lastRestoreError = !_ready || !_shoreReady
                ? failure + " | RollbackIncomplete:" +
                    (_shoreError ?? "not ready")
                : failure + " | PreviousWorldRestored";
            _restoring = false;
        }

        private void CreateGangway()
        {
            var gangway = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gangway.name = "PersistentGangway | ship to shore";
            gangway.transform.SetParent(transform, false);
            gangway.transform.position = new Vector3(16.25f, -0.06f, 3f);
            gangway.transform.localScale = new Vector3(8.5f, 0.12f, 1.8f);
            Tint(gangway, new Color(0.55f, 0.42f, 0.28f));
            var dock = gangway.AddComponent<PersistentGangway>();
            dock.Configure(this);
        }

        private IEnumerator LoadShoreEnvironment()
        {
            _shoreReady = false;
            _shoreError = null;

            if (!Application.CanStreamedLevelBeLoaded(ShoreSceneName))
            {
                _shoreError = "ShoreSceneNotInBuildSettings";
                yield break;
            }

            var existing = SceneManager.GetSceneByName(ShoreSceneName);
            if (!existing.IsValid() || !existing.isLoaded)
            {
                var operation = SceneManager.LoadSceneAsync(
                    ShoreSceneName, LoadSceneMode.Additive);
                if (operation == null)
                {
                    _shoreError = "ShoreSceneLoadFailed";
                    yield break;
                }
                yield return operation;
            }

            var scene = SceneManager.GetSceneByName(ShoreSceneName);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                _shoreError = "ShoreSceneLoadIncomplete";
                yield break;
            }

            ShoreEnvironmentRuntime environment = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == null) continue;
                if (root.GetComponentInChildren<WorldSessionScope>(true) != null)
                {
                    _shoreError = "ShoreSceneContainsWorldSession";
                    yield break;
                }
                if (root.GetComponentInChildren<Camera>(true) != null)
                {
                    _shoreError = "ShoreSceneContainsCamera";
                    yield break;
                }
                if (root.GetComponentInChildren<Light>(true) != null)
                {
                    _shoreError = "ShoreSceneContainsLight";
                    yield break;
                }
                if (root.GetComponentInChildren<PersistentCrewActor>(true) != null ||
                    root.GetComponentInChildren<TacticalActorProxy>(true) != null)
                {
                    _shoreError = "ShoreSceneContainsActor";
                    yield break;
                }
                if (environment == null)
                    environment = root.GetComponentInChildren<ShoreEnvironmentRuntime>(true);
            }

            if (environment == null)
            {
                _shoreError = "ShoreEnvironmentBuilderMissing";
                yield break;
            }

            float wait = 5f;
            while (!environment.IsReady && wait > 0f)
            {
                wait -= Time.deltaTime;
                yield return null;
            }
            if (!environment.IsReady)
            {
                _shoreError = "ShoreEnvironmentNotReady:" + environment.Error;
                yield break;
            }

            _shoreReady = true;
            ApplyRestoredLocations();
        }

        private void ApplyRestoredLocations()
        {
            if (_session == null || _commander == null)
                return;
            UpdateAwayCheckpoint();
            var state = _session.Capture().State;
            var savedShore = new Dictionary<EntityId, PhysicalActorLocation>();
            foreach (var location in _session.PhysicalActorLocations)
                if (location.Theater == PhysicalTheater.Shore)
                    savedShore[location.CrewId] = location;
            int awayIndex = 0;
            foreach (var actor in _crewActors)
            {
                var member = state.CrewDuty.Get(actor.ActorId);
                if (member == null || !member.IsAway)
                    continue;
                int index = awayIndex++;
                var fallback = ShoreEntryPoint + ShoreEntryOffset(index);
                if (!actor.IsOnShip && actor.ShoreAgent.enabled &&
                    actor.ShoreAgent.isOnNavMesh)
                    continue;
                ReleaseShipPresence(actor.ActorId);
                actor.ShipMotor.SuspendForShoreDeparture();
                if (savedShore.TryGetValue(actor.ActorId, out var saved))
                {
                    if (!actor.SetShoreMode(
                            new Vector3(saved.ShoreX, 0f, saved.ShoreZ)))
                    {
                        // An exact saved position must never degrade to the
                        // authored entry point; fail the restore instead.
                        _lastRestoreError = "SavedShorePlacementFailed:" +
                            actor.ActorId.Value;
                        _shoreError = _lastRestoreError;
                        _shoreReady = false;
                        Physics.SyncTransforms();
                        return;
                    }
                }
                else if (!actor.SetShoreMode(fallback))
                {
                    _lastTransitStatus = "ReconcileShoreFailed:" +
                        actor.ActorId.Value;
                }
            }
            Physics.SyncTransforms();
            ReconcileCrewPresence();
        }

        private static Vector3 ShoreEntryOffset(int index) =>
            new Vector3((index % 3) * 1f, 0f, (index / 3) * 1f);

        private EntityId[] GetAwayIds()
        {
            if (_session == null)
                return Array.Empty<EntityId>();
            return _session.Capture().State.CrewDuty.Members
                .Where(m => m.IsAway)
                .Select(m => m.Id)
                .OrderBy(id => id.Value, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Publish the shoreline party as the union of every away crew member.
        /// Selecting a second actor for the gangway must never overwrite the
        /// first actor's party membership.
        /// </summary>
        private void UpdateAwayCheckpoint()
        {
            if (_session == null)
                return;
            SetCheckpointForAway(GetAwayIds());
        }

        private void SetCheckpointForAway(IReadOnlyList<EntityId> away)
        {
            if (away == null || away.Count == 0)
            {
                _session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    EntityId.Parse(ShipSceneId), EntityId.Parse(ShipAnchorId)));
                return;
            }

            var current = _session.SceneCheckpoint;
            var sceneId = current != null && current.Theater == PhysicalTheater.Shore
                ? current.SceneId : EntityId.Parse(ShoreSceneId);
            var siteId = current != null && current.Theater == PhysicalTheater.Shore &&
                         current.SiteId.IsValid
                ? current.SiteId : EntityId.Parse(ShoreSiteId);
            var anchorId = current != null && current.Theater == PhysicalTheater.Shore &&
                           current.SpawnAnchorId.IsValid
                ? current.SpawnAnchorId : EntityId.Parse(ShoreAnchorId);
            _session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                sceneId, siteId, anchorId, away));
        }

        private void ReleaseShipPresence(EntityId id)
        {
            if (_commander == null)
                return;
            _commander.GetComponent<ShipCrewAgencyCoordinator>()?
                .ReleaseActorForDeparture(id);
            _commander.GetComponent<ShipCrewDutyAutonomy>()?
                .ReleaseActorForDeparture(id);
        }

        /// <summary>
        /// Repair only inconsistent physical modes and missing party membership.
        /// Normal moving actors on the correct side are never teleported.
        /// </summary>
        public void ReconcileCrewPresence()
        {
            if (_session == null || _commander == null || !_ready ||
                _restoring || _crossing || _queueRunning)
                return;
            if (!_shoreReady && GetAwayIds().Length > 0)
                return;

            UpdateAwayCheckpoint();
            var state = _session.Capture().State;
            int awayIndex = 0;
            foreach (var actor in _crewActors)
            {
                var member = state.CrewDuty.Get(actor.ActorId);
                if (member == null)
                    continue;
                if (member.IsAway)
                {
                    int index = awayIndex++;
                    if (!actor.IsOnShip && actor.ShoreAgent.enabled &&
                        actor.ShoreAgent.isOnNavMesh)
                        continue;
                    ReleaseShipPresence(actor.ActorId);
                    actor.ShipMotor.SuspendForShoreDeparture();
                    var offset = ShoreEntryOffset(index);
                    if (!actor.SetShoreMode(ShoreEntryPoint + offset))
                        _lastTransitStatus = "ReconcileShoreFailed:" +
                            actor.ActorId.Value;
                }
                else if (!actor.IsOnShip || !actor.ShipMotor.enabled)
                {
                    actor.ShipMotor.ResumeFromShoreAt(GangwayCell);
                    actor.SetShipPosition(actor.ShipMotor.transform.position);
                    actor.SetShipMode();
                }
            }
            Physics.SyncTransforms();
        }

        public void ReconcilePresenceNow() => ReconcileCrewPresence();

        private void Update()
        {
            if (!_ready || _restoring || _crossing || _queueRunning ||
                _session == null)
                return;
            if (Time.unscaledTime - _lastReconcileTime < 0.5f)
                return;
            _lastReconcileTime = Time.unscaledTime;
            ReconcileCrewPresence();
        }

        public bool TryCrossActor(EntityId actorId, EntityId transitionId = default)
        {
            if (!actorId.IsValid)
            {
                _lastTransitStatus = "InvalidActor";
                return false;
            }
            return TryCrossSelected(new[] { actorId }, transitionId);
        }

        /// <summary>
        /// Queue every selected crew member through the one gangway. Each
        /// actor crosses physically before the next one starts.
        /// </summary>
        public bool TryCrossSelected(IReadOnlyList<EntityId> selected,
            EntityId transitionId = default)
        {
            if (_queueRunning || _crossing)
            {
                _lastTransitStatus = "TransitBusy";
                return false;
            }
            if (selected == null || selected.Count == 0)
            {
                _lastTransitStatus = "NoCrewSelection";
                return false;
            }

            ReconcileCrewPresence();
            var state = _session.Capture().State;
            var valid = selected.Distinct()
                .Where(id => _crewActors.Any(a => a.ActorId == id) &&
                             state.CrewDuty.Get(id) != null)
                .ToList();
            var ids = new List<EntityId>();
            ids.AddRange(OrderTransitIds(valid.Where(id =>
                !state.CrewDuty.Get(id).IsAway), true));
            ids.AddRange(OrderTransitIds(valid.Where(id =>
                state.CrewDuty.Get(id).IsAway), false));
            if (ids.Count == 0)
            {
                _lastTransitStatus = "UnknownCrew";
                return false;
            }
            return StartTransit(ids, transitionId,
                false, default, false, default, default);
        }

        /// <summary>
        /// Direct ship-to-shore order: the selected onboard crew walk to the
        /// gangway, cross in sequence, then receive the shore move at the click.
        /// Crew already ashore receive the move directly.
        /// </summary>
        public bool TryTravelSelectedToShore(IReadOnlyList<EntityId> selected,
            Vector3 shorePoint, EntityId transitionId = default)
        {
            if (_queueRunning || _crossing)
            {
                _lastTransitStatus = "TransitBusy";
                return false;
            }
            if (selected == null || selected.Count == 0)
            {
                _lastTransitStatus = "NoCrewSelection";
                return false;
            }
            ReconcileCrewPresence();
            var state = _session.Capture().State;
            var ids = OrderTransitIds(selected.Distinct().Where(id =>
            {
                var member = state.CrewDuty.Get(id);
                return member != null && !member.IsAway;
            }), true);
            if (ids.Count == 0)
            {
                if (_commander != null &&
                    selected.Any(id => state.CrewDuty.Get(id) != null))
                    return _commander.IssueMoveOrder(shorePoint);
                _lastTransitStatus = "NoEligibleCrew";
                return false;
            }
            return StartTransit(ids, transitionId,
                true, shorePoint, false, default, default);
        }

        /// <summary>
        /// Direct shore-to-ship order: the selected away crew return through
        /// the gangway in sequence, then receive the ship move at the click.
        /// </summary>
        public bool TryTravelSelectedToShip(IReadOnlyList<EntityId> selected,
            ShipGridCell shipCell, Vector3 shipWorldPoint,
            EntityId transitionId = default)
        {
            if (_queueRunning || _crossing)
            {
                _lastTransitStatus = "TransitBusy";
                return false;
            }
            if (selected == null || selected.Count == 0)
            {
                _lastTransitStatus = "NoCrewSelection";
                return false;
            }
            ReconcileCrewPresence();
            var state = _session.Capture().State;
            var ids = OrderTransitIds(selected.Distinct().Where(id =>
            {
                var member = state.CrewDuty.Get(id);
                return member != null && member.IsAway;
            }), false);
            if (ids.Count == 0)
            {
                if (_commander != null &&
                    selected.Any(id => state.CrewDuty.Get(id) != null))
                    return _commander.IssueShipMoveOrder(shipCell, shipWorldPoint);
                _lastTransitStatus = "NoEligibleCrew";
                return false;
            }
            return StartTransit(ids, transitionId,
                false, default, true, shipCell, shipWorldPoint);
        }

        private bool StartTransit(IReadOnlyList<EntityId> ids,
            EntityId transitionId, bool hasShoreDestination, Vector3 shorePoint,
            bool hasShipDestination, ShipGridCell shipCell, Vector3 shipWorldPoint)
        {
            if (_queueRunning || _crossing)
            {
                _lastTransitStatus = "TransitBusy";
                return false;
            }
            _queueRunning = true;
            _lastTransitStatus = "TransitQueued";
            StartCoroutine(ProcessTransitQueue(ids.ToList(),
                ResolveTransitionId(transitionId), hasShoreDestination, shorePoint,
                hasShipDestination, shipCell, shipWorldPoint));
            return true;
        }

        private IEnumerator ProcessTransitQueue(List<EntityId> ids,
            EntityId transitionId, bool hasShoreDestination, Vector3 shorePoint,
            bool hasShipDestination, ShipGridCell shipCell, Vector3 shipWorldPoint)
        {
            StageTransitActors(ids);
            _lastTransitStatus = "CrewMovingToGangway";
            int success = 0;
            int transitIndex = 0;
            foreach (var id in ids)
            {
                int index = transitIndex++;
                var actor = _crewActors.FirstOrDefault(a => a.ActorId == id);
                if (actor == null)
                    continue;
                var member = _session.Capture().State.CrewDuty.Get(id);
                if (member != null && !member.IsAway)
                {
                    float stageTimeout = 12f;
                    while ((actor.ShipMotor.IsMoving || actor.ShipMotor.IsRecovering) &&
                           stageTimeout > 0f)
                    {
                        stageTimeout -= Time.deltaTime;
                        yield return null;
                    }
                    if (actor.ShipMotor.IsMoving || actor.ShipMotor.IsRecovering)
                        _lastTransitStatus = "CrewStageTimeout:" + id.Value;
                }
                while (_crossing)
                    yield return null;
                _stepSucceeded = false;
                if (!TryCross(actor, transitionId, index,
                        hasShoreDestination, shorePoint))
                {
                    _lastTransitStatus = "TransitSkipped:" + id.Value + ":" +
                        _lastTransitStatus;
                    continue;
                }
                while (_crossing)
                    yield return null;
                if (_stepSucceeded)
                    success++;
                UpdateAwayCheckpoint();
            }

            _queueRunning = false;
            UpdateAwayCheckpoint();
            ReconcileCrewPresence();
            if (success > 0 && hasShoreDestination && _commander != null)
                _commander.IssueMoveOrder(shorePoint);
            else if (success > 0 && hasShipDestination && _commander != null)
                _commander.IssueShipMoveOrder(shipCell, shipWorldPoint);
            var completed = "TransitComplete " + success + "/" + ids.Count;
            _lastTransitStatus = success < ids.Count &&
                !string.IsNullOrEmpty(_lastTransitStatus)
                ? completed + " | " + _lastTransitStatus
                : completed;
        }

        private List<EntityId> OrderTransitIds(IEnumerable<EntityId> ids, bool toShore)
        {
            return ids
                .OrderBy(id => toShore ? ShipGangwayPriority(id) : ShoreGangwayPriority(id))
                .ThenBy(id => id.Value, StringComparer.Ordinal)
                .ToList();
        }

        private int ShipGangwayPriority(EntityId id)
        {
            var actor = _crewActors.FirstOrDefault(a => a.ActorId == id);
            if (actor == null)
                return int.MaxValue;
            var map = _bootstrap != null ? _bootstrap.Map : null;
            if (map != null && actor.ShipMotor != null)
            {
                var path = map.FindPath(actor.ShipMotor.LastReachedCell, GangwayCell);
                if (path.Count > 0)
                    return path.Count;
            }
            var gangwayWorld = _shipRoot != null
                ? _shipRoot.TransformPoint(new Vector3(11.5f, 0f, 2.5f))
                : transform.position;
            return Mathf.RoundToInt(
                Vector3.Distance(actor.transform.position, gangwayWorld) * 100f);
        }

        private int ShoreGangwayPriority(EntityId id)
        {
            var actor = _crewActors.FirstOrDefault(a => a.ActorId == id);
            return actor == null ? int.MaxValue : Mathf.RoundToInt(
                Vector3.Distance(actor.transform.position, ShoreEntryPoint) * 100f);
        }

        private void StageTransitActors(IReadOnlyList<EntityId> ids)
        {
            var state = _session.Capture().State;
            int shipIndex = 0;
            int shoreIndex = 0;
            foreach (var id in ids)
            {
                var actor = _crewActors.FirstOrDefault(a => a.ActorId == id);
                var member = state.CrewDuty.Get(id);
                if (actor == null || member == null)
                    continue;
                if (!member.IsAway)
                {
                    var offset = ShipGangwayQueue[
                        Mathf.Min(shipIndex, ShipGangwayQueue.Length - 1)];
                    shipIndex++;
                    var cell = new ShipGridCell(
                        EntityId.Parse(EngineRoomId), offset.x, offset.y);
                    if (actor.ShipMotor == null || !actor.ShipMotor.IsConfigured ||
                        actor.ShipMotor.IsMoving || actor.ShipMotor.IsRecovering ||
                        actor.ShipMotor.LastReachedCell == cell)
                        continue;
                    actor.ShipMotor.TryMoveTo(cell);
                }
                else
                {
                    int index = shoreIndex++;
                    var point = ShoreQueuePoint(index);
                    var agent = actor.ShoreAgent;
                    if (agent == null || !agent.enabled || !agent.isOnNavMesh)
                        continue;
                    agent.isStopped = false;
                    agent.stoppingDistance = 0.15f;
                    agent.SetDestination(point);
                }
            }
        }

        private static Vector3 ShoreQueuePoint(int index)
        {
            switch (index)
            {
                case 0: return ShoreEntryPoint;
                case 1: return ShoreEntryPoint + new Vector3(0f, 0f, 1.2f);
                case 2: return ShoreEntryPoint + new Vector3(0f, 0f, -1.2f);
                case 3: return ShoreEntryPoint + new Vector3(1.5f, 0f, 0f);
                case 4: return ShoreEntryPoint + new Vector3(1.5f, 0f, 1.2f);
                default: return ShoreEntryPoint + new Vector3(1.5f, 0f, -1.2f);
            }
        }

        private static Vector3 ShoreDestinationOffset(int index)
        {
            switch (index)
            {
                case 0: return Vector3.zero;
                case 1: return new Vector3(1.2f, 0f, 0f);
                case 2: return new Vector3(-1.2f, 0f, 0f);
                case 3: return new Vector3(0f, 0f, 1.2f);
                default: return new Vector3(0f, 0f, -1.2f);
            }
        }

        private bool TryCross(PersistentCrewActor actor, EntityId transitionId,
            int transitIndex, bool hasShoreDestination, Vector3 shorePoint)
        {
            if (_session == null || actor == null)
            {
                _lastTransitStatus = "WorldNotReady";
                return false;
            }
            var member = _session.Capture().State.CrewDuty.Get(actor.ActorId);
            if (member == null)
            {
                _lastTransitStatus = "UnknownCrew";
                return false;
            }

            if (!member.IsAway)
            {
                if (!_shoreReady)
                {
                    _lastTransitStatus = string.IsNullOrEmpty(_shoreError)
                        ? "ShoreNotReady" : "ShoreError:" + _shoreError;
                    return false;
                }
                if (!actor.IsOnShip || actor.ShipMotor == null ||
                    !actor.ShipMotor.enabled || !actor.ShipMotor.IsConfigured ||
                    actor.ShipMotor.IsMoving || actor.ShipMotor.IsRecovering)
                {
                    _lastTransitStatus = "ShipActorMustBeStationary";
                    return false;
                }
                _crossing = true;
                StartCoroutine(CrossToShore(actor, transitionId, transitIndex,
                    hasShoreDestination, shorePoint));
                return true;
            }

            if (actor.IsOnShip || actor.ShoreAgent == null ||
                !actor.ShoreAgent.enabled || !actor.ShoreAgent.isOnNavMesh)
            {
                _lastTransitStatus = "ShoreActorModeUnavailable";
                return false;
            }
            _crossing = true;
            StartCoroutine(CrossToShip(actor, transitionId));
            return true;
        }

        private static EntityId ResolveTransitionId(EntityId transitionId) =>
            transitionId.IsValid
                ? transitionId
                : EntityId.Parse("transition:e12:cross:" + Guid.NewGuid().ToString("N"));

        private IEnumerator CrossToShore(PersistentCrewActor actor,
            EntityId transitionId, int transitIndex, bool hasShoreDestination,
            Vector3 shorePoint)
        {
            var motor = actor.ShipMotor;
            var targetCell = GangwayCell;
            var shoreExit = ShoreQueuePoint(transitIndex);
            _lastTransitStatus = "WalkingToGangway";

            if (motor.LastReachedCell != targetCell)
            {
                if (!motor.TryMoveTo(targetCell))
                {
                    _lastTransitStatus = "GangwayPathRejected:" + motor.LastError;
                    _crossing = false;
                    yield break;
                }
            }

            float timeout = 8f;
            while (motor.LastReachedCell != targetCell && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            if (motor.LastReachedCell != targetCell)
            {
                _lastTransitStatus = "GangwayPathTimeout";
                _crossing = false;
                yield break;
            }

            var agency = _commander.GetComponent<ShipCrewAgencyCoordinator>();
            var duty = _commander.GetComponent<ShipCrewDutyAutonomy>();
            agency?.ReleaseActorForDeparture(actor.ActorId);
            duty?.ReleaseActorForDeparture(actor.ActorId);
            motor.SuspendForShoreDeparture();
            motor.enabled = false;
            actor.ShoreAgent.enabled = false;

            _lastTransitStatus = "CrossingToShore";
            while (Vector3.Distance(actor.transform.position, shoreExit) > 0.01f)
            {
                actor.transform.position = Vector3.MoveTowards(
                    actor.transform.position, shoreExit, 5f * Time.deltaTime);
                var direction = shoreExit - actor.transform.position;
                if (direction.sqrMagnitude > 0.001f)
                    actor.transform.rotation = Quaternion.LookRotation(
                        new Vector3(direction.x, 0f, direction.z));
                yield return null;
            }
            actor.transform.position = shoreExit;

            if (!actor.SetShoreMode(shoreExit))
            {
                _lastTransitStatus = "ShoreEntryNotOnNavMesh";
                RestoreActorToShip(actor, targetCell);
                _crossing = false;
                yield break;
            }

            var transfer = ShoreCrewTransfer.Transfer(
                _session, new[] { actor.ActorId }, true, transitionId);
            if (!transfer.Success)
            {
                _lastTransitStatus = "ShoreTransferRejected:" + transfer.Failure;
                RestoreActorToShip(actor, targetCell);
                _crossing = false;
                yield break;
            }

            try
            {
                UpdateAwayCheckpoint();
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                _lastTransitStatus = "ShoreCheckpointRejected:" + ex.Message;
                _stepSucceeded = false;
                _crossing = false;
                yield break;
            }

            // Let each actor leave the gangway mouth for its own destination
            // as soon as it lands, instead of accumulating in one stack until
            // every selected actor has finished crossing.
            if (hasShoreDestination && actor.ShoreAgent.enabled &&
                actor.ShoreAgent.isOnNavMesh)
            {
                var desired = shorePoint + ShoreDestinationOffset(transitIndex);
                if (NavMesh.SamplePosition(desired, out var destination,
                       2f, NavMesh.AllAreas))
                {
                    actor.ShoreAgent.isStopped = false;
                    actor.ShoreAgent.stoppingDistance = 0.1f;
                    actor.ShoreAgent.SetDestination(destination.position);
                }
            }

            _stepSucceeded = true;
            _lastTransitStatus = "Ashore";
            _crossing = false;
            Physics.SyncTransforms();
        }

        private void RestoreActorToShip(PersistentCrewActor actor, ShipGridCell cell)
        {
            actor.ShipMotor.ResumeFromShoreAt(cell);
            actor.SetShipPosition(actor.ShipMotor.transform.position);
            actor.SetShipMode();
        }

        private IEnumerator CrossToShip(PersistentCrewActor actor, EntityId transitionId)
        {
            var member = _session.Capture().State.CrewDuty.Get(actor.ActorId);
            if (member == null || !member.IsAway)
            {
                _lastTransitStatus = "ShorePartyMismatch";
                _crossing = false;
                yield break;
            }

            var agent = actor.ShoreAgent;
            if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            {
                _lastTransitStatus = "ShoreAgentUnavailable";
                _crossing = false;
                yield break;
            }

            _lastTransitStatus = "WalkingToShoreGangway";
            agent.isStopped = false;
            agent.stoppingDistance = 0.1f;
            if (!agent.SetDestination(ShoreEntryPoint))
            {
                _lastTransitStatus = "ShoreGangwayPathRejected";
                _crossing = false;
                yield break;
            }

            float timeout = 8f;
            while ((agent.pathPending ||
                    agent.remainingDistance > agent.stoppingDistance + 0.2f) &&
                   timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            if (agent.pathPending ||
                agent.remainingDistance > agent.stoppingDistance + 0.2f)
            {
                _lastTransitStatus = "ShoreGangwayPathTimeout";
                _crossing = false;
                yield break;
            }

            agent.isStopped = true;
            agent.ResetPath();
            agent.enabled = false;

            var shipCellWorld = _shipRoot.TransformPoint(
                new Vector3(11.5f, 0f, 2.5f));
            _lastTransitStatus = "CrossingToShip";
            while (Vector3.Distance(actor.transform.position, shipCellWorld) > 0.01f)
            {
                actor.transform.position = Vector3.MoveTowards(
                    actor.transform.position, shipCellWorld, 5f * Time.deltaTime);
                var direction = shipCellWorld - actor.transform.position;
                if (direction.sqrMagnitude > 0.001f)
                    actor.transform.rotation = Quaternion.LookRotation(
                        new Vector3(direction.x, 0f, direction.z));
                yield return null;
            }
            actor.transform.position = shipCellWorld;
            actor.ShipMotor.ResumeFromShoreAt(GangwayCell);
            actor.SetShipMode();

            // Exclude the returning actor from the shore party before its
            // availability command, while retaining every other away actor.
            var remaining = GetAwayIds().Where(id => id != actor.ActorId).ToArray();
            try
            {
                SetCheckpointForAway(remaining);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                _lastTransitStatus = "ShipCheckpointRejected:" + ex.Message;
                actor.ShipMotor.SuspendForShoreDeparture();
                actor.SetShoreMode(ShoreEntryPoint);
                _crossing = false;
                yield break;
            }

            var transfer = ShoreCrewTransfer.Transfer(
                _session, new[] { actor.ActorId }, false, transitionId);
            if (!transfer.Success)
            {
                _lastTransitStatus = "ShipTransferRejected:" + transfer.Failure;
                UpdateAwayCheckpoint();
                actor.ShipMotor.SuspendForShoreDeparture();
                if (!actor.SetShoreMode(ShoreEntryPoint))
                    _lastTransitStatus = "ShoreReturnRollbackFailed";
                _crossing = false;
                yield break;
            }

            _stepSucceeded = true;
            UpdateAwayCheckpoint();
            _lastTransitStatus = "Aboard";
            _crossing = false;
            Physics.SyncTransforms();
        }

        private PersistentCrewActor CreateCrewActor(Transform parent, EntityId id,
            ShipGridCell cell, Color color, Transform shipRoot,
            IReadOnlyDictionary<EntityId, Vector3> roomOrigins)
        {
            if (!roomOrigins.TryGetValue(cell.RoomId, out var origin))
                throw new InvalidOperationException("Missing room origin for crew spawn.");

            var root = new GameObject(id.Value);
            root.transform.SetParent(parent, false);
            root.transform.position = shipRoot.TransformPoint(
                origin + new Vector3(cell.X + 0.5f, 0f, cell.Y + 0.5f));

            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.72f, 0f);
            collider.height = 1.4f;
            collider.radius = 0.30f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "CrewBody";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.73f, 0f);
            body.transform.localScale = new Vector3(0.62f, 0.70f, 0.62f);
            Tint(body, color);
            var bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null)
                RemoveCollider(bodyCollider);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "SelectionRing";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ring.transform.localScale = new Vector3(0.93f, 0.015f, 0.93f);
            Tint(ring, new Color(0.95f, 0.98f, 0.68f));
            var ringCollider = ring.GetComponent<Collider>();
            if (ringCollider != null)
                RemoveCollider(ringCollider);
            ring.SetActive(false);

            var shipWalker = root.AddComponent<ShipGridWalker>();
            var shipSelection = root.AddComponent<ShipCrewSelectionProxy>();
            shipSelection.SetActorId(id);

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = 5f;
            agent.acceleration = 12f;
            agent.stoppingDistance = 0.1f;
            agent.radius = 0.4f;
            agent.height = 2f;
            agent.enabled = false;

            var shoreActor = root.AddComponent<TacticalActorProxy>();
            shoreActor.SetActorId(id);

            var persistent = root.AddComponent<PersistentCrewActor>();
            persistent.Configure(id, shipWalker, shipSelection, agent, shoreActor, ring);
            return persistent;
        }

        private static ShipGeometry ResolveShipGeometry(Transform shipRoot)
        {
            var authoring = shipRoot.GetComponent<PersistentShipGeometry>();
            if (authoring == null || authoring.Rooms == null ||
                authoring.Rooms.Length == 0)
                throw new InvalidOperationException(
                    "Persistent ship prefab lacks authored geometry bindings.");
            var origins = new Dictionary<EntityId, Vector3>();
            foreach (var binding in authoring.Rooms)
            {
                if (binding == null || binding.Origin == null || binding.Floor == null)
                    throw new InvalidOperationException(
                        "Every authored ship room requires an origin and floor.");
                var id = EntityId.Parse(binding.RoomId);
                if (!origins.TryAdd(id,
                        shipRoot.InverseTransformPoint(binding.Origin.position)))
                    throw new InvalidOperationException(
                        "Duplicate room origin in persistent ship prefab.");
            }
            return new ShipGeometry
            {
                RoomOrigins = origins,
                Rooms = authoring.Rooms,
                Doors = authoring.Doors
            };
        }

        private sealed class ShipGeometry
        {
            public Dictionary<EntityId, Vector3> RoomOrigins;
            public ShipTacticalSceneBootstrap.RoomBinding[] Rooms;
            public ShipTacticalSceneBootstrap.DoorBinding[] Doors;
        }

        private static void Tint(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                         Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            if (shader == null)
                return;
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            renderer.material = material;
        }

        private static void RemoveCollider(Collider collider)
        {
            if (Application.isPlaying)
                Destroy(collider);
            else
                DestroyImmediate(collider);
        }

        private static void EnsureDefaultDutyAssignment(WorldSession session)
        {
            // A loaded save owns its persisted assignments, including a
            // deliberate unassignment; only a fresh run gets the scene default.
            if (session.WasRestored)
                return;
            AssignStation(session, EntityId.Parse(EngineerId),
                EntityId.Parse(EnginePlantId), "engineer");
            AssignStation(session, EntityId.Parse(ScoutId),
                EntityId.Parse(BridgeConsoleId), "scout");
        }

        private static void AssignStation(WorldSession session, EntityId crew,
            EntityId stationId, string roleKey)
        {
            var state = session.Capture().State;
            if (state.GetCrewStation(crew).IsValid)
                return;
            var station = state.Layout.GetModule(stationId);
            if (station == null)
                return;
            var assignment = session.Submit(new AssignCrewCommand(
                EntityId.Parse("cmd:e12:assign-" + roleKey + ":" + state.Revision),
                crew, station.Id, state.Revision));
            if (!assignment.Success)
                Debug.LogWarning("Default " + roleKey + " assignment rejected: " +
                    assignment.Failure);
        }

        public static WorldState CreateDefaultState(bool captainAway = false,
            bool withActivities = true)
        {
            var bridge = EntityId.Parse(BridgeRoomId);
            var engine = EntityId.Parse(EngineRoomId);
            var blueprint = EntityId.Parse(BlueprintId);
            var engineBlueprint = EntityId.Parse(EngineBlueprintId);
            var bridgeConsole = EntityId.Parse(BridgeConsoleId);
            var enginePlant = EntityId.Parse(EnginePlantId);
            var layout = ShipLayoutState.Create(
                new[]
                {
                    new ShipCompartmentSlot(bridge, 6, 6),
                    new ShipCompartmentSlot(engine, 6, 6)
                },
                new[]
                {
                    new ShipModuleBlueprint(blueprint, ShipModuleKind.Auxiliary,
                        1, 1, EntityId.Parse("item:ship:machine-parts"), 1, 1),
                    new ShipModuleBlueprint(engineBlueprint, ShipModuleKind.Engine,
                        1, 1, EntityId.Parse("item:ship:machine-parts"), 1, 1)
                },
                new[]
                {
                    new ShipModule(bridgeConsole, blueprint, bridge, 0, 5),
                    new ShipModule(enginePlant, engineBlueprint, engine, 4, 4)
                });

            var members = new[]
            {
                new CrewMember(CrewIds[0], DutyRole.Watch, 900, 200,
                    0, 1440, isAway: captainAway, hunger: 80),
                new CrewMember(CrewIds[1], DutyRole.Repair, 950, 0, 0, 1440),
                new CrewMember(CrewIds[2], DutyRole.Watch, 900, 200,
                    0, 1440, thirst: 800),
                new CrewMember(CrewIds[3], DutyRole.Medical, 900, 200,
                    720, 400, fatigue: 700)
            };

            if (!withActivities)
            {
                return WorldState.Create(WorldMinute.Zero,
                    Array.Empty<SupplyStock>(), CrewIds,
                    layout: layout, crewDuty: CrewState.Create(members));
            }

            var water = EntityId.Parse(WaterItemId);
            var pickup = EntityId.Parse(WaterPickupId);
            var items = InventoryState.Create(
                new ItemCatalog(new[] { new ItemDefinition(water, 1, 100, 10) }),
                new[] { new SalvagePickup(pickup, water, 2) },
                10000, 5, 10000, 5);
            items = items.TryCollect(pickup).State.TryUnload(water, 2).State;

            var crew = CrewState.Create(members,
                jobs: new[] { new RepairJob(EntityId.Parse(RepairJobId), 100) },
                consumables: new[] { new CrewConsumable(water, 0, 600) },
                interactions: CrewInteractionState.Create(new[]
                {
                    new CrewInteractionSlot(EntityId.Parse(WorkSlotId), enginePlant,
                        CrewInteractionVerb.Work, -1, 0, 0),
                    new CrewInteractionSlot(EntityId.Parse(WatchWorkSlotId), bridgeConsole,
                        CrewInteractionVerb.Work, 2, 0, 0),
                    new CrewInteractionSlot(EntityId.Parse(RestSlotId), bridgeConsole,
                        CrewInteractionVerb.Rest, 1, 0, 0),
                    new CrewInteractionSlot(EntityId.Parse(DrinkSlotId), bridgeConsole,
                        CrewInteractionVerb.Drink, 0, -1, 2)
                }));
            return WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), CrewIds,
                inventory: items, crewDuty: crew, layout: layout);
        }
    }
}
