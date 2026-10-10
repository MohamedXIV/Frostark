using FrostArk.Core.Application;
using VContainer;
using VContainer.Unity;

namespace FrostArk.Unity
{
    /// <summary>
    /// Run/session composition root. No process-global state or scene-owned
    /// resource totals: one WorldSession is scoped to this LifetimeScope.
    /// A future persistent parent scope may provide infrastructure ports.
    /// </summary>
    public sealed class WorldSessionScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            // Explicit constructor selection: WorldSession also supports restoring
            // from an existing WorldState, which is not a DI dependency.
            builder.Register(_ => new WorldSession(), Lifetime.Scoped);
            builder.RegisterComponentInHierarchy<WorldSessionPresenter>();
        }
    }
}
