using System;
using System.Collections.Generic;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using UnityEngine;
using UnityEngine.AI;
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
        private string _initializationError;

        public bool IsReady => _ready;
        public WorldSession Session => _session;
        public Transform ShipRoot => _shipRoot;
        public ShipTacticalSceneBootstrap Bootstrap => _bootstrap;
        public TacticalCommandController Commander => _commander;
        public Camera GameplayCamera => _camera;
        public string InitializationError => _initializationError;
        public IReadOnlyList<PersistentCrewActor> CrewActors => _crewActors;

        public static ShipGridCell GangwayCell =>
            new ShipGridCell(EntityId.Parse(EngineRoomId), 5, 3);

        public WorldState ProvideInitialState() => CreateDefaultState();

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
            if (session.SceneCheckpoint.Theater != PhysicalTheater.Ship)
                throw new InvalidOperationException(
                    "Ship-only world build requires a ship physical checkpoint.");

            var shipObject = new GameObject("ShipWorld | persistent");
            shipObject.transform.SetParent(transform, false);
            _shipRoot = shipObject.transform;

            var geometry = BuildShipGeometry(_shipRoot);

            var commanderObject = new GameObject("TacticalCommandController");
            commanderObject.transform.SetParent(transform, false);
            _commander = commanderObject.AddComponent<TacticalCommandController>();

            var cameraObject = new GameObject("GameplayCamera");
            cameraObject.transform.SetParent(transform, false);
            cameraObject.transform.position = new Vector3(6f, 21f, -5.5f);
            cameraObject.transform.LookAt(new Vector3(6f, 0f, 3f));
            cameraObject.tag = "MainCamera";
            _camera = cameraObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = 9f;
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
            for (int i = 0; i < CrewIds.Length; i++)
            {
                var member = state.CrewDuty.Get(CrewIds[i]);
                if (member == null)
                    throw new InvalidOperationException(
                        "Persistent state is missing crew " + CrewIds[i].Value);
                var cell = new ShipGridCell(
                    EntityId.Parse(BridgeRoomId), StartCells[i].x, StartCells[i].y);
                var actor = CreateCrewActor(actorParent.transform, CrewIds[i], cell,
                    CrewColors[i], _shipRoot, geometry.RoomOrigins);
                _crewActors.Add(actor);
                bindings.Add(new ShipTacticalSceneBootstrap.CrewBinding
                {
                    Actor = actor.ShipSelection,
                    StartingRoomId = BridgeRoomId,
                    StartingCell = StartCells[i]
                });
            }

            var bootstrapObject = new GameObject("ShipTacticalSceneBootstrap");
            bootstrapObject.transform.SetParent(transform, false);
            _bootstrap = bootstrapObject.AddComponent<ShipTacticalSceneBootstrap>();
            _bootstrap.Configure(_shipRoot, _commander,
                geometry.Rooms, geometry.Doors, bindings.ToArray());
            _bootstrap.Initialize(session);
            _commander.RefreshSceneRegistrations();
            Physics.SyncTransforms();
            _ready = true;
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
            agent.speed = 4f;
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

        private static ShipGeometry BuildShipGeometry(Transform shipRoot)
        {
            var bridge = EntityId.Parse(BridgeRoomId);
            var engine = EntityId.Parse(EngineRoomId);

            var bridgeOrigin = Marker(shipRoot, "BridgeOrigin", Vector3.zero);
            var engineOrigin = Marker(shipRoot, "EngineOrigin", new Vector3(6f, 0f, 0f));
            var bridgeFloor = Floor(shipRoot, "BridgeFloor", Vector3.zero, bridge);
            var engineFloor = Floor(shipRoot, "EngineFloor", new Vector3(6f, 0f, 0f), engine);

            Hull(shipRoot);
            DecorationCube(shipRoot, "BridgeConsole",
                new Vector3(0.5f, 0.45f, 5.5f), new Vector3(0.8f, 0.9f, 0.8f),
                new Color(0.20f, 0.59f, 0.68f), false);
            DecorationCube(shipRoot, "EnginePlant",
                new Vector3(10.5f, 0.45f, 4.5f), new Vector3(0.8f, 0.9f, 0.8f),
                new Color(0.86f, 0.46f, 0.25f), false);
            var doorVisual = DecorationCube(shipRoot, "BulkheadHatch",
                new Vector3(6f, 0.75f, 2.5f), new Vector3(0.2f, 1.5f, 1f),
                new Color(0.83f, 0.34f, 0.20f), false);
            doorVisual.SetActive(false);

            var origins = new Dictionary<EntityId, Vector3>
            {
                [bridge] = shipRoot.InverseTransformPoint(bridgeOrigin.position),
                [engine] = shipRoot.InverseTransformPoint(engineOrigin.position)
            };

            return new ShipGeometry
            {
                RoomOrigins = origins,
                Rooms = new[]
                {
                    new ShipTacticalSceneBootstrap.RoomBinding
                    {
                        RoomId = BridgeRoomId,
                        Origin = bridgeOrigin,
                        Floor = bridgeFloor
                    },
                    new ShipTacticalSceneBootstrap.RoomBinding
                    {
                        RoomId = EngineRoomId,
                        Origin = engineOrigin,
                        Floor = engineFloor
                    }
                },
                Doors = new[]
                {
                    new ShipTacticalSceneBootstrap.DoorBinding
                    {
                        DoorId = DoorId,
                        FromRoomId = BridgeRoomId,
                        FromCell = new Vector2Int(5, 2),
                        ToRoomId = EngineRoomId,
                        ToCell = new Vector2Int(0, 2),
                        Open = true
                    }
                }
            };
        }

        private sealed class ShipGeometry
        {
            public Dictionary<EntityId, Vector3> RoomOrigins;
            public ShipTacticalSceneBootstrap.RoomBinding[] Rooms;
            public ShipTacticalSceneBootstrap.DoorBinding[] Doors;
        }

        private static Transform Marker(Transform parent, string name, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            return go.transform;
        }

        private static ShipRoomClickSurface Floor(Transform parent, string name,
            Vector3 roomOrigin, EntityId roomId)
        {
            var floor = DecorationCube(parent, name,
                roomOrigin + new Vector3(3f, -0.12f, 3f),
                new Vector3(6f, 0.24f, 6f),
                new Color(0.25f, 0.35f, 0.40f), true);
            var surface = floor.AddComponent<ShipRoomClickSurface>();
            surface.Configure(roomId, parent, roomOrigin);
            return surface;
        }

        private static void Hull(Transform shipRoot)
        {
            var iron = new Color(0.12f, 0.18f, 0.22f);
            DecorationCube(shipRoot, "OuterHullNorth",
                new Vector3(6f, 0.55f, 6.08f), new Vector3(12.4f, 1.1f, 0.2f), iron, false);
            DecorationCube(shipRoot, "OuterHullSouth",
                new Vector3(6f, 0.55f, -0.08f), new Vector3(12.4f, 1.1f, 0.2f), iron, false);
            DecorationCube(shipRoot, "OuterHullWest",
                new Vector3(-0.08f, 0.55f, 3f), new Vector3(0.2f, 1.1f, 6.2f), iron, false);
            DecorationCube(shipRoot, "OuterHullEastSouth",
                new Vector3(12.08f, 0.55f, 0.95f), new Vector3(0.2f, 1.1f, 2.1f), iron, false);
            DecorationCube(shipRoot, "OuterHullEastNorth",
                new Vector3(12.08f, 0.55f, 5.05f), new Vector3(0.2f, 1.1f, 2.1f), iron, false);
            DecorationCube(shipRoot, "BulkheadSouthOfDoor",
                new Vector3(6f, 0.55f, 1f), new Vector3(0.16f, 1.1f, 2f), iron, false);
            DecorationCube(shipRoot, "BulkheadNorthOfDoor",
                new Vector3(6f, 0.55f, 4.5f), new Vector3(0.16f, 1.1f, 3f), iron, false);
        }

        private static GameObject DecorationCube(Transform parent, string name,
            Vector3 localPosition, Vector3 scale, Color color, bool keepCollider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = scale;
            Tint(go, color);
            if (!keepCollider)
            {
                var collision = go.GetComponent<Collider>();
                if (collision != null)
                    RemoveCollider(collision);
            }
            return go;
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

        public static WorldState CreateDefaultState()
        {
            var bridge = EntityId.Parse(BridgeRoomId);
            var engine = EntityId.Parse(EngineRoomId);
            var layout = ShipLayoutState.Create(
                new[]
                {
                    new ShipCompartmentSlot(bridge, 6, 6),
                    new ShipCompartmentSlot(engine, 6, 6)
                },
                new[]
                {
                    new ShipModuleBlueprint(EntityId.Parse("blueprint:ship:machine"),
                        ShipModuleKind.Auxiliary, 1, 1,
                        EntityId.Parse("item:ship:machine-parts"), 1, 1)
                },
                Array.Empty<ShipModule>());
            layout = layout.Place(EntityId.Parse("module:ship:bridge-console"),
                EntityId.Parse("blueprint:ship:machine"), bridge, 0, 5);
            layout = layout.Place(EntityId.Parse("module:ship:engine-plant"),
                EntityId.Parse("blueprint:ship:machine"), engine, 4, 4);

            var members = CrewState.Create(new[]
            {
                new CrewMember(CrewIds[0], DutyRole.Watch, 900, 80, 0, 1440),
                new CrewMember(CrewIds[1], DutyRole.Engineering, 900, 80, 0, 1440),
                new CrewMember(CrewIds[2], DutyRole.Watch, 900, 80, 0, 1440),
                new CrewMember(CrewIds[3], DutyRole.Repair, 900, 80, 0, 1440)
            });
            return WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), CrewIds,
                layout: layout, crewDuty: members);
        }
    }
}
