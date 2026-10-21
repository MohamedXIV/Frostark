using System;
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
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    public sealed class ShipCrewAgencyEditModeTests
    {
        private static readonly EntityId A = EntityId.Parse("crew:agency:engineer");
        private static readonly EntityId Room = EntityId.Parse("room:agency:bridge");
        private static readonly EntityId Scene = EntityId.Parse("scene:agency:ship");
        private static readonly EntityId Galley = EntityId.Parse("module:agency:galley");
        private static readonly EntityId Blueprint = EntityId.Parse("blueprint:agency:galley");
        private static readonly EntityId Water = EntityId.Parse("item:agency:water");
        private static readonly EntityId Pickup = EntityId.Parse("pickup:agency:water");
        private static readonly EntityId Slot = EntityId.Parse("slot:agency:drink");
        private static ShipGridCell C(int x, int y) => new ShipGridCell(Room, x, y);

        private sealed class Fixture : IDisposable
        {
            public readonly GameObject Root = new GameObject("G2-Agency-Ship");
            public readonly GameObject Input = new GameObject("G2-Agency-Input");
            public readonly WorldSession Session;
            public readonly TacticalCommandController Commander;
            public readonly ShipGridWalker Walker;
            public readonly ShipCrewAgencyCoordinator Agency;

            public Fixture(int thirst = 800, bool stocked = true)
            {
                var layout = ShipLayoutState.Create(new[]
                {
                    new ShipCompartmentSlot(Room, 6, 6)
                }, new[]
                {
                    new ShipModuleBlueprint(Blueprint, ShipModuleKind.Auxiliary,
                        1, 1, Water, 1, 1)
                }, Array.Empty<ShipModule>());
                layout = layout.Place(Galley, Blueprint, Room, 4, 4);
                var inventory = InventoryState.Create(
                    new ItemCatalog(new[] { new ItemDefinition(Water, 1, 100, 10) }),
                    new[] { new SalvagePickup(Pickup, Water, 2) },
                    10000, 5, 10000, 5);
                if (stocked)
                    inventory = inventory.TryCollect(Pickup).State.TryUnload(Water, 2).State;
                var crew = CrewState.Create(new[]
                {
                    new CrewMember(A, DutyRole.Engineering, 900, 100, 0, 1440,
                        thirst: thirst)
                }, consumables: new[] { new CrewConsumable(Water, 0, 600) },
                interactions: CrewInteractionState.Create(new[]
                {
                    new CrewInteractionSlot(Slot, Galley, CrewInteractionVerb.Drink,
                        0, -1, 2)
                }));
                Session = new WorldSession(WorldState.Create(WorldMinute.Zero,
                    Array.Empty<SupplyStock>(), new[] { A },
                    crewDuty: crew, layout: layout, inventory: inventory));
                Session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    Scene, EntityId.Parse("anchor:agency:start")));
                var map = ShipSpatialMap.Derive(layout,
                    Array.Empty<ShipModuleSpatialProfile>(),
                    Array.Empty<ShipRoomPortal>());
                var actor = new GameObject("AgencyEngineer");
                actor.transform.SetParent(Root.transform, false);
                actor.transform.localPosition = new Vector3(1.5f, 0f, 1.5f);
                Walker = actor.AddComponent<ShipGridWalker>();
                Walker.Configure(Root.transform, map,
                    new Dictionary<EntityId, Vector3> { [Room] = Vector3.zero },
                    C(1, 1), 4f);
                var proxy = actor.AddComponent<ShipCrewSelectionProxy>();
                proxy.SetActorId(A);
                proxy.BindScene(Scene);
                Commander = Input.AddComponent<TacticalCommandController>();
                Commander.SetSession(Session);
                Commander.SetShipSpatialMap(map);
                Commander.RegisterShipActor(proxy);
                Agency = Input.AddComponent<ShipCrewAgencyCoordinator>();
                Agency.Configure(Session, Commander, map);
            }

            public void AdvanceWalk(int frames = 120)
            {
                for (int i = 0; i < frames && Walker.IsMoving; i++)
                    Walker.Tick(0.1f);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Input);
                UnityEngine.Object.DestroyImmediate(Root);
                Session.Dispose();
            }
        }

        [Test]
        public void SelectedCrewDoesNotStartAutonomousProvisionAndDeselectCanResume()
        {
            using (var f = new Fixture())
            {
                f.Commander.SelectActor(A);
                f.Agency.Tick();
                Assert.That(f.Agency.LastDecision(A),
                    Is.EqualTo(CrewAgencyDecisionKind.HoldForPlayerSelection));
                Assert.That(f.Agency.HasPendingProvision(A), Is.False);
                Assert.That(f.Session.Revision, Is.Zero);

                f.Commander.ClearSelection();
                f.Agency.Tick();
                Assert.That(f.Agency.HasPendingProvision(A), Is.True);
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Slot),
                    Is.Not.Null);
                Assert.That(f.Session.Revision, Is.EqualTo(1));
                Assert.That(f.Session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water), Is.EqualTo(2),
                    "A claim alone cannot spend water before physical arrival.");
            }
        }

        [Test]
        public void DeselectDuringPlayerMoveKeepsOrderUntilArrivalThenUsesRealStock()
        {
            using (var f = new Fixture())
            {
                f.Commander.SelectActor(A);
                Assert.That(f.Commander.IssueShipMoveOrder(C(2, 1),
                    new Vector3(2.5f, 0f, 1.5f)), Is.True);
                Assert.That(f.Agency.HasActivePlayerMove(A), Is.True);
                f.Commander.ClearSelection();
                f.Agency.Tick();
                Assert.That(f.Agency.LastDecision(A),
                    Is.EqualTo(CrewAgencyDecisionKind.ContinuePlayerOrder));
                Assert.That(f.Session.Revision, Is.Zero);
                f.AdvanceWalk();
                Assert.That(f.Walker.LastReachedCell, Is.EqualTo(C(2, 1)));

                f.Agency.Tick();
                Assert.That(f.Agency.HasActivePlayerMove(A), Is.False);
                Assert.That(f.Agency.HasPendingProvision(A), Is.True);
                Assert.That(f.Session.Revision, Is.EqualTo(1),
                    "A validated claim is committed only after the player order finishes.");
                f.AdvanceWalk();
                Assert.That(f.Walker.LastReachedCell, Is.EqualTo(C(4, 3)));
                f.Agency.Tick();
                Assert.That(f.Agency.HasPendingProvision(A), Is.False);
                Assert.That(f.Session.Revision, Is.EqualTo(2),
                    "Claim and use must each be authoritative, not a local simulated stock.");
                Assert.That(f.Session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water), Is.EqualTo(1));
                Assert.That(f.Session.Capture().State.CrewDuty.Get(A).Thirst,
                    Is.EqualTo(200));
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty, "Successful use releases the claim atomically.");
            }
        }

        [Test]
        public void SelectingAnActiveNormalNeedStopsAtSafeCellAndReleasesClaim()
        {
            using (var f = new Fixture())
            {
                f.Agency.Tick();
                Assert.That(f.Agency.HasPendingProvision(A), Is.True);
                f.Walker.Tick(0.08f); // physically between cells
                Assert.That(f.Walker.HasIncomingReservation, Is.True);
                f.Commander.SelectActor(A);
                f.Agency.Tick();
                Assert.That(f.Walker.IsMoving, Is.True,
                    "Mid-edge interruption must keep moving to a safe center.");
                f.AdvanceWalk();
                f.Agency.Tick();
                Assert.That(f.Agency.HasPendingProvision(A), Is.False);
                Assert.That(f.Walker.HasIncomingReservation, Is.False);
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty);
                Assert.That(f.Session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water), Is.EqualTo(2));
                Assert.That(f.Agency.LastDecision(A),
                    Is.Not.EqualTo(CrewAgencyDecisionKind.SeekProvision));
            }
        }

        [Test]
        public void AcceptedPlayerMoveSupersedesProvisionAndReleasesItsClaim()
        {
            using (var f = new Fixture())
            {
                f.Agency.Tick();
                Assert.That(f.Agency.HasPendingProvision(A), Is.True);
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.GetClaim(Slot),
                    Is.Not.Null);

                f.Commander.SelectActor(A);
                Assert.That(f.Commander.IssueShipMoveOrder(C(2, 1),
                    new Vector3(2.5f, 0f, 1.5f)), Is.True,
                    "A player order must still accept at a safe route boundary.");
                Assert.That(f.Walker.IsMoving, Is.True,
                    "Issuing the player order must not stop the order it just accepted.");
                Assert.That(f.Agency.HasPendingProvision(A), Is.False);
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty, "Superseded autonomy must release its claim immediately.");
                Assert.That(f.Session.Revision, Is.EqualTo(2),
                    "Claim and release are both canonical commands.");

                f.AdvanceWalk();
                Assert.That(f.Walker.LastReachedCell, Is.EqualTo(C(2, 1)));
                Assert.That(f.Session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water), Is.EqualTo(2),
                    "An interrupted provision cannot spend stock.");
                f.Agency.Tick();
                Assert.That(f.Agency.HasActivePlayerMove(A), Is.False);
                Assert.That(f.Agency.HasPendingProvision(A), Is.False);
            }
        }

        [Test]
        public void CriticalNeedWithEmptyStockStaysUnmetAndCreatesNoClaim()
        {
            using (var f = new Fixture(thirst: 900, stocked: false))
            {
                f.Agency.Tick();
                Assert.That(f.Agency.LastDecision(A),
                    Is.EqualTo(CrewAgencyDecisionKind.UnmetCriticalNeed));
                Assert.That(f.Agency.HasPendingProvision(A), Is.False);
                Assert.That(f.Session.Capture().State.CrewDuty.Interactions.Claims,
                    Is.Empty);
                Assert.That(f.Session.Revision, Is.Zero);
            }
        }

        [Test]
        public void AcceptedMoveSurvivesBinarySaveAndReentryAtSafeAuthoredSpawn()
        {
            using (var f = new Fixture(thirst: 0))
            {
                f.Commander.SelectActor(A);
                Assert.That(f.Commander.IssueShipMoveOrder(C(3, 1),
                    new Vector3(3.5f, 0f, 1.5f)), Is.True);
                f.Walker.Tick(0.08f); // halfway through the accepted move
                Assert.That(f.Walker.HasIncomingReservation, Is.True);
                var encoded = SaveCodec.Encode(f.Session.Capture());
                var decoded = SaveCodec.TryDecode(encoded);
                Assert.That(decoded.Success, Is.True);
                Assert.That(decoded.Value.PendingCrewMoves.Count, Is.EqualTo(1));
                Assert.That(decoded.Value.PendingCrewMoves[0].ShipGoal,
                    Is.EqualTo(C(3, 1)));

                using (var restored = WorldSession.Restore(decoded.Value))
                {
                    // Scene loader establishes the canonical authored spawn; it
                    // does NOT restore an interpolated midpoint or mouse selection.
                    var map = ShipSpatialMap.Derive(restored.Capture().State.Layout,
                        Array.Empty<ShipModuleSpatialProfile>(),
                        Array.Empty<ShipRoomPortal>());
                    f.Commander.ClearSelection();
                    f.Walker.transform.localPosition = new Vector3(1.5f, 0f, 1.5f);
                    f.Walker.Configure(f.Root.transform, map,
                        new Dictionary<EntityId, Vector3> { [Room] = Vector3.zero },
                        C(1, 1), 4f);
                    f.Commander.SetSession(restored);
                    f.Agency.Configure(restored, f.Commander, map);
                    Assert.That(f.Commander.Selection.Count, Is.Zero);
                    Assert.That(f.Agency.HasActivePlayerMove(A), Is.True);
                    Assert.That(f.Walker.IsMoving, Is.True);
                    f.AdvanceWalk();
                    f.Agency.Tick();
                    Assert.That(f.Walker.LastReachedCell, Is.EqualTo(C(3, 1)));
                    Assert.That(f.Agency.HasActivePlayerMove(A), Is.False);
                    Assert.That(restored.PendingCrewMoves, Is.Empty);
                    Assert.That(restored.Revision, Is.Zero,
                        "Replaying physical Move must not duplicate World commands.");
                }
            }
        }

        [Test]
        public void EmergencyCanPreemptPlayerMoveAtSafeBoundaryAndNeedsRealStock()
        {
            using (var f = new Fixture(thirst: 975))
            {
                f.Commander.SelectActor(A);
                Assert.That(f.Commander.IssueShipMoveOrder(C(3, 1),
                    new Vector3(3.5f, 0f, 1.5f)), Is.True);
                f.Walker.Tick(0.08f); // reserve a real incoming cell
                Assert.That(f.Walker.HasIncomingReservation, Is.True);
                f.Agency.Tick();
                Assert.That(f.Agency.LastDecision(A),
                    Is.EqualTo(CrewAgencyDecisionKind.EmergencyNeedsHandover));
                f.AdvanceWalk();
                Assert.That(f.Walker.IsMoving, Is.False);
                Assert.That(f.Walker.HasIncomingReservation, Is.False);
                f.Agency.Tick();
                Assert.That(f.Agency.HasActivePlayerMove(A), Is.False);
                Assert.That(f.Agency.HasPendingProvision(A), Is.True);
                f.AdvanceWalk();
                f.Agency.Tick();
                Assert.That(f.Session.Capture().State.CrewDuty.Get(A).Thirst,
                    Is.EqualTo(375));
                Assert.That(f.Session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water), Is.EqualTo(1));
            }

            using (var f = new Fixture(thirst: 975, stocked: false))
            {
                f.Commander.SelectActor(A);
                f.Agency.Tick();
                Assert.That(f.Agency.LastDecision(A),
                    Is.EqualTo(CrewAgencyDecisionKind.UnmetCriticalNeed));
                Assert.That(f.Agency.HasPendingProvision(A), Is.False);
                Assert.That(f.Session.Revision, Is.Zero);
            }
        }
    }
}
