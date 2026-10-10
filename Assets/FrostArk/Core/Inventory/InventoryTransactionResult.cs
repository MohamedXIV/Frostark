namespace FrostArk.Core.Inventory
{
    public enum InventoryContainer
    {
        Pack = 0,
        ShipHold = 1
    }

    public enum InventoryError
    {
        None = 0,
        UnknownPickup = 1,
        AlreadyCollected = 2,
        UnknownItem = 3,
        InvalidQuantity = 4,
        InsufficientStock = 5,
        TooHeavy = 6,
        NotEnoughSlots = 7
    }

    /// <summary>Either a new immutable inventory state or the identical rejected original.</summary>
    public sealed class InventoryTransactionResult
    {
        public InventoryState State { get; }
        public InventoryError Error { get; }
        public bool Success => Error == InventoryError.None;

        internal InventoryTransactionResult(InventoryState state, InventoryError error)
        {
            State = state;
            Error = error;
        }
    }
}
