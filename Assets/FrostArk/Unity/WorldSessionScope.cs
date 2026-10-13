using System;
using FrostArk.Core.Application;
using FrostArk.Core.Inventory;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using VContainer;
using VContainer.Unity;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Contract for scene or fixture components capable of supplying an initial WorldState
    /// before container composition without tight coupling to WorldSessionScope.
    /// </summary>
    public interface IInitialWorldStateProvider
    {
        WorldState ProvideInitialState();
    }

    /// <summary>
    /// Run/session composition root. No process-global state or scene-owned
    /// resource totals: one WorldSession is scoped to this LifetimeScope.
    /// A future persistent parent scope may provide infrastructure ports.
    /// </summary>
    public sealed class WorldSessionScope : LifetimeScope
    {
        private WorldState _initialState;
        private SaveGameDto _restoreSnapshot;
        private Func<WorldMinute, long> _restoreAmbient;

        /// <summary>
        /// Explicitly configure the initial authoritative WorldState for this scope instance.
        /// Must be called before LifetimeScope.Build() / container resolution.
        /// </summary>
        public void SetInitialState(WorldState state)
        {
            _initialState = state;
        }

        /// <summary>
        /// Configure an already decoded, validated save before scope Build.
        /// The scene bootstrap provider must not overwrite this restored run.
        /// Authored thermal ships still require an explicit climate provider.
        /// </summary>
        public void SetRestoreSnapshot(SaveGameDto snapshot, Func<WorldMinute, long> ambientMilliC = null)
        {
            _restoreSnapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _restoreAmbient = ambientMilliC;
        }

        protected override void Configure(IContainerBuilder builder)
        {
            if (_initialState == null)
            {
                var provider = GetComponent<IInitialWorldStateProvider>();
                if (provider != null)
                {
                    _initialState = provider.ProvideInitialState();
                }
            }

            // Explicit constructor selection: WorldSession initializes with the provided
            // initial state or default foundation state (minute 0).
            builder.Register<WorldSession>(_ => _restoreSnapshot != null
                ? WorldSession.Restore(_restoreSnapshot, ambientMilliC: _restoreAmbient)
                : _initialState != null ? new WorldSession(_initialState)
                : new WorldSession(), Lifetime.Scoped);
            builder.RegisterComponentInHierarchy<WorldSessionPresenter>();

            // Entire run root may optionally opt into the ship/shore scene
            // adapter. Destination environment scenes must remain scope-free.
            var transit = GetComponent<ShoreSceneTransitCoordinator>();
            if (transit != null)
                builder.RegisterComponent(transit);

            var commander = GetComponentInChildren<TacticalCommandController>() ?? FindAnyObjectByType<TacticalCommandController>();
            if (commander != null)
            {
                builder.RegisterComponent(commander);
            }
        }
    }
}
