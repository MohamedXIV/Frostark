using UnityEngine;
using FrostArk.Core.World;

namespace FrostArk.Unity
{
    /// <summary>
    /// Deterministic fixture bootstrapper attached to FrostArkSessionScope in
    /// TacticalVerticalSlice.unity. Prepares the fixture's initial authoritative state
    /// before VContainer container build.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class TacticalSliceBootstrap : MonoBehaviour, IInitialWorldStateProvider
    {
        public WorldState ProvideInitialState()
        {
            return TacticalSliceFixtureState.CreateDefaultState();
        }

        private void Awake()
        {
            var scope = GetComponent<WorldSessionScope>();
            if (scope != null)
            {
                scope.SetInitialState(ProvideInitialState());
            }
        }
    }
}
