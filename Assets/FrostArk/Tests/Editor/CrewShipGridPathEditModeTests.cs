using System;
using FrostArk.Unity;
using NUnit.Framework;
using UnityEngine;

namespace FrostArk.Unity.Tests
{
    public sealed class CrewShipGridPathEditModeTests
    {
        [Test]
        public void AuthoredBulkheadForcesPathThroughActualDoor()
        {
            var grid = CrewGalleyFixtureState.Grid();
            var route = grid.FindPath(CrewGalleyFixtureState.StartCell,
                CrewGalleyFixtureState.DrinkCell);
            Assert.That(route.Count, Is.GreaterThan(2));
            Assert.That(route[0], Is.EqualTo(CrewGalleyFixtureState.StartCell));
            Assert.That(route[route.Count - 1],
                Is.EqualTo(CrewGalleyFixtureState.DrinkCell));
            Assert.That(route, Does.Contain(new Vector2Int(4, 5)));
            foreach (var cell in route)
                Assert.That(grid.CanStand(cell), Is.True, "Path must never pass through wall.");

            for (int i = 1; i < route.Count; i++)
            {
                var delta = route[i] - route[i - 1];
                Assert.That(Mathf.Abs(delta.x) + Mathf.Abs(delta.y),
                    Is.EqualTo(1), "Grid path must not diagonally cut corners.");
            }
        }

        [Test]
        public void GridDisallowsOccupiedGalleyAndOutsideShip()
        {
            var grid = CrewGalleyFixtureState.Grid();
            Assert.That(grid.CanStand(CrewGalleyFixtureState.GalleyCell), Is.False);
            Assert.That(grid.FindPath(CrewGalleyFixtureState.StartCell,
                CrewGalleyFixtureState.GalleyCell), Is.Empty);
            Assert.That(grid.FindPath(new Vector2Int(-1, 0),
                CrewGalleyFixtureState.EatCell), Is.Empty);
            Assert.Throws<ArgumentException>(() =>
                new CrewShipGridPath(3, 3, new[] { new Vector2Int(3, 1) }));
        }

        [Test]
        public void ManualPickGridRejectsBulkheadsAndSmoothCurveCannotCutWalls()
        {
            var grid = CrewGalleyFixtureState.Grid();
            var start = grid.WorldCenter(CrewGalleyFixtureState.StartCell, 1f);
            var wall = grid.WorldCenter(new Vector2Int(4, 2), 1f);
            Assert.That(grid.WorldToCell(start), Is.EqualTo(
                CrewGalleyFixtureState.StartCell));
            Assert.That(grid.CanTraverse(start, wall), Is.False,
                "A smoothed diagonal or manual shortcut cannot walk through a bulkhead.");
            Assert.That(grid.CanTraverse(start, grid.WorldCenter(new Vector2Int(2, 1), 1f)),
                Is.True);
            Assert.That(grid.CanStand(grid.WorldToCell(
                grid.WorldCenter(CrewGalleyFixtureState.GalleyCell))), Is.False,
                "Clicking on top of a physical Galley machine is not a walk target.");
            Assert.That(grid.CanStand(grid.WorldToCell(new Vector3(200, 0, 200))), Is.False);
        }

        [Test]
        public void PathIsRepeatableAndAllCoordinatesStayOnGrid()
        {
            var grid = CrewGalleyFixtureState.Grid();
            var first = grid.FindPath(CrewGalleyFixtureState.StartCell,
                CrewGalleyFixtureState.EatCell);
            var second = grid.FindPath(CrewGalleyFixtureState.StartCell,
                CrewGalleyFixtureState.EatCell);
            Assert.That(second, Is.EqualTo(first));
            foreach (var cell in first)
                Assert.That(grid.Contains(cell), Is.True);
            Assert.That(grid.FindPath(CrewGalleyFixtureState.EatCell,
                CrewGalleyFixtureState.EatCell),
                Is.EqualTo(new[] { CrewGalleyFixtureState.EatCell }));
        }
    }
}
