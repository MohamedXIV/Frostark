using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.World;
using UnityEngine;
using UnityEngine.EventSystems;
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
        private readonly Dictionary<EntityId, bool> _lastMoveDispatch =
            new Dictionary<EntityId, bool>();
        /// <summary>
        /// Per-actor acceptance of the latest move DISPATCH (not completion).
        /// False means no executor or a rejected NavMesh destination.
        /// </summary>
        public IReadOnlyDictionary<EntityId, bool> LastMoveDispatch => _lastMoveDispatch;
        [SerializeField, Min(3f)] private float dragThresholdPixels = 12f;
        private const int MaxSelection = 4;
        private bool _selectionPointerHeld;
        private Vector2 _selectionPointerOrigin;

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
            {
                if (_selectionController.IsSelected(actorId) ||
                    _selectionController.Count < MaxSelection)
                    _selectionController.Toggle(actorId);
            }
            else
                _selectionController.Select(actorId);
        }

        public void ClearSelection()
        {
            _selectionController.Clear();
        }

        public bool IssueMoveOrder(Vector3 worldDestination)
        {
            _lastMoveDispatch.Clear();
            foreach (var id in _selectionController.SelectedActorIds)
                _lastMoveDispatch[id] = false;

            var selectedActors = GetSelectedActorProxies();
            if (selectedActors.Count == 0)
                return false;

            var accepted = new List<EntityId>();
            for (int i = 0; i < selectedActors.Count; i++)
            {
                var actor = selectedActors[i];
                Vector3 target = worldDestination;

                // Small group spacing; advanced NavMesh formations are owned by #32.
                if (selectedActors.Count > 1)
                {
                    float angle = (i * 360f / selectedActors.Count) * Mathf.Deg2Rad;
                    Vector3 offset = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * 1.2f;
                    target += offset;
                }

                bool dispatched = actor.MoveTo(target);
                _lastMoveDispatch[actor.ActorId] = dispatched;
                if (dispatched)
                    accepted.Add(actor.ActorId);
            }

            if (accepted.Count == 0)
                return false;

            // Never announce rejected crew members as successfully ordered.
            var intent = TacticalOrderIntent.MoveTo(
                accepted, worldDestination.x, worldDestination.z);
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

        /// <summary>
        /// Primary pointer gesture: select a visible actor, or clear transient
        /// selection on empty space. Never issues Move, Attack or Interact.
        /// Those are explicit context/right-click commands.
        /// </summary>
        public bool ProcessPointerClick(Vector2 screenPoint, bool isAdditive = false)
        {
            if (tacticalCamera == null) tacticalCamera = Camera.main;
            if (tacticalCamera == null) return false;
            var ray = tacticalCamera.ScreenPointToRay(screenPoint);
            var hits = Physics.RaycastAll(ray, 100f, ~0,
                QueryTriggerInteraction.Ignore).OrderBy(h => h.distance);
            // Only the first physical surface counts: no selecting through walls.
            foreach (var hit in hits)
            {
                var actor = hit.collider.GetComponentInParent<TacticalActorProxy>();
                if (actor != null && _registeredActors.Contains(actor))
                {
                    SelectActor(actor.ActorId, isAdditive);
                    return true;
                }
                break;
            }
            if (!isAdditive) ClearSelection();
            return false;
        }

        /// <summary>
        /// Drag rectangle uses camera projections plus an unoccluded collider
        /// ray. Actors outside this scene's registrations cannot be selected.
        /// Selection is ephemeral; no order or duty is cancelled by replacement.
        /// </summary>
        public int ProcessBoxSelection(Vector2 screenStart, Vector2 screenEnd,
            bool isAdditive = false)
        {
            if (tacticalCamera == null) tacticalCamera = Camera.main;
            if (tacticalCamera == null) return 0;
            var rect = Rect.MinMaxRect(
                Mathf.Min(screenStart.x, screenEnd.x),
                Mathf.Min(screenStart.y, screenEnd.y),
                Mathf.Max(screenStart.x, screenEnd.x),
                Mathf.Max(screenStart.y, screenEnd.y));
            var ids = new List<EntityId>();
            if (isAdditive)
                ids.AddRange(_selectionController.SelectedActorIds);
            foreach (var actor in _registeredActors
                .Where(a => a != null)
                .OrderBy(a => a.ActorId.Value, StringComparer.Ordinal))
            {
                if (ids.Count >= MaxSelection) break;
                if (ids.Contains(actor.ActorId)) continue;
                var collider = actor.GetComponent<Collider>();
                if (collider == null || !collider.enabled) continue;
                var center = collider.bounds.center;
                var projected = tacticalCamera.WorldToScreenPoint(center);
                if (projected.z <= 0 || !rect.Contains(new Vector2(projected.x, projected.y)))
                    continue;
                var ray = tacticalCamera.ScreenPointToRay(projected);
                var hits = Physics.RaycastAll(ray, 100f, ~0,
                    QueryTriggerInteraction.Ignore);
                var nearest = hits.OrderBy(h => h.distance).FirstOrDefault();
                if (nearest.collider == null ||
                    nearest.collider.GetComponentInParent<TacticalActorProxy>() != actor)
                    continue;
                ids.Add(actor.ActorId);
            }
            _selectionController.SelectMultiple(ids);
            return _selectionController.Count;
        }

        /// <summary>
        /// Context/right-click dispatch. Actor choice remains on primary click.
        /// The shore executor is the existing NavMesh-based TacticalActorProxy.
        /// </summary>
        public bool ProcessContextClick(Vector2 screenPoint)
        {
            if (_selectionController.Count == 0) return false;
            if (tacticalCamera == null) tacticalCamera = Camera.main;
            if (tacticalCamera == null) return false;
            var ray = tacticalCamera.ScreenPointToRay(screenPoint);
            var hits = Physics.RaycastAll(ray, 100f, ~0,
                QueryTriggerInteraction.Ignore).OrderBy(h => h.distance).ToArray();
            // Context actions operate on the first physical hit only: a target
            // concealed behind a wall is not magically actionable by click.
            if (hits.Length == 0) return false;
            var first = hits[0];
            var target = first.collider.GetComponentInParent<TacticalCombatTargetProxy>();
            if (target != null && target.IsHostile && !target.IsDead)
                return IssueAttackOrder(target);
            var interactable = first.collider.GetComponentInParent<TacticalInteractableProxy>();
            if (interactable != null && !interactable.IsDepleted)
                return IssueInteractOrder(interactable);
            if (first.collider.GetComponentInParent<TacticalActorProxy>() != null)
                return false;
            if (Physics.Raycast(ray, out var groundHit, 100f, groundLayer,
                    QueryTriggerInteraction.Ignore))
                return IssueMoveOrder(groundHit.point);
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
            if (mouse == null) return;
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            bool additive = keyboard != null &&
                (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
            bool pointerOverUi = EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject();
            var point = mouse.position.ReadValue();

            if (mouse.leftButton.wasPressedThisFrame)
            {
                _selectionPointerHeld = !pointerOverUi;
                _selectionPointerOrigin = point;
            }
            if (mouse.leftButton.wasReleasedThisFrame && _selectionPointerHeld)
            {
                if (!pointerOverUi)
                {
                    if ((point - _selectionPointerOrigin).sqrMagnitude >
                        dragThresholdPixels * dragThresholdPixels)
                        ProcessBoxSelection(_selectionPointerOrigin, point, additive);
                    else
                        ProcessPointerClick(point, additive);
                }
                _selectionPointerHeld = false;
            }
            if (mouse.rightButton.wasPressedThisFrame && !pointerOverUi)
                ProcessContextClick(point);
        }

        private void OnGUI()
        {
            if (!_selectionPointerHeld || UnityEngine.InputSystem.Mouse.current == null)
                return;
            var point = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            if ((point - _selectionPointerOrigin).sqrMagnitude <=
                dragThresholdPixels * dragThresholdPixels)
                return;
            var old = GUI.color;
            GUI.color = new Color(0.3f, 0.77f, 1f, 0.48f);
            GUI.Box(Rect.MinMaxRect(
                Mathf.Min(_selectionPointerOrigin.x, point.x),
                Screen.height - Mathf.Max(_selectionPointerOrigin.y, point.y),
                Mathf.Max(_selectionPointerOrigin.x, point.x),
                Screen.height - Mathf.Min(_selectionPointerOrigin.y, point.y)),
                GUIContent.none);
            GUI.color = old;
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
