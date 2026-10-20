using System;
using System.Collections.Generic;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;
using UnityEngine;
using VContainer;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Single-scene interaction bridge: Core selects/claims/consumes; Unity
    /// interpolates along an authored ship-local path. Arrival, not a claim,
    /// authorizes calling UseCrewProvisionInteractionCommand.
    /// </summary>
    public sealed class CrewGalleyController : MonoBehaviour
    {
        [SerializeField] private Transform engineer;
        [SerializeField] private Transform galleySource;
        [SerializeField] private float walkingMetersPerSecond = 2.1f;
        [SerializeField] private bool autoBegin = true;

        private WorldSession _session;
        private CrewShipGridPath _grid;
        private IReadOnlyList<Vector2Int> _route = Array.Empty<Vector2Int>();
        private Vector2Int _currentCell;
        private int _routeIndex;
        private EntityId _claimedSlot;
        private EntityId _item;
        private long _commandSequence;
        private bool _moving;
        private bool _goingHome;
        private bool _paused;
        private bool _started;
        private string _status = "Waiting for world session";

        public bool IsBound => _session != null;
        public bool IsMoving => _moving;
        public bool IsPaused => _paused;
        public string Status => _status;
        public EntityId HeldSlot => _claimedSlot;
        public Vector2Int CurrentCell => _currentCell;

        public void Configure(Transform actor, Transform source)
        {
            engineer = actor;
            galleySource = source;
        }

        [Inject]
        public void Construct(WorldSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        private void Start()
        {
            _grid = CrewGalleyFixtureState.Grid();
            _currentCell = CrewGalleyFixtureState.StartCell;
            _started = true;
            if (engineer == null || galleySource == null)
            {
                _paused = true;
                _status = "Missing authored galley or crew Transform";
                return;
            }
            engineer.position = _grid.WorldCenter(_currentCell, 1f);
            if (!SlotAnchorMatchesSource(CrewGalleyFixtureState.Eat,
                    CrewGalleyFixtureState.EatCell) ||
                !SlotAnchorMatchesSource(CrewGalleyFixtureState.Drink,
                    CrewGalleyFixtureState.DrinkCell))
            {
                _paused = true;
                _status = "Galley slot/source authoring mismatch; refusing to use";
                return;
            }
            _status = "Ready — critical crew needs visible in the panel";
            if (autoBegin) TryStart();
        }

        private bool SlotAnchorMatchesSource(EntityId slotId, Vector2Int target)
        {
            if (_session == null) return false;
            var slot = _session.Capture().State.CrewDuty.Interactions.GetSlot(slotId);
            return slot != null && slot.SourceId == CrewGalleyFixtureState.Galley &&
                target == CrewGalleyFixtureState.GalleyCell +
                    new Vector2Int(slot.LocalX, slot.LocalY) &&
                _grid.CanStand(target) &&
                Vector3.Distance(galleySource.position,
                    _grid.WorldCenter(CrewGalleyFixtureState.GalleyCell, galleySource.position.y)) < 0.1f;
        }

        public bool TryStart()
        {
            if (!_started || _session == null || engineer == null || _moving) return false;
            _paused = false;
            _goingHome = false;
            return PlanNext();
        }

        private bool PlanNext()
        {
            if (_session == null) return false;
            var world = _session.Capture().State;
            var actor = world.CrewDuty.Get(CrewGalleyFixtureState.Engineer);
            if (actor == null || actor.IsAway || actor.Wounds >= 1000)
            {
                _paused = true;
                _status = "Crew member unavailable; no interaction";
                return false;
            }

            var urgent = world.CrewDuty.AssessNeeds(CrewGalleyFixtureState.Engineer);
            if (!urgent.IsCritical)
            {
                ReturnToWork();
                return true;
            }

            var candidates = CrewProvisionInteractionDiscovery.Find(world,
                CrewGalleyFixtureState.Engineer);
            foreach (var offer in candidates)
            {
                if (offer.SourceId != CrewGalleyFixtureState.Galley) continue;
                var goal = offer.SlotId == CrewGalleyFixtureState.Eat ?
                    CrewGalleyFixtureState.EatCell :
                    offer.SlotId == CrewGalleyFixtureState.Drink ?
                    CrewGalleyFixtureState.DrinkCell : new Vector2Int(-1, -1);
                if (!SlotAnchorMatchesSource(offer.SlotId, goal)) continue;
                var route = _grid.FindPath(_currentCell, goal);
                if (route.Count == 0) continue;

                var result = _session.Submit(new ClaimCrewInteractionCommand(
                    NextCommand("claim"), CrewGalleyFixtureState.Engineer,
                    offer.SlotId, 120, _session.Revision));
                if (!result.Success)
                {
                    _status = "Claim denied: " + result.Failure;
                    continue;
                }

                _claimedSlot = offer.SlotId;
                _item = offer.ItemId;
                _route = route;
                _routeIndex = 1;
                _goingHome = false;
                _moving = true;
                _status = "Critical " + (offer.Verb == CrewInteractionVerb.Eat ?
                    "hunger" : "thirst") + ": slot reserved, approaching galley";
                return true;
            }

            _paused = true;
            _status = "Critical Need but no reachable, stocked galley slot";
            return false;
        }

        private void Update()
        {
            if (!_moving || _session == null || engineer == null || _grid == null)
                return;
            if (!_goingHome)
            {
                var claim = _session.Capture().State.CrewDuty.Interactions.GetClaim(_claimedSlot);
                if (claim == null || claim.CrewId != CrewGalleyFixtureState.Engineer ||
                    claim.ExpiresAt <= _session.Minute)
                {
                    CancelTrip("Reservation lost or expired before arrival", pause: true);
                    return;
                }
            }

            if (_routeIndex >= _route.Count)
            {
                Arrived();
                return;
            }

            var nextCell = _route[_routeIndex];
            if (!_grid.CanStand(nextCell))
            {
                CancelTrip("Route blocked; no free food or teleport", pause: true);
                return;
            }
            var destination = _grid.WorldCenter(nextCell, 1f);
            engineer.position = Vector3.MoveTowards(engineer.position, destination,
                walkingMetersPerSecond * Time.deltaTime);
            var direction = destination - engineer.position;
            if (direction.sqrMagnitude > 0.0001f)
                engineer.rotation = Quaternion.Slerp(engineer.rotation,
                    Quaternion.LookRotation(direction.normalized, Vector3.up),
                    Mathf.Min(1, Time.deltaTime * 8f));

            if (Vector3.SqrMagnitude(engineer.position - destination) <= 0.004f)
            {
                engineer.position = destination;
                _currentCell = nextCell;
                _routeIndex++;
                if (_routeIndex >= _route.Count) Arrived();
            }
        }

        private void Arrived()
        {
            _moving = false;
            if (_goingHome)
            {
                _paused = true;
                _goingHome = false;
                _status = "Returned to engineering; needs satisfied";
                return;
            }
            var intendedCell = _claimedSlot == CrewGalleyFixtureState.Eat ?
                CrewGalleyFixtureState.EatCell : CrewGalleyFixtureState.DrinkCell;
            if (!SlotAnchorMatchesSource(_claimedSlot, intendedCell) ||
                _currentCell != intendedCell ||
                Vector3.Distance(engineer.position,
                    _grid.WorldCenter(intendedCell, 1f)) > 0.15f)
            {
                CancelTrip("Arrival/slot proximity invalid", pause: true);
                return;
            }

            var result = _session.Submit(new UseCrewProvisionInteractionCommand(
                NextCommand("use"), CrewGalleyFixtureState.Engineer,
                _claimedSlot, _item, _session.Revision));
            if (!result.Success)
            {
                CancelTrip("Use rejected: " + result.Failure, pause: true);
                return;
            }

            _claimedSlot = default;
            _item = default;
            _status = "Used real galley provision; inventory and Needs updated";
            PlanNext();
        }

        private void ReturnToWork()
        {
            var route = _grid.FindPath(_currentCell, CrewGalleyFixtureState.StartCell);
            if (route.Count == 0)
            {
                _paused = true;
                _status = "Needs satisfied, but route to engineering is blocked";
                return;
            }
            _route = route;
            _routeIndex = 1;
            _goingHome = true;
            _moving = true;
            _status = "Needs recovered: returning to engineering";
        }

        public void CancelTrip(string reason = "Cancelled by player", bool pause = true)
        {
            _moving = false;
            _goingHome = false;
            _route = Array.Empty<Vector2Int>();
            if (_session != null && _claimedSlot.IsValid)
            {
                var claim = _session.Capture().State.CrewDuty.Interactions.GetClaim(_claimedSlot);
                if (claim != null && claim.CrewId == CrewGalleyFixtureState.Engineer)
                {
                    _session.Submit(new ReleaseCrewInteractionCommand(
                        NextCommand("cancel"), CrewGalleyFixtureState.Engineer,
                        _claimedSlot, _session.Revision));
                }
            }
            _claimedSlot = default;
            _item = default;
            _paused = pause;
            _status = reason;
        }

        private EntityId NextCommand(string kind) =>
            EntityId.Parse("command:galley:fixture:" + kind + ":" + (++_commandSequence));

        private void OnGUI()
        {
            if (_session == null) return;
            var state = _session.Capture().State;
            var actor = state.CrewDuty.Get(CrewGalleyFixtureState.Engineer);
            if (actor == null) return;

            GUILayout.BeginArea(new Rect(12, 12, 395, 245), GUI.skin.box);
            GUILayout.Label("FrostArk | Crew Galley — Unity fixture");
            GUILayout.Label("World minute: " + state.Minute.Value +
                " | Core revision: " + state.Revision);
            GUILayout.Label("Engineer Hunger: " + actor.Hunger +
                "   Thirst: " + actor.Thirst + "   Fatigue: " + actor.Fatigue);
            GUILayout.Label("Rations: " +
                state.Inventory.GetAmount(InventoryContainer.ShipHold,
                    CrewGalleyFixtureState.Ration) + "   Water: " +
                state.Inventory.GetAmount(InventoryContainer.ShipHold,
                    CrewGalleyFixtureState.Water));
            GUILayout.Label("Engineering staffed skill: " +
                state.CrewDuty.EffectiveSkill(DutyRole.Engineering, state.Minute));
            GUILayout.Label("Claim: " + (_claimedSlot.IsValid ? _claimedSlot.Value : "none"));
            GUILayout.Label(_status);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Start / Replan")) TryStart();
            if (GUILayout.Button("Cancel")) CancelTrip();
            if (GUILayout.Button("Advance 10 min")) _session.Advance(10);
            GUILayout.EndHorizontal();
            GUILayout.Label("Clock advances only via explicit button (fixture).");
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (_session == null || !_claimedSlot.IsValid) return;
            try { CancelTrip("Scene teardown"); }
            catch (ObjectDisposedException) { /* The LifetimeScope already disposed. */ }
        }
    }
}
