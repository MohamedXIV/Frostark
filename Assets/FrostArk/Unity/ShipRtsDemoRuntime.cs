using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Standalone, deliberately opt-in ship RTS playtest entry point for
    /// Assets/FrostArk/Scenes/ShipRtsDemo.unity. Builds the visible authored
    /// 2-room demo at runtime so the tracked scene can be opened and played
    /// without hand-authoring inspector references or a moving NavMesh.
    /// This is a fixture, NOT a game save or production scene pipeline.
    /// </summary>
    public sealed class ShipRtsDemoRuntime : MonoBehaviour
    {
        public const string DemoSceneId = "scene:demo:ship-rts";
        public const string BridgeRoomId = "room:demo:bridge";
        public const string EngineRoomId = "room:demo:engine";
        public const string DoorId = "door:demo:bulkhead";

        private static readonly EntityId[] CrewIds =
        {
            EntityId.Parse("crew:demo:captain"),
            EntityId.Parse("crew:demo:engineer"),
            EntityId.Parse("crew:demo:scout"),
            EntityId.Parse("crew:demo:medic")
        };

        private GameObject _environment;
        private WorldSession _session;
        private ShipTacticalSceneBootstrap _bootstrap;
        private TacticalCommandController _commander;
        private GameObject _doorVisual;
        private Transform _shipRoot;
        private bool _doorOpen = true;
        private bool _showHelp = true;
        private bool _simulateShipMotion;
        private float _bobClock;
        private GUIStyle _hudStyle;

        public bool IsReady { get; private set; }
        public WorldSession Session => _session;
        public TacticalCommandController Commander => _commander;
        public ShipTacticalSceneBootstrap Bootstrap => _bootstrap;

        private void Start() => BuildDemo();

        /// <summary>Idempotent scene bootstrap; also callable by EditMode tests.</summary>
        public void BuildDemo()
        {
            if (IsReady) return;
            _environment = new GameObject("Ship RTS Demo | Generated Environment");
            _environment.transform.SetParent(transform, false);
            _shipRoot = new GameObject("ShipRoot | animated ship-local frame").transform;
            _shipRoot.SetParent(_environment.transform, false);

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
                    new ShipModuleBlueprint(EntityId.Parse("blueprint:demo:machine"),
                        ShipModuleKind.Auxiliary, 1, 1,
                        EntityId.Parse("item:demo:machine-parts"), 1, 1)
                },
                Array.Empty<ShipModule>());
            layout = layout.Place(EntityId.Parse("module:demo:bridge-console"),
                EntityId.Parse("blueprint:demo:machine"), bridge, 0, 5);
            layout = layout.Place(EntityId.Parse("module:demo:engine-plant"),
                EntityId.Parse("blueprint:demo:machine"), engine, 4, 4);
            var members = CrewState.Create(new[]
            {
                new CrewMember(CrewIds[0], DutyRole.Watch, 900, 80, 0, 1440),
                new CrewMember(CrewIds[1], DutyRole.Engineering, 900, 80, 0, 1440),
                new CrewMember(CrewIds[2], DutyRole.Watch, 900, 80, 0, 1440),
                new CrewMember(CrewIds[3], DutyRole.Repair, 900, 80, 0, 1440)
            });
            var world = WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(), CrewIds,
                layout: layout, crewDuty: members);
            _session = new WorldSession(world);

            // Keep exactly one authority and only one explicit fresh-run
            // checkpoint: no inferred save location or duplicate actor data.
            var bootstrapObject = new GameObject("ShipTacticalSceneBootstrap");
            bootstrapObject.transform.SetParent(_environment.transform, false);
            _bootstrap = bootstrapObject.AddComponent<ShipTacticalSceneBootstrap>();
            _bootstrap.ConfigureNewRunCheckpoint(EntityId.Parse(DemoSceneId),
                EntityId.Parse("anchor:demo:bridge-start"));
            _session.SetSceneCheckpoint(_bootstrap.InitialNewRunCheckpoint());

            var bridgeOrigin = Marker("Bridge local origin", Vector3.zero);
            var engineOrigin = Marker("Engine local origin", new Vector3(6f, 0f, 0f));
            var bridgeFloor = Floor("Bridge | clickable floor", Vector3.zero,
                new Color(0.25f, 0.35f, 0.40f), bridge);
            var engineFloor = Floor("Engine | clickable floor", new Vector3(6f, 0f, 0f),
                new Color(0.35f, 0.31f, 0.28f), engine);

            // Deliberately unobstructed physically by default. Door endpoints
            // are (5,2) in bridge and (0,2) in engine in canonical local cells.
            // Door closure updates both the authored portal and visible hatch.
            Hull();
            DecorationCube("Bridge console | solid module",
                new Vector3(0.5f, 0.45f, 5.5f), new Vector3(0.8f, 0.9f, 0.8f),
                new Color(0.20f, 0.59f, 0.68f), false);
            DecorationCube("Engine plant | solid module",
                new Vector3(10.5f, 0.45f, 4.5f), new Vector3(0.8f, 0.9f, 0.8f),
                new Color(0.86f, 0.46f, 0.25f), false);
            CreateDoorVisual();

            var actorBindings = new List<ShipTacticalSceneBootstrap.CrewBinding>();
            var placements = new[]
            {
                new Vector2Int(1, 1), new Vector2Int(1, 3),
                new Vector2Int(2, 2), new Vector2Int(3, 3)
            };
            var colors = new[]
            {
                new Color(0.95f, 0.76f, 0.45f),
                new Color(0.42f, 0.78f, 0.92f),
                new Color(0.76f, 0.85f, 0.56f),
                new Color(0.88f, 0.56f, 0.70f)
            };
            for (int i = 0; i < CrewIds.Length; i++)
            {
                var actor = CreateCrew(CrewIds[i], placements[i], colors[i]);
                actorBindings.Add(new ShipTacticalSceneBootstrap.CrewBinding
                {
                    Actor = actor, StartingRoomId = bridge.Value,
                    StartingCell = placements[i]
                });
            }

            var cameraObject = new GameObject("TacticalCamera");
            cameraObject.transform.SetParent(_environment.transform, false);
            cameraObject.transform.position = new Vector3(6f, 21f, -5.5f);
            cameraObject.transform.LookAt(new Vector3(6f, 0f, 3f));
            cameraObject.tag = "MainCamera";
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 9.0f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 150f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.14f, 0.20f);

            var lightObj = new GameObject("Overcast cold ship light");
            lightObj.transform.SetParent(_environment.transform, false);
            lightObj.transform.rotation = Quaternion.Euler(54f, -25f, 0f);
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.color = new Color(0.77f, 0.86f, 0.95f);

            var commanderObject = new GameObject("TacticalCommandController");
            commanderObject.transform.SetParent(_environment.transform, false);
            _commander = commanderObject.AddComponent<TacticalCommandController>();
            _commander.SetCamera(camera);

            _bootstrap.Configure(_shipRoot, _commander,
                new[]
                {
                    new ShipTacticalSceneBootstrap.RoomBinding
                    { RoomId = bridge.Value, Origin = bridgeOrigin, Floor = bridgeFloor },
                    new ShipTacticalSceneBootstrap.RoomBinding
                    { RoomId = engine.Value, Origin = engineOrigin, Floor = engineFloor }
                },
                new[]
                {
                    new ShipTacticalSceneBootstrap.DoorBinding
                    {
                        DoorId = DoorId, FromRoomId = bridge.Value,
                        FromCell = new Vector2Int(5, 2),
                        ToRoomId = engine.Value, ToCell = new Vector2Int(0, 2),
                        Open = true
                    }
                }, actorBindings.ToArray());
            _bootstrap.Initialize(_session);
            Physics.SyncTransforms();
            IsReady = true;
        }

        private Transform Marker(string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_shipRoot, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        private static void Tint(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            // Avoid modifying Unity's global built-in materials.
            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                Shader.Find("Standard") ?? Shader.Find("Unlit/Color");
            if (shader == null) return;
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            renderer.material = material;
        }

        private GameObject DecorationCube(string name, Vector3 position,
            Vector3 scale, Color color, bool clickable)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(_shipRoot, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            Tint(go, color);
            if (!clickable)
            {
                var collision = go.GetComponent<Collider>();
                if (collision != null) Destroy(collision);
            }
            return go;
        }

        private ShipRoomClickSurface Floor(string name, Vector3 roomOrigin,
            Color color, EntityId id)
        {
            var floor = DecorationCube(name,
                roomOrigin + new Vector3(3f, -0.12f, 3f),
                new Vector3(6f, 0.24f, 6f), color, true);
            var surface = floor.AddComponent<ShipRoomClickSurface>();
            surface.Configure(id, _shipRoot, roomOrigin);
            return surface;
        }

        private void Hull()
        {
            var iron = new Color(0.12f, 0.18f, 0.22f);
            DecorationCube("Outer hull north", new Vector3(6f, 0.55f, 6.08f),
                new Vector3(12.4f, 1.1f, 0.2f), iron, false);
            DecorationCube("Outer hull south", new Vector3(6f, 0.55f, -0.08f),
                new Vector3(12.4f, 1.1f, 0.2f), iron, false);
            DecorationCube("Outer hull west", new Vector3(-0.08f, 0.55f, 3f),
                new Vector3(0.2f, 1.1f, 12.3f / 2f), iron, false);
            DecorationCube("Outer hull east", new Vector3(12.08f, 0.55f, 3f),
                new Vector3(0.2f, 1.1f, 12.3f / 2f), iron, false);
            DecorationCube("Bulkhead south of door", new Vector3(6f, 0.55f, 1f),
                new Vector3(0.16f, 1.1f, 2f), iron, false);
            DecorationCube("Bulkhead north of door", new Vector3(6f, 0.55f, 4.5f),
                new Vector3(0.16f, 1.1f, 3f), iron, false);
            for (int x = 0; x < 12; x++)
            {
                DecorationCube("Deck seam " + x,
                    new Vector3(x + 0.5f, 0.015f, 0.12f),
                    new Vector3(0.05f, 0.02f, 5.8f),
                    new Color(0.13f, 0.24f, 0.27f), false);
            }
        }

        private void CreateDoorVisual()
        {
            _doorVisual = DecorationCube("Bulkhead hatch (closed visual)",
                new Vector3(6f, 0.75f, 2.5f), new Vector3(0.2f, 1.5f, 1f),
                new Color(0.83f, 0.34f, 0.20f), false);
            _doorVisual.SetActive(false);
        }

        private ShipCrewSelectionProxy CreateCrew(EntityId id, Vector2Int cell, Color color)
        {
            var root = new GameObject(id.Value);
            root.transform.SetParent(_shipRoot, false);
            root.transform.localPosition = new Vector3(cell.x + 0.5f, 0f, cell.y + 0.5f);
            var collider = root.AddComponent<CapsuleCollider>();
            collider.center = new Vector3(0f, 0.72f, 0f);
            collider.height = 1.4f;
            collider.radius = 0.30f;
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Low-poly crew silhouette";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.73f, 0f);
            body.transform.localScale = new Vector3(0.62f, 0.70f, 0.62f);
            Tint(body, color);
            var bodyCollider = body.GetComponent<Collider>();
            if (bodyCollider != null) Destroy(bodyCollider);

            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "Selected crew outline";
            ring.transform.SetParent(root.transform, false);
            ring.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            ring.transform.localScale = new Vector3(0.93f, 0.015f, 0.93f);
            Tint(ring, new Color(0.95f, 0.98f, 0.68f));
            var ringCollider = ring.GetComponent<Collider>();
            if (ringCollider != null) Destroy(ringCollider);
            ring.SetActive(false);

            root.AddComponent<ShipGridWalker>();
            var actor = root.AddComponent<ShipCrewSelectionProxy>();
            actor.SetActorId(id);
            actor.SetSelectionMarker(ring);
            return actor;
        }

        public void SetDoorOpen(bool open)
        {
            if (!IsReady) return;
            if (_bootstrap.SetDoorOpen(EntityId.Parse(DoorId), open))
            {
                _doorOpen = open;
                if (_doorVisual != null) _doorVisual.SetActive(!open);
            }
        }

        private void Update()
        {
            if (!IsReady) return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.oKey.wasPressedThisFrame) SetDoorOpen(true);
                if (keyboard.cKey.wasPressedThisFrame) SetDoorOpen(false);
                if (keyboard.hKey.wasPressedThisFrame) _showHelp = !_showHelp;
                if (keyboard.mKey.wasPressedThisFrame) _simulateShipMotion = !_simulateShipMotion;
            }
            if (_simulateShipMotion)
            {
                _bobClock += Time.deltaTime;
                _shipRoot.localPosition = new Vector3(
                    Mathf.Sin(_bobClock * 0.2f) * 0.18f, 0f,
                    Mathf.Sin(_bobClock * 0.4f) * 0.15f);
            }
        }

        private void OnGUI()
        {
            if (!IsReady) return;
            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 15,
                    normal = { textColor = Color.white }
                };
            }
            GUI.Box(new Rect(12, 12, 480, _showHelp ? 184 : 82), "FROSTARK | SHIP RTS DEMO");
            GUI.Label(new Rect(28, 39, 440, 25),
                "Selected: " + _commander.Selection.Count +
                "    Door: " + (_doorOpen ? "OPEN" : "CLOSED") +
                "    Move: " + (_simulateShipMotion ? "ON" : "OFF"), _hudStyle);
            if (_showHelp)
            {
                GUI.Label(new Rect(28, 66, 440, 25),
                    "Left click / Shift-click: select. Drag: up to four crew.", _hudStyle);
                GUI.Label(new Rect(28, 90, 440, 25),
                    "Right click floor: move. Blank click: deselect.", _hudStyle);
                GUI.Label(new Rect(28, 114, 440, 25),
                    "O / C: open / close bulkhead. M: move ship.", _hudStyle);
                GUI.Label(new Rect(28, 138, 440, 25),
                    "H: help. Orders are local-grid; no NavMesh on ship.", _hudStyle);
            }
            if (GUI.Button(new Rect(28, _showHelp ? 160 : 62, 100, 27), "Open door"))
                SetDoorOpen(true);
            if (GUI.Button(new Rect(142, _showHelp ? 160 : 62, 100, 27), "Close door"))
                SetDoorOpen(false);
            if (GUI.Button(new Rect(256, _showHelp ? 160 : 62, 110, 27), "Move ship"))
                _simulateShipMotion = !_simulateShipMotion;
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Dispose();
                _session = null;
            }
        }
    }
}
