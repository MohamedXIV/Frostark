using System;
using FrostArk.Core.Application;
using FrostArk.Core.Commands;
using UnityEngine;
using VContainer;

namespace FrostArk.Unity
{
    /// <summary>
    /// Minimal Unity presentation adapter. All displayed state is projected
    /// from WorldSession; no duplicate authoritative crew or supply fields.
    /// Does not advance simulation from Update or FixedUpdate.
    /// </summary>
    public sealed class WorldSessionPresenter : MonoBehaviour
    {
        private WorldSession _session;

        public bool IsBound => _session != null;
        public string StatusText { get; private set; } = "Session not bound";

        [Inject]
        public void Construct(WorldSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (_session != null) _session.Changed -= OnSessionChanged;

            _session = session;
            _session.Changed += OnSessionChanged;
            RenderCurrent();
        }

        public WorldSessionView Read()
        {
            if (_session == null)
                throw new InvalidOperationException("Presenter requires WorldSession injection.");
            return _session.Read();
        }

        public CommandResult Submit(IGameCommand command)
        {
            if (_session == null)
                throw new InvalidOperationException("Presenter requires WorldSession injection.");
            return _session.Submit(command);
        }

        private void OnSessionChanged(WorldSessionChange change)
        {
            RenderCurrent();
        }

        private void RenderCurrent()
        {
            var view = _session.Read();
            StatusText = "FrostArk | minute " + view.Minute.Value +
                " | revision " + view.Revision;
        }

        private void OnDestroy()
        {
            if (_session != null)
            {
                _session.Changed -= OnSessionChanged;
                _session = null;
            }
        }
    }
}
