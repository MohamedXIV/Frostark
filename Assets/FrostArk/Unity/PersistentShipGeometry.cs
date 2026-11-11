using System;
using UnityEngine;

namespace FrostArk.Unity
{
    /// <summary>
    /// Authored geometry contract for the one persistent ship prefab. Room and
    /// door bindings are resolved after instantiation; this component owns no
    /// session, crew, camera, simulation state, or runtime-generated objects.
    /// </summary>
    public sealed class PersistentShipGeometry : MonoBehaviour
    {
        [SerializeField] private ShipTacticalSceneBootstrap.RoomBinding[] rooms =
            Array.Empty<ShipTacticalSceneBootstrap.RoomBinding>();
        [SerializeField] private ShipTacticalSceneBootstrap.DoorBinding[] doors =
            Array.Empty<ShipTacticalSceneBootstrap.DoorBinding>();

        public ShipTacticalSceneBootstrap.RoomBinding[] Rooms => rooms;
        public ShipTacticalSceneBootstrap.DoorBinding[] Doors => doors;

        public void Configure(
            ShipTacticalSceneBootstrap.RoomBinding[] roomBindings,
            ShipTacticalSceneBootstrap.DoorBinding[] doorBindings)
        {
            rooms = roomBindings ??
                throw new ArgumentNullException(nameof(roomBindings));
            doors = doorBindings ??
                throw new ArgumentNullException(nameof(doorBindings));
        }
    }
}
