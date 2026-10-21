using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using UnityEngine;
using VContainer;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Explicit scene authoring bridge: WorldSession owns ship construction
    /// and crew identity; this component owns Unity room floor colliders,
    /// local room origins, doors and already-placed character GameObjects.
    /// It neither spawns duplicate crew nor invents a layout/save checkpoint.
    /// </summary>
    public sealed class ShipTacticalSceneBootstrap : MonoBehaviour
    {
        [Serializable]
        public sealed class RoomBinding
        {
            public string RoomId;
            public Transform Origin;
            public ShipRoomClickSurface Floor;
        }

        [Serializable]
        public sealed class DoorBinding
        {
            public string DoorId;
            public string FromRoomId;
            public Vector2Int FromCell;
            public string ToRoomId;
            public Vector2Int ToCell;
            public bool Open = true;
        }

        [Serializable]
        public sealed class CrewBinding
        {
            public ShipCrewSelectionProxy Actor;
            public string StartingRoomId;
            public Vector2Int StartingCell;
        }

        [SerializeField] private Transform shipRoot;
        [SerializeField] private TacticalCommandController commander;
        [SerializeField] private RoomBinding[] rooms = Array.Empty<RoomBinding>();
        [SerializeField] private DoorBinding[] doors = Array.Empty<DoorBinding>();
        [SerializeField] private CrewBinding[] crew = Array.Empty<CrewBinding>();
        [SerializeField, Min(0.1f)] private float crewMoveSpeed = 2.5f;
        [Header("Explicit new-run ship spawn (never applied to restored saves)")]
        [SerializeField] private bool establishShipCheckpointForFreshRun;
        [SerializeField] private string startingSceneId = "scene:ship-interior";
        [SerializeField] private string startingAnchorId = "anchor:ship-start";
        private WorldSession _session;
        private ShipSpatialMap _map;
        private uint _layoutFingerprint;

        public ShipSpatialMap Map => _map;

        /// <summary>
        /// Opt-in scene-local new-run location. WorldSessionScope calls this
        /// ONLY on freshly constructed sessions, never on a restored snapshot.
        /// Missing legacy checkpoints remain unknown rather than invented.
        /// </summary>
        public PhysicalSceneCheckpoint InitialNewRunCheckpoint() =>
            establishShipCheckpointForFreshRun
                ? PhysicalSceneCheckpoint.Ship(EntityId.Parse(startingSceneId),
                    EntityId.Parse(startingAnchorId))
                : null;

        public void ConfigureNewRunCheckpoint(EntityId sceneId, EntityId spawnAnchorId)
        {
            if (!sceneId.IsValid || !spawnAnchorId.IsValid)
                throw new ArgumentException("Valid initial ship scene and anchor required.");
            startingSceneId = sceneId.Value;
            startingAnchorId = spawnAnchorId.Value;
            establishShipCheckpointForFreshRun = true;
        }

        /// <summary>Expose identical inspector/authored data to test fixtures.</summary>
        public void Configure(Transform root, TacticalCommandController input,
            RoomBinding[] roomBindings, DoorBinding[] doorBindings,
            CrewBinding[] crewBindings)
        {
            shipRoot = root != null ? root : throw new ArgumentNullException(nameof(root));
            commander = input != null ? input : throw new ArgumentNullException(nameof(input));
            rooms = roomBindings ?? throw new ArgumentNullException(nameof(roomBindings));
            doors = doorBindings ?? throw new ArgumentNullException(nameof(doorBindings));
            crew = crewBindings ?? throw new ArgumentNullException(nameof(crewBindings));
        }

        [Inject]
        public void Construct(WorldSession session) => Initialize(session);

        public void Initialize(WorldSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (shipRoot == null || commander == null)
                throw new InvalidOperationException("Ship root and tactical commander are required.");
            var checkpoint = session.SceneCheckpoint;
            if (checkpoint == null || checkpoint.Theater != PhysicalTheater.Ship)
                throw new InvalidOperationException("Ship bootstrap requires authoritative ship checkpoint.");

            var state = session.Capture().State;
            var origins = new Dictionary<EntityId, Vector3>();
            foreach (var binding in rooms)
            {
                if (binding == null || binding.Origin == null || binding.Floor == null)
                    throw new InvalidOperationException("Every room requires an origin and floor collider.");
                var id = EntityId.Parse(binding.RoomId);
                if (!state.Layout.Rooms.Any(r => r.Id == id) ||
                    !origins.TryAdd(id, shipRoot.InverseTransformPoint(binding.Origin.position)))
                    throw new InvalidOperationException("Room ID missing in canonical layout or duplicated.");
            }

            if (_session != null)
                _session.Changed -= OnWorldChanged;
            _session = session;
            _map = BuildMap(state.Layout);
            _layoutFingerprint = state.Layout.Fingerprint();
            foreach (var binding in rooms)
            {
                var id = EntityId.Parse(binding.RoomId);
                binding.Floor.Configure(id, shipRoot, origins[id]);
            }

            commander.SetSession(session);
            commander.SetShipSpatialMap(_map);
            foreach (var binding in crew)
            {
                if (binding == null || binding.Actor == null)
                    throw new InvalidOperationException("Every crew binding needs a scene actor.");
                var id = binding.Actor.ActorId;
                var member = state.CrewDuty.Get(id);
                if (!id.IsValid || !state.HasCrew(id) || member == null ||
                    member.IsAway || member.Wounds >= 1000)
                    throw new InvalidOperationException("Spawned crew must be canonical and shipboard.");
                var roomId = EntityId.Parse(binding.StartingRoomId);
                if (!origins.ContainsKey(roomId))
                    throw new InvalidOperationException("Crew starts in an unregistered room.");
                var walker = binding.Actor.Walker;
                if (walker == null)
                    throw new InvalidOperationException("Ship crew needs a ShipGridWalker.");
                walker.Configure(shipRoot, _map, origins,
                    new ShipGridCell(roomId, binding.StartingCell.x, binding.StartingCell.y),
                    crewMoveSpeed);
                binding.Actor.BindScene(checkpoint.SceneId);
                commander.RegisterShipActor(binding.Actor);
            }
            // One scene-local arbitration adapter reuses canonical crew state
            // and the existing RTS commander; no autonomous duplicate clock.
            var agency = commander.GetComponent<ShipCrewAgencyCoordinator>();
            if (agency == null)
                agency = commander.gameObject.AddComponent<ShipCrewAgencyCoordinator>();
            agency.Configure(session, commander, _map);
            _session.Changed += OnWorldChanged;
        }

        private void OnWorldChanged(WorldSessionChange change)
        {
            if (_session == null) return;
            var layout = _session.Capture().State.Layout;
            if (layout.Fingerprint() != _layoutFingerprint)
                RebuildNavigation();
        }

        private void OnDestroy()
        {
            if (_session != null)
                _session.Changed -= OnWorldChanged;
            _session = null;
        }

        private ShipSpatialMap BuildMap(ShipLayoutState layout)
        {
            var portals = new List<ShipRoomPortal>();
            foreach (var door in doors)
            {
                if (door == null)
                    throw new InvalidOperationException("Null authored ship door.");
                portals.Add(new ShipRoomPortal(EntityId.Parse(door.DoorId),
                    new ShipGridCell(EntityId.Parse(door.FromRoomId),
                        door.FromCell.x, door.FromCell.y),
                    new ShipGridCell(EntityId.Parse(door.ToRoomId),
                        door.ToCell.x, door.ToCell.y), door.Open));
            }
            // Optional module spatial profiles are not yet canonical authored
            // state. The map conservatively blocks all placed module footprints.
            return ShipSpatialMap.Derive(layout,
                Array.Empty<ShipModuleSpatialProfile>(), portals);
        }

        /// <summary>
        /// Re-read canonical placements after actual build/damage changes.
        /// A route blocked by a door/layout change is invalidated by walker;
        /// it is not silently teleported or granted an interaction.
        /// </summary>
        public void RebuildNavigation()
        {
            if (_session == null) throw new InvalidOperationException("Ship scene is not initialized.");
            var layout = _session.Capture().State.Layout;
            _map = BuildMap(layout);
            _layoutFingerprint = layout.Fingerprint();
            commander.SetShipSpatialMap(_map);
            var agency = commander.GetComponent<ShipCrewAgencyCoordinator>();
            if (agency != null) agency.SetMap(_map);
            foreach (var binding in crew)
                if (binding != null && binding.Actor != null && binding.Actor.Walker != null)
                    binding.Actor.Walker.RebindMap(_map);
        }

        public bool SetDoorOpen(EntityId doorId, bool open)
        {
            if (!doorId.IsValid) return false;
            foreach (var door in doors)
            {
                if (door != null && door.DoorId == doorId.Value)
                {
                    if (door.Open == open) return true;
                    door.Open = open;
                    RebuildNavigation();
                    return true;
                }
            }
            return false;
        }
    }
}
