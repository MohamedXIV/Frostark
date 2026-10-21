using System;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Selectable physical crew proxy inside the ship. Unlike shore combat
    /// actors this is NOT a NavMeshAgent and carries no shadow crew state.
    /// Stable identity is taken from the same canonical WorldState roster.
    /// </summary>
    [SelectionBase]
    public sealed class ShipCrewSelectionProxy : MonoBehaviour
    {
        [SerializeField] private string actorId = "crew:ship:worker";
        [SerializeField] private GameObject selectionMarker;
        private EntityId _id;
        private EntityId _sceneId;

        public EntityId SceneId => _sceneId;

        /// <summary>Configured by the authoritative ship scene bootstrap.</summary>
        public void BindScene(EntityId logicalSceneId)
        {
            if (!logicalSceneId.IsValid)
                throw new ArgumentException("Valid logical scene required.", nameof(logicalSceneId));
            _sceneId = logicalSceneId;
        }

        public EntityId ActorId
        {
            get
            {
                if (!_id.IsValid && !string.IsNullOrEmpty(actorId))
                    _id = EntityId.Parse(actorId);
                return _id;
            }
        }

        public ShipGridWalker Walker => GetComponent<ShipGridWalker>();

        public void SetActorId(EntityId id)
        {
            if (!id.IsValid) throw new ArgumentException("Crew ID required.", nameof(id));
            _id = id;
            actorId = id.Value;
        }

        public void SetSelectionMarker(GameObject marker)
        {
            selectionMarker = marker;
            SyncMarker();
        }

        public bool IsSelected { get; private set; }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            SyncMarker();
        }

        private void SyncMarker()
        {
            if (selectionMarker != null && selectionMarker != gameObject)
                selectionMarker.SetActive(IsSelected);
        }
    }
}
