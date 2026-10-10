namespace FrostArk.Core.Commands
{
    public enum CommandFailure
    {
        None = 0,
        InvalidCommandId = 1,
        StaleRevision = 2,
        DuplicateCommand = 3,
        InvalidQuantity = 4,
        UnknownSupply = 5,
        InsufficientSupply = 6,
        UnknownCrew = 7,
        InvalidStation = 8,
        CrewAlreadyAssigned = 9,
        CrewNotAssigned = 10,
        UnsupportedCommand = 11
    }
}