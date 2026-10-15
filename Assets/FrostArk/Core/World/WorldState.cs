using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using FrostArk.Core.Crew;
using FrostArk.Core.Combat;
using FrostArk.Core.Inventory;
using FrostArk.Core.Economy;
using FrostArk.Core.Ship;
using FrostArk.Core.Worldgen;

namespace FrostArk.Core.World
{
    /// <summary>
    /// Canonical immutable-on-commit world aggregate for early R0 systems.
    /// Collections never escape to consumers. A successful command creates a
    /// new revision; a rejected command retains the identical prior object.
    /// </summary>
    public sealed class WorldState
    {
        private readonly Dictionary<string, long> _supplies;
        private readonly HashSet<string> _crewIds;
        private readonly Dictionary<string, string> _crewStations;
        private readonly HashSet<string> _processedCommands;
        public InventoryState Inventory { get; }
        public ShipThermalState Thermal { get; }
        public CrewState CrewDuty { get; }
        public ShipLayoutState Layout { get; }
        public ShipNavigationState Navigation { get; }
        public EconomyState Economy { get; }
        public CombatState Combat { get; }
        public IslandSiteProgress SiteProgress { get; }

        public WorldMinute Minute { get; }
        public long Revision { get; }

        private WorldState(WorldMinute minute, long revision,
            Dictionary<string, long> supplies, HashSet<string> crewIds,
            Dictionary<string, string> crewStations, HashSet<string> processedCommands,
            InventoryState inventory, ShipThermalState thermal, CrewState crewDuty, ShipLayoutState layout,
            ShipNavigationState navigation, EconomyState economy, CombatState combat,
            IslandSiteProgress siteProgress)
        {
            Minute = minute;
            Revision = revision;
            _supplies = supplies;
            _crewIds = crewIds;
            _crewStations = crewStations;
            _processedCommands = processedCommands;
            Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
            Thermal = thermal ?? throw new ArgumentNullException(nameof(thermal));
            CrewDuty = crewDuty ?? throw new ArgumentNullException(nameof(crewDuty));
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            Economy = economy ?? throw new ArgumentNullException(nameof(economy));
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            SiteProgress = siteProgress ?? throw new ArgumentNullException(nameof(siteProgress));
        }

        public static WorldState Create(WorldMinute minute,
            IEnumerable<SupplyStock> supplies, IEnumerable<EntityId> crew,
            InventoryState inventory = null, ShipThermalState thermal = null,
            CrewState? crewDuty = null, ShipLayoutState? layout = null, ShipNavigationState? navigation = null, EconomyState? economy = null, CombatState? combat = null,
            IslandSiteProgress? siteProgress = null)
        {
            if (supplies == null)
                throw new ArgumentNullException(nameof(supplies));
            if (crew == null)
                throw new ArgumentNullException(nameof(crew));

            var supplyMap = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var supply in supplies)
            {
                if (!supply.Id.IsValid || supply.Quantity < 0 ||
                    !supplyMap.TryAdd(supply.Id.Value, supply.Quantity))
                    throw new ArgumentException("Supply identities must be unique and non-negative.", nameof(supplies));
            }

            var crews = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in crew)
            {
                if (!member.IsValid || !crews.Add(member.Value))
                    throw new ArgumentException("Crew identities must be valid and unique.", nameof(crew));
            }

            var duty = crewDuty ?? CrewState.Empty;
            foreach (var protocol in duty.Treatments)
                if (!(inventory ?? InventoryState.Empty).HasItem(protocol.ItemId))
                    throw new ArgumentException("Treatment items must be authored in InventoryState.",
                        nameof(crewDuty));
            foreach (var member in duty.Members)
                if (!crews.Contains(member.Id.Value))
                    throw new ArgumentException("Authored crew member is absent from world registry.", nameof(crewDuty));
            var shipLayout = layout ?? ShipLayoutState.Empty;
            foreach (var module in shipLayout.Modules)
                if (module.RepairReserved && duty.GetRepair(module.Id) == null)
                    throw new ArgumentException(
                        "A reserved ship module requires a persistent paid repair job.", nameof(layout));

            var equipped = combat ?? CombatState.Empty;
            foreach (var weapon in equipped.Weapons)
            {
                if (!(inventory ?? InventoryState.Empty).HasItem(weapon.Definition.Id) ||
                    !(inventory ?? InventoryState.Empty).HasItem(weapon.Definition.AmmoItemId))
                    throw new ArgumentException("Weapons and ammunition require authored item definitions.",
                        nameof(combat));
            }
            return new WorldState(minute, 0, supplyMap, crews,
                new Dictionary<string, string>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal), inventory ?? InventoryState.Empty,
                thermal ?? ShipThermalState.Empty, duty, shipLayout,
                navigation ?? ShipNavigationState.Empty, economy ?? EconomyState.Empty,
                equipped, siteProgress ?? IslandSiteProgress.Empty);
        }

        public bool HasSupply(EntityId id) => id.IsValid && _supplies.ContainsKey(id.Value);
        public long GetSupply(EntityId id) =>
            id.IsValid && _supplies.TryGetValue(id.Value, out var amount) ? amount : 0;
        public bool HasCrew(EntityId id) => id.IsValid && _crewIds.Contains(id.Value);

        public EntityId GetCrewStation(EntityId crewId) =>
            crewId.IsValid && _crewStations.TryGetValue(crewId.Value, out var station)
                ? EntityId.Parse(station) : default;

        internal bool HasProcessed(EntityId commandId) =>
            commandId.IsValid && _processedCommands.Contains(commandId.Value);

        private WorldState CopyWithCommand(string commandId,
            Dictionary<string, long> supplies, Dictionary<string, string> crewStations,
            InventoryState inventory = null, CrewState? crewDuty = null, ShipLayoutState? layout = null, ShipNavigationState? navigation = null,
            EconomyState? economy = null, CombatState? combat = null,
            IslandSiteProgress? siteProgress = null)
        {
            long nextRevision = checked(Revision + 1);
            var commands = new HashSet<string>(_processedCommands, StringComparer.Ordinal);
            commands.Add(commandId);
            return new WorldState(Minute, nextRevision, supplies,
                _crewIds, crewStations, commands, inventory ?? Inventory, Thermal,
                crewDuty ?? CrewDuty, layout ?? Layout, navigation ?? Navigation,
                economy ?? Economy, combat ?? Combat, siteProgress ?? SiteProgress);
        }

        internal WorldState WithWithdrawal(EntityId supplyId, long amount, EntityId commandId)
        {
            var supplies = new Dictionary<string, long>(_supplies, StringComparer.Ordinal);
            supplies[supplyId.Value] = checked(supplies[supplyId.Value] - amount);
            return CopyWithCommand(commandId.Value, supplies, _crewStations);
        }

        internal WorldState WithAssignment(EntityId crewId, EntityId stationId, EntityId commandId)
        {
            var stations = new Dictionary<string, string>(_crewStations, StringComparer.Ordinal);
            stations.Add(crewId.Value, stationId.Value);
            return CopyWithCommand(commandId.Value, _supplies, stations);
        }

        internal WorldState WithUnassignment(EntityId crewId, EntityId commandId)
        {
            var stations = new Dictionary<string, string>(_crewStations, StringComparer.Ordinal);
            stations.Remove(crewId.Value);
            return CopyWithCommand(commandId.Value, _supplies, stations);
        }


        internal WorldState WithInventoryChange(InventoryState inventory, EntityId commandId)
        {
            if (inventory == null) throw new ArgumentNullException(nameof(inventory));
            return CopyWithCommand(commandId.Value, _supplies, _crewStations, inventory);
        }

        internal WorldState WithCrewTreatment(CrewState healed, InventoryState paid,
            EntityId commandId)
        {
            if (healed == null || paid == null) throw new ArgumentNullException(nameof(healed));
            return CopyWithCommand(commandId.Value, _supplies, _crewStations,
                inventory: paid, crewDuty: healed);
        }

        internal WorldState WithCrewWound(CrewState wounded, EntityId commandId)
        {
            if (wounded == null) throw new ArgumentNullException(nameof(wounded));
            return CopyWithCommand(commandId.Value, _supplies, _crewStations,
                crewDuty: wounded);
        }

        internal WorldState WithCrewAway(EntityId crewId, bool away, EntityId commandId) =>
            CopyWithCommand(commandId.Value, _supplies, _crewStations,
                crewDuty: CrewDuty.SetAway(crewId, away));

        internal WorldState WithShipLayoutChange(ShipLayoutState layout,
            InventoryState inventory, CrewState crewDuty, EntityId commandId) =>
            CopyWithCommand(commandId.Value, _supplies, _crewStations, inventory, crewDuty, layout);

        internal WorldState WithMarketTrade(EconomyState next, InventoryState inventory,
            long credits, EntityId commandId)
        {
            if (credits < 0 || !HasSupply(EntityId.Parse("supply:credits")))
                throw new ArgumentException("Trade requires existing wallet with nonnegative balance.");
            var supplies = new Dictionary<string, long>(_supplies, StringComparer.Ordinal);
            supplies["supply:credits"] = credits;
            return CopyWithCommand(commandId.Value, supplies, _crewStations,
                inventory: inventory, economy: next);
        }

        /// <summary>Trusted session fixed-combat-tick projection; not a player command
        /// and not a second world calendar. Hits are NOT resolved here.</summary>
        public WorldState AtCombatTick(long tick)
        {
            var next = Combat.AtTick(tick);
            if (ReferenceEquals(next, Combat)) return this;
            return new WorldState(Minute, Revision, _supplies, _crewIds,
                _crewStations, _processedCommands, Inventory, Thermal, CrewDuty,
                Layout, Navigation, Economy, next, SiteProgress);
        }

        internal WorldState WithWeaponShot(EntityId weaponId, InventoryState paidAmmo,
            EntityId commandId)
        {
            if (paidAmmo == null) throw new ArgumentNullException(nameof(paidAmmo));
            return CopyWithCommand(commandId.Value, _supplies, _crewStations,
                inventory: paidAmmo, combat: Combat.Fired(weaponId));
        }

        internal WorldState WithSiteChange(IslandSiteProgress next, EntityId commandId)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            return CopyWithCommand(commandId.Value, _supplies, _crewStations,
                siteProgress: next);
        }

        internal WorldState WithWorkshopStart(EconomyState next, InventoryState inventory,
            long fuelUsed, EntityId commandId)
        {
            if (fuelUsed < 0) throw new ArgumentOutOfRangeException(nameof(fuelUsed));
            var supplies = new Dictionary<string, long>(_supplies, StringComparer.Ordinal);
            var fuel = EntityId.Parse("supply:fuel");
            if (fuelUsed > 0)
            {
                if (!HasSupply(fuel) || GetSupply(fuel) < fuelUsed)
                    throw new ArgumentException("Insufficient world fuel.");
                supplies[fuel.Value] = GetSupply(fuel) - fuelUsed;
            }
            return CopyWithCommand(commandId.Value, supplies, _crewStations,
                inventory: inventory, economy: next);
        }

        /// <summary>Commits due jobs once. A no-Heat scheduler may also use
        /// this as its minute owner; never increments player-command revision.</summary>
        internal WorldState WithWorkshopMinute(WorldMinute minute, EconomyState next,
            InventoryState inventory)
        {
            if (next == null || inventory == null) throw new ArgumentNullException(nameof(next));
            if (minute.Value != Minute.Value && minute.Value != checked(Minute.Value + 1))
                throw new InvalidOperationException("Workshop time and WorldClock diverged.");
            return new WorldState(minute, Revision, _supplies, _crewIds,
                _crewStations, _processedCommands, inventory, Thermal, CrewDuty, Layout,
                Navigation, next, Combat, SiteProgress);
        }

        internal WorldState WithNavigationCommand(ShipNavigationState navigation, EntityId commandId) =>
            CopyWithCommand(commandId.Value, _supplies, _crewStations,
                navigation: navigation);

        // Crew phase runs before Heat and does not advance world time itself.
        // The heat adapter commits the same minute once after all crew effects.
        internal WorldState WithCrewTick(WorldMinute minute, CrewState nextCrew)
        {
            if (nextCrew == null) throw new ArgumentNullException(nameof(nextCrew));
            if (minute.Value != checked(Minute.Value + 1))
                throw new ArgumentOutOfRangeException(nameof(minute));
            // Only a previously paid, persisted module repair reservation may
            // restore integrity. Unrelated legacy repair jobs cannot heal modules.
            var updatedLayout = Layout;
            foreach (var job in CrewDuty.RepairJobs)
                if (nextCrew.GetRepair(job.Id) == null)
                    updatedLayout = updatedLayout.CompleteRepair(job.Id);
            return new WorldState(Minute, Revision, _supplies, _crewIds,
                _crewStations, _processedCommands, Inventory, Thermal, nextCrew, updatedLayout, Navigation, Economy, Combat, SiteProgress);
        }

        /// <summary>
        /// Commit one *simulation* minute without manufacturing a command ID or
        /// incrementing a player-command revision. Existing command/replay
        /// provenance remains bounded across weeks of thermal ticks.
        /// </summary>
        internal WorldState WithThermalMinute(WorldMinute minute, ThermalTickResult tick)
        {
            if (tick == null) throw new ArgumentNullException(nameof(tick));
            if (minute.Value != checked(Minute.Value + 1))
                throw new ArgumentOutOfRangeException(nameof(minute), "Thermal steps require consecutive minutes.");
            if (Thermal.Rooms.Count == 0)
                throw new InvalidOperationException("Cannot apply thermal step to an empty ship.");
            if (tick.FuelConsumed < 0)
                throw new ArgumentOutOfRangeException(nameof(tick));
            var fuelId = EntityId.Parse("supply:fuel");
            var amount = GetSupply(fuelId);
            if (tick.FuelConsumed > amount)
                throw new InvalidOperationException("Boiler cannot burn fuel not held by canonical WorldState.");
            var supplies = new Dictionary<string, long>(_supplies, StringComparer.Ordinal);
            if (tick.FuelConsumed > 0)
                supplies[fuelId.Value] = checked(amount - tick.FuelConsumed);
            return new WorldState(minute, Revision, supplies, _crewIds,
                _crewStations, _processedCommands, Inventory, tick.State, CrewDuty, Layout, Navigation, Economy, Combat, SiteProgress);
        }

        /// <summary>Travel phase uses the minute already committed by Heat.</summary>
        internal WorldState WithTravelTick(WorldMinute minute, NavigationTickResult tick)
        {
            if (tick == null) throw new ArgumentNullException(nameof(tick));
            if (minute != Minute)
                throw new InvalidOperationException("Travel must follow the authoritative Heat phase.");
            var fuelId = EntityId.Parse("supply:fuel");
            var remaining = GetSupply(fuelId);
            if (tick.FuelConsumed <= 0 || tick.FuelConsumed > remaining)
                throw new InvalidOperationException("Travel requires a positive, available fuel payment.");
            var supplies = new Dictionary<string, long>(_supplies, StringComparer.Ordinal);
            supplies[fuelId.Value] = checked(remaining - tick.FuelConsumed);
            return new WorldState(Minute, Revision, supplies, _crewIds, _crewStations,
                _processedCommands, Inventory, Thermal, CrewDuty, Layout, tick.State, Economy, Combat, SiteProgress);
        }

        /// <summary>
        /// Explicit world-minute projection; commands remain revision controlled.
        /// No existing snapshot is mutated by advancing the authoritative clock.
        /// </summary>
        public WorldState AtMinute(WorldMinute minute)
        {
            if (minute < Minute)
                throw new ArgumentOutOfRangeException(nameof(minute), "World time cannot go backwards.");
            return new WorldState(minute, Revision, _supplies, _crewIds, _crewStations, _processedCommands, Inventory, Thermal, CrewDuty, Layout, Navigation, Economy, Combat, SiteProgress);
        }

        internal IReadOnlyList<SupplyStock> SnapshotSupplies() =>
            _supplies.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new SupplyStock(EntityId.Parse(p.Key), p.Value)).ToArray();

        internal IReadOnlyList<EntityId> SnapshotCrew() =>
            _crewIds.OrderBy(s => s, StringComparer.Ordinal).Select(EntityId.Parse).ToArray();

        internal IReadOnlyList<KeyValuePair<EntityId, EntityId>> SnapshotAssignments() =>
            _crewStations.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new KeyValuePair<EntityId, EntityId>(
                    EntityId.Parse(p.Key), EntityId.Parse(p.Value))).ToArray();

        internal IReadOnlyList<EntityId> SnapshotProcessedCommands() =>
            _processedCommands.OrderBy(s => s, StringComparer.Ordinal).Select(EntityId.Parse).ToArray();

        internal static WorldState RestoreSnapshot(WorldMinute minute, long revision,
            IEnumerable<SupplyStock> supplies, IEnumerable<EntityId> crew,
            IEnumerable<KeyValuePair<EntityId, EntityId>> assignments,
            IEnumerable<EntityId> processed, InventoryState inventory = null,
            ShipThermalState thermal = null, CrewState? crewDuty = null, ShipLayoutState? layout = null, ShipNavigationState? navigation = null,
            EconomyState? economy = null, CombatState? combat = null,
            IslandSiteProgress? siteProgress = null)
        {
            if (revision < 0)
                throw new ArgumentOutOfRangeException(nameof(revision));
            var baseline = Create(minute, supplies, crew, inventory: inventory, crewDuty: crewDuty, layout: layout, navigation: navigation,
                economy: economy, combat: combat, siteProgress: siteProgress);
            var stations = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in assignments ?? throw new ArgumentNullException(nameof(assignments)))
            {
                if (!baseline.HasCrew(pair.Key) || !pair.Value.IsValid ||
                    !stations.TryAdd(pair.Key.Value, pair.Value.Value))
                    throw new ArgumentException("Invalid or duplicate crew assignment.", nameof(assignments));
            }

            var commands = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in processed ?? throw new ArgumentNullException(nameof(processed)))
            {
                if (!id.IsValid || !commands.Add(id.Value))
                    throw new ArgumentException("Duplicate or invalid command provenance.", nameof(processed));
            }
            if (commands.Count != revision)
                throw new ArgumentException("Revision and processed command count disagree.", nameof(revision));
            long shots = 0;
            foreach (var weapon in baseline.Combat.Weapons)
                shots = checked(shots + weapon.ShotCount);
            if (shots > revision)
                throw new ArgumentException("Weapon shot count exceeds committed command provenance.", nameof(combat));
            return new WorldState(minute, revision, baseline._supplies,
                baseline._crewIds, stations, commands, inventory ?? InventoryState.Empty,
                thermal ?? ShipThermalState.Empty, baseline.CrewDuty, baseline.Layout,
                baseline.Navigation, baseline.Economy, baseline.Combat, baseline.SiteProgress);
        }

        /// <summary>
        /// Stable FNV-1a replay checkpoint, NOT a cryptographic signature or a
        /// persistence substitute. Length prefixes avoid ambiguous delimiters.
        /// </summary>
        public uint Fingerprint()
        {
            var canonical = new StringBuilder();

            void Add(string part)
            {
                canonical.Append(part.Length.ToString(CultureInfo.InvariantCulture))
                    .Append(':').Append(part);
            }

            Add(Minute.Value.ToString(CultureInfo.InvariantCulture));
            Add(Revision.ToString(CultureInfo.InvariantCulture));
            foreach (var pair in _supplies.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                Add("s"); Add(pair.Key); Add(pair.Value.ToString(CultureInfo.InvariantCulture));
            }
            foreach (var id in _crewIds.OrderBy(s => s, StringComparer.Ordinal))
            {
                Add("c"); Add(id);
            }
            foreach (var pair in _crewStations.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                Add("j"); Add(pair.Key); Add(pair.Value);
            }
            foreach (var id in _processedCommands.OrderBy(s => s, StringComparer.Ordinal))
            {
                Add("x"); Add(id);
            }
            Add("thermal");
            Add(Thermal.Fingerprint().ToString(CultureInfo.InvariantCulture));
            Add("inventory");
            Add(Inventory.Fingerprint().ToString(CultureInfo.InvariantCulture));
            Add("crew-duty");
            Add(CrewDuty.Fingerprint().ToString(CultureInfo.InvariantCulture));
            Add("ship-layout"); Add(Layout.Fingerprint().ToString(CultureInfo.InvariantCulture));
            Add("navigation"); Add(Navigation.Fingerprint().ToString(CultureInfo.InvariantCulture));
            Add("economy"); Add(Economy.Fingerprint().ToString(CultureInfo.InvariantCulture));
            Add("combat"); Add(Combat.Fingerprint().ToString(CultureInfo.InvariantCulture));
            Add("site-progress"); Add(SiteProgress.Fingerprint().ToString(CultureInfo.InvariantCulture));
            return StableHash32.Of(canonical.ToString());
        }
    }
}