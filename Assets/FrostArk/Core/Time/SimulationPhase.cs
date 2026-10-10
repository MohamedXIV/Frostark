namespace FrostArk.Core.Time
{
    /// <summary>
    /// Stable ordering contract: new phases should be allocated explicit values.
    /// Do not reorder existing values without considering save/replay semantics.
    /// </summary>
    public enum SimulationPhase
    {
        Commands = 0,
        Crew = 10,
        Supplies = 20,
        Heat = 30,
        Wellbeing = 40,
        Travel = 50,
        WorldEvents = 60,
        Publish = 70
    }
}