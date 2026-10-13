using FrostArk.Core.Inventory;
using FrostArk.Core.World;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Canonical initial WorldState factory strictly for the tactical vertical slice fixture.
    /// Kept out of production WorldSessionScope composition so production sessions
    /// start from explicitly owned run/session content and cannot bleed fixture state.
    /// </summary>
    public static class TacticalSliceFixtureState
    {
        public static WorldState CreateDefaultState()
        {
            var item = new ItemDefinition(EntityId.Parse("item:scrap_metal"), 1, 100, 50);
            var catalog = new ItemCatalog(new[] { item });
            var pickup = new SalvagePickup(EntityId.Parse("salvage:supply_cache"), item.Id, 5);
            var inventory = InventoryState.Create(catalog, new[] { pickup }, 50000, 20, 500000, 100);

            var crew = new[]
            {
                EntityId.Parse("actor:protagonist"),
                EntityId.Parse("actor:companion")
            };
            var supplies = new[]
            {
                new SupplyStock(EntityId.Parse("supply:fuel"), 100),
                new SupplyStock(EntityId.Parse("supply:rations"), 50)
            };

            return WorldState.Create(WorldMinute.Zero, supplies, crew, inventory: inventory);
        }
    }
}
