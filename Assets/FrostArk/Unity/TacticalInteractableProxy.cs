using System;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Scene proxy for an interactable world object (e.g. salvage container, console, hatch).
    /// Does not own inventory totals or simulation clocks directly; provides interaction
    /// range checking and dispatches to authoritative command handlers.
    /// </summary>
    [SelectionBase]
    public class TacticalInteractableProxy : MonoBehaviour
    {
        [SerializeField]
        private string targetId = "salvage:crate_01";

        [SerializeField]
        private float interactionRange = 2.5f;

        [SerializeField]
        private GameObject activeVisual;

        [SerializeField]
        private GameObject depletedVisual;

        private EntityId _targetIdEntity;

        public EntityId TargetId
        {
            get
            {
                if (!_targetIdEntity.IsValid)
                {
                    if (string.IsNullOrEmpty(targetId))
                        targetId = "salvage:" + gameObject.name;
                    _targetIdEntity = EntityId.Parse(targetId);
                }
                return _targetIdEntity;
            }
            private set => _targetIdEntity = value;
        }
        public float InteractionRange => interactionRange;
        public bool IsDepleted { get; private set; }

        public event Action<TacticalActorProxy, TacticalInteractableProxy> OnInteracted;

        private void Awake()
        {
            var _ = TargetId;
            UpdateVisuals();
        }

        public void SetTargetId(EntityId id)
        {
            _targetIdEntity = id;
            targetId = id.ToString();
        }

        public void SetInteractionRange(float range)
        {
            interactionRange = Mathf.Max(0.5f, range);
        }

        public bool CanInteract(TacticalActorProxy actor)
        {
            if (IsDepleted || actor == null)
                return false;

            float dist = Vector3.Distance(transform.position, actor.transform.position);
            return dist <= interactionRange + 0.5f;
        }

        public bool Interact(TacticalActorProxy actor)
        {
            if (!CanInteract(actor))
                return false;

            IsDepleted = true;
            UpdateVisuals();
            OnInteracted?.Invoke(actor, this);
            return true;
        }

        public void ResetInteractable()
        {
            IsDepleted = false;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            if (activeVisual != null)
                activeVisual.SetActive(!IsDepleted);
            if (depletedVisual != null)
                depletedVisual.SetActive(IsDepleted);
        }
    }
}
