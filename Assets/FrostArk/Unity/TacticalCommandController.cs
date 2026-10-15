using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.World;
using UnityEngine;
using VContainer;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Tactical commander coordinating pointer interaction, selection state,
    /// tactical order intent generation, actor locomotion/interaction execution,
    /// and contextual hostile combat targeting.
    /// Bridges contextual scene interactions with authoritative WorldSession commands.
    /// </summary>
    public class TacticalCommandController : MonoBehaviour
    {
        [SerializeField]
        private Camera tacticalCamera;

        [SerializeField]
        private LayerMask groundLayer = ~0;

        [SerializeField]
        private TacticalDestinationMarker destinationMarker;

        private readonly List<TacticalActorProxy> _registeredActors = new List<TacticalActorProxy>();
        private readonly List<TacticalInteractableProxy> _registeredInteractables = new List<TacticalInteractableProxy>();
        private readonly List<TacticalCombatTargetProxy> _registeredTargets = new List<TacticalCombatTargetProxy>();
        private TacticalSelectionController _selectionController = new TacticalSelectionController();
        private WorldSession _session;

        public TacticalSelectionController Selection => _selectionController ?? (_selectionController = new TacticalSelectionController());
        public IReadOnlyList<TacticalActorProxy> Actors => _registeredActors;
        public IReadOnlyList<TacticalInteractableProxy> Interactables => _registeredInteractables;
        public IReadOnlyList<TacticalCombatTargetProxy> Targets => _registeredTargets;
        public TacticalOrderIntent LastIssuedIntent { get; private set; }

        public event Action<TacticalOrderIntent> OrderIssued;
        public event Action<TacticalActorProxy, TacticalCombatTargetProxy> ShotFired;
        public event Action<TacticalActorProxy, TacticalCombatTargetProxy, int> TargetHit;
        public event Action<TacticalActorProxy, TacticalInteractableProxy> InteractionCompleted;
        public event Action<TacticalActorProxy, TacticalInteractableProxy, string> InteractionFailed;

        private void Awake()
        {
            if (tacticalCamera == null)
                tacticalCamera = Camera.main;

            if (_selectionController == null)
                _selectionController = new TacticalSelectionController();
            _selectionController.SelectionChanged -= OnSelectionChanged;
            _selectionController.SelectionChanged += OnSelectionChanged;

            // Automatically find scene actors, interactables, and targets
            RefreshSceneRegistrations();
        }

        [Inject]
        public void Construct(WorldSession session)
        {
            SetSession(session);
        }

        public void SetSession(WorldSession session)
        {
            _session = session;
            foreach (var actor in _registeredActors)
            {
                if (actor != null)
                    actor.SetSession(_session);
            }
            SyncPersistedSalvageVisuals();
            SyncPersistedTargetVisuals();
        }

        private void SyncPersistedSalvageVisuals()
        {
            if (_session == null) return;
            var inventory = _session.Capture().State.Inventory;
            foreach (var target in _registeredInteractables)
                if (target != null)
                    target.SetDepleted(inventory.IsCollected(target.TargetId));
        }

        private void SyncPersistedTargetVisuals()
        {
            if (_session == null) return;
            var crew = _session.Capture().State.CrewDuty;
            foreach (var target in _registeredTargets)
            {
                if (target != null)
                {
                    var member = crew.Get(target.TargetId);
                    if (member != null)
                        target.SyncFromWounds(member.Wounds);
                }
            }
        }

        public void SetDestinationMarker(TacticalDestinationMarker marker)
        {
            destinationMarker = marker;
        }

        public void SetCamera(Camera cam)
        {
            tacticalCamera = cam;
        }

        public void RefreshSceneRegistrations()
        {
            _registeredActors.Clear();
            var foundActors = FindObjectsByType<TacticalActorProxy>();
            foreach (var actor in foundActors)
            {
                RegisterActor(actor);
            }

            _registeredInteractables.Clear();
            var foundInteractables = FindObjectsByType<TacticalInteractableProxy>();
            foreach (var interactable in foundInteractables)
            {
                RegisterInteractable(interactable);
            }

            _registeredTargets.Clear();
            var foundTargets = FindObjectsByType<TacticalCombatTargetProxy>();
            foreach (var target in foundTargets)
            {
                RegisterCombatTarget(target);
            }

            SyncActorSelectionVisuals();
        }

        public void RegisterActor(TacticalActorProxy actor)
        {
            if (actor != null && !_registeredActors.Contains(actor))
            {
                _registeredActors.Add(actor);
                actor.SetSession(_session);
                actor.OnShotFired -= HandleActorShotFired;
                actor.OnShotFired += HandleActorShotFired;
                actor.OnTargetHit -= HandleActorTargetHit;
                actor.OnTargetHit += HandleActorTargetHit;
                actor.SetSelected(_selectionController.IsSelected(actor.ActorId));
            }
        }

        public void UnregisterActor(TacticalActorProxy actor)
        {
            if (actor != null)
            {
                actor.OnShotFired -= HandleActorShotFired;
                actor.OnTargetHit -= HandleActorTargetHit;
                _registeredActors.Remove(actor);
                _selectionController.Exclude(actor.ActorId);
            }
        }

        public void RegisterInteractable(TacticalInteractableProxy interactable)
        {
            if (interactable != null && !_registeredInteractables.Contains(interactable))
            {
                _registeredInteractables.Add(interactable);
                interactable.OnInteracted -= HandleInteractableTriggered;
                interactable.OnInteracted += HandleInteractableTriggered;
                if (_session != null)
                    interactable.SetDepleted(
                        _session.Capture().State.Inventory.IsCollected(interactable.TargetId));
            }
        }

        public void RegisterCombatTarget(TacticalCombatTargetProxy target)
        {
            if (target != null && !_registeredTargets.Contains(target))
            {
                _registeredTargets.Add(target);
                if (_session != null)
                {
                    var member = _session.Capture().State.CrewDuty.Get(target.TargetId);
                    if (member != null)
                        target.SyncFromWounds(member.Wounds);
                }
            }
        }

        public void SelectActor(EntityId actorId, bool addToSelection = false)
        {
            if (addToSelection)
                _selectionController.Toggle(actorId);
            else
                _selectionController.Select(actorId);
        }

        public void ClearSelection()
        {
            _selectionController.Clear();
        }

        public bool IssueMoveOrder(Vector3 worldDestination)
        {
            if (_selectionController.Count == 0)
                return false;

            var selectedActors = GetSelectedActorProxies();
            if (selectedActors.Count == 0)
                return false;

            bool anyDispatched = false;
            for (int i = 0; i < selectedActors.Count; i++)
            {
                var actor = selectedActors[i];
                Vector3 target = worldDestination;

                // Subtle offset spread if multiple actors ordered
                if (selectedActors.Count > 1)
                {
                    float angle = (i * 360f / selectedActors.Count) * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.2f;
                    target += offset;
                }

                if (actor.MoveTo(target))
                {
                    anyDispatched = true;
                }
            }

            if (!anyDispatched)
            {
                return false;
            }

            var intent = TacticalOrderIntent.MoveTo(
                _selectionController.SelectedActorIds,
                worldDestination.x,
                worldDestination.z);

            LastIssuedIntent = intent;
            OrderIssued?.Invoke(intent);

            if (destinationMarker != null)
                destinationMarker.Show(worldDestination);

            return true;
        }

        public bool IssueInteractOrder(TacticalInteractableProxy interactable)
        {
            if (interactable == null || interactable.IsDepleted || _selectionController.Count == 0)
                return false;

            var selectedActors = GetSelectedActorProxies();
            if (selectedActors.Count == 0)
                return false;

            bool dispatched = selectedActors[0].ApproachAndInteract(interactable);
            if (!dispatched)
                return false;

            var intent = TacticalOrderIntent.InteractWith(
                _selectionController.SelectedActorIds,
                interactable.TargetId);

            LastIssuedIntent = intent;
            OrderIssued?.Invoke(intent);

            if (destinationMarker != null)
                destinationMarker.Show(interactable.transform.position);

            return true;
        }

        public bool IssueAttackOrder(TacticalCombatTargetProxy target)
        {
            if (target == null || target.IsDead || !target.IsHostile || _selectionController.Count == 0)
                return false;

            var selectedActors = GetSelectedActorProxies();
            if (selectedActors.Count == 0)
                return false;

            bool anyDispatched = false;
            foreach (var actor in selectedActors)
            {
                if (actor.AttackTarget(target, _session))
                    anyDispatched = true;
            }

            if (!anyDispatched)
                return false;

            var intent = TacticalOrderIntent.AttackTarget(
                _selectionController.SelectedActorIds,
                target.TargetId);

            LastIssuedIntent = intent;
            OrderIssued?.Invoke(intent);

            if (destinationMarker != null)
                destinationMarker.Show(target.transform.position);

            return true;
        }

        public bool IssueRetreatOrder(Vector3 retreatDestination)
        {
            if (_selectionController.Count == 0)
                return false;

            var selectedActors = GetSelectedActorProxies();
            if (selectedActors.Count == 0)
                return false;

            bool anyDispatched = false;
            for (int i = 0; i < selectedActors.Count; i++)
            {
                var actor = selectedActors[i];
                Vector3 target = retreatDestination;

                if (selectedActors.Count > 1)
                {
                    float angle = (i * 360f / selectedActors.Count) * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.0f;
                    target += offset;
                }

                if (actor.RetreatTo(target))
                {
                    anyDispatched = true;
                }
            }

            if (!anyDispatched)
                return false;

            var intent = TacticalOrderIntent.RetreatTo(
                _selectionController.SelectedActorIds,
                retreatDestination.x,
                retreatDestination.z);

            LastIssuedIntent = intent;
            OrderIssued?.Invoke(intent);

            if (destinationMarker != null)
                destinationMarker.Show(retreatDestination);

            return true;
        }

        public bool IssueReloadOrder()
        {
            if (_selectionController.Count == 0)
                return false;

            var selectedActors = GetSelectedActorProxies();
            if (selectedActors.Count == 0)
                return false;

            bool anyReloaded = false;
            foreach (var actor in selectedActors)
            {
                if (actor.TryExecuteReload())
                    anyReloaded = true;
            }

            return anyReloaded;
        }

        public bool ProcessPointerClick(Vector2 screenPoint, bool isAdditive = false)
        {
            if (tacticalCamera == null)
                tacticalCamera = Camera.main;
            if (tacticalCamera == null)
                return false;

            Ray ray = tacticalCamera.ScreenPointToRay(screenPoint);
            var hits = Physics.RaycastAll(ray, 100f);
            var sortedHits = hits.OrderBy(h => h.distance).ToArray();

            // 1. If actors are selected, check for clicking an attackable hostile target first
            if (_selectionController.Count > 0)
            {
                foreach (var hit in sortedHits)
                {
                    var target = hit.collider.GetComponentInParent<TacticalCombatTargetProxy>();
                    if (target != null && target.IsHostile && !target.IsDead)
                    {
                        return IssueAttackOrder(target);
                    }
                }
            }

            // 2. Check for clicking a friendly actor (selection)
            foreach (var hit in sortedHits)
            {
                var actor = hit.collider.GetComponentInParent<TacticalActorProxy>();
                if (actor != null)
                {
                    SelectActor(actor.ActorId, isAdditive);
                    return true;
                }
            }

            // 3. Check for clicking an interactable (if actors are selected)
            if (_selectionController.Count > 0)
            {
                foreach (var hit in sortedHits)
                {
                    var interactable = hit.collider.GetComponentInParent<TacticalInteractableProxy>();
                    if (interactable != null && !interactable.IsDepleted)
                    {
                        return IssueInteractOrder(interactable);
                    }
                }
            }

            // 4. Check for clicking ground
            if (Physics.Raycast(ray, out RaycastHit groundHit, 100f, groundLayer))
            {
                if (_selectionController.Count > 0)
                {
                    return IssueMoveOrder(groundHit.point);
                }
                else
                {
                    ClearSelection();
                }
            }

            return false;
        }

        private List<TacticalActorProxy> GetSelectedActorProxies()
        {
            var result = new List<TacticalActorProxy>();
            foreach (var id in _selectionController.SelectedActorIds)
            {
                var actor = _registeredActors.FirstOrDefault(a => a.ActorId == id);
                if (actor != null)
                    result.Add(actor);
            }
            return result;
        }

        private void OnSelectionChanged(TacticalSelection selection)
        {
            SyncActorSelectionVisuals();
        }

        private void SyncActorSelectionVisuals()
        {
            foreach (var actor in _registeredActors)
            {
                if (actor != null)
                {
                    actor.SetSelected(_selectionController.IsSelected(actor.ActorId));
                }
            }
        }

        private void HandleActorShotFired(TacticalActorProxy actor, TacticalCombatTargetProxy target)
        {
            ShotFired?.Invoke(actor, target);
        }

        private void HandleActorTargetHit(TacticalActorProxy actor, TacticalCombatTargetProxy target, int damage)
        {
            TargetHit?.Invoke(actor, target, damage);
        }

        private void HandleInteractableTriggered(TacticalActorProxy actor, TacticalInteractableProxy interactable)
        {
            if (interactable == null || interactable.IsDepleted)
                return;

            Debug.Log($"[TacticalCommandController] Actor {actor.ActorId} triggered interaction with {interactable.TargetId}");

            if (_session != null)
            {
                var commandId = EntityId.Parse($"cmd:salvage:{interactable.TargetId}:{Guid.NewGuid():N}");
                var cmd = new CollectSalvageCommand(
                    commandId,
                    interactable.TargetId,
                    _session.Revision);

                var result = _session.Submit(cmd);
                if (result.Success)
                {
                    Debug.Log($"[TacticalCommandController] CollectSalvageCommand committed successfully for {interactable.TargetId}. Revision: {_session.Revision}");
                    interactable.SetDepleted(true);
                    InteractionCompleted?.Invoke(actor, interactable);
                }
                else
                {
                    Debug.LogWarning($"[TacticalCommandController] CollectSalvageCommand rejected: {result.Failure}");
                    InteractionFailed?.Invoke(actor, interactable, result.Failure.ToString());
                }
            }
            else
            {
                Debug.LogWarning($"[TacticalCommandController] Interaction rejected for {interactable.TargetId}: No authoritative WorldSession is bound.");
                InteractionFailed?.Invoke(actor, interactable, "NoSessionBound");
            }
        }

        private void Update()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                var keyboard = UnityEngine.InputSystem.Keyboard.current;
                bool isShift = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
                Vector2 mousePos = mouse.position.ReadValue();
                ProcessPointerClick(mousePos, isShift);
            }
        }

        private void OnDestroy()
        {
            if (_selectionController != null)
            {
                _selectionController.SelectionChanged -= OnSelectionChanged;
            }
            foreach (var actor in _registeredActors)
            {
                if (actor != null)
                {
                    actor.OnShotFired -= HandleActorShotFired;
                    actor.OnTargetHit -= HandleActorTargetHit;
                }
            }
            foreach (var interactable in _registeredInteractables)
            {
                if (interactable != null)
                    interactable.OnInteracted -= HandleInteractableTriggered;
            }
        }
    }
}
