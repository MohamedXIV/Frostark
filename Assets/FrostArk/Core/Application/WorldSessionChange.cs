using System;
using System.Collections.Generic;
using FrostArk.Core.Events;

namespace FrostArk.Core.Application
{
    public enum WorldSessionChangeKind
    {
        Command = 0,
        Time = 1
    }

    /// <summary>
    /// A notification emitted only after committed WorldSession state changes.
    /// Its immutable projection reflects the authoritative post-commit revision.
    /// </summary>
    public sealed class WorldSessionChange
    {
        public WorldSessionChangeKind Kind { get; }
        public string KindName => Kind.ToString();
        public long Revision => View.Revision;
        public WorldSessionView View { get; }
        public IReadOnlyList<WorldEvent> CommandEvents { get; }

        internal WorldSessionChange(WorldSessionChangeKind kind, WorldSessionView view,
            IReadOnlyList<WorldEvent> commandEvents)
        {
            Kind = kind;
            View = view ?? throw new ArgumentNullException(nameof(view));
            CommandEvents = commandEvents ?? Array.Empty<WorldEvent>();
        }
    }
}
