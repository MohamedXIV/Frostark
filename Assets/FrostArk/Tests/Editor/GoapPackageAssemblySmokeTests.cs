using CrashKonijn.Agent.Runtime;
using CrashKonijn.Goap.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace FrostArk.Unity.Tests
{
    /// <summary>
    /// Package/assembly handshake only. This is NOT a planning-behaviour test:
    /// GOAP scene execution and Unity Editor availability are verified separately.
    /// This test intentionally does not initialize a scene or make world changes.
    /// </summary>
    public sealed class GoapPackageAssemblySmokeTests
    {
        [Test]
        public void GoapV3ExposesDistinctPlannerAndExecutorComponents()
        {
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(typeof(GoapActionProvider)), Is.True);
            Assert.That(typeof(MonoBehaviour).IsAssignableFrom(typeof(AgentBehaviour)), Is.True);
            Assert.That(typeof(GoapActionProvider), Is.Not.EqualTo(typeof(AgentBehaviour)));
        }
    }
}
