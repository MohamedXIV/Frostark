using System;
using System.Collections.Generic;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Scene-local ship Grid movement, entirely relative to a moving ship root.
    /// No NavMeshAgent and no extra simulation clock: caller supplies the
    /// canonical layout-derived map plus authored compartment local origins.
    /// Movement stays presentation-only until an authoritative action arrives.
    /// </summary>
    public sealed class ShipGridWalker : MonoBehaviour
    {
        private ShipSpatialMap _map;
        private Transform _shipRoot;
        private Dictionary<EntityId, Vector3> _roomOrigins;
        private IReadOnlyList<ShipGridCell> _route;
        private ShipGridCell _lastReachedCell;
        private ShipGridCell _orderedCell;
        private Vector3 _localPosition;
        private int _nextIndex;
        private float _speed;
        private float _currentSpeed;
        private ShipTrafficCoordinator _traffic;
        private bool _nextCellReserved;
        private ShipGridCell _reservedCell;
        private float _trafficWaitSeconds;
        private bool _recovering;
        private bool _stopAtNextSafeCell;
        public bool HasIncomingReservation => _nextCellReserved;
        public ShipGridCell IncomingReservation => _reservedCell;
        public bool IsRecovering => _recovering;

        // Current and next-step occupancy are intentionally different from
        // the exclusive station/work-slot claim owned by canonical crew logic.
        public bool HoldsCell(ShipGridCell cell) =>
            IsConfigured && (cell == _lastReachedCell ||
                             (_nextCellReserved && cell == _reservedCell));
        private const float Acceleration = 8f;
        private const float Braking = 10f;

        public float CurrentSpeed => _currentSpeed;

        public bool IsConfigured => _map != null;
        public bool IsMoving => _route != null && _nextIndex < _route.Count;
        public ShipGridCell LastReachedCell => _lastReachedCell;
        public ShipGridCell OrderedCell => _orderedCell;
        public string LastError { get; private set; }

        /// <summary>
        /// The actor must already stand at its authored cell's physical center.
        /// Scene bootstrap/reentry owns spawn validation; Configure never warps
        /// an actor or infers its room by rounding a world-space position.
        /// </summary>
        public void Configure(Transform shipRoot, ShipSpatialMap map,
            IReadOnlyDictionary<EntityId, Vector3> roomLocalOrigins,
            ShipGridCell startingCell, float speedMetersPerSecond = 2.5f)
        {
            if (shipRoot == null) throw new ArgumentNullException(nameof(shipRoot));
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (roomLocalOrigins == null) throw new ArgumentNullException(nameof(roomLocalOrigins));
            if (speedMetersPerSecond <= 0 || float.IsNaN(speedMetersPerSecond) ||
                float.IsInfinity(speedMetersPerSecond))
                throw new ArgumentOutOfRangeException(nameof(speedMetersPerSecond));
            if (!map.Walkable(startingCell))
                throw new ArgumentException("Starting cell is not walkable.", nameof(startingCell));

            var origins = new Dictionary<EntityId, Vector3>(roomLocalOrigins);
            if (!origins.TryGetValue(startingCell.RoomId, out var origin))
                throw new ArgumentException("Missing authored room origin.", nameof(roomLocalOrigins));
            var expected = origin + new Vector3(startingCell.X + 0.5f, 0f, startingCell.Y + 0.5f);
            var actual = shipRoot.InverseTransformPoint(transform.position);
            if (Vector3.Distance(expected, actual) > 0.1f)
                throw new ArgumentException("Crew proxy must already be at its authored cell.");

            if (_traffic != null) _traffic.Unregister(this);
            _shipRoot = shipRoot;
            _map = map;
            _roomOrigins = origins;
            _lastReachedCell = startingCell;
            _orderedCell = startingCell;
            _route = null;
            _nextIndex = 0;
            _localPosition = actual;
            _speed = speedMetersPerSecond;
            _currentSpeed = 0f;
            _nextCellReserved = false;
            _trafficWaitSeconds = 0f;
            _stopAtNextSafeCell = false;
            _traffic = shipRoot.GetComponent<ShipTrafficCoordinator>();
            if (_traffic == null)
                _traffic = shipRoot.gameObject.AddComponent<ShipTrafficCoordinator>();
            _traffic.Register(this);
            LastError = null;
        }

        private bool TryCenter(ShipGridCell cell, out Vector3 center)
        {
            if (_roomOrigins != null && _roomOrigins.TryGetValue(cell.RoomId, out var origin))
            {
                center = origin + new Vector3(cell.X + 0.5f, 0f, cell.Y + 0.5f);
                return true;
            }
            center = default;
            return false;
        }

        /// <summary>
        /// Interrupt a physical action only at a safe cell center. When
        /// halfway through an occupied edge we finish that committed edge
        /// and retain its reservation until arrival. Never warp or cut a wall.
        /// </summary>
        public void RequestStopAtSafeCell()
        {
            if (!IsConfigured || _recovering || !IsMoving) return;
            if (_nextCellReserved)
            {
                _stopAtNextSafeCell = true;
                return;
            }
            _route = null;
            _nextIndex = 0;
            _currentSpeed = 0f;
            _stopAtNextSafeCell = false;
            LastError = "PlayerOrSafetyInterrupted";
        }

        public bool TryMoveTo(ShipGridCell goal)
        {
            if (!IsConfigured)
            {
                LastError = "NotConfigured";
                return false;
            }
            if (_recovering)
            {
                LastError = "RecoveringToSafeCell";
                return false; // Finish retreating to the last reached cell center.
            }
            if (_nextCellReserved)
            {
                LastError = "AwaitingSafeRestart";
                return false; // Never redirect midway between reserved cells.
            }
            if (_traffic != null && !_traffic.CanPark(this, goal))
            {
                LastError = "DestinationOccupied";
                return false;
            }
            var path = _map.FindPath(_lastReachedCell, goal);
            if (path.Count == 0)
            {
                LastError = "NoRoute";
                return false; // Keep any previously accepted route intact.
            }
            foreach (var cell in path)
            {
                if (!TryCenter(cell, out _))
                {
                    LastError = "MissingRoomOrigin";
                    return false;
                }
            }
            _orderedCell = goal;
            _stopAtNextSafeCell = false;
            _trafficWaitSeconds = 0f;
            _route = path;
            _nextIndex = path.Count > 1 ? ChooseNextWaypoint(1) : path.Count;
            if (!IsMoving) _currentSpeed = 0f;
            LastError = null;
            return true;
        }

        // Use the furthest unobstructed cell center IN THE CURRENT ROOM.
        // Never skip a physical bulkhead portal or shortcut a module corner.
        private int ChooseNextWaypoint(int startIndex)
        {
            if (_route == null || startIndex >= _route.Count) return startIndex;
            // Shared corridors reserve one cell/edge at a time. Use the
            // smoothed long waypoint only when there are no other crew.
            if (_traffic != null && _traffic.Count > 1) return startIndex;
            int chosen = startIndex;
            for (int i = startIndex; i < _route.Count; i++)
            {
                if (_route[i].RoomId != _lastReachedCell.RoomId ||
                    !_map.CanTraverseStraight(_lastReachedCell, _route[i]))
                    break;
                chosen = i;
            }
            return chosen;
        }

        /// <summary>
        /// Layout/door changes invalidate or recompute remaining local routes.
        /// No geometry collision is overridden when an already-walked segment
        /// becomes obstructed; authoritative claim cancellation is caller-owned.
        /// </summary>
        public bool RebindMap(ShipSpatialMap updated)
        {
            if (updated == null) throw new ArgumentNullException(nameof(updated));
            if (!IsConfigured) { LastError = "NotConfigured"; return false; }
            if (_recovering)
            {
                _map = updated;
                return false; // Keep retreating along the already-walked edge.
            }
            // If the actor is physically between discrete cell centers, a
            // fresh path beginning at _lastReachedCell could diagonally cut
            // through an obstacle. Retreat to that center without teleporting;
            // the reservation stays held until the actor is safely back.
            if (IsMoving && (!TryCenter(_lastReachedCell, out var reachedCenter) ||
                (_localPosition - reachedCenter).sqrMagnitude > 0.0001f))
            {
                _map = updated;
                _route = null;
                _nextIndex = 0;
                LastError = "RouteInvalidated";
                _currentSpeed = 0f;
                _recovering = _nextCellReserved;
                return false;
            }
            var next = IsMoving ? updated.FindPath(_lastReachedCell, _orderedCell) :
                Array.Empty<ShipGridCell>();
            if (IsMoving && (next.Count == 0 || !RouteOriginsValid(next)))
            {
                _map = updated;
                _route = null;
                _nextIndex = 0;
                LastError = "RouteInvalidated";
                _currentSpeed = 0f;
                // At a cell center no physical step was taken, so releasing the
                // reserved destination is safe and unblocks the doorway.
                _nextCellReserved = false;
                return false;
            }
            _map = updated;
            if (IsMoving)
            {
                _route = next;
                _nextIndex = next.Count > 1 ? ChooseNextWaypoint(1) : next.Count;
            }
            LastError = null;
            return true;
        }

        private bool RouteOriginsValid(IReadOnlyList<ShipGridCell> path)
        {
            foreach (var cell in path)
                if (!TryCenter(cell, out _)) return false;
            return true;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>
        /// Deterministic scene-frame movement in ship-local coordinates.
        /// Called by Update normally and directly by Unity EditMode tests.
        /// Even while idle, the crew proxy follows a moving ship transform.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!IsConfigured || _shipRoot == null) return;
            if (deltaTime < 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));

            if (_recovering)
            {
                if (TryCenter(_lastReachedCell, out var recoveryPoint))
                {
                    var before = _localPosition;
                    var remaining = Vector3.Distance(before, recoveryPoint);
                    float desiredSpeed = Mathf.Min(_speed,
                        Mathf.Sqrt(2f * Braking * remaining));
                    _currentSpeed = Mathf.MoveTowards(_currentSpeed,
                        desiredSpeed, Acceleration * deltaTime);
                    _localPosition = Vector3.MoveTowards(before, recoveryPoint,
                        _currentSpeed * deltaTime);
                    var direction = _localPosition - before;
                    if (direction.sqrMagnitude > 0.000001f)
                    {
                        var desired = Quaternion.LookRotation(
                            _shipRoot.TransformDirection(direction.normalized), _shipRoot.up);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            desired, 360f * deltaTime);
                    }
                    if ((_localPosition - recoveryPoint).sqrMagnitude <= 0.000001f)
                    {
                        _localPosition = recoveryPoint;
                        _nextCellReserved = false;
                        _recovering = false;
                        _route = null;
                        _nextIndex = 0;
                        _currentSpeed = 0f;
                    }
                }
                else
                {
                    _nextCellReserved = false;
                    _recovering = false;
                    _route = null;
                    _nextIndex = 0;
                    _currentSpeed = 0f;
                    LastError = "MissingRoomOrigin";
                }
                transform.position = _shipRoot.TransformPoint(_localPosition);
                return;
            }

            if (IsMoving)
            {
                var nextCell = _route[_nextIndex];
                if (!_map.Walkable(nextCell))
                {
                    _route = null;
                    _nextIndex = 0;
                    LastError = "RouteInvalidated";
                    _currentSpeed = 0f;
                    _recovering = _nextCellReserved;
                }
                else if (_traffic != null && !_nextCellReserved &&
                         !_traffic.CanEnter(this, _lastReachedCell, nextCell))
                {
                    LastError = "WaitingForTraffic";
                    _currentSpeed = 0f;
                    // Reconsider temporary agents at a bounded interval, not
                    // each rendered frame. Preserve the accepted goal and
                    // return explicit waiting if no occupied-cell detour exists.
                    _trafficWaitSeconds += deltaTime;
                    if (_trafficWaitSeconds >= 0.75f)
                    {
                        _trafficWaitSeconds = 0f;
                        var alternate = _map.FindPath(_lastReachedCell, _orderedCell,
                            _traffic.BlockedCellsExcept(this));
                        if (alternate.Count > 1)
                        {
                            _route = alternate;
                            _nextIndex = ChooseNextWaypoint(1);
                        }
                    }
                }
                else if (TryCenter(nextCell, out var nextPoint))
                {
                    _nextCellReserved = true;
                    _reservedCell = nextCell;
                    _trafficWaitSeconds = 0f;
                    LastError = null;
                    var before = _localPosition;
                    // Accelerate on departure, brake toward the final arrival.
                    // Keep intermediate waypoints fast; never overshoot.
                    var remaining = Vector3.Distance(before, nextPoint);
                    for (int i = _nextIndex + 1; i < _route.Count; i++)
                    {
                        if (TryCenter(_route[i - 1], out var a) &&
                            TryCenter(_route[i], out var b))
                            remaining += Vector3.Distance(a, b);
                    }
                    float desiredSpeed = Mathf.Min(_speed,
                        Mathf.Sqrt(2f * Braking * remaining));
                    _currentSpeed = Mathf.MoveTowards(_currentSpeed,
                        desiredSpeed, Acceleration * deltaTime);
                    _localPosition = Vector3.MoveTowards(before, nextPoint,
                        _currentSpeed * deltaTime);
                    var direction = _localPosition - before;
                    if (direction.sqrMagnitude > 0.000001f)
                    {
                        var desired = Quaternion.LookRotation(
                            _shipRoot.TransformDirection(direction.normalized), _shipRoot.up);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            desired, 360f * deltaTime);
                    }
                    if ((_localPosition - nextPoint).sqrMagnitude <= 0.000001f)
                    {
                        _localPosition = nextPoint;
                        _lastReachedCell = nextCell;
                        _nextCellReserved = false;
                        _nextIndex++;
                        if (_stopAtNextSafeCell)
                        {
                            _stopAtNextSafeCell = false;
                            _route = null;
                            _nextIndex = 0;
                            _currentSpeed = 0f;
                            LastError = "PlayerOrSafetyInterrupted";
                        }
                        if (!IsMoving)
                        {
                            _route = null;
                            _currentSpeed = 0f;
                        }
                        else
                            _nextIndex = ChooseNextWaypoint(_nextIndex);
                    }
                }
                else
                {
                    _route = null;
                    _nextIndex = 0;
                    LastError = "MissingRoomOrigin";
                    _currentSpeed = 0f;
                }
            }
            transform.position = _shipRoot.TransformPoint(_localPosition);
        }

        private void OnDestroy()
        {
            if (_traffic != null) _traffic.Unregister(this);
        }

        /// <summary>
        /// Release transient shipboard corridor/cell occupancy when a crew
        /// member departs for shore so away actors never block onboard crew.
        /// </summary>
        public void SuspendForShoreDeparture()
        {
            _route = null;
            _nextIndex = 0;
            _currentSpeed = 0f;
            _nextCellReserved = false;
            _recovering = false;
            _stopAtNextSafeCell = false;
            _trafficWaitSeconds = 0f;
            LastError = null;
            if (_traffic != null)
                _traffic.Unregister(this);
        }

        /// <summary>
        /// Re-bind an existing shipboard walker when an away crew member
        /// returns from shore to the persistent ship without recreating the
        /// ship hierarchy or disturbing crew who remained aboard.
        /// </summary>
        public void ResumeFromShoreAt(ShipGridCell reentryCell)
        {
            if (_shipRoot == null || _map == null || _roomOrigins == null)
                throw new InvalidOperationException("Walker must be configured before shore return.");
            if (!_map.Walkable(reentryCell) || !TryCenter(reentryCell, out var center))
                throw new ArgumentException("Reentry cell must be walkable and in an authored room.", nameof(reentryCell));
            _route = null;
            _nextIndex = 0;
            _currentSpeed = 0f;
            _nextCellReserved = false;
            _recovering = false;
            _stopAtNextSafeCell = false;
            _trafficWaitSeconds = 0f;
            _lastReachedCell = reentryCell;
            _orderedCell = reentryCell;
            _localPosition = center;
            transform.position = _shipRoot.TransformPoint(_localPosition);
            if (_traffic == null)
            {
                _traffic = _shipRoot.GetComponent<ShipTrafficCoordinator>();
                if (_traffic == null)
                    _traffic = _shipRoot.gameObject.AddComponent<ShipTrafficCoordinator>();
            }
            _traffic.Register(this);
            LastError = null;
        }
    }
}
