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
        private Dictionary<EntityId, Vector3> _origins;
        private EntityId _boundShipSceneId;

        public ShipSpatialMap Map => _map;
        public Transform ShipRoot => shipRoot;
        public EntityId BoundShipSceneId => _boundShipSceneId;

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
            if (checkpoint == null)
                throw new InvalidOperationException("Ship bootstrap requires authoritative ship checkpoint.");
            var shipSceneId = checkpoint.Theater == PhysicalTheater.Ship
                ? checkpoint.SceneId
                : (session.ResidentShipSceneId ?? EntityId.Parse(startingSceneId));
            if (!shipSceneId.IsValid)
                throw new InvalidOperationException("Ship bootstrap requires authoritative ship checkpoint.");

            var state = session.CurrentState;
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
            _origins = origins;
            _boundShipSceneId = shipSceneId;
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
                    (checkpoint.Theater == PhysicalTheater.Ship && member.IsAway) ||
                    member.Wounds >= 1000)
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
                binding.Actor.BindScene(shipSceneId);
                commander.RegisterShipActor(binding.Actor);
                if (member.IsAway)
                {
                    walker.SuspendForShoreDeparture();
                    binding.Actor.gameObject.SetActive(false);
                }
                else
                {
                    binding.Actor.gameObject.SetActive(true);
                }
            }
            // One scene-local arbitration adapter reuses canonical crew state
            // and the existing RTS commander; no autonomous duplicate clock.
            var agency = commander.GetComponent<ShipCrewAgencyCoordinator>();
            if (agency == null)
                agency = commander.gameObject.AddComponent<ShipCrewAgencyCoordinator>();
            agency.Configure(session, commander, _map);
            var duty = commander.GetComponent<ShipCrewDutyAutonomy>();
            if (duty == null)
                duty = commander.gameObject.AddComponent<ShipCrewDutyAutonomy>();
            duty.Configure(session, commander, agency, _map);
            _session.Changed += OnWorldChanged;
        }

        public void SyncCrewPresenceWithSession()
        {
            if (_session == null || _map == null || _origins == null) return;
            var state = _session.CurrentState;
            var agency = commander != null ? commander.GetComponent<ShipCrewAgencyCoordinator>() : null;
            var duty = commander != null ? commander.GetComponent<ShipCrewDutyAutonomy>() : null;
            var traffic = shipRoot != null ? shipRoot.GetComponent<ShipTrafficCoordinator>() : null;

            foreach (var binding in crew)
            {
                if (binding == null || binding.Actor == null) continue;
                var id = binding.Actor.ActorId;
                var member = state.CrewDuty.Get(id);
                if (member == null) continue;
                if (member.IsAway && binding.Actor.gameObject.activeSelf)
                {
                    commander?.Selection.Exclude(id);
                    agency?.ReleaseActorForDeparture(id);
                    duty?.ReleaseActorForDeparture(id);
                    binding.Actor.Walker?.SuspendForShoreDeparture();
                    binding.Actor.gameObject.SetActive(false);
                }
            }

            foreach (var binding in crew)
            {
                if (binding == null || binding.Actor == null) continue;
                var id = binding.Actor.ActorId;
                var member = state.CrewDuty.Get(id);
                if (member == null || member.IsAway || binding.Actor.gameObject.activeSelf)
                    continue;
                var roomId = EntityId.Parse(binding.StartingRoomId);
                var preferred = new ShipGridCell(roomId, binding.StartingCell.x, binding.StartingCell.y);
                var reentryCell = ResolveSafeReentryCell(state.Layout, binding.Actor.Walker, traffic, preferred);
                binding.Actor.gameObject.SetActive(true);
                binding.Actor.Walker.ResumeFromShoreAt(reentryCell);
                commander?.RegisterShipActor(binding.Actor);
            }
        }

        private ShipGridCell ResolveSafeReentryCell(ShipLayoutState layout,
            ShipGridWalker walker, ShipTrafficCoordinator traffic, ShipGridCell preferred)
        {
            if (_map.Walkable(preferred) && !_map.IsPortalEndpoint(preferred) &&
                (traffic == null || traffic.CanPark(walker, preferred)))
                return preferred;
            var room = layout.Rooms.FirstOrDefault(r => r.Id == preferred.RoomId);
            if (room != null)
            {
                var best = preferred;
                int bestDist = int.MaxValue;
                for (int y = 0; y < room.Height; y++)
                {
                    for (int x = 0; x < room.Width; x++)
                    {
                        var candidate = new ShipGridCell(room.Id, x, y);
                        if (!_map.Walkable(candidate) || _map.IsPortalEndpoint(candidate))
                            continue;
                        if (traffic != null && !traffic.CanPark(walker, candidate))
                            continue;
                        int dist = Math.Abs(x - preferred.X) + Math.Abs(y - preferred.Y);
                        if (dist < bestDist)
                        {
                            bestDist = dist;
                            best = candidate;
                        }
                    }
                }
                if (bestDist != int.MaxValue)
                    return best;
            }
            return preferred;
        }

        private void OnWorldChanged(WorldSessionChange change)
        {
            if (_session == null) return;
            var layout = _session.CurrentState.Layout;
            if (layout.Fingerprint() != _layoutFingerprint)
                RebuildNavigation();
            SyncCrewPresenceWithSession();
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
            var layout = _session.CurrentState.Layout;
            _map = BuildMap(layout);
            _layoutFingerprint = layout.Fingerprint();
            commander.SetShipSpatialMap(_map);
            var agency = commander.GetComponent<ShipCrewAgencyCoordinator>();
            if (agency != null) agency.SetMap(_map);
            var duty = commander.GetComponent<ShipCrewDutyAutonomy>();
            if (duty != null) duty.SetMap(_map);
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
