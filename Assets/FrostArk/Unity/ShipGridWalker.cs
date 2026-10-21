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

        public bool TryMoveTo(ShipGridCell goal)
        {
            if (!IsConfigured)
            {
                LastError = "NotConfigured";
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
            // If the actor is physically between discrete cell centers, a
            // fresh path beginning at _lastReachedCell could diagonally cut
            // through an obstacle. Stop without teleporting; the scene can
            // issue a new validated order after reaching a safe restart pose.
            if (IsMoving && (!TryCenter(_lastReachedCell, out var reachedCenter) ||
                (_localPosition - reachedCenter).sqrMagnitude > 0.0001f))
            {
                _map = updated;
                _route = null;
                _nextIndex = 0;
                LastError = "RouteInvalidated";
                _currentSpeed = 0f;
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

            if (IsMoving)
            {
                var nextCell = _route[_nextIndex];
                if (!_map.Walkable(nextCell))
                {
                    _route = null;
                    _nextIndex = 0;
                    LastError = "RouteInvalidated";
                    _currentSpeed = 0f;
                }
                else if (TryCenter(nextCell, out var nextPoint))
                {
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
                        _nextIndex++;
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
    }
}
