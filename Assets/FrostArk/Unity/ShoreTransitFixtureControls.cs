using System;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Fixture-only IMGUI probe, deliberately attached only to the generated
    /// ship/shore transition test launcher. Production HUD is separate.
    /// </summary>
    public sealed class ShoreTransitFixtureControls : MonoBehaviour
    {
        [SerializeField] private ShoreSceneTransitCoordinator transit;
        [SerializeField] private SceneTransitDestination shore;
        [SerializeField] private SceneTransitDestination ship;

        public void Configure(ShoreSceneTransitCoordinator coordinator,
            SceneTransitDestination shoreDestination, SceneTransitDestination shipDestination)
        {
            transit = coordinator;
            shore = shoreDestination;
            ship = shipDestination;
        }

        private void OnGUI()
        {
            if (transit == null)
                return;

            GUILayout.BeginArea(new Rect(10, 10, 270, 135), GUI.skin.box);
            GUILayout.Label("FrostArk | scene transition fixture");
            GUILayout.Label("Click a character before departing.");

            GUI.enabled = !transit.IsBusy;
            if (GUILayout.Button("Depart to shore (selected squad)"))
                transit.TryDepartSelected(shore, UniqueOperationId("depart"));
            if (GUILayout.Button("Return to ship"))
                transit.TryReturn(ship, UniqueOperationId("return"));
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(transit.LastError))
                GUILayout.Label("Rejected: " + transit.LastError);
            GUILayout.EndArea();
        }

        private static EntityId UniqueOperationId(string action) =>
            EntityId.Parse("transition:fixture:" + action + ":" + Guid.NewGuid().ToString("N"));
    }
}
