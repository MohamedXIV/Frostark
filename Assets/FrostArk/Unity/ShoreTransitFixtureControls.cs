using System;
using System.Linq;
using FrostArk.Core.Save;
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
        [SerializeField] private TacticalCommandController commander;

        private Rect _hudRect = new Rect(10, 10, 320, 190);

        public Rect HudRect => _hudRect;

        public void Configure(ShoreSceneTransitCoordinator coordinator,
            SceneTransitDestination shoreDestination, SceneTransitDestination shipDestination,
            TacticalCommandController commandController = null)
        {
            transit = coordinator;
            shore = shoreDestination;
            ship = shipDestination;
            if (commandController != null)
                commander = commandController;
            EnsureCommanderBinding();
        }

        private void Awake()
        {
            EnsureCommanderBinding();
        }

        private void OnEnable()
        {
            EnsureCommanderBinding();
        }

        private void OnDisable()
        {
            if (commander != null)
                commander.SetPointerOverlayBlocker(null);
        }

        private void EnsureCommanderBinding()
        {
            if (commander == null)
                commander = GetComponentInChildren<TacticalCommandController>() ??
                            FindAnyObjectByType<TacticalCommandController>();
            if (commander != null)
                commander.SetPointerOverlayBlocker(IsPointerOverHud);
            if (transit == null)
                transit = GetComponentInChildren<ShoreSceneTransitCoordinator>() ??
                          FindAnyObjectByType<ShoreSceneTransitCoordinator>();
        }

        /// <summary>
        /// Input System mouse coordinates use bottom-left origin; legacy IMGUI uses top-left.
        /// Blocks world raycasts and pointer deselect when clicking fixture controls.
        /// </summary>
        public bool IsPointerOverHud(Vector2 screenPoint)
        {
            var topLeft = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return _hudRect.Contains(topLeft);
        }

        private void OnGUI()
        {
            if (transit == null)
                return;

            EnsureCommanderBinding();

            GUILayout.BeginArea(_hudRect, GUI.skin.box);
            GUILayout.Label("<b>FrostArk | Scene Transition Fixture</b>");

            int selectedCount = commander != null ? commander.Selection.Count : 0;
            if (selectedCount > 0)
            {
                var names = string.Join(", ", commander.Selection.SelectedActorIds.Select(id => id.Value));
                GUILayout.Label($"<b>Selected crew ({selectedCount}):</b> {names}");
            }
            else
            {
                GUILayout.Label("<color=#ffaa44>Selected crew: None</color> (click character to select)");
            }

            // Depart button: enabled only when a crew member is selected and transit is idle
            bool canDepart = !transit.IsBusy && selectedCount > 0;
            GUI.enabled = canDepart;
            if (GUILayout.Button("Depart to shore (selected squad)"))
                transit.TryDepartSelected(shore, UniqueOperationId("depart"));
            GUI.enabled = true;

            // Return button: enabled only when ashore and transit is idle
            bool isShoreTheater = transit.Session?.SceneCheckpoint != null &&
                                  transit.Session.SceneCheckpoint.Theater == PhysicalTheater.Shore;
            bool canReturn = !transit.IsBusy && isShoreTheater;
            GUI.enabled = canReturn;
            if (GUILayout.Button("Return to ship"))
                transit.TryReturn(ship, UniqueOperationId("return"));
            GUI.enabled = true;

            // Explanatory status text
            if (transit.IsBusy)
            {
                GUILayout.Label("<color=#88ccff>Transit in progress...</color>");
            }
            else if (selectedCount == 0 && !isShoreTheater)
            {
                GUILayout.Label("<color=#aaaaaa>Tip: Select at least 1 character to depart.</color>");
            }
            else if (!isShoreTheater)
            {
                GUILayout.Label("<color=#aaaaaa>Party is currently on ship.</color>");
            }
            else
            {
                GUILayout.Label("<color=#88ff88>Expedition is ashore.</color>");
            }

            if (!string.IsNullOrEmpty(transit.LastError))
                GUILayout.Label("<color=#ff4444>Rejected: " + transit.LastError + "</color>");

            GUILayout.EndArea();
        }

        private static EntityId UniqueOperationId(string action) =>
            EntityId.Parse("transition:fixture:" + action + ":" + Guid.NewGuid().ToString("N"));
    }
}
