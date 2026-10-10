using FrostArk.Core.World;

namespace FrostArk.Core.Time
{
    /// <summary>
    /// A deterministic per-world-minute system. Future WorldState adapters
    /// are responsible for supplying validated state and recording effects.
    /// Systems should not derive truth from Unity deltaTime or frame count.
    /// </summary>
    public interface IWorldMinuteSystem
    {
        SimulationPhase Phase { get; }
        string StableKey { get; }
        void Tick(WorldMinute minute);
    }
}