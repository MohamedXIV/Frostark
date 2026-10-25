using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Real PlayMode proof that an away protagonist does not stop onboard
    /// physical Work, Rest, or real water consumption under the one session.
    /// </summary>
    public sealed class PersistentWorldConcurrentCrewPlayModeTests
    {
        private static readonly EntityId Captain =
            EntityId.Parse(PersistentWorldRuntime.CaptainId);
        private static readonly EntityId Engineer =
            EntityId.Parse(PersistentWorldRuntime.EngineerId);
        private static readonly EntityId Medic =
            EntityId.Parse(PersistentWorldRuntime.MedicId);
        private static readonly EntityId Scout =
            EntityId.Parse(PersistentWorldRuntime.ScoutId);
        private static readonly EntityId BridgeConsole =
            EntityId.Parse("module:ship:bridge-console");
        private static readonly EntityId EnginePlant =
            EntityId.Parse("module:ship:engine-plant");
        private static readonly EntityId WorkSlot = EntityId.Parse("slot:ship:work");
        private static readonly EntityId RestSlot = EntityId.Parse("slot:ship:rest");
        private static readonly EntityId DrinkSlot = EntityId.Parse("slot:ship:drink");
        private static readonly EntityId RepairJobId = EntityId.Parse("repair:ship:hull");
        private static readonly EntityId Water = EntityId.Parse("item:ship:water");
        private static readonly EntityId Pickup = EntityId.Parse("pickup:ship:water");

        [UnityTest]
        public IEnumerator OnboardCrewWorkRestAndConsumeWhileCaptainExploresShore()
        {
            var root = new GameObject("E12-Concurrent-Crew");
            WorldSession session = null;
            try
            {
                session = new WorldSession(CreateState());
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Shore(
                    EntityId.Parse(PersistentWorldRuntime.ShoreSceneId),
                    EntityId.Parse(PersistentWorldRuntime.ShoreSiteId),
                    EntityId.Parse(PersistentWorldRuntime.ShoreAnchorId),
                    new[] { Captain }));
                var assign = session.Submit(new AssignCrewCommand(
                    EntityId.Parse("cmd:ship:assign-engineer"),
                    Engineer, EnginePlant, session.Revision));
                Assert.That(assign.Success, Is.True);

                var runtime = root.AddComponent<PersistentWorldRuntime>();
                runtime.BuildWorld(session);
                yield return null;
                yield return null;

                float wait = 8f;
                while (!runtime.IsReady || !runtime.IsShoreReady)
                {
                    if (wait <= 0f)
                        Assert.Fail("Persistent world did not initialize: " +
                            runtime.InitializationError + " / " + runtime.ShoreError);
                    wait -= Time.deltaTime;
                    yield return null;
                }

                var captain = runtime.CrewActors.First(a => a.ActorId == Captain);
                var engineer = runtime.CrewActors.First(a => a.ActorId == Engineer);
                var medic = runtime.CrewActors.First(a => a.ActorId == Medic);
                var scout = runtime.CrewActors.First(a => a.ActorId == Scout);
                var commander = runtime.Commander;
                var duty = commander.GetComponent<ShipCrewDutyAutonomy>();
                var agency = commander.GetComponent<ShipCrewAgencyCoordinator>();
                Assert.That(duty, Is.Not.Null);
                Assert.That(agency, Is.Not.Null);
                Assert.That(captain.IsOnShip, Is.False);
                Assert.That(session.Capture().State.CrewDuty.Get(Captain).IsAway, Is.True);

                // The protagonist physically explores the shore while the rest
                // of the crew remains in the one resident ship simulation.
                commander.SelectActor(Captain);
                Physics.SyncTransforms();
                var shoreDestination = new Vector3(25f, 0f, 6f);
                var shoreScreen = runtime.GameplayCamera.WorldToScreenPoint(
                    shoreDestination);
                Assert.That(commander.ProcessContextClick(shoreScreen), Is.True,
                    "The away protagonist must accept a real shore move.");
                var captainStart = captain.transform.position;
                var exploreDeadline = Time.realtimeSinceStartup + 3f;
                while (Vector3.Distance(captainStart, captain.transform.position) < 0.2f &&
                       Time.realtimeSinceStartup < exploreDeadline)
                    yield return null;
                Assert.That(Vector3.Distance(captainStart, captain.transform.position),
                    Is.GreaterThan(0.2f), "The protagonist must move on shore.");

                var readyDeadline = Time.realtimeSinceStartup + 25f;
                while (!OnboardActivitiesReady(duty, agency, session, engineer, medic) &&
                       Time.realtimeSinceStartup < readyDeadline)
                    yield return null;

                Assert.That(duty.ActiveVerb(Engineer),
                    Is.EqualTo(CrewInteractionVerb.Work));
                Assert.That(duty.IsStationed(Engineer), Is.True,
                    "Onboard repairer must reach the authored Work slot.");
                Assert.That(duty.ActiveVerb(Medic),
                    Is.EqualTo(CrewInteractionVerb.Rest));
                Assert.That(duty.IsStationed(Medic), Is.True,
                    "Onboard resting crew must reach the authored Rest slot.");
                Assert.That(session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water), Is.EqualTo(1),
                    "Onboard thirsty crew must consume exactly one real water item.");
                Assert.That(session.Capture().State.CrewDuty.Get(Scout).Thirst,
                    Is.EqualTo(200));

                yield return null; // Let the duty adapter attest physical presence.
                duty.Tick();
                agency.Tick();

                long repairBefore = session.Capture().State.CrewDuty
                    .GetRepair(RepairJobId).RemainingWork;
                int fatigueBefore = session.Capture().State.CrewDuty.Get(Medic).Fatigue;
                long waterBefore = session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Water);

                session.Advance(1);
                yield return null;

                var after = session.Capture().State;
                Assert.That(after.CrewDuty.GetRepair(RepairJobId).RemainingWork,
                    Is.EqualTo(repairBefore - 9),
                    "The stationed repairer must produce legitimate Work while ashore.");
                Assert.That(after.CrewDuty.Get(Medic).Fatigue,
                    Is.EqualTo(fatigueBefore - 2),
                    "The stationed resting crew must recover through the world clock.");
                Assert.That(after.Inventory.GetAmount(InventoryContainer.ShipHold, Water),
                    Is.EqualTo(waterBefore),
                    "Physical rest/work must not duplicate or phantom-consume water.");
                Assert.That(after.CrewDuty.Get(Captain).IsAway, Is.True);
                Assert.That(session.SceneCheckpoint.Theater,
                    Is.EqualTo(PhysicalTheater.Shore));
                Assert.That(captain.gameObject, Is.SameAs(
                    runtime.CrewActors.First(a => a.ActorId == Captain).gameObject));
                Assert.That(runtime.transform
                    .GetComponentsInChildren<ShipTacticalSceneBootstrap>(true).Length,
                    Is.EqualTo(1));
                Assert.That(runtime.transform
                    .GetComponentsInChildren<PersistentCrewActor>(true).Length,
                    Is.EqualTo(4));
            }
            finally
            {
                UnityEngine.Object.Destroy(root);
                session?.Dispose();
            }
            yield return null;
        }

        private static bool OnboardActivitiesReady(
            ShipCrewDutyAutonomy duty, ShipCrewAgencyCoordinator agency,
            WorldSession session, PersistentCrewActor engineer,
            PersistentCrewActor medic)
        {
            if (duty.ActiveVerb(engineer.ActorId) != CrewInteractionVerb.Work ||
                !duty.IsStationed(engineer.ActorId) ||
                duty.ActiveVerb(medic.ActorId) != CrewInteractionVerb.Rest ||
                !duty.IsStationed(medic.ActorId) ||
                agency.HasPendingProvision(Scout))
                return false;
            return session.Capture().State.Inventory.GetAmount(
                InventoryContainer.ShipHold, Water) == 1;
        }

        private static WorldState CreateState()
        {
            var bridge = EntityId.Parse(PersistentWorldRuntime.BridgeRoomId);
            var engine = EntityId.Parse(PersistentWorldRuntime.EngineRoomId);
            var blueprint = EntityId.Parse("blueprint:ship:machine");
            var layout = ShipLayoutState.Create(
                new[]
                {
                    new ShipCompartmentSlot(bridge, 6, 6),
                    new ShipCompartmentSlot(engine, 6, 6)
                },
                new[]
                {
                    new ShipModuleBlueprint(blueprint, ShipModuleKind.Auxiliary,
                        1, 1, EntityId.Parse("item:ship:machine-parts"), 1, 1)
                },
                new[]
                {
                    new ShipModule(BridgeConsole, blueprint, bridge, 0, 5),
                    new ShipModule(EnginePlant, blueprint, engine, 4, 4)
                });

            var items = InventoryState.Create(
                new ItemCatalog(new[] { new ItemDefinition(Water, 1, 100, 10) }),
                new[] { new SalvagePickup(Pickup, Water, 2) },
                10000, 5, 10000, 5);
            items = items.TryCollect(Pickup).State.TryUnload(Water, 2).State;

            var crew = CrewState.Create(new[]
            {
                new CrewMember(Captain, DutyRole.Watch, 900, 200,
                    0, 1440, isAway: true, hunger: 80),
                new CrewMember(Engineer, DutyRole.Repair, 950, 0, 0, 1440),
                new CrewMember(Medic, DutyRole.Medical, 900, 200,
                    720, 400, fatigue: 700),
                new CrewMember(Scout, DutyRole.Watch, 900, 200,
                    0, 1440, thirst: 800)
            }, jobs: new[] { new RepairJob(RepairJobId, 100) },
            consumables: new[] { new CrewConsumable(Water, 0, 600) },
            interactions: CrewInteractionState.Create(new[]
            {
                new CrewInteractionSlot(WorkSlot, EnginePlant,
                    CrewInteractionVerb.Work, -1, 0, 0),
                new CrewInteractionSlot(RestSlot, BridgeConsole,
                    CrewInteractionVerb.Rest, 1, 0, 0),
                new CrewInteractionSlot(DrinkSlot, BridgeConsole,
                    CrewInteractionVerb.Drink, 0, -1, 2)
            }));

            return WorldState.Create(WorldMinute.Zero,
                Array.Empty<SupplyStock>(),
                new[] { Captain, Engineer, Medic, Scout },
                inventory: items, crewDuty: crew, layout: layout);
        }

        [UnityTearDown]
        public IEnumerator CleanupConcurrentCrewWorld()
        {
            var runtimes = UnityEngine.Object.FindObjectsByType<PersistentWorldRuntime>(
                FindObjectsInactive.Include);
            foreach (var runtime in runtimes)
                if (runtime != null)
                    UnityEngine.Object.Destroy(runtime.transform.root.gameObject);
            yield return null;

            var shore = SceneManager.GetSceneByName(PersistentWorldRuntime.ShoreSceneName);
            if (shore.IsValid() && shore.isLoaded)
            {
                var unload = SceneManager.UnloadSceneAsync(shore);
                if (unload != null)
                    yield return unload;
            }

            var reset = SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
            if (reset != null)
                yield return reset;
        }
    }
}
