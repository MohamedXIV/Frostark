using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Scene-only physical expression of authoritative staffed duties.
    /// Uses exact staffed roster + persisted station assignment, and only
    /// authored Work/Rest slots. The existing WorldSession minute systems
    /// remain the SOLE executors of work, fatigue and rest consequences.
    /// No duplicate task progress, resource consumption or gameplay clock.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class ShipCrewDutyAutonomy : MonoBehaviour
    {
        private sealed class Activity
        {
            public EntityId SlotId;
            public ShipGridCell Arrival;
            public CrewInteractionVerb Verb;
            public bool Cancelling;
        }

        private WorldSession _session;
        private TacticalCommandController _commander;
        private ShipCrewAgencyCoordinator _agency;
        private ShipSpatialMap _map;
        private EntityId _observedSceneId;
        private readonly Dictionary<EntityId, Activity> _activities =
            new Dictionary<EntityId, Activity>();

        public bool HasActivity(EntityId id) => _activities.ContainsKey(id);
        public bool IsStationed(EntityId id) =>
            _activities.TryGetValue(id, out var a) && !a.Cancelling &&
            _commander != null && _commander.ShipActors.Any(actor =>
                actor != null && actor.ActorId == id && actor.Walker != null &&
                !actor.Walker.IsMoving &&
                actor.Walker.LastReachedCell == a.Arrival);
        public CrewInteractionVerb? ActiveVerb(EntityId id) =>
            _activities.TryGetValue(id, out var a) ? a.Verb : (CrewInteractionVerb?)null;

        public void Configure(WorldSession session, TacticalCommandController commander,
            ShipCrewAgencyCoordinator agency, ShipSpatialMap map)
        {
            if (_commander != null) _commander.OrderIssued -= OnOrderIssued;
            StopObservation();
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _commander = commander ?? throw new ArgumentNullException(nameof(commander));
            _agency = agency ?? throw new ArgumentNullException(nameof(agency));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _activities.Clear();
            var checkpoint = _session.SceneCheckpoint;
            if (checkpoint == null || checkpoint.Theater != PhysicalTheater.Ship)
                throw new InvalidOperationException("Physical duties require a canonical ship scene.");
            _observedSceneId = checkpoint.SceneId;
            _session.BeginShipWorkObservation(_observedSceneId);
            _commander.OrderIssued += OnOrderIssued;
        }

        private void StopObservation()
        {
            if (_session == null || !_observedSceneId.IsValid) return;
            try
            {
                _session.EndShipWorkObservation(_observedSceneId);
            }
            catch (ObjectDisposedException)
            {
                // Teardown may race session scope disposal; never submit a
                // resource mutation from MonoBehaviour destruction.
            }
            _observedSceneId = default;
        }

        public void SetMap(ShipSpatialMap map) =>
            _map = map ?? throw new ArgumentNullException(nameof(map));

        private static EntityId CommandId(string action, EntityId id, long revision) =>
            EntityId.Parse("cmd:duty-physical:" + action + ":" + id.Value +
                ":" + revision.ToString(CultureInfo.InvariantCulture));

        private void Release(EntityId id, EntityId slot)
        {
            var state = _session.Capture().State;
            var claim = state.CrewDuty.Interactions.GetClaim(slot);
            if (claim == null || claim.CrewId != id) return;
            _session.Submit(new ReleaseCrewInteractionCommand(
                CommandId("release", id, state.Revision), id, slot, state.Revision));
        }

        private void End(EntityId id, Activity activity)
        {
            _session.ClearShipWorkPresence(id);
            _activities.Remove(id);
            Release(id, activity.SlotId);
        }

        private void OnOrderIssued(TacticalOrderIntent intent)
        {
            if (intent == null || intent.Kind != TacticalOrderKind.Move) return;
            // Event is raised AFTER a Move has actually been accepted and
            // dispatched. Don't stop the newly accepted player route!
            foreach (var id in intent.ActorIds)
            {
                _session.ClearShipWorkPresence(id);
                if (_activities.TryGetValue(id, out var activity))
                    End(id, activity);
            }
        }

        private bool TryStart(EntityId id, ShipGridWalker walker,
            CrewDutyActivityOffer offer)
        {
            var world = _session.Capture().State;
            var slot = world.CrewDuty.Interactions.GetSlot(offer.SlotId);
            var module = world.Layout.GetModule(offer.SourceModuleId);
            if (slot == null || module == null ||
                !walker.IsConfigured || walker.IsMoving || walker.IsRecovering)
                return false;
            var goal = new ShipGridCell(module.RoomId,
                module.X + slot.LocalX, module.Y + slot.LocalY);
            if (!_map.Walkable(goal) || _map.IsPortalEndpoint(goal) ||
                _map.FindPath(walker.LastReachedCell, goal).Count == 0)
                return false;
            var previous = world.CrewDuty.Interactions.ClaimForCrew(id);
            if (previous != null && previous.SlotId != slot.Id)
                return false; // A Need or other authored claim owns the actor.
            // WorldSession claims survive save/reentry. Resuming the SAME
            // still-valid Work/Rest claim is not a new world command.
            if (previous == null)
            {
                var claim = _session.Submit(new ClaimCrewInteractionCommand(
                    CommandId("claim", id, world.Revision), id, slot.Id,
                    15, world.Revision));
                if (!claim.Success) return false;
            }
            if (!walker.TryMoveTo(goal))
            {
                Release(id, slot.Id);
                return false;
            }
            _activities[id] = new Activity
            {
                SlotId = slot.Id, Arrival = goal, Verb = offer.Verb
            };
            return true;
        }

        private void Update() => Tick();

        /// <summary>Explicit Tick is usable by Unity EditMode fixtures;
        /// no world-clock or offscreen work is advanced here.</summary>
        public void Tick()
        {
            if (_session == null || _commander == null || _map == null ||
                _session.SceneCheckpoint == null ||
                _session.SceneCheckpoint.Theater != PhysicalTheater.Ship)
                return;

            foreach (var actor in _commander.ShipActors.Where(a =>
                a != null && a.Walker != null && a.Walker.IsConfigured)
                .OrderBy(a => a.ActorId.Value, StringComparer.Ordinal))
            {
                var id = actor.ActorId;
                var walker = actor.Walker;
                // Every frame re-attests only genuinely current presence.
                _session.ClearShipWorkPresence(id);
                var selected = _commander.Selection.IsSelected(id);
                var hasPlayerOrder = _agency.HasActivePlayerMove(id);
                var world = _session.Capture().State;
                var policy = CrewAgencyPolicy.Evaluate(world, id,
                    selected, hasPlayerOrder);
                var member = world.CrewDuty.Get(id);
                // An owned Work/Rest claim can hide provision offers from the
                // existing offer discovery. Yield that physical slot once a
                // Need crosses the seek threshold, then let the ordinary
                // provision coordinator discover/claim the freed slot next Tick.
                bool priorityNeed = member != null &&
                    Math.Max(member.Hunger, member.Thirst) >=
                    CrewAgencyPolicy.SeekProvisionAt;

                if (_activities.TryGetValue(id, out var activity))
                {
                    var claim = world.CrewDuty.Interactions.GetClaim(activity.SlotId);
                    bool owned = claim != null && claim.CrewId == id;
                    var current = policy.Kind == CrewAgencyDecisionKind.ReadyForDuty
                        ? CrewDutyActivityPlanner.Find(world, id) : null;
                    bool stillAssigned = current.HasValue &&
                        current.Value.SlotId == activity.SlotId &&
                        current.Value.Verb == activity.Verb;

                    if (!owned || !stillAssigned || selected || hasPlayerOrder ||
                        priorityNeed ||
                        (walker.LastError != null &&
                         walker.LastError != "WaitingForTraffic"))
                    {
                        activity.Cancelling = true;
                        if (walker.IsMoving || walker.IsRecovering)
                            walker.RequestStopAtSafeCell();
                    }

                    // Never release an actively traversed edge before reaching
                    // a safe center; never consume stock merely by arriving.
                    if (activity.Cancelling)
                    {
                        if (!walker.IsMoving && !walker.IsRecovering)
                            End(id, activity);
                        continue;
                    }
                    if (!walker.IsMoving && walker.LastReachedCell != activity.Arrival)
                    {
                        End(id, activity);
                        continue;
                    }
                    // Only stationary real Work presence may authorize the
                    // next WorldClock minute's RepairJob productivity.
                    bool atAuthoredSlot = !walker.IsMoving && !walker.IsRecovering &&
                        !walker.HasIncomingReservation &&
                        walker.LastReachedCell == activity.Arrival &&
                        walker.LastError == null;
                    if (atAuthoredSlot)
                        _session.ReportShipDutyPresence(id, activity.SlotId);
                    // Rest attestation can renew a legitimate bed lease,
                    // never authorize Repair or Engineering productivity.

                    // Real Work/Rest occupancy spans many world-minutes. Keep
                    // the same canonical lease owner, renewing ONLY close to
                    // its expiry. No new item, staff job or physical order is
                    // created; the normal WorldClock still owns consequences.
                    if (atAuthoredSlot &&
                        claim.ExpiresAt.Value - world.Minute.Value <= 5)
                    {
                        var renewed = _session.Submit(new RenewCrewDutySlotCommand(
                            CommandId("renew", id, world.Revision), id, activity.SlotId,
                            15, world.Revision));
                        if (!renewed.Success)
                        {
                            activity.Cancelling = true;
                            if (walker.IsMoving || walker.IsRecovering)
                                walker.RequestStopAtSafeCell();
                            else
                                End(id, activity);
                        }
                    }
                    continue;
                }

                // A persisted claim may outlive its scene-local Activity
                // object after reentry. Only reconcile Work/Rest here; Eat
                // and Drink remain owned by the provision coordinator.
                var ownedClaim = world.CrewDuty.Interactions.ClaimForCrew(id);
                var ownedSlot = ownedClaim == null ? null :
                    world.CrewDuty.Interactions.GetSlot(ownedClaim.SlotId);
                bool isPhysicalDutyClaim = ownedSlot != null &&
                    (ownedSlot.Verb == CrewInteractionVerb.Work ||
                     ownedSlot.Verb == CrewInteractionVerb.Rest);

                if (policy.Kind != CrewAgencyDecisionKind.ReadyForDuty ||
                    priorityNeed || walker.IsMoving || walker.IsRecovering ||
                    _agency.HasPendingProvision(id))
                {
                    if (isPhysicalDutyClaim &&
                        !walker.HasIncomingReservation && !walker.IsRecovering)
                        Release(id, ownedClaim.SlotId);
                    continue;
                }

                var offer = CrewDutyActivityPlanner.Find(world, id);
                if (isPhysicalDutyClaim &&
                    (!offer.HasValue || offer.Value.SlotId != ownedClaim.SlotId))
                {
                    Release(id, ownedClaim.SlotId);
                    continue;
                }
                if (offer.HasValue) TryStart(id, walker, offer.Value);
            }
        }

        private void OnDestroy()
        {
            if (_commander != null) _commander.OrderIssued -= OnOrderIssued;
            StopObservation();
            // No commands during Unity destruction; claims are canonical with
            // WorldClock TTL, and the caller owns scene transit semantics.
        }
    }
}
