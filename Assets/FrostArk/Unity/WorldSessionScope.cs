using System;
using FrostArk.Core.Application;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;
using VContainer;
using VContainer.Unity;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Run/session composition root. No process-global state or scene-owned
    /// resource totals: one WorldSession is scoped to this LifetimeScope.
    /// A future persistent parent scope may provide infrastructure ports.
    /// </summary>
    public sealed class WorldSessionScope : LifetimeScope
    {
        public static Func<WorldState> StateOverrideFactory { get; set; }

        public static WorldState CreateDefaultState()
        {
            if (StateOverrideFactory != null)
                return StateOverrideFactory();

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

        protected override void Configure(IContainerBuilder builder)
        {
            // Explicit constructor selection: WorldSession initializes with canonical authoritative state
            builder.Register<WorldSession>(_ => new WorldSession(CreateDefaultState()), Lifetime.Scoped);
            builder.RegisterComponentInHierarchy<WorldSessionPresenter>();

            var commander = GetComponentInChildren<TacticalCommandController>() ?? FindFirstObjectByType<TacticalCommandController>();
            if (commander != null)
            {
                builder.RegisterComponent(commander);
            }
        }
    }
}
