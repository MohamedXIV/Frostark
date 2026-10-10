using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrostArk.Core.Economy;
using FrostArk.Core.World;

namespace FrostArk.Core.Save
{
    public static partial class SaveCodec
    {
        private static void WriteEconomy(BinaryWriter writer, EconomyState economy)
        {
            var recipes = economy.Recipes.Snapshot();
            WriteCount(writer, recipes.Count);
            foreach (var recipe in recipes)
            {
                WriteId(writer, recipe.Id);
                writer.Write(recipe.Version);
                WriteId(writer, recipe.StationId);
                writer.Write(recipe.MinutesPerBatch);
                WriteCount(writer, recipe.Inputs.Count);
                foreach (var row in recipe.Inputs)
                {
                    WriteId(writer, row.ItemId);
                    writer.Write(row.Quantity);
                }
                WriteCount(writer, recipe.Outputs.Count);
                foreach (var row in recipe.Outputs)
                {
                    WriteId(writer, row.ItemId);
                    writer.Write(row.Quantity);
                }
            }
            WriteCount(writer, economy.Stations.Count);
            foreach (var id in economy.Stations) WriteId(writer, id);
            WriteCount(writer, economy.Jobs.Count);
            foreach (var job in economy.Jobs)
            {
                WriteId(writer, job.Id);
                WriteId(writer, job.RecipeId);
                writer.Write(job.RecipeVersion);
                WriteId(writer, job.StationId);
                writer.Write(job.DueAt.Value);
            }

            WriteCount(writer, economy.Markets.Count);
            foreach (var market in economy.Markets)
            {
                WriteId(writer, market.Id);
                writer.Write(market.Credits);
                WriteCount(writer, market.Offers.Count);
                foreach (var offer in market.Offers)
                {
                    WriteId(writer, offer.ItemId);
                    writer.Write(offer.Stock);
                    writer.Write(offer.AskPrice);
                    writer.Write(offer.BidPrice);
                }
            }
        }

        private static EconomyState ReadEconomy(BinaryReader reader)
        {
            var recipes = new List<WorkshopRecipe>();
            List<RecipeStack> ReadRows()
            {
                var rows = new List<RecipeStack>();
                for (int i = 0, count = ReadCount(reader); i < count; i++)
                    rows.Add(new RecipeStack(ReadId(reader), reader.ReadInt64()));
                return rows;
            }
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var id = ReadId(reader);
                int version = reader.ReadInt32();
                var station = ReadId(reader);
                long minutes = reader.ReadInt64();
                var input = ReadRows();
                var output = ReadRows();
                recipes.Add(new WorkshopRecipe(id, version, station, minutes, input, output));
            }
            var catalog = new WorkshopCatalog(recipes);
            var stations = new List<EntityId>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
                stations.Add(ReadId(reader));
            var jobs = new List<WorkshopJob>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var id = ReadId(reader);
                var recipeId = ReadId(reader);
                int version = reader.ReadInt32();
                var station = ReadId(reader);
                var due = new WorldMinute(reader.ReadInt64());
                var recipe = catalog.Get(recipeId);
                if (recipe == null || recipe.Version != version ||
                    recipe.StationId != station)
                    throw new InvalidDataException("Unknown or mismatched prepaid workshop job.");
                jobs.Add(new WorkshopJob(id, recipe, due));
            }
            var markets = new List<MarketState>();
            for (int i = 0, count = ReadCount(reader); i < count; i++)
            {
                var marketId = ReadId(reader);
                long cash = reader.ReadInt64();
                var listings = new List<MarketListing>();
                for (int j = 0, offers = ReadCount(reader); j < offers; j++)
                    listings.Add(new MarketListing(ReadId(reader), reader.ReadInt64(),
                        reader.ReadInt64(), reader.ReadInt64()));
                markets.Add(new MarketState(marketId, cash, listings));
            }
            return EconomyState.Create(catalog, stations, jobs, markets);
        }
    }
}
