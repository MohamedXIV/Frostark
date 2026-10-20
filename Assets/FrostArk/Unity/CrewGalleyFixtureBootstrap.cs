using FrostArk.Core.World;
using UnityEngine;

namespace FrostArk.Unity
{
    /// <summary>
    /// Serialized scene provider for the canonical fixture WorldState.
    /// Matches the .cs asset name so Unity can resolve the MonoBehaviour
    /// when a generated scene is saved, closed, and reopened.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class CrewGalleyFixtureBootstrap : MonoBehaviour, IInitialWorldStateProvider
    {
        public WorldState ProvideInitialState() => CrewGalleyFixtureState.CreateDefaultState();

        private void Awake()
        {
            var scope = GetComponent<WorldSessionScope>();
            if (scope != null)
                scope.SetInitialState(ProvideInitialState());
        }
    }
}
