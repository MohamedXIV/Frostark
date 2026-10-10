using System;
using FrostArk.Core.Events;
using FrostArk.Core.Inventory;
using FrostArk.Core.World;

namespace FrostArk.Core.Commands
{
    /// <summary>
    /// Stateless, synchronous domain executor. Caller owns the authoritative
    /// current WorldState pointer and serializes all command commits. Every
    /// rejection returns the exact original state and no emitted events.
    /// </summary>
    public sealed class WorldCommandBus
    {
        public CommandResult Execute(WorldState state, IGameCommand command)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (command == null)
                throw new ArgumentNullException(nameof(command));

            if (!command.Id.IsValid)
                return CommandResult.Rejected(state, CommandFailure.InvalidCommandId);
            if (state.HasProcessed(command.Id))
                return CommandResult.Rejected(state, CommandFailure.DuplicateCommand);
            if (command.ExpectedRevision != state.Revision)
                return CommandResult.Rejected(state, CommandFailure.StaleRevision);

            if (command is WithdrawSupplyCommand withdrawal)
            {
                if (withdrawal.Quantity <= 0)
                    return CommandResult.Rejected(state, CommandFailure.InvalidQuantity);
                if (!state.HasSupply(withdrawal.SupplyId))
                    return CommandResult.Rejected(state, CommandFailure.UnknownSupply);
                if (state.GetSupply(withdrawal.SupplyId) < withdrawal.Quantity)
                    return CommandResult.Rejected(state, CommandFailure.InsufficientSupply);

                var next = state.WithWithdrawal(withdrawal.SupplyId, withdrawal.Quantity, withdrawal.Id);
                return CommandResult.Committed(next, new WorldEvent(command.Id,
                    state.Minute, next.Revision, WorldEventKind.SupplyWithdrawn,
                    withdrawal.SupplyId, quantity: withdrawal.Quantity));
            }

            if (command is AssignCrewCommand assignment)
            {
                if (!state.HasCrew(assignment.CrewId))
                    return CommandResult.Rejected(state, CommandFailure.UnknownCrew);
                if (!assignment.StationId.IsValid)
                    return CommandResult.Rejected(state, CommandFailure.InvalidStation);
                if (state.GetCrewStation(assignment.CrewId).IsValid)
                    return CommandResult.Rejected(state, CommandFailure.CrewAlreadyAssigned);

                var next = state.WithAssignment(assignment.CrewId, assignment.StationId, assignment.Id);
                return CommandResult.Committed(next, new WorldEvent(command.Id,
                    state.Minute, next.Revision, WorldEventKind.CrewAssigned,
                    assignment.CrewId, assignment.StationId));
            }

            if (command is UnassignCrewCommand unassignment)
            {
                if (!state.HasCrew(unassignment.CrewId))
                    return CommandResult.Rejected(state, CommandFailure.UnknownCrew);
                var stationId = state.GetCrewStation(unassignment.CrewId);
                if (!stationId.IsValid)
                    return CommandResult.Rejected(state, CommandFailure.CrewNotAssigned);

                var next = state.WithUnassignment(unassignment.CrewId, unassignment.Id);
                return CommandResult.Committed(next, new WorldEvent(command.Id,
                    state.Minute, next.Revision, WorldEventKind.CrewUnassigned,
                    unassignment.CrewId, stationId));
            }


            if (command is SetCrewAwayCommand shore)
            {
                if (!state.HasCrew(shore.CrewId))
                    return CommandResult.Rejected(state, CommandFailure.UnknownCrew);
                var member = state.CrewDuty.Get(shore.CrewId);
                if (member == null)
                    return CommandResult.Rejected(state, CommandFailure.UnknownCrew);
                if (member.IsAway == shore.Away)
                    return CommandResult.Rejected(state, CommandFailure.CrewAvailabilityUnchanged);
                var next = state.WithCrewAway(shore.CrewId, shore.Away, command.Id);
                return CommandResult.Committed(next, new WorldEvent(command.Id,
                    state.Minute, next.Revision,
                    shore.Away ? WorldEventKind.CrewDispatched : WorldEventKind.CrewReturned,
                    shore.CrewId));
            }

            if (command is BuildShipModuleCommand build)
            {
                var blueprint = state.Layout.GetBlueprint(build.BlueprintId);
                if (blueprint == null)
                    return CommandResult.Rejected(state, CommandFailure.UnknownBlueprint);
                if (!state.Layout.CanPlace(build.ModuleId, build.BlueprintId,
                    build.RoomId, build.X, build.Y))
                    return CommandResult.Rejected(state, CommandFailure.InvalidPlacement);
                var spend = state.Inventory.TryConsumeShip(blueprint.PartId, blueprint.BuildCost);
                if (!spend.Success)
                    return CommandResult.Rejected(state, CommandFailure.InsufficientMaterials);
                var next = state.WithShipLayoutChange(state.Layout.Place(
                    build.ModuleId, build.BlueprintId, build.RoomId, build.X, build.Y),
                    spend.State, state.CrewDuty, command.Id);
                return CommandResult.Committed(next, new WorldEvent(command.Id, state.Minute,
                    next.Revision, WorldEventKind.ShipModuleBuilt, build.ModuleId));
            }
            if (command is DamageShipModuleCommand damage)
            {
                var module = state.Layout.GetModule(damage.ModuleId);
                if (module == null)
                    return CommandResult.Rejected(state, CommandFailure.UnknownModule);
                if (damage.DamagePermille <= 0 || damage.DamagePermille > 1000 ||
                    module.IntegrityPermille == 0)
                    return CommandResult.Rejected(state, CommandFailure.InvalidQuantity);
                var next = state.WithShipLayoutChange(state.Layout.Damage(damage.ModuleId,
                    damage.DamagePermille), state.Inventory, state.CrewDuty, command.Id);
                return CommandResult.Committed(next, new WorldEvent(command.Id, state.Minute,
                    next.Revision, WorldEventKind.ShipModuleDamaged, damage.ModuleId,
                    quantity: damage.DamagePermille));
            }
            if (command is StartShipModuleRepairCommand repair)
            {
                var module = state.Layout.GetModule(repair.ModuleId);
                if (module == null)
                    return CommandResult.Rejected(state, CommandFailure.UnknownModule);
                if (module.IntegrityPermille == 1000)
                    return CommandResult.Rejected(state, CommandFailure.ModuleNotDamaged);
                if (module.RepairReserved || state.CrewDuty.GetRepair(repair.ModuleId) != null)
                    return CommandResult.Rejected(state, CommandFailure.ModuleAlreadyRepairing);
                var type = state.Layout.GetBlueprint(module.BlueprintId);
                var spend = state.Inventory.TryConsumeShip(type!.PartId, type.RepairCost);
                if (!spend.Success)
                    return CommandResult.Rejected(state, CommandFailure.InsufficientMaterials);
                var next = state.WithShipLayoutChange(state.Layout.ReserveRepair(repair.ModuleId),
                    spend.State, state.CrewDuty.EnqueueRepair(repair.ModuleId, 500), command.Id);
                return CommandResult.Committed(next, new WorldEvent(command.Id, state.Minute,
                    next.Revision, WorldEventKind.ShipModuleRepairStarted, repair.ModuleId,
                    quantity: type.RepairCost));
            }

            if (command is CollectSalvageCommand collect)
            {
                var outcome = state.Inventory.TryCollect(collect.PickupId);
                if (!outcome.Success)
                    return CommandResult.Rejected(state, MapInventoryFailure(outcome.Error));
                var next = state.WithInventoryChange(outcome.State, command.Id);
                return CommandResult.Committed(next, new WorldEvent(command.Id,
                    state.Minute, next.Revision, WorldEventKind.SalvageCollected,
                    collect.PickupId));
            }

            if (command is UnloadToShipCommand unload)
            {
                var outcome = state.Inventory.TryUnload(unload.ItemId, unload.Quantity);
                if (!outcome.Success)
                    return CommandResult.Rejected(state, MapInventoryFailure(outcome.Error));
                var next = state.WithInventoryChange(outcome.State, command.Id);
                return CommandResult.Committed(next, new WorldEvent(command.Id,
                    state.Minute, next.Revision, WorldEventKind.SalvageUnloaded,
                    unload.ItemId, quantity: unload.Quantity));
            }

            return CommandResult.Rejected(state, CommandFailure.UnsupportedCommand);
        }

        private static CommandFailure MapInventoryFailure(InventoryError error)
        {
            switch (error)
            {
                case InventoryError.UnknownPickup: return CommandFailure.UnknownPickup;
                case InventoryError.AlreadyCollected: return CommandFailure.AlreadyCollected;
                case InventoryError.UnknownItem: return CommandFailure.UnknownItem;
                case InventoryError.InvalidQuantity: return CommandFailure.InvalidQuantity;
                case InventoryError.InsufficientStock: return CommandFailure.InsufficientInventory;
                case InventoryError.TooHeavy: return CommandFailure.InventoryTooHeavy;
                case InventoryError.NotEnoughSlots: return CommandFailure.InventoryNoSlots;
                default: throw new ArgumentOutOfRangeException(nameof(error));
            }
        }
    }
}