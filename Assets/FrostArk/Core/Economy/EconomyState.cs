using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;

namespace FrostArk.Core.Economy
{
    /// <summary>
    /// Immutable prepaid job: its output can be delivered once on/after DueAt.
    /// Its identity is permanent and separate from transient Unity instances.
    /// </summary>
    public sealed class WorkshopJob
    {
        public EntityId Id { get; }
        public EntityId RecipeId { get; }
        public int RecipeVersion { get; }
        public EntityId StationId { get; }
        public WorldMinute DueAt { get; }
        private readonly RecipeStack[] _outputs;
        public IReadOnlyList<RecipeStack> Outputs => Array.AsReadOnly(_outputs);

        internal WorkshopJob(EntityId id, WorkshopRecipe recipe, WorldMinute dueAt)
            : this(id, recipe?.Id ?? default, recipe?.Version ?? 0,
                recipe?.StationId ?? default, dueAt, recipe?.Outputs)
        { }

        internal WorkshopJob(EntityId id, EntityId recipeId, int recipeVersion,
            EntityId stationId, WorldMinute dueAt, IEnumerable<RecipeStack> outputs)
        {
            if (!id.IsValid || !recipeId.IsValid || !stationId.IsValid)
                throw new ArgumentException("Workshop job needs stable identities.");
            if (recipeVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(recipeVersion));
            if (outputs == null) throw new ArgumentNullException(nameof(outputs));
            _outputs = outputs.ToArray();
            if (_outputs.Length == 0 || _outputs.Any(x => x == null))
                throw new ArgumentException("Job must have outputs.", nameof(outputs));
            Id = id;
            RecipeId = recipeId;
            RecipeVersion = recipeVersion;
            StationId = stationId;
            DueAt = dueAt;
        }
    }

    public sealed class WorkshopMinuteResult
    {
        public EconomyState Economy { get; }
        public InventoryState Inventory { get; }
        internal WorkshopMinuteResult(EconomyState economy, InventoryState inventory)
        {
            Economy = economy;
            Inventory = inventory;
        }
    }

    /// <summary>Authored recipe + station truth and pending paid production.</summary>
    public sealed class EconomyState
    {
        private readonly EntityId[] _stations;
        private readonly WorkshopJob[] _jobs;
        private readonly WorkshopCatalog _recipes;
        private readonly MarketState[] _markets;
        public WorkshopCatalog Recipes => _recipes;
        public IReadOnlyList<EntityId> Stations => Array.AsReadOnly(_stations);
        public IReadOnlyList<WorkshopJob> Jobs => Array.AsReadOnly(_jobs);
        public IReadOnlyList<MarketState> Markets => Array.AsReadOnly(_markets);
        public MarketState GetMarket(EntityId id) => id.IsValid ?
            Array.Find(_markets, x => x.Id == id) : null;

        private EconomyState(WorkshopCatalog recipes, EntityId[] stations, WorkshopJob[] jobs,
            MarketState[] markets)
        {
            _recipes = recipes;
            _stations = stations;
            _jobs = jobs;
            _markets = markets;
        }

        public static EconomyState Empty => Create(
            new WorkshopCatalog(Array.Empty<WorkshopRecipe>()), Array.Empty<EntityId>());

        public static EconomyState Create(WorkshopCatalog recipes, IEnumerable<EntityId> stations,
            IEnumerable<WorkshopJob> jobs = null,
            IEnumerable<MarketState> markets = null)
        {
            if (recipes == null) throw new ArgumentNullException(nameof(recipes));
            if (stations == null) throw new ArgumentNullException(nameof(stations));
            var stationArray = stations.ToArray();
            if (stationArray.Any(x => !x.IsValid) ||
                stationArray.Select(x => x.Value).Distinct(StringComparer.Ordinal).Count() != stationArray.Length)
                throw new ArgumentException("Invalid/duplicate workstation.", nameof(stations));
            stationArray = stationArray.OrderBy(x => x.Value, StringComparer.Ordinal).ToArray();
            var queue = (jobs ?? Array.Empty<WorkshopJob>()).ToArray();
            if (queue.Any(x => x == null) ||
                queue.Select(x => x.Id.Value).Distinct(StringComparer.Ordinal).Count() != queue.Length ||
                queue.Select(x => x.StationId.Value).Distinct(StringComparer.Ordinal).Count() != queue.Length)
                throw new ArgumentException("Duplicate job/station reservation.", nameof(jobs));
            foreach (var job in queue)
            {
                var recipe = recipes.Get(job.RecipeId);
                if (recipe == null || recipe.Version != job.RecipeVersion ||
                    recipe.StationId != job.StationId ||
                    !stationArray.Contains(job.StationId) ||
                    !recipe.Outputs.Select(x => (x.ItemId, x.Quantity))
                        .SequenceEqual(job.Outputs.Select(x => (x.ItemId, x.Quantity))))
                    throw new ArgumentException("Pending job does not match an authored recipe and station.", nameof(jobs));
            }
            var marketArray = (markets ?? Array.Empty<MarketState>()).ToArray();
            if (marketArray.Any(x => x == null) ||
                marketArray.Select(x => x.Id.Value).Distinct(StringComparer.Ordinal).Count()
                    != marketArray.Length)
                throw new ArgumentException("Null or duplicate merchant ID.", nameof(markets));
            return new EconomyState(recipes, stationArray,
                queue.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray(),
                marketArray.OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToArray());
        }

        public bool CanStart(EntityId jobId, EntityId recipeId)
        {
            var recipe = Recipes.Get(recipeId);
            return jobId.IsValid && recipe != null &&
                _stations.Contains(recipe.StationId) &&
                _jobs.All(x => x.Id != jobId && x.StationId != recipe.StationId);
        }

        internal EconomyState Start(EntityId jobId, EntityId recipeId, WorldMinute current)
        {
            if (!CanStart(jobId, recipeId)) throw new ArgumentException("Unavailable recipe/station/job.");
            var recipe = Recipes.Get(recipeId);
            var job = new WorkshopJob(jobId, recipe,
                new WorldMinute(checked(current.Value + recipe.MinutesPerBatch)));
            return Create(Recipes, _stations, _jobs.Concat(new[] { job }), _markets);
        }

        public WorkshopMinuteResult CompleteDue(WorldMinute now, InventoryState inventory)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            var working = inventory;
            var remaining = new List<WorkshopJob>();
            foreach (var job in _jobs)
            {
                if (job.DueAt > now)
                {
                    remaining.Add(job);
                    continue;
                }
                var outcome = working.TryExchangeShip(
                    Array.Empty<KeyValuePair<EntityId, long>>(),
                    job.Outputs.Select(x => new KeyValuePair<EntityId, long>(x.ItemId, x.Quantity)));
                if (!outcome.Success)
                    remaining.Add(job); // Full hold: never erase prepaid work.
                else
                    working = outcome.State;
            }
            return new WorkshopMinuteResult(Create(Recipes, _stations, remaining, _markets), working);
        }

        internal EconomyState WithMarket(MarketState next)
        {
            if (next == null || GetMarket(next.Id) == null)
                throw new ArgumentException("Unknown market.", nameof(next));
            return Create(Recipes, _stations, _jobs,
                _markets.Select(x => x.Id == next.Id ? next : x));
        }

        public uint Fingerprint()
        {
            var sb = new StringBuilder();
            void Add(string value) => sb.Append(value.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(value);
            foreach (var s in _stations) { Add("station"); Add(s.Value); }
            foreach (var recipe in Recipes.Snapshot())
            {
                Add("recipe"); Add(recipe.Id.Value);
                Add(recipe.Version.ToString(CultureInfo.InvariantCulture));
                Add(recipe.StationId.Value);
                Add(recipe.MinutesPerBatch.ToString(CultureInfo.InvariantCulture));
                Add(recipe.FuelUnitsPerBatch.ToString(CultureInfo.InvariantCulture));
                foreach (var x in recipe.Inputs)
                {
                    Add("in"); Add(x.ItemId.Value);
                    Add(x.Quantity.ToString(CultureInfo.InvariantCulture));
                }
                foreach (var x in recipe.Outputs)
                {
                    Add("out"); Add(x.ItemId.Value);
                    Add(x.Quantity.ToString(CultureInfo.InvariantCulture));
                }
            }
            foreach (var job in _jobs)
            {
                Add("job"); Add(job.Id.Value); Add(job.RecipeId.Value);
                Add(job.RecipeVersion.ToString(CultureInfo.InvariantCulture));
                Add(job.StationId.Value);
                Add(job.DueAt.Value.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var market in _markets)
            {
                Add("market"); Add(market.Id.Value);
                Add(market.Credits.ToString(CultureInfo.InvariantCulture));
                foreach (var offer in market.Offers)
                {
                    Add(offer.ItemId.Value);
                    Add(offer.Stock.ToString(CultureInfo.InvariantCulture));
                    Add(offer.AskPrice.ToString(CultureInfo.InvariantCulture));
                    Add(offer.BidPrice.ToString(CultureInfo.InvariantCulture));
                }
            }
            return StableHash32.Of(sb.ToString());
        }
    }
}
