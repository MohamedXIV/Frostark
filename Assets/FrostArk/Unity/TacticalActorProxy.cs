using System;
using FrostArk.Core.World;
using UnityEngine;
using UnityEngine.AI;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Scene proxy for a commandable on-foot character (protagonist or crew).
    /// Manages Unity NavMesh locomotion, selection visuals, and interaction approach.
    /// Does not store authoritative character health, inventory, or skills.
    /// </summary>
    [SelectionBase]
    [RequireComponent(typeof(NavMeshAgent))]
    public class TacticalActorProxy : MonoBehaviour
    {
        [SerializeField]
        private string actorId = "actor:protagonist";

        [SerializeField]
        private GameObject selectionMarker;

        private NavMeshAgent _agent;
        private TacticalInteractableProxy _pendingInteractable;
        private EntityId _actorIdEntity;

        public EntityId ActorId
        {
            get
            {
                if (!_actorIdEntity.IsValid)
                {
                    if (string.IsNullOrEmpty(actorId))
                        actorId = "actor:" + gameObject.name;
                    _actorIdEntity = EntityId.Parse(actorId);
                }
                return _actorIdEntity;
            }
            private set => _actorIdEntity = value;
        }

        public bool IsSelected { get; private set; }
        public NavMeshAgent Agent => _agent != null ? _agent : (_agent = GetComponent<NavMeshAgent>());
        public TacticalInteractableProxy PendingInteraction => _pendingInteractable;

        public bool IsMoving
        {
            get
            {
                if (_agent == null || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh)
                    return false;
                return _agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance + 0.1f;
            }
        }

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            var _ = ActorId;
            SetSelected(false);
        }

        public void SetActorId(EntityId id)
        {
            _actorIdEntity = id;
            actorId = id.ToString();
        }

        public void SetSelected(bool selected)
        {
            IsSelected = selected;
            if (selectionMarker != null)
                selectionMarker.SetActive(selected);
        }

        public void SetSelectionMarker(GameObject marker)
        {
            selectionMarker = marker;
            if (selectionMarker != null)
                selectionMarker.SetActive(IsSelected);
        }

        public bool MoveTo(Vector3 destination)
        {
            CancelPendingInteraction();

            if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
            {
                Agent.isStopped = false;
                Agent.stoppingDistance = 0.1f;
                return Agent.SetDestination(destination);
            }

            return false;
        }

        public bool ApproachAndInteract(TacticalInteractableProxy interactable)
        {
            if (interactable == null || interactable.IsDepleted)
                return false;

            _pendingInteractable = interactable;

            if (interactable.CanInteract(this))
            {
                ExecutePendingInteraction();
                return true;
            }

            if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
            {
                Agent.isStopped = false;
                Agent.stoppingDistance = Mathf.Max(0.5f, interactable.InteractionRange * 0.8f);
                return Agent.SetDestination(interactable.transform.position);
            }

            return false;
        }

        public void CancelPendingInteraction()
        {
            _pendingInteractable = null;
            if (Agent != null && Agent.isActiveAndEnabled)
            {
                Agent.stoppingDistance = 0.1f;
            }
        }

        private void Update()
        {
            if (_pendingInteractable == null)
                return;

            if (_pendingInteractable.IsDepleted)
            {
                CancelPendingInteraction();
                return;
            }

            if (_pendingInteractable.CanInteract(this))
            {
                ExecutePendingInteraction();
            }
        }

        private void ExecutePendingInteraction()
        {
            if (_pendingInteractable != null)
            {
                var target = _pendingInteractable;
                _pendingInteractable = null;

                if (Agent != null && Agent.isActiveAndEnabled && Agent.isOnNavMesh)
                {
                    Agent.isStopped = true;
                    Agent.ResetPath();
                    Agent.stoppingDistance = 0.1f;
                }

                target.Interact(this);
            }
        }
    }
}
