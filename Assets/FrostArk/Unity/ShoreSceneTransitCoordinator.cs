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
        private Transform _residentShipRoot;
        private EntityId _residentShipSceneId;

        public bool IsBusy { get; private set; }
        public string LastError { get; private set; }
        public WorldSession Session => _session;
        public Transform ResidentShipRoot => _residentShipRoot;
        public System.Collections.Generic.IReadOnlyList<SceneTransitDestination> Destinations => destinations;

        public event Action<PhysicalSceneCheckpoint> TransitionCompleted;

        public void BindResidentShipRoot(Transform shipRoot, EntityId shipSceneId)
        {
            _residentShipRoot = shipRoot != null ? shipRoot :
                throw new ArgumentNullException(nameof(shipRoot));
            if (!shipSceneId.IsValid)
                throw new ArgumentException("Valid ship scene ID required.", nameof(shipSceneId));
            _residentShipSceneId = shipSceneId;
        }

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
            if (_commander != null && _session != null)
                _commander.SetSession(_session);
        }


        /// <summary>
        /// Explicit fixture/authoring hook. Set before scene-resume requests.
        /// A missing authored destination is a reported error, not a fallback.
        /// </summary>
        public void ConfigureDestinations(params SceneTransitDestination[] authored)
        {
            destinations = authored ?? throw new ArgumentNullException(nameof(authored));
        }

        public bool TryDepartSelected(SceneTransitDestination shore, EntityId transitionId)
        {
            if (_commander == null)
                _commander = GetComponentInChildren<TacticalCommandController>();
            if (_commander == null)
                return Reject("MissingTacticalCommander");
            return TryDepart(shore, _commander.Selection.SelectedActorIds, transitionId);
        }

        /// <summary>
        /// The caller has selected the party and requested an authored landing.
        /// The physical destination must be loaded and validated before crew
        /// availability changes. This is not an implicit movement command.
        /// </summary>
        public bool TryDepart(SceneTransitDestination shore,
            IEnumerable<EntityId> selectedCrew, EntityId transitionId)
        {
            if (IsBusy)
                return Reject("SceneTransitUnavailable");
            if (selectedCrew == null)
                return Reject("NoCrewSelection");
            var party = selectedCrew.ToArray();
            return TryBegin(shore, party, transitionId, commitCrew: true,
                expectedTheater: PhysicalTheater.Shore);
        }

        public bool TryReturn(SceneTransitDestination ship, EntityId transitionId)
        {
            if (IsBusy)
                return Reject("SceneTransitUnavailable");
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
        public bool TryRestorePhysicalScene(PhysicalSceneCheckpoint checkpoint = null)
        {
            if (IsBusy)
                return Reject("SceneTransitUnavailable");
            var saved = checkpoint ?? _session?.SceneCheckpoint;
            if (saved == null)
                return Reject("MissingPhysicalCheckpoint");

            var destination = destinations?.FirstOrDefault(x => x != null && x.Matches(saved));
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
            if (!previous.IsValid() || !previous.isLoaded)
                return Reject("InvalidActiveScene");
            if (commitCrew && previous.path == destination.ScenePath)
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
            yield return null;
            Scene loaded = default;
            bool sameActiveScene = !commitCrew && previous.IsValid() && previous.isLoaded &&
                previous.path == destination.ScenePath;
            bool reuseLoadedShip = !sameActiveScene && destination.Theater == PhysicalTheater.Ship &&
                TryFindLoadedShipScene(destination, out loaded);
            if (sameActiveScene)
            {
                loaded = previous;
            }
            else if (!reuseLoadedShip)
            {
                var operation = SceneManager.LoadSceneAsync(destination.ScenePath, LoadSceneMode.Additive);
                if (operation == null)
                {
                    Reject("SceneLoadFailed");
                    IsBusy = false;
                    yield break;
                }
                yield return operation;

                loaded = SceneManager.GetSceneByPath(destination.ScenePath);
                if (!loaded.IsValid() || !loaded.isLoaded)
                {
                    Reject("SceneLoadIncomplete");
                    IsBusy = false;
                    yield break;
                }
            }
            else
            {
                PrepareResidentShipSceneForReturn(loaded, party);
            }

            bool shouldUnloadOnFailure = !reuseLoadedShip && !sameActiveScene && loaded != previous;

            if (!TryResolve(loaded, destination, party, reuseLoadedShip, out var anchor,
                    out var proxies, out var shipProxies, out var sceneCamera, out var failure))
            {
                Reject(failure);
                if (shouldUnloadOnFailure)
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
                    proxies[i].Agent == null || !proxies[i].Agent.isActiveAndEnabled ||
                    !proxies[i].Agent.Warp(hit.position) || !proxies[i].Agent.isOnNavMesh)
                {
                    Reject("ActorSpawnNotOnNavMesh");
                    if (shouldUnloadOnFailure)
                        yield return SceneManager.UnloadSceneAsync(loaded);
                    IsBusy = false;
                    yield break;
                }
            }
            for (var i = 0; i < shipProxies.Count; i++)
            {
                if (shipProxies[i].Walker == null || !shipProxies[i].Walker.IsConfigured)
                {
                    Reject("MissingConfiguredShipWalker");
                    if (shouldUnloadOnFailure)
                        yield return SceneManager.UnloadSceneAsync(loaded);
                    IsBusy = false;
                    yield break;
                }
            }

            PhysicalSceneCheckpoint checkpoint = null;
            string commitFailure = null;
            try
            {
                checkpoint = destination.MakeCheckpoint(party);
                if (commitCrew)
                {
                    var transfer = ShoreCrewTransfer.Transfer(_session, party,
                        destination.Theater == PhysicalTheater.Shore, transitionId);
                    if (!transfer.Success)
                    {
                        // Ordinary rejected groups fail preflight, without
                        // touching the world. Rare reentrant mid-batch
                        // conflicts must remain visible for reconciliation.
                        commitFailure = "CrewTransferRejected:" + transfer.Failure;
                    }
                }
                if (commitFailure == null)
                    _session.SetSceneCheckpoint(checkpoint);
            }
            catch (Exception ex) when (ex is ArgumentException ||
                                       ex is InvalidOperationException)
            {
                commitFailure = "CheckpointRejected:" + ex.Message;
            }
            if (commitFailure != null)
            {
                Reject(commitFailure);
                if (shouldUnloadOnFailure)
                    yield return SceneManager.UnloadSceneAsync(loaded);
                IsBusy = false;
                yield break;
            }

            if (destination.Theater == PhysicalTheater.Shore)
                SyncLoadedShoreProxies(loaded, party);
            else
                SyncResidentShipBootstraps(loaded);

            if (SceneManager.GetActiveScene() != loaded && !SceneManager.SetActiveScene(loaded))
            {
                // Commit has happened; never pretend domain state rolled back.
                Reject("SceneActivationFailedAfterDomainCommit");
                IsBusy = false;
                yield break;
            }

            bool keepPreviousScene = previous.IsValid() && previous.isLoaded &&
                previous != loaded && IsResidentShipScene(previous);
            if (keepPreviousScene)
            {
                SyncResidentShipSceneForShoreDeparture(previous, party, sceneCamera);
                if (_residentShipRoot == null)
                {
                    var shipRootObj = previous.GetRootGameObjects()
                        .FirstOrDefault(go => go != null && (go.name == "ShipDeckEnvironment" ||
                            go.GetComponentInChildren<ShipTacticalSceneBootstrap>(true) != null));
                    if (shipRootObj != null)
                    {
                        var bootstrap = shipRootObj.GetComponentInChildren<ShipTacticalSceneBootstrap>(true);
                        EntityId shipSceneId = bootstrap != null && bootstrap.BoundShipSceneId.IsValid
                            ? bootstrap.BoundShipSceneId
                            : EntityId.Parse("scene:ship:deck");
                        BindResidentShipRoot(shipRootObj.transform, shipSceneId);
                    }
                }
            }
            else if (previous.IsValid() && previous.isLoaded &&
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
                if (_session != null)
                    _commander.SetSession(_session);
                _commander.ClearSelection();
                _commander.SetCamera(sceneCamera);
                _commander.RefreshSceneRegistrations();
            }

            LastError = null;
            IsBusy = false;
            TransitionCompleted?.Invoke(checkpoint);
        }

        private bool IsResidentShipScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
                return false;
            if (_residentShipRoot != null && _residentShipRoot.gameObject.scene == scene)
                return true;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root != null && (root.name == "ShipDeckEnvironment" || root.GetComponentInChildren<ShipTacticalSceneBootstrap>(true) != null))
                    return true;
            }
            if (destinations != null && !string.IsNullOrEmpty(scene.path))
            {
                foreach (var dest in destinations)
                {
                    if (dest != null && dest.IsConfigured &&
                        dest.Theater == PhysicalTheater.Ship &&
                        dest.ScenePath == scene.path)
                        return true;
                }
            }
            return false;
        }

        private bool TryFindLoadedShipScene(SceneTransitDestination destination, out Scene loaded)
        {
            loaded = default;
            if (destination == null || destination.Theater != PhysicalTheater.Ship)
                return false;

            // 1. If we have a bound resident ship root in an active loaded scene matching ship identity:
            if (_residentShipRoot != null &&
                (!_residentShipSceneId.IsValid || !destination.SceneId.IsValid || _residentShipSceneId == destination.SceneId))
            {
                var rootScene = _residentShipRoot.gameObject.scene;
                if (rootScene.IsValid() && rootScene.isLoaded)
                {
                    loaded = rootScene;
                    return true;
                }
            }

            // 2. Scan loaded scenes for an existing resident ship scene:
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var candidate = SceneManager.GetSceneAt(i);
                if (!candidate.IsValid() || !candidate.isLoaded) continue;
                if (IsResidentShipScene(candidate))
                {
                    foreach (var root in candidate.GetRootGameObjects())
                    {
                        if (root == null) continue;
                        var bootstrap = root.GetComponentInChildren<ShipTacticalSceneBootstrap>(true);
                        if (bootstrap != null && (!destination.SceneId.IsValid || bootstrap.BoundShipSceneId == destination.SceneId))
                        {
                            BindResidentShipRoot(root.transform, bootstrap.BoundShipSceneId);
                            loaded = candidate;
                            return true;
                        }
                        if (root.name == "ShipDeckEnvironment")
                        {
                            var sceneId = destination.SceneId.IsValid ? destination.SceneId : EntityId.Parse("scene:ship:deck");
                            BindResidentShipRoot(root.transform, sceneId);
                            loaded = candidate;
                            return true;
                        }
                    }
                }
            }

            // 3. Fallback: exact path match if no resident ship is loaded:
            var byPath = SceneManager.GetSceneByPath(destination.ScenePath);
            if (byPath.IsValid() && byPath.isLoaded)
            {
                loaded = byPath;
                return true;
            }

            return false;
        }

        private static void SyncLoadedShoreProxies(Scene shoreScene, EntityId[] activeParty)
        {
            var partySet = new HashSet<EntityId>(activeParty);
            foreach (var root in shoreScene.GetRootGameObjects())
            {
                if (root == null) continue;
                foreach (var actor in root.GetComponentsInChildren<TacticalActorProxy>(true))
                {
                    if (actor != null)
                        actor.gameObject.SetActive(partySet.Contains(actor.ActorId));
                }
            }
        }

        private static void SyncResidentShipBootstraps(Scene shipScene)
        {
            foreach (var root in shipScene.GetRootGameObjects())
            {
                if (root == null) continue;
                foreach (var bootstrap in root.GetComponentsInChildren<ShipTacticalSceneBootstrap>(true))
                    bootstrap?.SyncCrewPresenceWithSession();
            }
        }

        private static void SyncResidentShipSceneForShoreDeparture(Scene shipScene,
            EntityId[] departingParty, Camera shoreCamera)
        {
            var awaySet = new HashSet<EntityId>(departingParty);
            foreach (var root in shipScene.GetRootGameObjects())
            {
                if (root == null) continue;
                foreach (var bootstrap in root.GetComponentsInChildren<ShipTacticalSceneBootstrap>(true))
                    bootstrap?.SyncCrewPresenceWithSession();
                foreach (var actor in root.GetComponentsInChildren<TacticalActorProxy>(true))
                {
                    if (actor != null && awaySet.Contains(actor.ActorId))
                        actor.gameObject.SetActive(false);
                }
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    if (cam != null && cam != shoreCamera)
                        cam.enabled = false;
                }
            }
        }

        private static void PrepareResidentShipSceneForReturn(Scene shipScene,
            EntityId[] returningParty)
        {
            var returnSet = new HashSet<EntityId>(returningParty);
            foreach (var root in shipScene.GetRootGameObjects())
            {
                if (root == null) continue;
                foreach (var cam in root.GetComponentsInChildren<Camera>(true))
                {
                    if (cam != null)
                        cam.enabled = true;
                }
                foreach (var actor in root.GetComponentsInChildren<TacticalActorProxy>(true))
                {
                    if (actor != null && returnSet.Contains(actor.ActorId))
                        actor.gameObject.SetActive(true);
                }
            }
        }

        private bool TryResolve(Scene loaded, SceneTransitDestination destination,
            EntityId[] party, bool reuseLoadedShip, out Transform anchor,
            out List<TacticalActorProxy> proxies,
            out List<ShipCrewSelectionProxy> shipProxies,
            out Camera camera, out string failure)
        {
            anchor = null;
            camera = null;
            proxies = new List<TacticalActorProxy>();
            shipProxies = new List<ShipCrewSelectionProxy>();
            failure = null;
            var seen = new HashSet<EntityId>();
            var seenShip = new HashSet<EntityId>();
            var targets = new HashSet<EntityId>(party);

            foreach (var root in loaded.GetRootGameObjects())
            {
                if (!reuseLoadedShip &&
                    root.GetComponentsInChildren<WorldSessionScope>(true).Length > 0)
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
                if (destination.Theater == PhysicalTheater.Ship)
                {
                    foreach (var shipActor in root.GetComponentsInChildren<ShipCrewSelectionProxy>(true))
                    {
                        if (!seenShip.Add(shipActor.ActorId))
                        {
                            failure = "DuplicatePersistentActorProxyId";
                            return false;
                        }
                        if (targets.Contains(shipActor.ActorId))
                            shipProxies.Add(shipActor);
                    }
                }
                if (camera == null)
                    camera = root.GetComponentInChildren<Camera>(true);
            }

            var resolvedProxies = proxies;
            var resolvedShipProxies = shipProxies;
            if (anchor == null)
                failure = "MissingSceneReentryAnchor";
            else if (resolvedProxies.Count + resolvedShipProxies.Count < targets.Count ||
                     !targets.All(id => resolvedProxies.Any(p => p.ActorId == id) ||
                                        resolvedShipProxies.Any(s => s.ActorId == id)))
                failure = "MissingPersistentCrewProxy";
            else if (camera == null)
                failure = "MissingTacticalSceneCamera";
            else
            {
                proxies.Sort((a, b) => string.CompareOrdinal(a.ActorId.Value, b.ActorId.Value));
                shipProxies.Sort((a, b) => string.CompareOrdinal(a.ActorId.Value, b.ActorId.Value));
            }

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
