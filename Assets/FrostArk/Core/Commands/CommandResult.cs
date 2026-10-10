using System;
using System.Collections.Generic;
using FrostArk.Core.Events;
using FrostArk.Core.World;

namespace FrostArk.Core.Commands
{
    public sealed class CommandResult
    {
        public WorldState State { get; }
        public CommandFailure Failure { get; }
        public bool Success => Failure == CommandFailure.None;
        public IReadOnlyList<WorldEvent> Events { get; }

        private CommandResult(WorldState state, CommandFailure failure, IReadOnlyList<WorldEvent> events)
        {
            State = state;
            Failure = failure;
            Events = events;
        }

        internal static CommandResult Rejected(WorldState original, CommandFailure failure) =>
            new CommandResult(original, failure, Array.Empty<WorldEvent>());

        internal static CommandResult Committed(WorldState next, WorldEvent worldEvent) =>
            new CommandResult(next, CommandFailure.None,
                Array.AsReadOnly(new[] { worldEvent }));
    }
}