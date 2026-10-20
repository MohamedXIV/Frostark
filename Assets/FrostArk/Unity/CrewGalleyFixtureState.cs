using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Authored game-world starting content for the isolated Galley scene.
    /// One Core WorldState owns crew Needs, job eligibility and all provisions.
    /// </summary>
    public static class CrewGalleyFixtureState
    {
        public static readonly EntityId Engineer = EntityId.Parse("crew:galley:engineer");
        public static readonly EntityId Relief = EntityId.Parse("crew:galley:relief");
        public static readonly EntityId Ration = EntityId.Parse("item:galley:ration");
        public static readonly EntityId Water = EntityId.Parse("item:galley:water");
        public static readonly EntityId Galley = EntityId.Parse("object:ship:galley");
        public static readonly EntityId Eat = EntityId.Parse("slot:ship:galley:eat");
        public static readonly EntityId Drink = EntityId.Parse("slot:ship:galley:drink");

        public const int Width = 10;
        public const int Height = 8;
        public static readonly Vector2Int StartCell = new Vector2Int(1, 1);
        public static readonly Vector2Int GalleyCell = new Vector2Int(7, 6);
        public static readonly Vector2Int EatCell = new Vector2Int(7, 4);
        public static readonly Vector2Int DrinkCell = new Vector2Int(7, 5);

        // Wall has exactly one door opening at y=5; navigation must go around.
        public static Vector2Int[] WallCells() => new[]
        {
            new Vector2Int(4, 0), new Vector2Int(4, 1),
            new Vector2Int(4, 2), new Vector2Int(4, 3),
            new Vector2Int(4, 4), new Vector2Int(4, 6),
            new Vector2Int(4, 7), GalleyCell
        };

        public static CrewShipGridPath Grid() =>
            new CrewShipGridPath(Width, Height, WallCells());

        public static WorldState CreateDefaultState()
        {
            var inventory = InventoryState.Create(new ItemCatalog(new[]
            {
                new ItemDefinition(Ration, 1, 200, 20),
                new ItemDefinition(Water, 1, 250, 20)
            }), new[]
            {
                new SalvagePickup(EntityId.Parse("pickup:galley:rations"), Ration, 4),
                new SalvagePickup(EntityId.Parse("pickup:galley:water"), Water, 4)
            }, 10000, 10, 20000, 20);
            inventory = inventory.TryCollect(EntityId.Parse("pickup:galley:rations")).State
                .TryCollect(EntityId.Parse("pickup:galley:water")).State
                .TryUnload(Ration, 4).State
                .TryUnload(Water, 4).State;

            var slots = CrewInteractionState.Create(new[]
            {
                new CrewInteractionSlot(Eat, Galley, CrewInteractionVerb.Eat, 0, -2, 2),
                new CrewInteractionSlot(Drink, Galley, CrewInteractionVerb.Drink, 0, -1, 2)
            });
            var crew = CrewState.Create(new[]
            {
                new CrewMember(Engineer, DutyRole.Engineering, 900, 0, 0, 1440,
                    hunger: 950, thirst: 960),
                new CrewMember(Relief, DutyRole.Watch, 800, 600, 0, 1440)
            }, consumables: new[]
            {
                new CrewConsumable(Ration, 600, 0),
                new CrewConsumable(Water, 0, 650)
            }, interactions: slots);
            return WorldState.Create(WorldMinute.Zero,
                System.Array.Empty<SupplyStock>(), new[] { Engineer, Relief },
                inventory: inventory, crewDuty: crew);
        }
    }

    /// <summary>Configures the same fixture WorldState before VContainer Build.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CrewGalleyFixtureBootstrap : MonoBehaviour, IInitialWorldStateProvider
    {
        public WorldState ProvideInitialState() => CrewGalleyFixtureState.CreateDefaultState();

        private void Awake()
        {
            var scope = GetComponent<WorldSessionScope>();
            if (scope != null) scope.SetInitialState(ProvideInitialState());
        }
    }
}
