using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FrostArk.Core.Commands;
using FrostArk.Core.Crew;
using FrostArk.Core.Economy;
using FrostArk.Core.Ship;
using FrostArk.Core.Save;
using FrostArk.Core.Time;
using FrostArk.Core.World;

namespace FrostArk.Core.Application
{
    /// <summary>
    /// The application-owned, scoped authority for one run. Never duplicate
    /// totals into a MonoBehaviour: every read projection derives from _state.
    /// Core is Unity-independent and no callbacks determine simulation time.
    /// </summary>
    public sealed class WorldSession : IDisposable
    {
        private WorldState _state;
        private readonly SimulationScheduler _scheduler;
        private readonly WorldCommandBus _commands = new WorldCommandBus();
        private readonly IReadOnlyList<ProceduralOverride> _overrides;
        private PhysicalSceneCheckpoint _sceneCheckpoint;
        // Transient, scene-scoped repair attendance. Never saved, and never
        // substitutes for the canonical crew roster, staffed role or claim.
        private bool _shipWorkObservationActive;
        private EntityId _shipWorkScene;
        private readonly Dictionary<EntityId, WorkPresence> _workPresence =
            new Dictionary<EntityId, WorkPresence>();

        private readonly struct WorkPresence
        {
            public readonly EntityId SlotId;
            public readonly WorldMinute SampledAt;
            public WorkPresence(EntityId slotId, WorldMinute sampledAt)
            {
                SlotId = slotId;
                SampledAt = sampledAt;
            }
        }

        private bool _disposed;

        public event Action<WorldSessionChange> Changed;

        /// <summary>Empty foundation fixture, not final authored starting content.</summary>
        public WorldSession() : this(WorldState.Create(WorldMinute.Zero,
            Array.Empty<SupplyStock>(), Array.Empty<EntityId>())) {}

        public WorldSession(WorldState state, uint seed = 0,
            IEnumerable<IWorldMinuteSystem> systems = null,
            Func<WorldMinute, long> ambientMilliC = null)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _scheduler = new SimulationScheduler(new WorldClock(state.Minute),
                BindCanonicalSystems(systems, ambientMilliC), seed);
            _overrides = Array.Empty<ProceduralOverride>();
            _sceneCheckpoint = null;
        }

        private WorldSession(SaveGameDto snapshot,
            IEnumerable<IWorldMinuteSystem> systems,
            Func<WorldMinute, long> ambientMilliC)
        {
            _state = snapshot.State;
            _scheduler = snapshot.CreateScheduler(BindCanonicalSystems(systems, ambientMilliC));
            _overrides = new List<ProceduralOverride>(snapshot.ProceduralOverrides).AsReadOnly();
            _sceneCheckpoint = snapshot.SceneCheckpoint;
        }

        // Built-in reducers must read and commit through this ONE session-owned
        // WorldState pointer. The scheduler only owns ordering and time; it does
        // not own fuel, crew, jobs, movement, or inventory.
        private IEnumerable<IWorldMinuteSystem> BindCanonicalSystems(
            IEnumerable<IWorldMinuteSystem> additionalSystems,
            Func<WorldMinute, long> ambientMilliC)
        {
            if (_state.Thermal.Rooms.Count > 0 && ambientMilliC == null)
                throw new ArgumentException(
                    "An authored thermal world requires an explicit ambient temperature provider.",
                    nameof(ambientMilliC));

            var canonical = new List<IWorldMinuteSystem>
            {
                new ShipCrewMinuteSystem(() => _state, next => _state = next,
                    MayProduceAtWorldMinute),
                new ShipThermalMinuteSystem(() => _state, next => _state = next,
                    ambientMilliC ?? (_ => 0L), MayProduceAtWorldMinute),
                new WorkshopMinuteSystem(() => _state, next => _state = next),
                new ShipTravelMinuteSystem(() => _state, next => _state = next)
            };
            if (additionalSystems != null)
                canonical.AddRange(additionalSystems);
            return canonical;
        }

        public long Revision { get { Guard(); return _state.Revision; } }
        public WorldMinute Minute { get { Guard(); return _state.Minute; } }
        public PhysicalSceneCheckpoint SceneCheckpoint { get { Guard(); return _sceneCheckpoint; } }

        /// <summary>
        /// Record an authored scene/anchor re-entry checkpoint without advancing
        /// simulation time. Call after successful crew transfer and before saving.
        /// A null checkpoint means physical re-entry has not been established.
        /// </summary>
        public void SetSceneCheckpoint(PhysicalSceneCheckpoint checkpoint)
        {
            Guard();
            checkpoint?.ValidateAgainst(_state);
            if (checkpoint == null || checkpoint.Theater != PhysicalTheater.Ship ||
                checkpoint.SceneId != _shipWorkScene)
                StopShipWorkObservationInternal();
            _sceneCheckpoint = checkpoint;
        }

        /// <summary>
        /// The loaded ship scene opts in to observed physical repair attendance.
        /// When no loaded scene owns this observation, canonical offscreen
        /// staffing behaves exactly as before (including bulk fast-forward).
        /// </summary>
        public void BeginShipWorkObservation(EntityId sceneId)
        {
            Guard();
            if (!sceneId.IsValid || _sceneCheckpoint == null ||
                _sceneCheckpoint.Theater != PhysicalTheater.Ship ||
                _sceneCheckpoint.SceneId != sceneId)
                throw new InvalidOperationException("Work observation needs the loaded canonical ship scene.");
            _shipWorkObservationActive = true;
            _shipWorkScene = sceneId;
            _workPresence.Clear();
        }

        public void EndShipWorkObservation(EntityId sceneId)
        {
            Guard();
            // A departing stale scene must never clear a replacement scene's
            // newer authority, even if Unity teardown events arrive late.
            if (_shipWorkObservationActive && _shipWorkScene == sceneId)
                StopShipWorkObservationInternal();
        }

        private void StopShipWorkObservationInternal()
        {
            _shipWorkObservationActive = false;
            _shipWorkScene = default;
            _workPresence.Clear();
        }

        /// <summary>
        /// Scene adapter calls ONLY after checking the physical walker is
        /// stationary at the real authored Work slot. This proves presence
        /// for ONE following WorldClock minute, not an arbitrary time skip.
        /// Core additionally checks the canonical assignment, slot, owner,
        /// availability and lease. No revision or event is generated.
        /// </summary>
        public bool ReportShipWorkPresence(EntityId crewId, EntityId slotId)
        {
            Guard();
            var workSlot = _state.CrewDuty.Interactions.GetSlot(slotId);
            return workSlot != null && workSlot.Verb == CrewInteractionVerb.Work &&
                ReportShipDutyPresence(crewId, slotId);
        }

        /// <summary>
        /// A rendered stationary Work OR Rest actor can attest the real owned
        /// physical slot for one following minute. Rest is never repair or
        /// boiler productivity, but can safely extend its own slot lease.
        /// </summary>
        public bool ReportShipDutyPresence(EntityId crewId, EntityId slotId)
        {
            Guard();
            if (!_shipWorkObservationActive || !_shipWorkScene.IsValid ||
                _sceneCheckpoint == null ||
                _sceneCheckpoint.Theater != PhysicalTheater.Ship ||
                _sceneCheckpoint.SceneId != _shipWorkScene)
                return false;
            var member = _state.CrewDuty.Get(crewId);
            var slot = _state.CrewDuty.Interactions.GetSlot(slotId);
            var claim = _state.CrewDuty.Interactions.GetClaim(slotId);
            if (member == null || ! _state.HasCrew(crewId) ||
                member.IsAway || member.Wounds >= 1000 ||
                slot == null ||
                (slot.Verb != CrewInteractionVerb.Work &&
                 slot.Verb != CrewInteractionVerb.Rest) ||
                _state.Layout.GetModule(slot.SourceId) == null ||
                (slot.Verb == CrewInteractionVerb.Work &&
                 _state.GetCrewStation(crewId) != slot.SourceId) ||
                claim == null || claim.CrewId != crewId ||
                claim.ExpiresAt <= _state.Minute)
                return false;
            _workPresence[crewId] = new WorkPresence(slotId, _state.Minute);
            return true;
        }

        /// <summary>Selection, issued order, moving, cancelled/invalidated
        /// activity or scene destruction revoke the transient physical proof.</summary>
        public void ClearShipWorkPresence(EntityId crewId)
        {
            Guard();
            _workPresence.Remove(crewId);
        }

        private bool MayProduceAtWorldMinute(EntityId crewId, DutyRole role,
            WorldMinute minute)
        {
            if (!_shipWorkObservationActive ||
                (role != DutyRole.Repair && role != DutyRole.Engineering))
                return true; // Offscreen and non-work systems are unchanged.
            if (role == DutyRole.Engineering)
            {
                // Existing saves/ships may not author a physical Engineering
                // Work station yet. Do not turn off their canonical boilers
                // simply by loading the scene. Once authored, require presence.
                var station = _state.GetCrewStation(crewId);
                if (!station.IsValid || _state.Layout.GetModule(station) == null ||
                    !_state.CrewDuty.Interactions.Slots.Any(slot =>
                        slot.SourceId == station && slot.Verb == CrewInteractionVerb.Work))
                    return true;
            }
            if (!_workPresence.TryGetValue(crewId, out var proof) ||
                proof.SampledAt.Value != minute.Value - 1)
                return false;
            var slot = _state.CrewDuty.Interactions.GetSlot(proof.SlotId);
            var claim = _state.CrewDuty.Interactions.GetClaim(proof.SlotId);
            return slot != null && slot.Verb == CrewInteractionVerb.Work &&
                _state.Layout.GetModule(slot.SourceId) != null &&
                _state.GetCrewStation(crewId) == slot.SourceId &&
                claim != null && claim.CrewId == crewId &&
                claim.ExpiresAt > minute;
        }

        public WorldSessionView Read()
        {
            Guard();
            return new WorldSessionView(_state);
        }

        public CommandResult Submit(IGameCommand command)
        {
            Guard();
            var result = _commands.Execute(_state, command);
            if (result.Success)
            {
                _state = result.State;
                Notify(new WorldSessionChange(WorldSessionChangeKind.Command,
                    new WorldSessionView(_state), result.Events));
            }
            return result;
        }

        public void Schedule(ScheduledWorldEvent worldEvent)
        {
            Guard();
            _scheduler.Schedule(worldEvent);
        }

        public WorldAdvanceResult Advance(long minutes)
        {
            Guard();
            // Never let a single rendered frame renew an entire sleep/skip.
            // One live, observed minute may extend a real occupied duty lease.
            if (minutes == 1) RenewFreshObservedDutyLeases();
            return CommitAdvance(_scheduler.Advance(minutes));
        }

        public WorldAdvanceResult AdvanceUntil(WorldMinute target,
            SleepInterruptionPolicy policy = null)
        {
            Guard();
            if (target.Value - _scheduler.Clock.Current.Value == 1)
                RenewFreshObservedDutyLeases();
            return CommitAdvance(_scheduler.AdvanceUntil(target, policy));
        }

        /// <summary>
        /// Safe single-minute boundary for on-stage Work/Rest attendance:
        /// renew via the existing revisioned command only when an actual
        /// stationary slot was freshly witnessed THIS world minute. An
        /// Advance(20) never reuses that observation at later boundaries.
        /// No free extension for travelling, unclaimed, expired or offscreen.
        /// </summary>
        private void RenewFreshObservedDutyLeases()
        {
            if (!_shipWorkObservationActive || _sceneCheckpoint == null ||
                _sceneCheckpoint.Theater != PhysicalTheater.Ship ||
                _sceneCheckpoint.SceneId != _shipWorkScene) return;
            var now = _scheduler.Clock.Current;
            foreach (var pair in _workPresence.OrderBy(x => x.Key.Value,
                StringComparer.Ordinal).ToArray())
            {
                var crew = pair.Key;
                var proof = pair.Value;
                if (proof.SampledAt != now) continue;
                var slot = _state.CrewDuty.Interactions.GetSlot(proof.SlotId);
                var claim = _state.CrewDuty.Interactions.GetClaim(proof.SlotId);
                var member = _state.CrewDuty.Get(crew);
                if (slot == null || claim == null || claim.CrewId != crew ||
                    (slot.Verb != CrewInteractionVerb.Work &&
                     slot.Verb != CrewInteractionVerb.Rest) ||
                    member == null || member.IsAway || member.Wounds >= 1000 ||
                    !_state.HasCrew(crew) ||
                    _state.Layout.GetModule(slot.SourceId) == null ||
                    (slot.Verb == CrewInteractionVerb.Work &&
                     _state.GetCrewStation(crew) != slot.SourceId) ||
                    claim.ExpiresAt <= now ||
                    claim.ExpiresAt.Value - now.Value > 5)
                    continue;
                // Arrival and ownership are necessary but not sufficient:
                // a shift change or an urgent Need must not prolong stale
                // Work/Rest ownership once the canonical duty offer vanishes.
                var offered = CrewDutyActivityPlanner.Find(_state, crew);
                if (!offered.HasValue || offered.Value.SlotId != proof.SlotId)
                    continue;
                var cmdId = EntityId.Parse("cmd:observed-duty-renew:" +
                    crew.Value + ":" + now.Value.ToString(CultureInfo.InvariantCulture));
                // The regular WorldCommandBus enforces provenance, lifetime,
                // expected revision and ownership; don't bypass WorldState.
                Submit(new RenewCrewDutySlotCommand(cmdId, crew, proof.SlotId,
                    15, _state.Revision));
            }
        }

        public void AdvanceCombatTick(long tick)
        {
            Guard();
            var next = _state.AtCombatTick(tick);
            if (!ReferenceEquals(next, _state))
            {
                _state = next;
                Notify(new WorldSessionChange(WorldSessionChangeKind.Time,
                    new WorldSessionView(_state), Array.Empty<FrostArk.Core.Events.WorldEvent>()));
            }
        }

        public SaveGameDto Capture()
        {
            Guard();
            return SaveGameDto.Capture(_state, _scheduler, _overrides, _sceneCheckpoint);
        }

        public static WorldSession Restore(SaveGameDto snapshot,
            IEnumerable<IWorldMinuteSystem> systems = null,
            Func<WorldMinute, long> ambientMilliC = null)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));
            return new WorldSession(snapshot, systems, ambientMilliC);
        }

        private WorldAdvanceResult CommitAdvance(WorldAdvanceResult result)
        {
            if (result.AdvancedMinutes > 0)
            {
                _state = _state.AtMinute(result.EndedAt);
                Notify(new WorldSessionChange(WorldSessionChangeKind.Time,
                    new WorldSessionView(_state), Array.Empty<FrostArk.Core.Events.WorldEvent>()));
            }
            return result;
        }

        private void Notify(WorldSessionChange change)
        {
            Changed?.Invoke(change);
        }

        private void Guard()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WorldSession));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopShipWorkObservationInternal();
            Changed = null;
        }
    }
}
