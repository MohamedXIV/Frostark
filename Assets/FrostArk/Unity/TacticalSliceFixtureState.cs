using FrostArk.Core.Combat;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Canonical initial WorldState factory strictly for the tactical vertical slice fixture.
    /// Equips protagonist with carried rifle and ammo, defines combat weapons and cooldowns,
    /// authors crew and hostile targets, and preserves uncollected salvage caches.
    /// </summary>
    public static class TacticalSliceFixtureState
    {
        public static readonly EntityId ScrapItemId = EntityId.Parse("item:scrap_metal");
        public static readonly EntityId RifleItemId = EntityId.Parse("item:weapon:rifle");
        public static readonly EntityId AmmoItemId = EntityId.Parse("item:ammo:rifle_rounds");
        public static readonly EntityId CachePickupId = EntityId.Parse("salvage:supply_cache");
        public static readonly EntityId ProtagonistId = EntityId.Parse("actor:protagonist");
        public static readonly EntityId CompanionId = EntityId.Parse("actor:companion");
        public static readonly EntityId EnemyId = EntityId.Parse("actor:enemy");

        public static WorldState CreateDefaultState()
        {
            var scrapItem = new ItemDefinition(ScrapItemId, 1, 100, 50);
            var rifleItem = new ItemDefinition(RifleItemId, 1, 2500, 1);
            var ammoItem = new ItemDefinition(AmmoItemId, 1, 20, 100);
            var catalog = new ItemCatalog(new[] { scrapItem, rifleItem, ammoItem });

            var cachePickup = new SalvagePickup(CachePickupId, scrapItem.Id, 5);
            var riflePickup = new SalvagePickup(EntityId.Parse("salvage:gear:rifle"), rifleItem.Id, 1);
            var ammoPickup = new SalvagePickup(EntityId.Parse("salvage:gear:ammo"), ammoItem.Id, 20);

            var inventory = InventoryState.Create(catalog,
                new[] { cachePickup, riflePickup, ammoPickup },
                50000, 20, 500000, 100);

            // Carried gear in protagonist pack: rifle and 20 rounds of ammunition
            inventory = inventory.TryCollect(riflePickup.Id).State;
            inventory = inventory.TryCollect(ammoPickup.Id).State;

            var crew = new[]
            {
                ProtagonistId,
                CompanionId,
                EnemyId
            };

            var supplies = new[]
            {
                new SupplyStock(EntityId.Parse("supply:fuel"), 100),
                new SupplyStock(EntityId.Parse("supply:rations"), 50)
            };

            var crewDuty = CrewState.Create(new[]
            {
                new CrewMember(ProtagonistId, DutyRole.Watch, 900, 200, 0, 1440),
                new CrewMember(CompanionId, DutyRole.Repair, 850, 150, 0, 1440),
                new CrewMember(EnemyId, DutyRole.Watch, 500, 100, 0, 1440, wounds: 0)
            });

            var combat = CombatState.Create(new[]
            {
                new WeaponDefinition(RifleItemId, AmmoItemId, ammoPerShot: 1, cooldownTicks: 3)
            });

            return WorldState.Create(WorldMinute.Zero, supplies, crew,
                inventory: inventory, crewDuty: crewDuty, combat: combat);
        }

        public static WorldState CreateStateWithMagazineWeapon(int magazineCapacity = 5, int reloadTicks = 4)
        {
            var baseState = CreateDefaultState();
            var combat = CombatState.Create(new[]
            {
                new WeaponDefinition(RifleItemId, AmmoItemId, ammoPerShot: 1, cooldownTicks: 3,
                    magazineCapacity: magazineCapacity, reloadTicks: reloadTicks)
            });
            return WorldState.Create(baseState.Minute,
                new[]
                {
                    new SupplyStock(EntityId.Parse("supply:fuel"), 100),
                    new SupplyStock(EntityId.Parse("supply:rations"), 50)
                },
                new[] { ProtagonistId, CompanionId, EnemyId },
                inventory: baseState.Inventory,
                crewDuty: baseState.CrewDuty,
                combat: combat);
        }
    }
}
