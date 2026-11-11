using System;
using FrostArk.Core.Ship;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Authored per-room physical floor. The first raycast collider decides
    /// which room a right-click targets. Converts a world hit into one
    /// ship-root-local cell; never guesses the room from screen X/Z alone.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class ShipRoomClickSurface : MonoBehaviour
    {
        [SerializeField] private string roomId = "room:ship";
        [SerializeField] private Transform shipRoot;
        [SerializeField] private Vector3 roomOriginInShip;

        public EntityId RoomId =>
            string.IsNullOrEmpty(roomId) ? default : EntityId.Parse(roomId);

        public void Configure(EntityId id, Transform root, Vector3 localOrigin)
        {
            if (!id.IsValid) throw new ArgumentException("Valid room ID required.", nameof(id));
            if (root == null) throw new ArgumentNullException(nameof(root));
            roomId = id.Value;
            shipRoot = root;
            roomOriginInShip = localOrigin;
        }

        public bool TryResolve(Vector3 hitWorldPoint, ShipSpatialMap map,
            out ShipGridCell destination)
        {
            destination = default;
            if (map == null || shipRoot == null || !RoomId.IsValid ||
                !gameObject.activeInHierarchy || !enabled)
                return false;
            var local = shipRoot.InverseTransformPoint(hitWorldPoint) - roomOriginInShip;
            if (float.IsNaN(local.x) || float.IsInfinity(local.x) ||
                float.IsNaN(local.z) || float.IsInfinity(local.z))
                return false;
            // A raycast may hit a vertical side of a floor slab. The resulting
            // cell is still bounded by the same canonical authored room.
            var cell = new ShipGridCell(RoomId,
                Mathf.FloorToInt(local.x), Mathf.FloorToInt(local.z));
            if (!map.Walkable(cell)) return false;
            destination = cell;
            return true;
        }
    }
}
