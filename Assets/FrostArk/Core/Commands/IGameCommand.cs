using FrostArk.Core.World;

namespace FrostArk.Core.Commands
{
    /// <summary>
    /// A command must be executed against the committed revision it observed.
    /// The application serializes commits; the domain enforces revision checks.
    /// </summary>
    public interface IGameCommand
    {
        EntityId Id { get; }
        long ExpectedRevision { get; }
    }
}