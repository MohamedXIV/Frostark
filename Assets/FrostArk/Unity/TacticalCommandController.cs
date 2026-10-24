using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
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
        private readonly List<ShipCrewSelectionProxy> _shipActors = new List<ShipCrewSelectionProxy>();
        private ShipSpatialMap _shipSpatialMap;
        private readonly List<TacticalInteractableProxy> _registeredInteractables = new List<TacticalInteractableProxy>();
        private readonly List<TacticalCombatTargetProxy> _registeredTargets = new List<TacticalCombatTargetProxy>();
        private TacticalSelectionController _selectionController = new TacticalSelectionController();
        private bool _selectionEventsBound;
        private WorldSession _session;
        private readonly Dictionary<EntityId, bool> _lastMoveDispatch =
            new Dictionary<EntityId, bool>();
        /// <summary>
        /// Per-actor acceptance of the latest move DISPATCH (not completion).
        /// False means no executor or a rejected NavMesh destination.
        /// </summary>
        public IReadOnlyDictionary<EntityId, bool> LastMoveDispatch => _lastMoveDispatch;
        private readonly Dictionary<EntityId, CrewMoveDispatchDecision> _theaterDispatch =
            new Dictionary<EntityId, CrewMoveDispatchDecision>();
        public IReadOnlyDictionary<EntityId, CrewMoveDispatchDecision> LastTheaterMoveDispatch =>
            _theaterDispatch;
        [SerializeField, Min(3f)] private float dragThresholdPixels = 12f;
        private const int MaxSelection = 4;
        private bool _selectionPointerHeld;
        private Vector2 _selectionPointerOrigin;

        // Optional scene-local IMGUI overlay blocker. EventSystem alone only
        // handles uGUI, not GUI.Button/GUI.Box in an OnGUI() fixture.
        private Func<Vector2, bool> _isPointerOverSceneOverlay;

        /// <summary>
        /// Hook non-EventSystem overlays without coupling this reusable RTS
        /// controller to any specific scene/HUD implementation. Screen points
        /// use bottom-left InputSystem coordinates, just like screen rays.
        /// </summary>
        public void AddPointerOverlayBlocker(Func<Vector2, bool> blocker)
        {
            if (blocker == null) return;
            var prev = _isPointerOverSceneOverlay;
            _isPointerOverSceneOverlay = prev == null ? blocker : pt => prev(pt) || blocker(pt);
        }

        public void SetPointerOverlayBlocker(Func<Vector2, bool> blocker)
        {
            _isPointerOverSceneOverlay = blocker;
        }

        public bool IsPointerBlocked(Vector2 screenPoint)
        {
            return (EventSystem.current != null &&
                    EventSystem.current.IsPointerOverGameObject()) ||
                   (_isPointerOverSceneOverlay?.Invoke(screenPoint) ?? false);
        }

        // An EditMode-created MonoBehaviour may be configured through its public
        // API before Unity invokes Awake. Bind selection notifications when first
        // used as well as in Awake, once per controller instance.
        private void EnsureSelectionBinding()
        {
            if (_selectionController == null)
            {
                _selectionController = new TacticalSelectionController();
                _selectionEventsBound = false;
            }
            if (_selectionEventsBound) return;
            _selectionController.SelectionChanged += OnSelectionChanged;
            _selectionEventsBound = true;
        }

        public TacticalSelectionController Selection
        {
            get
            {
                EnsureSelectionBinding();
                return _selectionController;
            }
        }
        public IReadOnlyList<TacticalActorProxy> Actors => _registeredActors;
        public IReadOnlyList<ShipCrewSelectionProxy> ShipActors => _shipActors;
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

            EnsureSelectionBinding();

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

        /// <summary>
        /// Scene authoring bootstrap supplies a map derived from the canonical
        /// ShipLayoutState and authored portals. Absent map rejects ship clicks.
        /// This is a presentation projection, never a shadow world state.
        /// </summary>
        public void SetShipSpatialMap(ShipSpatialMap map)
        {
            _shipSpatialMap = map ?? throw new ArgumentNullException(nameof(map));
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
            EnsureSelectionBinding();
            _registeredActors.Clear();
            var foundActors = FindObjectsByType<TacticalActorProxy>();
            foreach (var actor in foundActors)
            {
                RegisterActor(actor);
            }

            _shipActors.Clear();
            foreach (var shipActor in FindObjectsByType<ShipCrewSelectionProxy>())
                RegisterShipActor(shipActor);

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
            EnsureSelectionBinding();
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

        public void RegisterShipActor(ShipCrewSelectionProxy actor)
        {
            EnsureSelectionBinding();
            if (actor == null || _shipActors.Contains(actor)) return;
            if (!actor.ActorId.IsValid) return;
            _shipActors.Add(actor);
            actor.SetSelected(_selectionController.IsSelected(actor.ActorId));
        }

        public void UnregisterShipActor(ShipCrewSelectionProxy actor)
        {
            if (actor == null) return;
            _shipActors.Remove(actor);
            // Only remove selection when no other registered scene proxy uses
            // the stable ID; dispatch itself refuses duplicate executors.
            if (!_shipActors.Any(a => a != null && a.ActorId == actor.ActorId) &&
                !_registeredActors.Any(a => a != null && a.ActorId == actor.ActorId))
                _selectionController.Exclude(actor.ActorId);
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
            EnsureSelectionBinding();
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
            EnsureSelectionBinding();
            _selectionController.Clear();
        }

        public bool IssueMoveOrder(Vector3 worldDestination)
        {
            // A ship-local move MUST carry RoomId: world X/Z is not enough.
            if (_session != null && _session.SceneCheckpoint != null)
            {
                if (_session.SceneCheckpoint.Theater == PhysicalTheater.Ship)
                    return false;
                if (_session.SceneCheckpoint.Theater == PhysicalTheater.Shore)
                    return IssueShoreMoveOrder(worldDestination);
            }
            _theaterDispatch.Clear();
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

        /// <summary>
        /// Move selected crew in the loaded ship scene through room-aware A*.
        /// Target cell is resolved from the clicked, authored room floor.
        /// Every accepted actor gets the current physical order; changing
        /// selection does not cancel the walker's already-accepted path.
        /// </summary>
        public bool IssueShipMoveOrder(ShipGridCell cell, Vector3 clickedWorldPoint)
        {
            _theaterDispatch.Clear();
            _lastMoveDispatch.Clear();
            foreach (var id in _selectionController.SelectedActorIds)
                _lastMoveDispatch[id] = false;
            if (_session == null || _session.SceneCheckpoint == null ||
                _shipSpatialMap == null || !_shipSpatialMap.Walkable(cell) ||
                _selectionController.Count == 0)
                return false;

            var activeShipActors = _shipActors
                .Where(actor => actor != null && actor.isActiveAndEnabled &&
                                actor.gameObject.activeInHierarchy && actor.Walker != null)
                .ToArray();
            var executors = activeShipActors
                .Select(actor => (ICrewTheaterMoveExecutor)new ShipGridCrewExecutor(
                    actor.ActorId, actor.SceneId, actor.Walker)).ToArray();

            var goals = TacticalGroupDestinationAdapters.PlanShipGoals(
                _shipSpatialMap, _selectionController.SelectedActorIds, activeShipActors, cell);
            var outcomes = TacticalTheaterSelectionCommands.MoveSelectedAssigned(
                _session, _selectionController, clickedWorldPoint, PhysicalTheater.Ship,
                goals, executors);
            return RecordPhysicalDispatch(outcomes, clickedWorldPoint);
        }

        private bool IssueShoreMoveOrder(Vector3 worldPoint)
        {
            _theaterDispatch.Clear();
            _lastMoveDispatch.Clear();
            foreach (var id in _selectionController.SelectedActorIds)
                _lastMoveDispatch[id] = false;
            if (_session == null || _session.SceneCheckpoint == null ||
                _session.SceneCheckpoint.Theater != PhysicalTheater.Shore ||
                _selectionController.Count == 0)
                return false;
            var scene = _session.SceneCheckpoint.SceneId;
            var executors = _registeredActors
                .Where(actor => actor != null && actor.isActiveAndEnabled &&
                                actor.gameObject.activeInHierarchy)
                .Select(actor => (ICrewTheaterMoveExecutor)new ShoreNavMeshCrewExecutor(
                    actor, scene)).ToArray();
            var goals = TacticalGroupDestinationAdapters.PlanShoreGoals(
                _selectionController.SelectedActorIds, _registeredActors, worldPoint);
            var outcomes = TacticalTheaterSelectionCommands.MoveSelectedAssigned(
                _session, _selectionController, worldPoint, PhysicalTheater.Shore,
                goals, executors);
            return RecordPhysicalDispatch(outcomes, worldPoint);
        }

        private bool RecordPhysicalDispatch(
            IReadOnlyList<CrewMoveDispatchDecision> outcomes, Vector3 worldPoint)
        {
            var accepted = new List<EntityId>();
            foreach (var result in outcomes)
            {
                _lastMoveDispatch[result.ActorId] = result.Accepted;
                _theaterDispatch[result.ActorId] = result;
                if (result.Accepted) accepted.Add(result.ActorId);
            }
            if (accepted.Count == 0) return false;

            LastIssuedIntent = TacticalOrderIntent.MoveTo(
                accepted, worldPoint.x, worldPoint.z);
            OrderIssued?.Invoke(LastIssuedIntent);
            if (destinationMarker != null) destinationMarker.Show(worldPoint);
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
                var shipActor = hit.collider.GetComponentInParent<ShipCrewSelectionProxy>();
                if (shipActor != null)
                {
                    if (!_shipActors.Contains(shipActor))
                        RegisterShipActor(shipActor);
                    SelectActor(shipActor.ActorId, isAdditive);
                    return true;
                }
                var actor = hit.collider.GetComponentInParent<TacticalActorProxy>();
                if (actor != null)
                {
                    if (!_registeredActors.Contains(actor))
                        RegisterActor(actor);
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
            EnsureSelectionBinding();
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
            foreach (var actor in _shipActors
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
                var nearest = Physics.RaycastAll(ray, 100f, ~0,
                    QueryTriggerInteraction.Ignore)
                    .OrderBy(h => h.distance).FirstOrDefault();
                if (nearest.collider == null ||
                    nearest.collider.GetComponentInParent<ShipCrewSelectionProxy>() != actor)
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
            var surface = first.collider.GetComponentInParent<ShipRoomClickSurface>();
            if (surface != null && _session != null && _session.SceneCheckpoint != null)
            {
                if (_shipSpatialMap == null ||
                    !surface.TryResolve(first.point, _shipSpatialMap, out var localCell))
                    return false;
                return IssueShipMoveOrder(localCell, first.point);
            }

            var gangway = first.collider.GetComponentInParent<ShoreGangwayInteractable>();
            if (gangway != null)
            {
                var selectedProxies = GetSelectedActorProxies();
                if (selectedProxies.Count > 0)
                    return IssueInteractOrder(gangway);
                return gangway.TryInteractDirect(_selectionController.SelectedActorIds.ToArray());
            }

            var interactable = first.collider.GetComponentInParent<TacticalInteractableProxy>();
            if (interactable != null && !interactable.IsDepleted)
                return IssueInteractOrder(interactable);

            if (_session != null && _session.SceneCheckpoint != null &&
                _session.SceneCheckpoint.Theater == PhysicalTheater.Ship)
                return false;

            var target = first.collider.GetComponentInParent<TacticalCombatTargetProxy>();
            if (target != null && target.IsHostile && !target.IsDead)
                return IssueAttackOrder(target);
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
                if (_session != null && _session.SceneCheckpoint != null &&
                    _session.SceneCheckpoint.Theater == PhysicalTheater.Shore)
                {
                    var member = _session.Capture().State.CrewDuty?.Get(id);
                    if (member != null && !member.IsAway)
                        continue;
                }
                var actor = _registeredActors.FirstOrDefault(
                    a => a != null && a.ActorId == id && a.gameObject.activeInHierarchy);
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
                    actor.SetSelected(_selectionController.IsSelected(actor.ActorId));
            }
            foreach (var actor in _shipActors)
            {
                if (actor != null)
                    actor.SetSelected(_selectionController.IsSelected(actor.ActorId));
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
            var point = mouse.position.ReadValue();
            bool pointerOverUi = IsPointerBlocked(point);

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
            if (_selectionController != null && _selectionEventsBound)
            {
                _selectionController.SelectionChanged -= OnSelectionChanged;
                _selectionEventsBound = false;
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
