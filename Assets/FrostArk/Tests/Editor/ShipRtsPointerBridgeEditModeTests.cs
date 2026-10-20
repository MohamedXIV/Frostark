using System;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.Inventory;
using FrostArk.Core.Crew;
using FrostArk.Core.Save;
using FrostArk.Core.Ship;
using FrostArk.Core.World;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Unity G2 mouse-to-physical-order bridge. Authored tests: do not count
    /// them as executed without a real Editor test report.
    /// </summary>
    public sealed class ShipRtsPointerBridgeEditModeTests
    {
        private static readonly EntityId ShipScene = EntityId.Parse("scene:test-rts-ship");
        private static readonly EntityId Bridge = EntityId.Parse("room:test-bridge");
        private static readonly EntityId Engine = EntityId.Parse("room:test-engine");
        private static readonly EntityId First = EntityId.Parse("crew:bridge-operator");
        private static readonly EntityId Second = EntityId.Parse("crew:second-operator");
        private static readonly EntityId Door = EntityId.Parse("portal:bulkhead");
        private static readonly EntityId Parts = EntityId.Parse("item:rts:parts");
        private static readonly EntityId Blueprint = EntityId.Parse("blueprint:rts:obstruction");

        private sealed class Scene : IDisposable
        {
            public readonly GameObject Root = new GameObject("G2-ShipRts");
            public readonly TacticalCommandController Commander;
            public readonly ShipGridWalker One;
            public readonly ShipGridWalker Two;
            public readonly ShipCrewSelectionProxy FirstActor;
            public readonly ShipCrewSelectionProxy SecondActor;
            public readonly Camera Camera;
            public readonly WorldSession Session;
            public readonly ShipTacticalSceneBootstrap Bootstrap;

            private static GameObject Child(GameObject root, string name, Vector3 position)
            {
                var go = new GameObject(name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = position;
                return go;
            }

            private static ShipRoomClickSurface Floor(GameObject root, string name,
                Vector3 roomOrigin)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = name;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = roomOrigin + new Vector3(3f, -0.05f, 3f);
                go.transform.localScale = new Vector3(6, 0.1f, 6);
                return go.AddComponent<ShipRoomClickSurface>();
            }

            private static (ShipCrewSelectionProxy proxy, ShipGridWalker walker)
                Crew(GameObject root, EntityId id, Vector3 position)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                go.name = id.Value;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = position;
                var walker = go.AddComponent<ShipGridWalker>();
                var actor = go.AddComponent<ShipCrewSelectionProxy>();
                actor.SetActorId(id);
                return (actor, walker);
            }

            public Scene(bool openDoor, bool secondInEngine = false,
                bool initializeBootstrap = true)
            {
                var layout = ShipLayoutState.Create(new[]
                {
                    new ShipCompartmentSlot(Bridge, 6, 6),
                    new ShipCompartmentSlot(Engine, 6, 6)
                }, new[]
                {
                    new ShipModuleBlueprint(Blueprint, ShipModuleKind.Auxiliary, 1, 1, Parts, 1, 1)
                }, Array.Empty<ShipModule>());
                var inventory = InventoryState.Create(
                    new ItemCatalog(new[] { new ItemDefinition(Parts, 1, 1, 4) }),
                    new[] { new SalvagePickup(EntityId.Parse("pickup:rts:parts"), Parts, 4) },
                    100, 10, 100, 10);
                var collected = inventory.TryCollect(EntityId.Parse("pickup:rts:parts"));
                if (!collected.Success) throw new InvalidOperationException("Stock fixture invalid.");
                var unloaded = collected.State.TryUnload(Parts, 4);
                if (!unloaded.Success) throw new InvalidOperationException("Ship stock fixture invalid.");
                var duty = CrewState.Create(new[]
                {
                    new CrewMember(First, DutyRole.Engineering, 850, 100, 0, 1440),
                    new CrewMember(Second, DutyRole.Watch, 850, 100, 0, 1440)
                });
                Session = new WorldSession(WorldState.Create(WorldMinute.Zero,
                    Array.Empty<SupplyStock>(), new[] { First, Second },
                    crewDuty: duty, layout: layout, inventory: unloaded.State));
                Session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    ShipScene, EntityId.Parse("anchor:bridge")));

                var commanderGo = Child(Root, "Commander", Vector3.zero);
                Commander = commanderGo.AddComponent<TacticalCommandController>();
                var bridgeOrigin = Child(Root, "BridgeOrigin", Vector3.zero);
                var engineOrigin = Child(Root, "EngineOrigin", new Vector3(6f, 0f, 0f));
                var bridgeFloor = Floor(Root, "BridgeFloor", Vector3.zero);
                var engineFloor = Floor(Root, "EngineFloor", new Vector3(6f, 0f, 0f));
                (FirstActor, One) = Crew(Root, First, new Vector3(1.5f, 0f, 1.5f));
                var secondPos = secondInEngine ?
                    new Vector3(7.5f, 0f, 3.5f) : new Vector3(1.5f, 0f, 3.5f);
                (SecondActor, Two) = Crew(Root, Second, secondPos);

                var cam = Child(Root, "RtsCamera", new Vector3(6f, 18f, -5f));
                Camera = cam.AddComponent<Camera>();
                Camera.pixelRect = new Rect(0, 0, 1280, 720);
                Camera.transform.LookAt(new Vector3(6f, 0f, 3f));
                Commander.SetCamera(Camera);

                Bootstrap = Child(Root, "Bootstrap", Vector3.zero)
                    .AddComponent<ShipTacticalSceneBootstrap>();
                Bootstrap.Configure(Root.transform, Commander, new[]
                {
                    new ShipTacticalSceneBootstrap.RoomBinding
                    { RoomId = Bridge.Value, Origin = bridgeOrigin.transform, Floor = bridgeFloor },
                    new ShipTacticalSceneBootstrap.RoomBinding
                    { RoomId = Engine.Value, Origin = engineOrigin.transform, Floor = engineFloor }
                }, new[]
                {
                    new ShipTacticalSceneBootstrap.DoorBinding
                    {
                        DoorId = Door.Value,
                        FromRoomId = Bridge.Value,
                        FromCell = new Vector2Int(5, 2),
                        ToRoomId = Engine.Value,
                        ToCell = new Vector2Int(0, 2),
                        Open = openDoor
                    }
                }, new[]
                {
                    new ShipTacticalSceneBootstrap.CrewBinding
                    { Actor = FirstActor, StartingRoomId = Bridge.Value,
                        StartingCell = new Vector2Int(1, 1) },
                    new ShipTacticalSceneBootstrap.CrewBinding
                    { Actor = SecondActor,
                        StartingRoomId = secondInEngine ? Engine.Value : Bridge.Value,
                        StartingCell = secondInEngine ? new Vector2Int(1, 3) : new Vector2Int(1, 3) }
                });
                if (initializeBootstrap)
                    Bootstrap.Initialize(Session);
                else
                    Commander.SetSession(Session);
                Physics.SyncTransforms();
            }

            public Vector2 Screen(Vector3 world) =>
                Camera.WorldToScreenPoint(world);

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Root);
                Session.Dispose();
            }
        }

        [Test]
        public void BoxSelectAndRoomRightClickDispatchTwoRealShipWalkers()
        {
            using (var scene = new Scene(true))
            {
                var a = scene.Screen(scene.FirstActor.GetComponent<Collider>().bounds.center);
                var b = scene.Screen(scene.SecondActor.GetComponent<Collider>().bounds.center);
                var start = Vector2.Min(a, b) - Vector2.one * 30f;
                var end = Vector2.Max(a, b) + Vector2.one * 30f;
                Assert.That(scene.Commander.ProcessBoxSelection(start, end),
                    Is.EqualTo(2));
                Assert.That(scene.FirstActor.IsSelected, Is.True);
                Assert.That(scene.SecondActor.IsSelected, Is.True);
                Assert.That(scene.Commander.ProcessContextClick(
                    scene.Screen(new Vector3(8.5f, 0f, 2.5f))), Is.True);
                Assert.That(scene.Commander.LastIssuedIntent.ActorIds,
                    Is.EquivalentTo(new[] { First, Second }));
                Assert.That(scene.Commander.LastTheaterMoveDispatch.Values
                    .All(x => x.Accepted), Is.True);
                Assert.That(scene.One.IsMoving && scene.Two.IsMoving, Is.True);
                Assert.That(scene.One.OrderedCell,
                    Is.EqualTo(new ShipGridCell(Engine, 2, 2)));
                Assert.That(scene.Two.OrderedCell,
                    Is.EqualTo(new ShipGridCell(Engine, 2, 2)));

                // A blank click deselects but does not revoke movement.
                scene.Commander.ProcessPointerClick(
                    scene.Screen(new Vector3(3.5f, 0f, 5.5f)));
                Assert.That(scene.Commander.Selection.Count, Is.Zero);
                Assert.That(scene.One.IsMoving && scene.Two.IsMoving, Is.True);
                Assert.That(scene.Session.Revision, Is.Zero);
            }
        }

        [Test]
        public void ClosedDoorRejectsBridgeCrewAndAcceptsEngineCrewIndividually()
        {
            using (var scene = new Scene(false, secondInEngine: true))
            {
                scene.Commander.SelectActor(First);
                scene.Commander.SelectActor(Second, addToSelection: true);
                Assert.That(scene.Commander.ProcessContextClick(
                    scene.Screen(new Vector3(8.5f, 0f, 2.5f))), Is.True);
                Assert.That(scene.Commander.LastTheaterMoveDispatch[First].Status,
                    Is.EqualTo(CrewMoveDispatchStatus.ExecutorRejected));
                Assert.That(scene.Commander.LastTheaterMoveDispatch[Second].Accepted, Is.True);
                Assert.That(scene.Commander.LastMoveDispatch[First], Is.False);
                Assert.That(scene.Commander.LastMoveDispatch[Second], Is.True);
                Assert.That(scene.Commander.LastIssuedIntent.ActorIds,
                    Is.EqualTo(new[] { Second }));
                Assert.That(scene.One.IsMoving, Is.False);
                Assert.That(scene.Two.IsMoving, Is.True);
                Assert.That(scene.Session.Revision, Is.Zero);
            }
        }

        [Test]
        public void MissingSpatialBootstrapNeverUsesShoreNavMeshForShipCommand()
        {
            using (var scene = new Scene(true, initializeBootstrap: false))
            {
                scene.Commander.RegisterShipActor(scene.FirstActor);
                scene.Commander.SelectActor(First);
                Assert.That(scene.Commander.IssueMoveOrder(new Vector3(8.5f, 0f, 2.5f)),
                    Is.False);
                Assert.That(scene.Commander.ProcessContextClick(
                    scene.Screen(new Vector3(8.5f, 0f, 2.5f))), Is.False);
                Assert.That(scene.Commander.LastIssuedIntent, Is.Null);
                Assert.That(scene.One.IsMoving, Is.False);
                Assert.That(scene.Session.Revision, Is.Zero);
            }
        }

        [Test]
        public void DoorClosingInvalidatesMovingCrewAndFutureRightClickRejectsRoute()
        {
            using (var scene = new Scene(true))
            {
                scene.Commander.SelectActor(First);
                Assert.That(scene.Commander.ProcessContextClick(
                    scene.Screen(new Vector3(8.5f, 0f, 2.5f))), Is.True);
                Assert.That(scene.One.IsMoving, Is.True);
                Assert.That(scene.Bootstrap.SetDoorOpen(Door, false), Is.True);
                Assert.That(scene.One.IsMoving, Is.False);
                Assert.That(scene.One.LastError, Is.EqualTo("RouteInvalidated"));
                Assert.That(scene.Commander.ProcessContextClick(
                    scene.Screen(new Vector3(8.5f, 0f, 2.5f))), Is.False);
                Assert.That(scene.Commander.LastTheaterMoveDispatch[First].Status,
                    Is.EqualTo(CrewMoveDispatchStatus.ExecutorRejected));
            }
        }
        [Test]
        public void CommittedModuleBuildAutomaticallyInvalidatesBlockedShipRoute()
        {
            using (var scene = new Scene(true))
            {
                scene.Commander.SelectActor(First);
                Assert.That(scene.Commander.ProcessContextClick(
                    scene.Screen(new Vector3(8.5f, 0f, 2.5f))), Is.True);
                Assert.That(scene.One.IsMoving, Is.True);
                var placement = new BuildShipModuleCommand(
                    EntityId.Parse("command:rts:door-obstruction"),
                    EntityId.Parse("module:rts:door-obstruction"),
                    Blueprint, Bridge, 5, 2, scene.Session.Revision);
                var result = scene.Session.Submit(placement);
                Assert.That(result.Success, Is.True, result.Failure.ToString());
                Assert.That(scene.Session.Revision, Is.EqualTo(1));
                Assert.That(scene.Bootstrap.Map.Walkable(new ShipGridCell(Bridge, 5, 2)),
                    Is.False, "Committed world layout should rederive room navigation.");
                Assert.That(scene.One.IsMoving, Is.False,
                    "Invalidating a real doorway must stop an accepted route.");
                Assert.That(scene.One.LastError, Is.EqualTo("RouteInvalidated"));
                Assert.That(scene.Session.Capture().State.Inventory.GetAmount(
                    InventoryContainer.ShipHold, Parts), Is.EqualTo(3));
                Assert.That(scene.Commander.ProcessContextClick(
                    scene.Screen(new Vector3(8.5f, 0f, 2.5f))), Is.False);
                Assert.That(scene.Commander.LastTheaterMoveDispatch[First].Status,
                    Is.EqualTo(CrewMoveDispatchStatus.ExecutorRejected));
            }
        }

        [Test]
        public void FreshShipCheckpointMustBeExplicitAndDoesNotDefaultFromSceneObjects()
        {
            var root = new GameObject("G2-Bootstrap-Only");
            try
            {
                var bootstrap = root.AddComponent<ShipTacticalSceneBootstrap>();
                Assert.That(bootstrap.InitialNewRunCheckpoint(), Is.Null);
                bootstrap.ConfigureNewRunCheckpoint(ShipScene,
                    EntityId.Parse("anchor:new-run"));
                var checkpoint = bootstrap.InitialNewRunCheckpoint();
                Assert.That(checkpoint.Theater, Is.EqualTo(PhysicalTheater.Ship));
                Assert.That(checkpoint.SceneId, Is.EqualTo(ShipScene));
                Assert.That(checkpoint.SpawnAnchorId,
                    Is.EqualTo(EntityId.Parse("anchor:new-run")));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

    }
}
