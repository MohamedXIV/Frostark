using System;
using System.Collections;
using System.Collections.Generic;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// An actual rendered Unity-frame galley interaction, not simulated
    /// Tick-only arrival. The real WorldSession owns slot, stock and Needs.
    /// </summary>
    public sealed class ShipCrewProvisionPlayModeTests
    {
        private static readonly EntityId Crew = EntityId.Parse("crew:drink:g3");
        private static readonly EntityId Room = EntityId.Parse("room:drink:g3");
        private static readonly EntityId Scene = EntityId.Parse("scene:drink:g3");
        private static readonly EntityId Galley = EntityId.Parse("module:drink:g3:galley");
        private static readonly EntityId Blueprint = EntityId.Parse("blueprint:drink:g3");
        private static readonly EntityId Water = EntityId.Parse("item:drink:g3:water");
        private static readonly EntityId Pickup = EntityId.Parse("pickup:drink:g3:water");
        private static readonly EntityId Drink = EntityId.Parse("slot:drink:g3");
        private static ShipGridCell C(int x, int y) => new ShipGridCell(Room, x, y);

        [UnityTest]
        public IEnumerator ThirstyCrewOnlyConsumesRealWaterAfterReachingAuthoredGalleySlot()
        {
            var root = new GameObject("G3-Real-Galley-Ship");
            WorldSession session = null;
            try
            {
                var layout = ShipLayoutState.Create(new[]
                {
                    new ShipCompartmentSlot(Room, 7, 7)
                }, new[]
                {
                    new ShipModuleBlueprint(Blueprint, ShipModuleKind.Auxiliary,
                        1, 1, Water, 1, 1)
                }, new[]
                {
                    new ShipModule(Galley, Blueprint, Room, 4, 4)
                });
                var items = InventoryState.Create(
                    new ItemCatalog(new[] { new ItemDefinition(Water, 1, 100, 10) }),
                    new[] { new SalvagePickup(Pickup, Water, 2) },
                    10000, 5, 10000, 5);
                items = items.TryCollect(Pickup).State.TryUnload(Water, 2).State;
                var members = CrewState.Create(new[]
                {
                    new CrewMember(Crew, DutyRole.Engineering, 920, 200,
                        0, 1440, thirst: 800)
                }, consumables: new[]
                {
                    new CrewConsumable(Water, 0, 600)
                }, interactions: CrewInteractionState.Create(new[]
                {
                    new CrewInteractionSlot(Drink, Galley,
                        CrewInteractionVerb.Drink, 0, -1, 2)
                }));
                session = new WorldSession(WorldState.Create(
                    WorldMinute.Zero, Array.Empty<SupplyStock>(),
                    new[] { Crew }, inventory: items, crewDuty: members,
                    layout: layout));
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    Scene, EntityId.Parse("anchor:drink:g3:start")));

                var map = ShipSpatialMap.Derive(layout,
                    Array.Empty<ShipModuleSpatialProfile>(),
                    Array.Empty<ShipRoomPortal>());
                var actor = new GameObject("G3-Thirsty-Crew");
                actor.transform.SetParent(root.transform, false);
                actor.transform.localPosition = new Vector3(1.5f, 0f, 1.5f);
                var walker = actor.AddComponent<ShipGridWalker>();
                walker.Configure(root.transform, map,
                    new Dictionary<EntityId, Vector3> { [Room] = Vector3.zero },
                    C(1, 1), 8f);
                var proxy = actor.AddComponent<ShipCrewSelectionProxy>();
                proxy.SetActorId(Crew);
                proxy.BindScene(Scene);

                var controller = new GameObject("G3-Galley-Controller");
                controller.transform.SetParent(root.transform, false);
                var commander = controller.AddComponent<TacticalCommandController>();
                commander.SetSession(session);
                commander.SetShipSpatialMap(map);
                commander.RegisterShipActor(proxy);
                var agency = controller.AddComponent<ShipCrewAgencyCoordinator>();
                agency.Configure(session, commander, map);

                agency.Tick(); // Deterministic activation; subsequent movement uses actual Unity frames.
                Assert.That(agency.HasPendingProvision(Crew), Is.True);
                Assert.That(walker.IsMoving, Is.True);
                Assert.That(session.Revision, Is.EqualTo(1),
                    "Only the real slot claim should be committed before moving.");
                Assert.That(session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water), Is.EqualTo(2));
                Assert.That(session.Capture().State.CrewDuty.Get(Crew).Thirst,
                    Is.EqualTo(800));
                Assert.That(session.Capture().State.CrewDuty.Interactions.GetClaim(Drink)
                    .CrewId, Is.EqualTo(Crew));

                var arrived = false;
                for (int frame = 0; frame < 450 && agency.HasPendingProvision(Crew); frame++)
                {
                    yield return null; // Real ShipGridWalker.Update, not simulated Tick.
                    arrived |= walker.LastReachedCell == C(4, 3);
                }
                agency.Tick();
                Assert.That(arrived, Is.True,
                    "Crew must reach the authored clearance cell before using water.");
                Assert.That(agency.HasPendingProvision(Crew), Is.False,
                    "A completed provision cannot repeat its own use.");
                Assert.That(walker.LastReachedCell, Is.EqualTo(C(4, 3)));
                Assert.That(walker.HasIncomingReservation, Is.False);
                Assert.That(session.Revision, Is.EqualTo(2),
                    "One claim and one real Use commit, exactly once.");
                Assert.That(session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water), Is.EqualTo(1));
                Assert.That(session.Capture().State.CrewDuty.Get(Crew).Thirst,
                    Is.EqualTo(200));
                Assert.That(session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty);
                for (int frame = 0; frame < 5; frame++) yield return null;
                Assert.That(session.Revision, Is.EqualTo(2));
                Assert.That(session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water), Is.EqualTo(1));
                Assert.That(session.Minute, Is.EqualTo(WorldMinute.Zero),
                    "Physical interaction does not manufacture a simulation minute.");
            }
            finally
            {
                root.SetActive(false);
                UnityEngine.Object.Destroy(root);
                session?.Dispose();
            }
        }
    }
}
