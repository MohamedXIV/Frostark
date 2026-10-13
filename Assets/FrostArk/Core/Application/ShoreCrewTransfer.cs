using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Commands;
using FrostArk.Core.World;

namespace FrostArk.Core.Application
{
    /// <summary>
    /// The outcome of a ship/shore crew availability transition.
    /// Domain mutations, if any, are the existing revisioned crew commands.
    /// No scene, spawn-point, inventory or UI selection state is owned here.
    /// </summary>
    public sealed class ShoreCrewTransferResult
    {
        private readonly IReadOnlyList<EntityId> _actorIds;
        private readonly IReadOnlyList<CommandResult> _commandResults;

        public bool Success => Failure == CommandFailure.None;
        public CommandFailure Failure { get; }
        public IReadOnlyList<EntityId> ActorIds => _actorIds;
        public IReadOnlyList<CommandResult> CommandResults => _commandResults;

        internal ShoreCrewTransferResult(EntityId[] actors, CommandFailure failure,
            IList<CommandResult> commandResults)
        {
            _actorIds = Array.AsReadOnly((EntityId[])actors.Clone());
            _commandResults = Array.AsReadOnly(commandResults.ToArray());
            Failure = failure;
        }
    }

    /// <summary>
    /// Minimal session-bound bridge for ordered crew shore departure/return.
    /// A successful transfer changes *existing* persisted CrewDuty.IsAway
    /// flags via SetCrewAwayCommand; it never creates scene-local crew,
    /// duplicates inventory, or advances a second clock.
    ///
    /// All candidates are preflighted before the first command, to prevent
    /// ordinary invalid mixed selections from partially dispatching.
    /// Multi-member commands are still sequential canonical commits, not
    /// a new atomic batch domain command: observe CommandResults and do not
    /// load a scene after any mid-batch failure (for example, a subscriber
    /// concurrently changing the session revision).
    /// </summary>
    public static class ShoreCrewTransfer
    {
        public static ShoreCrewTransferResult Transfer(WorldSession session,
            IEnumerable<EntityId> actorIds, bool toShore, EntityId transitionId)
        {
            if (session == null)
                throw new ArgumentNullException(nameof(session));
            if (actorIds == null)
                throw new ArgumentNullException(nameof(actorIds));
            if (!transitionId.IsValid)
                throw new ArgumentException("A unique stable transfer operation ID is required.",
                    nameof(transitionId));

            // Freeze UI selection once; ordinal order keeps repeated valid
            // requests deterministic regardless of click/selection order.
            var ordered = actorIds.ToArray();
            if (ordered.Length == 0 || ordered.Any(id => !id.IsValid))
                throw new ArgumentException(
                    "The transfer roster must contain at least one valid persistent actor ID.",
                    nameof(actorIds));

            ordered = ordered.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
            for (int i = 1; i < ordered.Length; i++)
                if (ordered[i] == ordered[i - 1])
                    throw new ArgumentException("A crew member cannot appear twice.",
                        nameof(actorIds));

            var state = session.Capture().State;
            foreach (var actor in ordered)
            {
                if (!state.HasCrew(actor) || state.CrewDuty.Get(actor) == null)
                    return new ShoreCrewTransferResult(ordered,
                        CommandFailure.UnknownCrew, Array.Empty<CommandResult>());
                if (state.CrewDuty.Get(actor).IsAway == toShore)
                    return new ShoreCrewTransferResult(ordered,
                        CommandFailure.CrewAvailabilityUnchanged, Array.Empty<CommandResult>());
            }

            var results = new List<CommandResult>(ordered.Length);
            foreach (var actor in ordered)
            {
                // Each actor retains the *same* stable ID before and after a
                // transition. The caller supplies an operation ID that is
                // unique per departure/return, not a Unity instance ID.
                var commandId = EntityId.Parse("cmd:shore-transfer:" +
                    transitionId.Value.Length + ":" + transitionId.Value +
                    ":" + actor.Value.Length + ":" + actor.Value);
                var outcome = session.Submit(new SetCrewAwayCommand(
                    commandId, actor, toShore, session.Revision));
                results.Add(outcome);
                if (!outcome.Success)
                    return new ShoreCrewTransferResult(ordered, outcome.Failure, results);
            }

            return new ShoreCrewTransferResult(ordered, CommandFailure.None, results);
        }
    }
}
