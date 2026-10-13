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
    /// tactical order intent generation, and actor locomotion/interaction execution.
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
        private TacticalSelectionController _selectionController = new TacticalSelectionController();
        private WorldSession _session;

        public TacticalSelectionController Selection => _selectionController ?? (_selectionController = new TacticalSelectionController());
        public IReadOnlyList<TacticalActorProxy> Actors => _registeredActors;
        public IReadOnlyList<TacticalInteractableProxy> Interactables => _registeredInteractables;
        public TacticalOrderIntent LastIssuedIntent { get; private set; }

        public event Action<TacticalOrderIntent> OrderIssued;

        private void Awake()
        {
            if (tacticalCamera == null)
                tacticalCamera = Camera.main;

            if (_selectionController == null)
                _selectionController = new TacticalSelectionController();
            _selectionController.SelectionChanged -= OnSelectionChanged;
            _selectionController.SelectionChanged += OnSelectionChanged;

            // Automatically find scene actors and interactables
            RefreshSceneRegistrations();
        }

        [Inject]
        public void Construct(WorldSession session)
        {
            _session = session;
        }

        public void SetSession(WorldSession session)
        {
            _session = session;
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
            _registeredActors.AddRange(FindObjectsByType<TacticalActorProxy>(FindObjectsSortMode.None));

            _registeredInteractables.Clear();
            var foundInteractables = FindObjectsByType<TacticalInteractableProxy>(FindObjectsSortMode.None);
            foreach (var interactable in foundInteractables)
            {
                RegisterInteractable(interactable);
            }

            SyncActorSelectionVisuals();
        }

        public void RegisterActor(TacticalActorProxy actor)
        {
            if (actor != null && !_registeredActors.Contains(actor))
            {
                _registeredActors.Add(actor);
                actor.SetSelected(_selectionController.IsSelected(actor.ActorId));
            }
        }

        public void UnregisterActor(TacticalActorProxy actor)
        {
            if (actor != null)
            {
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

            var intent = TacticalOrderIntent.MoveTo(
                _selectionController.SelectedActorIds,
                worldDestination.x,
                worldDestination.z);

            LastIssuedIntent = intent;
            OrderIssued?.Invoke(intent);

            if (destinationMarker != null)
                destinationMarker.Show(worldDestination);

            var selectedActors = GetSelectedActorProxies();
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

                actor.MoveTo(target);
            }

            return true;
        }

        public bool IssueInteractOrder(TacticalInteractableProxy interactable)
        {
            if (interactable == null || interactable.IsDepleted || _selectionController.Count == 0)
                return false;

            var intent = TacticalOrderIntent.InteractWith(
                _selectionController.SelectedActorIds,
                interactable.TargetId);

            LastIssuedIntent = intent;
            OrderIssued?.Invoke(intent);

            if (destinationMarker != null)
                destinationMarker.Show(interactable.transform.position);

            var selectedActors = GetSelectedActorProxies();
            if (selectedActors.Count > 0)
            {
                // Primary ordered actor approaches and performs contextual interaction
                selectedActors[0].ApproachAndInteract(interactable);
            }

            return true;
        }

        public bool ProcessPointerClick(Vector2 screenPoint, bool isAdditive = false)
        {
            if (tacticalCamera == null)
                tacticalCamera = Camera.main;
            if (tacticalCamera == null)
                return false;

            Ray ray = tacticalCamera.ScreenPointToRay(screenPoint);

            // 1. Check for clicking an actor
            var actorHits = Physics.RaycastAll(ray, 100f);
            foreach (var hit in actorHits.OrderBy(h => h.distance))
            {
                var actor = hit.collider.GetComponentInParent<TacticalActorProxy>();
                if (actor != null)
                {
                    SelectActor(actor.ActorId, isAdditive);
                    return true;
                }
            }

            // 2. Check for clicking an interactable (if actors are selected)
            if (_selectionController.Count > 0)
            {
                foreach (var hit in actorHits.OrderBy(h => h.distance))
                {
                    var interactable = hit.collider.GetComponentInParent<TacticalInteractableProxy>();
                    if (interactable != null && !interactable.IsDepleted)
                    {
                        return IssueInteractOrder(interactable);
                    }
                }
            }

            // 3. Check for clicking ground
            if (Physics.Raycast(ray, out RaycastHit groundHit, 100f, groundLayer))
            {
                if (_selectionController.Count > 0)
                {
                    return IssueMoveOrder(groundHit.point);
                }
                else
                {
                    // Clicked empty ground with no selection: clear
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

        private void HandleInteractableTriggered(TacticalActorProxy actor, TacticalInteractableProxy interactable)
        {
            Debug.Log($"[TacticalCommandController] Actor {actor.ActorId} interacted with {interactable.TargetId}");

            // Submit authoritative command to WorldSession if present
            if (_session != null)
            {
                // Authoritative withdrawal or salvage event
                var cmd = new WithdrawSupplyCommand(
                    EntityId.Parse("cmd:interaction"),
                    interactable.TargetId,
                    1,
                    _session.Revision);
                // Submitting command through session authority
                _session.Submit(cmd);
            }
        }

        private void Update()
        {
            // Mouse click input support in PlayMode
            if (Input.GetMouseButtonDown(0))
            {
                bool isShift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                ProcessPointerClick(Input.mousePosition, isShift);
            }
        }

        private void OnDestroy()
        {
            if (_selectionController != null)
            {
                _selectionController.SelectionChanged -= OnSelectionChanged;
            }
            foreach (var interactable in _registeredInteractables)
            {
                if (interactable != null)
                    interactable.OnInteracted -= HandleInteractableTriggered;
            }
        }
    }
}
