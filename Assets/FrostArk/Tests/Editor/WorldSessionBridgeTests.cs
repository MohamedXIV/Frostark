using System;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using FrostArk.Core.World;
using FrostArk.Unity;
using EntityId = FrostArk.Core.World.EntityId;
using NUnit.Framework;
using UnityEngine;
using VContainer;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// G2 only: cannot run via the standalone dotnet G1 workflow.
    /// Must run inside Unity 6000.6.0f1 with VContainer 1.19.0 imported.
    /// </summary>
    public sealed class WorldSessionBridgeTests
    {
        [Test]
        public void ScopedPresenterResolvesOneAuthoritativeSessionAndReleasesIt()
        {
            var root = new GameObject("G2-WorldSessionScope");
            WorldSessionScope scope = null;
            WorldSession session = null;

            try
            {
                var child = new GameObject("G2-Presenter");
                child.transform.SetParent(root.transform, false);
                var presenter = child.AddComponent<WorldSessionPresenter>();
                scope = root.AddComponent<WorldSessionScope>();
                if (scope.Container == null)
                    scope.Build();

                session = scope.Container.Resolve<WorldSession>();
                Assert.That(scope.Container.Resolve<WorldSession>(), Is.SameAs(session));
                Assert.That(presenter.IsBound, Is.True);
                Assert.That(presenter.Read().Revision, Is.Zero);
                Assert.That(presenter.StatusText, Does.Contain("minute 0"));

                // The presenter submits a command to the same scoped authority.
                // Here a missing supply must be rejected without mirror totals.
                var rejection = presenter.Submit(new WithdrawSupplyCommand(
                    EntityId.Parse("command:missing"), EntityId.Parse("supply:missing"), 1, 0));
                Assert.That(rejection.Success, Is.False);
                Assert.That(session.Revision, Is.Zero);
            }
            finally
            {
                if (scope != null)
                    scope.DisposeCore();
                UnityEngine.Object.DestroyImmediate(root);
            }

            if (session != null)
                Assert.Throws<ObjectDisposedException>(() => session.Read());
        }
    }
}
