using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Crew;
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
    /// G3 runtime acceptance: actual Unity frames update ship-root-local
    /// walkers. This is deliberately separate from Core NUnit and G2 EditMode.
    /// Requires the Unity test runner; not yet executed on this PR.
    /// </summary>
    public sealed class ShipTheaterMovePlayModeTests
    {
        private static readonly EntityId ShipScene = EntityId.Parse("scene:rts-ship");
        private static readonly EntityId Bridge = EntityId.Parse("room:bridge");
        private static readonly EntityId Engine = EntityId.Parse("room:engine");
        private static readonly EntityId A = EntityId.Parse("crew:alpha");
        private static readonly EntityId B = EntityId.Parse("crew:beta");

        private static ShipGridCell Cell(EntityId room, int x, int y) =>
            new ShipGridCell(room, x, y);

        [UnityTest]
        public IEnumerator GroupMoveTraversesAuthoredDoorWhileShipRootMoves()
        {
            var root = new GameObject("G3-ShipRoot");
            WorldSession session = null;
            try
            {
                var layout = ShipLayoutState.Create(new[]
                {
                    new ShipCompartmentSlot(Bridge, 6, 6),
                    new ShipCompartmentSlot(Engine, 6, 6)
                }, Array.Empty<ShipModuleBlueprint>(), Array.Empty<ShipModule>());
                var map = ShipSpatialMap.Derive(layout,
                    Array.Empty<ShipModuleSpatialProfile>(),
                    new[] { new ShipRoomPortal(EntityId.Parse("portal:bulkhead"),
                        Cell(Bridge, 5, 2), Cell(Engine, 0, 2), true) });
                var crew = CrewState.Create(new[]
                {
                    new CrewMember(A, DutyRole.Engineering, 850, 100, 0, 1440),
                    new CrewMember(B, DutyRole.Watch, 810, 120, 0, 1440)
                });
                session = new WorldSession(WorldState.Create(WorldMinute.Zero,
                    Array.Empty<SupplyStock>(), new[] { A, B }, crewDuty: crew,
                    layout: layout));
                session.SetSceneCheckpoint(PhysicalSceneCheckpoint.Ship(
                    ShipScene, EntityId.Parse("anchor:bridge")));

                var origins = new Dictionary<EntityId, Vector3>
                {
                    [Bridge] = Vector3.zero,
                    [Engine] = new Vector3(6f, 0f, 0f)
                };
                var start = new[] { Cell(Bridge, 1, 1), Cell(Bridge, 1, 3) };
                var ids = new[] { A, B };
                var walkers = new ShipGridWalker[2];
                var actors = new ShipCrewSelectionProxy[2];
                for (int i = 0; i < walkers.Length; i++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.name = "ShipCrew-" + ids[i].Value;
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = origins[start[i].RoomId] +
                        new Vector3(start[i].X + 0.5f, 0, start[i].Y + 0.5f);
                    walkers[i] = go.AddComponent<ShipGridWalker>();
                    walkers[i].Configure(root.transform, map, origins, start[i], 12f);
                    actors[i] = go.AddComponent<ShipCrewSelectionProxy>();
                    actors[i].SetActorId(ids[i]);
                    actors[i].BindScene(ShipScene);
                }

                var engineFloor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                engineFloor.name = "EngineRoomFloor";
                engineFloor.transform.SetParent(root.transform, false);
                engineFloor.transform.localPosition = new Vector3(9f, -0.05f, 3f);
                engineFloor.transform.localScale = new Vector3(6f, 0.1f, 6f);
                engineFloor.AddComponent<ShipRoomClickSurface>().Configure(
                    Engine, root.transform, origins[Engine]);

                var cameraObject = new GameObject("ShipRTSCamera");
                cameraObject.transform.SetParent(root.transform, false);
                cameraObject.transform.position = new Vector3(7f, 16f, -5f);
                cameraObject.transform.LookAt(new Vector3(7f, 0f, 3f));
                var camera = cameraObject.AddComponent<Camera>();
                camera.pixelRect = new Rect(0f, 0f, 1280f, 720f);

                var commanderObject = new GameObject("ShipRTSCommander");
                commanderObject.transform.SetParent(root.transform, false);
                var commander = commanderObject.AddComponent<TacticalCommandController>();
                commander.SetCamera(camera);
                commander.SetSession(session);
                commander.SetShipSpatialMap(map);
                commander.RegisterShipActor(actors[0]);
                commander.RegisterShipActor(actors[1]);
                commander.SelectActor(A);
                commander.SelectActor(B, addToSelection: true);

                Physics.SyncTransforms();
                var target = Cell(Engine, 2, 2);
                Assert.That(commander.ProcessContextClick(
                    camera.WorldToScreenPoint(new Vector3(8.5f, 0f, 2.5f))), Is.True,
                    "Camera right-click must dispatch through the production RTS controller.");
                Assert.That(commander.LastTheaterMoveDispatch.Count, Is.EqualTo(2));
                Assert.That(commander.LastTheaterMoveDispatch.Values.All(x => x.Accepted),
                    Is.True);
                Assert.That(commander.LastIssuedIntent.ActorIds,
                    Is.EquivalentTo(ids));
                commander.ClearSelection();
                root.transform.position = new Vector3(30, 0, -12);
                for (int frame = 0; frame < 150 &&
                    (walkers[0].IsMoving || walkers[1].IsMoving); frame++)
                    yield return null; // Real Unity Update rather than artificial Tick.
                Assert.That(walkers[0].IsMoving || walkers[1].IsMoving, Is.False,
                    "Crew should reach the next room within the bounded frame budget.");
                Assert.That(walkers[0].LastReachedCell.RoomId, Is.EqualTo(Engine));
                Assert.That(walkers[1].LastReachedCell.RoomId, Is.EqualTo(Engine));
                Assert.That(walkers[0].LastReachedCell, Is.Not.EqualTo(walkers[1].LastReachedCell),
                    "Group movement must finish at two separate unclaimed arrival cells.");
                foreach (var walker in walkers)
                {
                    Assert.That(Math.Abs(walker.LastReachedCell.X - target.X) +
                        Math.Abs(walker.LastReachedCell.Y - target.Y), Is.LessThanOrEqualTo(3));
                    Assert.That(root.transform.InverseTransformPoint(
                        walker.transform.position).x, Is.InRange(6.5f, 11.5f));
                }
                Assert.That(commander.Selection.Count, Is.Zero);
                Assert.That(session.Revision, Is.Zero);
            }
            finally
            {
                if (session != null) session.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
