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
    /// Physical ship adapter for #112 agency decisions. Reads canonical Needs
    /// and interaction offers; only WorldSession.Submit may claim/use/release
    /// real resources. Does not tick the WorldClock or fabricate crew, jobs
    /// or stock. Current player locomotion is tracked separately from selection.
    /// </summary>
    public sealed class ShipCrewAgencyCoordinator : MonoBehaviour
    {
        private sealed class PendingProvision
        {
            public EntityId SlotId;
            public EntityId ItemId;
            public ShipGridCell Arrival;
            public bool CancelRequested;
        }

        private WorldSession _session;
        private TacticalCommandController _commander;
        private ShipSpatialMap _map;
        private readonly Dictionary<EntityId, ShipGridCell> _playerMoveGoals =
            new Dictionary<EntityId, ShipGridCell>();
        private readonly Dictionary<EntityId, PendingProvision> _provisions =
            new Dictionary<EntityId, PendingProvision>();
        private readonly Dictionary<EntityId, CrewAgencyDecisionKind> _decisions =
            new Dictionary<EntityId, CrewAgencyDecisionKind>();

        public bool HasActivePlayerMove(EntityId id) => _playerMoveGoals.ContainsKey(id);
        public bool HasPendingProvision(EntityId id) => _provisions.ContainsKey(id);
        public CrewAgencyDecisionKind LastDecision(EntityId id) =>
            _decisions.TryGetValue(id, out var decision) ? decision :
                CrewAgencyDecisionKind.Unavailable;

        public void Configure(WorldSession session, TacticalCommandController commander,
            ShipSpatialMap map)
        {
            if (_commander != null)
                _commander.OrderIssued -= OnOrderIssued;
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _commander = commander ?? throw new ArgumentNullException(nameof(commander));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _playerMoveGoals.Clear();
            _provisions.Clear();
            _decisions.Clear();
            _commander.OrderIssued += OnOrderIssued;
        }

        public void SetMap(ShipSpatialMap map) =>
            _map = map ?? throw new ArgumentNullException(nameof(map));

        private void OnOrderIssued(TacticalOrderIntent intent)
        {
            if (_commander == null || intent.Kind != TacticalOrderKind.Move) return;
            foreach (var id in intent.ActorIds)
            {
                var actor = _commander.ShipActors.FirstOrDefault(a =>
                    a != null && a.ActorId == id && a.Walker != null);
                if (actor == null) continue;
                // Event is emitted only after accepted physical dispatch; the
                // actor's individual local ordered cell is now authoritative
                // for tracking this scene action, not the group click center.
                _playerMoveGoals[id] = actor.Walker.OrderedCell;
                if (_provisions.TryGetValue(id, out var need))
                {
                    // The accepted player route already supersedes the
                    // autonomous activity. Release its reservation without
                    // stopping the newly issued order.
                    _provisions.Remove(id);
                    ReleaseOwned(id, need.SlotId);
                }
            }
        }

        private static EntityId CommandId(string action, EntityId crewId, long revision) =>
            EntityId.Parse("cmd:crew-agency:" + action + ":" + crewId.Value + ":" +
                revision.ToString(CultureInfo.InvariantCulture));

        private void ReleaseOwned(EntityId crewId, EntityId slotId)
        {
            if (_session == null) return;
            var state = _session.Capture().State;
            var claim = state.CrewDuty.Interactions.GetClaim(slotId);
            if (claim == null || claim.CrewId != crewId) return;
            _session.Submit(new ReleaseCrewInteractionCommand(
                CommandId("release", crewId, state.Revision), crewId, slotId,
                state.Revision));
        }

        private void EndProvision(EntityId id, PendingProvision pending)
        {
            _provisions.Remove(id);
            ReleaseOwned(id, pending.SlotId);
        }

        /// <summary>
        /// Scan physical completion; never use a claimed slot merely because
        /// its destination was assigned. A real arrived and still-owned claim
        /// is mandatory for consumption. All failures release when possible.
        /// </summary>
        private void CompleteProvision(EntityId id, ShipGridWalker walker,
            PendingProvision pending, bool selected)
        {
            // An already committed emergency provision can finish even if
            // the user has selected this actor; ordinary autonomous provision
            // yields to selection at the next safe cell boundary.
            var member = _session.Capture().State.CrewDuty.Get(id);
            bool emergency = member != null &&
                Math.Max(member.Hunger, member.Thirst) >= CrewAgencyPolicy.EmergencyInterruptAt;
            if (selected && !emergency && !pending.CancelRequested)
            {
                pending.CancelRequested = true;
                _decisions[id] = CrewAgencyDecisionKind.HoldForPlayerSelection;
                walker.RequestStopAtSafeCell();
            }
            var world = _session.Capture().State;
            var claim = world.CrewDuty.Interactions.GetClaim(pending.SlotId);
            if (claim == null || claim.CrewId != id)
            {
                if (walker.IsMoving) walker.RequestStopAtSafeCell();
                _provisions.Remove(id);
                return;
            }
            if (walker.IsMoving || walker.IsRecovering) return;
            if (pending.CancelRequested || walker.LastError != null ||
                walker.LastReachedCell != pending.Arrival)
            {
                EndProvision(id, pending);
                return;
            }

            // Remove first to make event callbacks/reentrant Tick idempotent.
            _provisions.Remove(id);
            var result = _session.Submit(new UseCrewProvisionInteractionCommand(
                CommandId("use", id, world.Revision),
                id, pending.SlotId, pending.ItemId, world.Revision));
            if (!result.Success) ReleaseOwned(id, pending.SlotId);
        }

        private bool TryBeginProvision(EntityId id, ShipGridWalker walker,
            CrewProvisionInteractionOffer offer)
        {
            var world = _session.Capture().State;
            var slot = world.CrewDuty.Interactions.GetSlot(offer.SlotId);
            var module = world.Layout.GetModule(offer.SourceId);
            // Unplaced demo-only sources aren't proof of physical arrival.
            if (slot == null || module == null) return false;
            var goal = new ShipGridCell(module.RoomId,
                module.X + slot.LocalX, module.Y + slot.LocalY);
            if (!_map.Walkable(goal) || _map.IsPortalEndpoint(goal) ||
                _map.FindPath(walker.LastReachedCell, goal).Count == 0)
                return false;
            if (walker.IsMoving || walker.IsRecovering) return false;

            var claimed = _session.Submit(new ClaimCrewInteractionCommand(
                CommandId("claim", id, world.Revision), id, offer.SlotId,
                15, world.Revision));
            if (!claimed.Success) return false;
            if (!walker.TryMoveTo(goal))
            {
                ReleaseOwned(id, offer.SlotId);
                return false;
            }
            _provisions[id] = new PendingProvision
            {
                SlotId = offer.SlotId, ItemId = offer.ItemId, Arrival = goal
            };
            return true;
        }

        private void Update() => Tick();

        /// <summary>
        /// Deterministic synchronous evaluation for EditMode tests. Unity owns
        /// presentation frames; Needs still advance only with WorldSession.
        /// </summary>
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
                var selected = _commander.Selection.IsSelected(id);
                if (_provisions.TryGetValue(id, out var pending))
                {
                    CompleteProvision(id, walker, pending, selected);
                    continue;
                }

                bool activeOrder = _playerMoveGoals.TryGetValue(id, out var goal);
                if (activeOrder && !walker.IsMoving && !walker.IsRecovering)
                {
                    _playerMoveGoals.Remove(id);
                    activeOrder = false;
                }

                var decision = CrewAgencyPolicy.Evaluate(_session.Capture().State,
                    id, selected, activeOrder);
                _decisions[id] = decision.Kind;
                if (decision.Kind == CrewAgencyDecisionKind.EmergencyNeedsHandover &&
                    activeOrder)
                {
                    walker.RequestStopAtSafeCell();
                    if (!walker.IsMoving && !walker.IsRecovering)
                    {
                        _playerMoveGoals.Remove(id);
                        activeOrder = false;
                    }
                    else
                        continue;
                }
                if ((decision.Kind == CrewAgencyDecisionKind.SeekProvision ||
                     decision.Kind == CrewAgencyDecisionKind.EmergencyNeedsHandover) &&
                    decision.HasProvision && !activeOrder)
                {
                    if (!TryBeginProvision(id, walker, decision.Provision) &&
                        decision.Severity >= CrewMember.CriticalNeedAt)
                        _decisions[id] = CrewAgencyDecisionKind.UnmetCriticalNeed;
                }
            }
        }

        private void OnDestroy()
        {
            if (_commander != null)
                _commander.OrderIssued -= OnOrderIssued;
            // Do not submit commands from Unity object destruction; a scene
            // transfer/disposal order is not guaranteed. The canonical claim
            // TTL/scene-transfer rules still apply; #112 save/reentry ongoing.
        }
    }
}
