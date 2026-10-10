using System;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.World;

namespace FrostArk.Core.Economy
{
    public sealed class RecipeStack
    {
        public EntityId ItemId { get; }
        public long Quantity { get; }

        public RecipeStack(EntityId itemId, long quantity)
        {
            if (!itemId.IsValid)
                throw new ArgumentException("Recipe stack requires an item ID.", nameof(itemId));
            if (quantity <= 0)
                throw new ArgumentOutOfRangeException(nameof(quantity));
            ItemId = itemId;
            Quantity = quantity;
        }

        public long ForBatches(long batches)
        {
            if (batches <= 0) throw new ArgumentOutOfRangeException(nameof(batches));
            return checked(Quantity * batches);
        }
    }

    /// <summary>
    /// Authoring contract for a reproducible recipe, NOT permission to mint
    /// items. WorldCommandBus will validate and spend canonical inventory.
    /// </summary>
    public sealed class WorkshopRecipe
    {
        private readonly RecipeStack[] _inputs;
        private readonly RecipeStack[] _outputs;

        public EntityId Id { get; }
        public int Version { get; }
        public EntityId StationId { get; }
        public long MinutesPerBatch { get; }
        public long FuelUnitsPerBatch { get; }
        public IReadOnlyList<RecipeStack> Inputs => Array.AsReadOnly(_inputs);
        public IReadOnlyList<RecipeStack> Outputs => Array.AsReadOnly(_outputs);

        public WorkshopRecipe(EntityId id, int version, EntityId stationId,
            long minutesPerBatch, IEnumerable<RecipeStack> inputs,
            IEnumerable<RecipeStack> outputs, long fuelUnitsPerBatch = 0)
        {
            if (!id.IsValid) throw new ArgumentException("Recipe ID is required.", nameof(id));
            if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
            if (!stationId.IsValid)
                throw new ArgumentException("Station ID is required.", nameof(stationId));
            if (minutesPerBatch <= 0)
                throw new ArgumentOutOfRangeException(nameof(minutesPerBatch));
            if (fuelUnitsPerBatch < 0)
                throw new ArgumentOutOfRangeException(nameof(fuelUnitsPerBatch));

            _inputs = CanonicalStacks(inputs, nameof(inputs));
            _outputs = CanonicalStacks(outputs, nameof(outputs));
            Id = id;
            Version = version;
            StationId = stationId;
            MinutesPerBatch = minutesPerBatch;
            FuelUnitsPerBatch = fuelUnitsPerBatch;
        }

        private static RecipeStack[] CanonicalStacks(IEnumerable<RecipeStack> items, string name)
        {
            if (items == null) throw new ArgumentNullException(name);
            var stacks = items.ToArray();
            if (stacks.Length == 0 || stacks.Any(x => x == null))
                throw new ArgumentException("Recipe requires nonempty, non-null item stacks.", name);
            stacks = stacks.OrderBy(x => x.ItemId.Value, StringComparer.Ordinal).ToArray();
            for (int i = 1; i < stacks.Length; i++)
                if (stacks[i].ItemId == stacks[i - 1].ItemId)
                    throw new ArgumentException("Duplicate ingredient item identity.", name);
            return stacks;
        }
    }

    public sealed class WorkshopCatalog
    {
        private readonly Dictionary<string, WorkshopRecipe> _recipes;
        public WorkshopCatalog(IEnumerable<WorkshopRecipe> recipes)
        {
            if (recipes == null) throw new ArgumentNullException(nameof(recipes));
            _recipes = new Dictionary<string, WorkshopRecipe>(StringComparer.Ordinal);
            foreach (var recipe in recipes)
                if (recipe == null || !_recipes.TryAdd(recipe.Id.Value, recipe))
                    throw new ArgumentException("Duplicate/null recipe ID.", nameof(recipes));
        }

        public WorkshopRecipe Get(EntityId id) =>
            id.IsValid && _recipes.TryGetValue(id.Value, out var recipe) ? recipe : null;

        public IReadOnlyList<WorkshopRecipe> Snapshot() =>
            Array.AsReadOnly(_recipes.OrderBy(x => x.Key, StringComparer.Ordinal)
                .Select(x => x.Value).ToArray());
    }
}
