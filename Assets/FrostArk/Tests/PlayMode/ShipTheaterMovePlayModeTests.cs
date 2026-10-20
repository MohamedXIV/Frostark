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
                    Array.Empty<SupplyStock>(), new[] { A, B }, crewDuty: crew));
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
                for (int i = 0; i < walkers.Length; i++)
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    go.name = "ShipCrew-" + ids[i].Value;
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = origins[start[i].RoomId] +
                        new Vector3(start[i].X + 0.5f, 0, start[i].Y + 0.5f);
                    walkers[i] = go.AddComponent<ShipGridWalker>();
                    walkers[i].Configure(root.transform, map, origins, start[i], 12f);
                }

                var selection = new TacticalSelectionController();
                selection.SelectMultiple(ids);
                var target = Cell(Engine, 2, 2);
                var outcome = TacticalTheaterSelectionCommands.MoveSelectedOnShip(
                    session, selection, target, new Vector3(8.5f, 0, 2.5f),
                    new ICrewTheaterMoveExecutor[]
                    {
                        new ShipGridCrewExecutor(A, ShipScene, walkers[0]),
                        new ShipGridCrewExecutor(B, ShipScene, walkers[1])
                    });
                Assert.That(outcome.Count, Is.EqualTo(2));
                Assert.That(outcome.All(x => x.Accepted), Is.True);

                selection.Clear();
                root.transform.position = new Vector3(30, 0, -12);
                for (int frame = 0; frame < 150 &&
                    (walkers[0].IsMoving || walkers[1].IsMoving); frame++)
                    yield return null; // Real Unity Update rather than artificial Tick.
                Assert.That(walkers[0].IsMoving || walkers[1].IsMoving, Is.False,
                    "Crew should reach the next room within the bounded frame budget.");
                foreach (var walker in walkers)
                {
                    Assert.That(walker.LastReachedCell, Is.EqualTo(target));
                    Assert.That(root.transform.InverseTransformPoint(
                        walker.transform.position).x, Is.EqualTo(8.5f).Within(0.1f));
                }
                Assert.That(selection.Count, Is.Zero);
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
