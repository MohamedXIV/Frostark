using System.Collections.Generic;
using FrostArk.Core.Crew;
using FrostArk.Core.World;

namespace FrostArk.Core.Application
{
    /// <summary>
    /// Read-only projection over one immutable WorldState revision. Presentation
    /// may keep this revision for display without owning mutable simulation data.
    /// </summary>
    public sealed class WorldSessionView
    {
        private readonly WorldState _source;

        internal WorldSessionView(WorldState source)
        {
            _source = source;
        }

        public WorldMinute Minute => _source.Minute;
        public long Revision => _source.Revision;
        public int CrewCount => _source.CrewCount;
        public int MaxCrewCapacity => WorldState.MaxCrewCapacity;
        public IReadOnlyList<CrewIdentity> CrewIdentities => _source.CrewIdentities;
        public long GetSupply(EntityId supplyId) => _source.GetSupply(supplyId);
        public EntityId GetCrewStation(EntityId crewId) => _source.GetCrewStation(crewId);
        public bool HasCrew(EntityId crewId) => _source.HasCrew(crewId);
        public CrewIdentity? GetCrewIdentity(EntityId crewId) => _source.GetCrewIdentity(crewId);
        public CrewMember? GetCrewMember(EntityId crewId) => _source.CrewDuty.Get(crewId);
    }
}
