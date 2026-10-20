using System;
using System.Collections.Generic;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;
using UnityEngine;
using UnityEngine.InputSystem;
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
        [SerializeField] private bool allowRtsInput = true;
        [SerializeField] private Camera rtsCamera;
        [SerializeField, Range(0.04f, 0.35f)] private float motionSmoothTime = 0.12f;
        [SerializeField] private float turningDegreesPerSecond = 300f;

        private WorldSession _session;
        private CrewShipGridPath _grid;
        private IReadOnlyList<Vector2Int> _route = Array.Empty<Vector2Int>();
        private Vector2Int _currentCell;
        private int _routeIndex;
        // If a trip is cancelled between cell centers, finish the already
        // entered walkable segment before following a newly planned route.
        // Never snap an actor across a corner or through a bulkhead.
        private Vector2Int? _unfinishedStep;
        private EntityId _claimedSlot;
        private EntityId _item;
        private long _commandSequence;
        private bool _moving;
        private bool _goingHome;
        private bool _paused;
        private bool _started;
        private bool _selected;
        private bool _manualMove;
        private Vector2Int _manualDestination;
        private Vector3 _walkVelocity;
        private string _status = "Waiting for world session";

        public bool IsBound => _session != null;
        public bool IsMoving => _moving;
        public bool IsPaused => _paused;
        public string Status => _status;
        public EntityId HeldSlot => _claimedSlot;
        public Vector2Int CurrentCell => _currentCell;
        public bool IsSelected => _selected;
        public bool IsManualOrder => _manualMove;
        public Vector2Int ManualDestination => _manualDestination;

        public void Configure(Transform actor, Transform source,
            float speed = 2.1f)
        {
            engineer = actor;
            galleySource = source;
            walkingMetersPerSecond = Mathf.Max(0.1f, speed);
        }

        public void ConfigureRtsCamera(Camera camera) => rtsCamera = camera;

        [Inject]
        public void Construct(WorldSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        private void Start()
        {
            _grid = CrewGalleyFixtureState.Grid();
            if (rtsCamera == null) rtsCamera = Camera.main;
            _currentCell = CrewGalleyFixtureState.StartCell;
            _started = true;
            if (_session == null)
            {
                _paused = true;
                _status = "WorldSession injection missing — cannot run Galley";
                return;
            }
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
            return galleySource != null && slot != null &&
                slot.SourceId == CrewGalleyFixtureState.Galley &&
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
            _manualMove = false;
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
                var route = _grid.FindPath(_unfinishedStep ?? _currentCell, goal);
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
                _routeIndex = _unfinishedStep.HasValue ? 0 : 1;
                _unfinishedStep = null;
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

        /// <summary>
        /// This fixture exposes RTS-style controls for the engineer, without
        /// touching sea navigation or attaching TacticalActorProxy/NavMesh.
        /// Player commands preempt the autonomous claim and leave Needs intact.
        /// </summary>
        public void SelectEngineer() => _selected = engineer != null;
        public void ClearSelection() => _selected = false;

        public bool IssueMoveOrder(Vector2Int destination)
        {
            if (!_started || !_selected || _grid == null || engineer == null ||
                !_grid.CanStand(destination))
                return false;
            var projectedStart = _moving && _routeIndex < _route.Count
                ? _route[_routeIndex] : _unfinishedStep ?? _currentCell;
            if (_grid.FindPath(projectedStart, destination).Count == 0)
            {
                _status = "No walkable route to selected destination";
                return false; // Invalid clicks must not interrupt the current duty.
            }
            CancelTrip("Player move order", pause: false);
            var next = _grid.FindPath(_unfinishedStep ?? _currentCell, destination);
            if (next.Count == 0) return false;
            _manualDestination = destination;
            _route = next;
            _routeIndex = _unfinishedStep.HasValue ? 0 : 1;
            _unfinishedStep = null;
            _manualMove = true;
            _moving = true;
            _paused = false;
            _status = "Manual move ordered — autonomy paused until Resume Auto";
            return true;
        }

        /// <summary>Clickable RTS input: left-select engineer, right-move.</summary>
        public bool ProcessRtsPointer(Vector2 screenPosition, bool rightClick)
        {
            if (!_started || !allowRtsInput || engineer == null) return false;
            var camera = rtsCamera != null ? rtsCamera : Camera.main;
            if (camera == null) return false;
            var ray = camera.ScreenPointToRay(screenPosition);
            if (!rightClick)
            {
                bool clickedEngineer = Physics.Raycast(ray, out var hit, 200f) &&
                    (hit.transform == engineer || hit.transform.IsChildOf(engineer));
                _selected = clickedEngineer;
                return clickedEngineer;
            }
            if (!_selected || _grid == null) return false;
            var floor = new Plane(Vector3.up, Vector3.zero);
            if (!floor.Raycast(ray, out float distance)) return false;
            return IssueMoveOrder(_grid.WorldToCell(ray.GetPoint(distance)));
        }

        private void ProcessRtsMouse()
        {
            if (!allowRtsInput || !_started || _grid == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;
            bool right = mouse.rightButton.wasPressedThisFrame;
            bool left = mouse.leftButton.wasPressedThisFrame;
            if (!right && !left) return;
            var screen = mouse.position.ReadValue();
            // Avoid issuing world orders through the fixture's IMGUI panel.
            if (screen.x < 410f && Screen.height - screen.y < 355f)
                return;
            ProcessRtsPointer(screen, right);
        }

        private void Update()
        {
            ProcessRtsMouse();
            if (!_moving || _session == null || engineer == null || _grid == null)
                return;
            if (!_goingHome)
            {
                var destinationCell = _claimedSlot == CrewGalleyFixtureState.Eat
                    ? CrewGalleyFixtureState.EatCell
                    : CrewGalleyFixtureState.DrinkCell;
                if (galleySource == null ||
                    !SlotAnchorMatchesSource(_claimedSlot, destinationCell))
                {
                    CancelTrip("Galley source or authored slot disappeared", pause: true);
                    return;
                }

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
            var before = engineer.position;
            float remaining = Vector3.Distance(before, destination);

            // Gently approach bends and begin looking around a corner before
            // entering it. Keep an actual route along the authored free cells.
            var direction = destination - before;
            float speed = walkingMetersPerSecond;
            bool isBend = false;
            if (_routeIndex + 1 < _route.Count)
            {
                var after = _grid.WorldCenter(_route[_routeIndex + 1], 1f);
                var incoming = destination - _grid.WorldCenter(_currentCell, 1f);
                var outgoing = after - destination;
                isBend = Vector3.Dot(incoming.normalized,
                    outgoing.normalized) < 0.25f;
                if (isBend)
                {
                    speed *= Mathf.Lerp(0.65f, 1f,
                        Mathf.Clamp01(remaining / 0.75f));
                    var lead = Mathf.Clamp01((0.65f - remaining) / 0.65f);
                    direction = Vector3.Slerp(direction.normalized,
                        outgoing.normalized, 0.35f * lead);
                }
            }
            var candidate = Vector3.SmoothDamp(before, destination,
                ref _walkVelocity, motionSmoothTime,
                Mathf.Max(0.1f, speed), Time.deltaTime);
            // Velocity blending may round a corner but must never cut through
            // a blocked bulkhead. Fall back to the original cardinal segment.
            if (!_grid.CanTraverse(before, candidate))
            {
                _walkVelocity = Vector3.zero;
                candidate = Vector3.MoveTowards(before, destination,
                    speed * Time.deltaTime);
                if (!_grid.CanTraverse(before, candidate))
                {
                    CancelTrip("Path obstructed; no wall clipping", pause: true);
                    return;
                }
            }
            engineer.position = candidate;
            if (direction.sqrMagnitude > 0.0001f)
                engineer.rotation = Quaternion.RotateTowards(engineer.rotation,
                    Quaternion.LookRotation(direction.normalized, Vector3.up),
                    turningDegreesPerSecond * Time.deltaTime);

            // Advance a little before the exact center of a bend. The
            // spring retains velocity into the next free corridor cell, so
            // turning is a small arc instead of an abrupt stop-and-snap.
            // Final interaction/RTS destinations still require true arrival.
            float arrivalRadius = isBend ? 0.23f : 0.06f;
            if (Vector3.SqrMagnitude(engineer.position - destination) <=
                arrivalRadius * arrivalRadius)
            {
                if (!isBend) engineer.position = destination;
                _currentCell = nextCell;
                _routeIndex++;
                if (_routeIndex >= _route.Count) Arrived();
            }
        }

        private void Arrived()
        {
            _moving = false;
            _walkVelocity = Vector3.zero;
            if (_manualMove)
            {
                _manualMove = false;
                _paused = true;
                _status = "Move order complete — autonomy paused; Resume Auto to continue";
                return;
            }
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
            var route = _grid.FindPath(_unfinishedStep ?? _currentCell,
                CrewGalleyFixtureState.StartCell);
            if (route.Count == 0)
            {
                _paused = true;
                _status = "Needs satisfied, but route to engineering is blocked";
                return;
            }
            _route = route;
            _routeIndex = _unfinishedStep.HasValue ? 0 : 1;
            _unfinishedStep = null;
            _goingHome = true;
            _moving = true;
            _status = "Needs recovered: returning to engineering";
        }

        public void CancelTrip(string reason = "Cancelled by player", bool pause = true)
        {
            // Preserve the current physical segment if the actor is halfway
            // between cells. The next goal may differ from the cancelled goal.
            if (_moving && engineer != null && _grid != null &&
                _routeIndex < _route.Count)
            {
                var priorCenter = _grid.WorldCenter(_currentCell, 1f);
                _unfinishedStep = Vector3.Distance(engineer.position, priorCenter) > 0.01f
                    ? _route[_routeIndex] : (Vector2Int?)null;
            }
            _moving = false;
            _goingHome = false;
            _manualMove = false;
            _walkVelocity = Vector3.zero;
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

            GUILayout.BeginArea(new Rect(12, 12, 395, 344), GUI.skin.box);
            GUILayout.Label("FrostArk | Crew Galley — Unity fixture");
            GUILayout.Label("World minute: " + state.Minute.Value +
                " | Core revision: " + state.Revision);
            // These are severity bars, NOT fullness/hydration percentages:
            // 1000 means starving/dehydrated; 0 means comfortable.
            var oldColor = GUI.color;
            GUI.color = actor.Hunger >= CrewMember.CriticalNeedAt
                ? new Color(1f, 0.48f, 0.32f) : Color.white;
            GUILayout.Label("HUNGER severity: " + actor.Hunger +
                "/1000 (" + (actor.Hunger >= CrewMember.CriticalNeedAt ?
                    "STARVING" : "not critical") + ")");
            GUI.color = actor.Thirst >= CrewMember.CriticalNeedAt
                ? new Color(1f, 0.48f, 0.32f) : Color.white;
            GUILayout.Label("THIRST severity: " + actor.Thirst +
                "/1000 (" + (actor.Thirst >= CrewMember.CriticalNeedAt ?
                    "DEHYDRATED" : "not critical") + ")");
            GUI.color = oldColor;
            GUILayout.Label("Fatigue severity: " + actor.Fatigue + "/1000");
            GUILayout.Label("0 = comfortable / 1000 = critical");
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
            if (GUILayout.Button("Resume Auto")) TryStart();
            if (GUILayout.Button("Cancel")) CancelTrip();
            if (GUILayout.Button("Advance 10 min")) _session.Advance(10);
            GUILayout.EndHorizontal();
            GUILayout.Label("RTS: left-click Engineer to select; right-click deck to move.");
            GUILayout.Label("Selected: " + (_selected ? "Engineer" : "none") +
                (_manualMove ? " | Manual order" : ""));
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
