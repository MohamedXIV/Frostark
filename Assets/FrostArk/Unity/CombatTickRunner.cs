using System;
using FrostArk.Core.Application;
using UnityEngine;
using VContainer;

namespace FrostArk.Unity
{
    /// <summary>
    /// Explicit owner of the trusted fixed-step combat tick progression in Unity.
    /// Advances WorldSession combat ticks at a fixed cadence without modifying
    /// the authoritative world calendar (WorldMinute) or fabricating commands.
    /// </summary>
    public class CombatTickRunner : MonoBehaviour
    {
        [SerializeField]
        private float tickRateHz = 10.0f;

        private WorldSession _session;
        private float _accumulator;
        private long _currentTick;

        public float TickRateHz
        {
            get => tickRateHz;
            set => tickRateHz = Mathf.Max(0.1f, value);
        }

        public long CurrentTick => _currentTick;

        [Inject]
        public void Construct(WorldSession session)
        {
            SetSession(session);
        }

        public void SetSession(WorldSession session)
        {
            _session = session;
            if (_session != null)
            {
                _currentTick = _session.Capture().State.Combat.Tick;
            }
        }

        private void FixedUpdate()
        {
            if (_session == null || tickRateHz <= 0f)
                return;

            float interval = 1.0f / tickRateHz;
            _accumulator += Time.fixedDeltaTime;

            while (_accumulator >= interval)
            {
                _accumulator -= interval;
                Step(1);
            }
        }

        public void Step(int ticks = 1)
        {
            if (ticks <= 0) return;
            _currentTick += ticks;
            if (_session != null)
            {
                _session.AdvanceCombatTick(_currentTick);
            }
        }
    }
}
