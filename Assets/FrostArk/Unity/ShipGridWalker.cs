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
            _nextIndex = path.Count > 1 ? 1 : path.Count;
            LastError = null;
            return true;
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
            var next = IsMoving ? updated.FindPath(_lastReachedCell, _orderedCell) :
                Array.Empty<ShipGridCell>();
            if (IsMoving && (next.Count == 0 || !RouteOriginsValid(next)))
            {
                _map = updated;
                _route = null;
                _nextIndex = 0;
                LastError = "RouteInvalidated";
                return false;
            }
            _map = updated;
            if (IsMoving)
            {
                _route = next;
                _nextIndex = next.Count > 1 ? 1 : next.Count;
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
                }
                else if (TryCenter(nextCell, out var nextPoint))
                {
                    var before = _localPosition;
                    _localPosition = Vector3.MoveTowards(before, nextPoint, _speed * deltaTime);
                    var direction = _localPosition - before;
                    if (direction.sqrMagnitude > 0.000001f)
                    {
                        var desired = Quaternion.LookRotation(
                            _shipRoot.TransformDirection(direction.normalized), _shipRoot.up);
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            desired, 540f * deltaTime);
                    }
                    if ((_localPosition - nextPoint).sqrMagnitude <= 0.000001f)
                    {
                        _localPosition = nextPoint;
                        _lastReachedCell = nextCell;
                        _nextIndex++;
                        if (!IsMoving) _route = null;
                    }
                }
                else
                {
                    _route = null;
                    _nextIndex = 0;
                    LastError = "MissingRoomOrigin";
                }
            }
            transform.position = _shipRoot.TransformPoint(_localPosition);
        }
    }
}
