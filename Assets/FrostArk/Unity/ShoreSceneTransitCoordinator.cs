using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Save;
using FrostArk.Core.World;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using VContainer;
using EntityId = FrostArk.Core.World.EntityId;

namespace FrostArk.Unity
{
    /// <summary>
    /// Optional run-root Unity adapter for the existing authoritative WorldSession.
    /// The root is retained across additive environment-scene changes; destination
    /// scenes must NOT own WorldSessionScope or duplicate a simulation clock.
    ///
    /// G2/G3 scene lifecycle acceptance is required before shipping this adapter.
    /// </summary>
    [RequireComponent(typeof(WorldSessionScope))]
    public sealed class ShoreSceneTransitCoordinator : MonoBehaviour
    {
        [SerializeField] private SceneTransitDestination[] destinations =
            Array.Empty<SceneTransitDestination>();

        private WorldSession _session;
        private TacticalCommandController _commander;

        public bool IsBusy { get; private set; }
        public string LastError { get; private set; }

        public event Action<PhysicalSceneCheckpoint> TransitionCompleted;

        private void Awake()
        {
            // This component is deliberately opt-in, only on the run-scope ROOT.
            // Environment scenes are loaded additively and then replace one
            // another without ever destroying the authoritative run container.
            if (transform.parent != null)
            {
                Debug.LogError("[FrostArk] Scene transit must be on the run scope root.", this);
                enabled = false;
                return;
            }
            DontDestroyOnLoad(gameObject);
        }

        [Inject]
        public void Construct(WorldSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _commander = GetComponentInChildren<TacticalCommandController>();
        }

        /// <summary>
        /// The caller has selected the party and requested an authored landing.
        /// The physical destination must be loaded and validated before crew
        /// availability changes. This is not an implicit movement command.
        /// </summary>
        public bool TryDepart(SceneTransitDestination shore,
            IEnumerable<EntityId> selectedCrew, EntityId transitionId)
        {
            if (selectedCrew == null)
                return Reject("NoCrewSelection");
            var party = selectedCrew.ToArray();
            return TryBegin(shore, party, transitionId, commitCrew: true,
                expectedTheater: PhysicalTheater.Shore);
        }

        public bool TryReturn(SceneTransitDestination ship, EntityId transitionId)
        {
            if (_session == null || _session.SceneCheckpoint == null ||
                _session.SceneCheckpoint.Theater != PhysicalTheater.Shore)
                return Reject("NoActiveShoreCheckpoint");
            return TryBegin(ship, _session.SceneCheckpoint.PartyIds.ToArray(),
                transitionId, commitCrew: true, expectedTheater: PhysicalTheater.Ship);
        }

        /// <summary>
        /// Call on a restored WorldSession whose v13 SceneCheckpoint is present.
        /// A legacy null checkpoint is UNKNOWN: never guess the ship scene.
        /// </summary>
        public bool TryRestorePhysicalScene()
        {
            if (_session == null || _session.SceneCheckpoint == null)
                return Reject("MissingPhysicalCheckpoint");

            var saved = _session.SceneCheckpoint;
            var destination = destinations.FirstOrDefault(x => x != null && x.Matches(saved));
            if (destination == null)
                return Reject("UnmappedSavedSceneOrAnchor");
            return TryBegin(destination, saved.PartyIds.ToArray(), default,
                commitCrew: false, expectedTheater: saved.Theater);
        }

        private bool TryBegin(SceneTransitDestination destination, EntityId[] party,
            EntityId transitionId, bool commitCrew, PhysicalTheater expectedTheater)
        {
            if (!enabled || IsBusy || _session == null)
                return Reject("SceneTransitUnavailable");
            if (destination == null || !destination.IsConfigured ||
                destination.Theater != expectedTheater)
                return Reject("InvalidSceneDestination");
            if (commitCrew && !transitionId.IsValid)
                return Reject("MissingTransferOperationId");
            if (party == null ||
                (commitCrew && party.Length == 0) ||
                party.Any(x => !x.IsValid) || party.Distinct().Count() != party.Length)
                return Reject("InvalidCrewManifest");
            if (!Application.CanStreamedLevelBeLoaded(destination.ScenePath))
                return Reject("SceneNotInBuildSettings");

            var previous = SceneManager.GetActiveScene();
            if (!previous.IsValid() || !previous.isLoaded ||
                previous.path == destination.ScenePath)
                return Reject("InvalidOrAlreadyActiveScene");

            LastError = null;
            IsBusy = true;
            StartCoroutine(LoadValidateAndTransfer(previous, destination, party,
                transitionId, commitCrew));
            return true; // Started asynchronously; NOT an accepted domain transition yet.
        }

        private IEnumerator LoadValidateAndTransfer(Scene previous,
            SceneTransitDestination destination, EntityId[] party, EntityId transitionId,
            bool commitCrew)
        {
            var operation = SceneManager.LoadSceneAsync(destination.ScenePath, LoadSceneMode.Additive);
            if (operation == null)
            {
                Reject("SceneLoadFailed");
                IsBusy = false;
                yield break;
            }
            yield return operation;

            var loaded = SceneManager.GetSceneByPath(destination.ScenePath);
            if (!loaded.IsValid() || !loaded.isLoaded)
            {
                Reject("SceneLoadIncomplete");
                IsBusy = false;
                yield break;
            }

            if (!TryResolve(loaded, destination, party, out var anchor,
                    out var proxies, out var sceneCamera, out var failure))
            {
                Reject(failure);
                yield return SceneManager.UnloadSceneAsync(loaded);
                IsBusy = false;
                yield break;
            }

            // Geometry and NavMesh are engine truth: validate and warp scene
            // proxies BEFORE touching crew WorldState. Failure leaves source
            // scene and authoritative WorldSession exactly as they were.
            for (var i = 0; i < proxies.Count; i++)
            {
                var samplePoint = anchor.position +
                    new Vector3((i % 3 - 1) * 1.0f, 0f, (i / 3) * 1.0f);
                if (!NavMesh.SamplePosition(samplePoint, out var hit, 2f, NavMesh.AllAreas) ||
                    proxies[i].Agent == null || !proxies[i].Agent.isOnNavMesh ||
                    !proxies[i].Agent.Warp(hit.position))
                {
                    Reject("ActorSpawnNotOnNavMesh");
                    yield return SceneManager.UnloadSceneAsync(loaded);
                    IsBusy = false;
                    yield break;
                }
            }

            PhysicalSceneCheckpoint checkpoint;
            try
            {
                checkpoint = destination.MakeCheckpoint(party);
                if (commitCrew)
                {
                    var transfer = ShoreCrewTransfer.Transfer(_session, party,
                        destination.Theater == PhysicalTheater.Shore, transitionId);
                    if (!transfer.Success)
                    {
                        // This can include rare mid-batch failure. Do not
                        // silently roll back successful canonical commands.
                        Reject("CrewTransferRejected:" + transfer.Failure);
                        yield return SceneManager.UnloadSceneAsync(loaded);
                        IsBusy = false;
                        yield break;
                    }
                }
                _session.SetSceneCheckpoint(checkpoint);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                Reject("CheckpointRejected:" + ex.Message);
                yield return SceneManager.UnloadSceneAsync(loaded);
                IsBusy = false;
                yield break;
            }

            if (!SceneManager.SetActiveScene(loaded))
            {
                // Commit has happened; never pretend domain state rolled back.
                Reject("SceneActivationFailedAfterDomainCommit");
                IsBusy = false;
                yield break;
            }

            if (previous.IsValid() && previous.isLoaded &&
                previous != loaded && previous.path != null)
            {
                var unload = SceneManager.UnloadSceneAsync(previous);
                if (unload != null)
                    yield return unload;
            }

            if (_commander == null)
                _commander = GetComponentInChildren<TacticalCommandController>();
            if (_commander != null)
            {
                _commander.ClearSelection();
                _commander.SetCamera(sceneCamera);
                _commander.RefreshSceneRegistrations();
            }

            LastError = null;
            IsBusy = false;
            TransitionCompleted?.Invoke(checkpoint);
        }

        private bool TryResolve(Scene loaded, SceneTransitDestination destination,
            EntityId[] party, out Transform anchor,
            out List<TacticalActorProxy> proxies, out Camera camera,
            out string failure)
        {
            anchor = null;
            camera = null;
            proxies = new List<TacticalActorProxy>();
            failure = null;
            var seen = new HashSet<EntityId>();
            var targets = new HashSet<EntityId>(party);

            foreach (var root in loaded.GetRootGameObjects())
            {
                if (root.GetComponentsInChildren<WorldSessionScope>(true).Length > 0)
                {
                    failure = "DestinationContainsAnotherSessionScope";
                    return false;
                }
                foreach (var marker in root.GetComponentsInChildren<SceneReentryAnchor>(true))
                {
                    if (marker.Matches(destination.SceneId, destination.AnchorId))
                    {
                        if (anchor != null)
                        {
                            failure = "DuplicateSceneReentryAnchor";
                            return false;
                        }
                        anchor = marker.transform;
                    }
                }
                foreach (var actor in root.GetComponentsInChildren<TacticalActorProxy>(true))
                {
                    if (!seen.Add(actor.ActorId))
                    {
                        failure = "DuplicatePersistentActorProxyId";
                        return false;
                    }
                    if (targets.Contains(actor.ActorId))
                        proxies.Add(actor);
                }
                if (camera == null)
                    camera = root.GetComponentInChildren<Camera>(true);
            }

            if (anchor == null)
                failure = "MissingSceneReentryAnchor";
            else if (proxies.Count != targets.Count)
                failure = "MissingPersistentCrewProxy";
            else if (camera == null)
                failure = "MissingTacticalSceneCamera";
            else
                proxies.Sort((a,b) => string.CompareOrdinal(a.ActorId.Value, b.ActorId.Value));

            return failure == null;
        }

        private bool Reject(string error)
        {
            LastError = error;
            Debug.LogWarning("[FrostArk] Scene transfer rejected: " + error, this);
            return false;
        }
    }
}
