using System;
using System.Collections;
using System.Linq;
using FrostArk.Core.Application;
using FrostArk.Core.Save;
using UnityEngine;
using UnityEngine.InputSystem;
using VContainer;

namespace FrostArk.Unity
{
    /// <summary>
    /// Player-accessible disk Save/Load coordinator. Provides F5/F9 hotkeys and
    /// a lightweight debug UI for saving and restoring the active WorldSession
    /// and aligning physical scenes via ShoreSceneTransitCoordinator.
    /// </summary>
    public sealed class DiskSaveCoordinator : MonoBehaviour
    {
        [SerializeField] private string slotName = "quicksave";
        [SerializeField] private bool showGui = true;

        private WorldSession _session;
        private SaveRepository _repository;
        private ISaveBlobStore _store;
        private string _lastStatus = "";
        private float _statusTimer = 0f;

        public bool IsBusy { get; private set; }
        public string LastStatus => _lastStatus;
        public ISaveBlobStore Store => _store;

        public event Action<SaveLoadResult> LoadCompleted;

        public void Initialize(WorldSession session, ISaveBlobStore customStore = null)
        {
            _session = session;
            _store = customStore ?? new LocalDiskSaveBlobStore(slotName: slotName);
            _repository = new SaveRepository(_store);
        }

        private void Start()
        {
            if (_session == null)
            {
                var scope = FindAnyObjectByType<WorldSessionScope>();
                if (scope != null && scope.Container != null)
                {
                    _session = scope.Container.Resolve<WorldSession>();
                }
            }
            if (_repository == null)
            {
                _store = new LocalDiskSaveBlobStore(slotName: slotName);
                _repository = new SaveRepository(_store);
            }
        }

        private void Update()
        {
            if (_statusTimer > 0f)
            {
                _statusTimer -= Time.deltaTime;
                if (_statusTimer <= 0f) _lastStatus = "";
            }

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.f5Key.wasPressedThisFrame)
                    QuickSave();
                else if (kb.f9Key.wasPressedThisFrame)
                    QuickLoad();
            }
        }

        public bool QuickSave()
        {
            if (IsBusy)
            {
                SetStatus("Save rejected: Save coordinator is busy.");
                return false;
            }
            if (_session == null)
            {
                var scope = FindAnyObjectByType<WorldSessionScope>();
                if (scope != null && scope.Container != null)
                    _session = scope.Container.Resolve<WorldSession>();
            }
            if (_session == null)
            {
                SetStatus("Save failed: No active WorldSession.");
                return false;
            }
            if (_repository == null)
            {
                _store = new LocalDiskSaveBlobStore(slotName: slotName);
                _repository = new SaveRepository(_store);
            }

            try
            {
                var snapshot = _session.Capture();
                _repository.Save(snapshot);
                SetStatus($"Saved: {slotName}.sav (Rev {snapshot.State.Revision}, Min {snapshot.State.Minute.Value})");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Save failed: " + ex.Message);
                return false;
            }
        }

        public SaveLoadResult QuickLoad()
        {
            if (IsBusy)
            {
                SetStatus("Load rejected: Save coordinator is busy.");
                return null;
            }
            if (_session == null)
            {
                var scope = FindAnyObjectByType<WorldSessionScope>();
                if (scope != null && scope.Container != null)
                    _session = scope.Container.Resolve<WorldSession>();
            }
            if (_session == null)
            {
                SetStatus("Load failed: No active WorldSession.");
                return null;
            }
            if (_repository == null)
            {
                _store = new LocalDiskSaveBlobStore(slotName: slotName);
                _repository = new SaveRepository(_store);
            }

            try
            {
                var result = _repository.Load();
                if (!result.Success)
                {
                    SetStatus("Load failed: " + result.Error);
                    return result;
                }

                // Restore physical scene alignment and actor positions if checkpoint exists
                if (result.Value.SceneCheckpoint != null)
                {
                    var transit = FindAnyObjectByType<ShoreSceneTransitCoordinator>();
                    if (transit == null)
                    {
                        SetStatus("Load failed: Missing ShoreSceneTransitCoordinator for physical restoration.");
                        return new SaveLoadResult(null, result.UsedBackup, SaveDecodeError.InvalidData);
                    }
                    if (transit.IsBusy)
                    {
                        SetStatus("Load failed: Scene transit is busy.");
                        return new SaveLoadResult(null, result.UsedBackup, SaveDecodeError.InvalidData);
                    }

                    // Pre-flight check that destination is mapped
                    var destination = transit.Destinations?.FirstOrDefault(x => x != null && x.Matches(result.Value.SceneCheckpoint));
                    if (destination == null)
                    {
                        SetStatus("Load failed: Physical restoration rejected (UnmappedSavedSceneOrAnchor).");
                        return new SaveLoadResult(null, result.UsedBackup, SaveDecodeError.InvalidData);
                    }

                    // Capture rollback state prior to mutating live session
                    var rollbackSnapshot = _session.Capture();

                    // Rehydrate domain state so scene loader and validation see restored world state
                    _session.RestoreSnapshot(result.Value);

                    if (!transit.TryRestorePhysicalScene(result.Value.SceneCheckpoint))
                    {
                        _session.RestoreSnapshot(rollbackSnapshot);
                        SetStatus($"Load failed: Physical restoration rejected ({transit.LastError}).");
                        return new SaveLoadResult(null, result.UsedBackup, SaveDecodeError.InvalidData);
                    }

                    // Asynchronous physical reentry started; do NOT report completed load until verified!
                    IsBusy = true;
                    SetStatus($"Loading: {slotName}.sav...");
                    StartCoroutine(WaitForPhysicalRestoreAndFinalize(result, rollbackSnapshot, transit));
                    return result;
                }
                else
                {
                    // Rehydrate the active WorldSession in-place immediately (no physical scene)
                    _session.RestoreSnapshot(result.Value);
                    SetStatus($"Loaded: {slotName}.sav (Rev {result.Value.State.Revision})" + (result.UsedBackup ? " [Backup]" : ""));
                    RefreshCommander();
                    LoadCompleted?.Invoke(result);
                    return result;
                }
            }
            catch (Exception ex)
            {
                SetStatus("Load failed: " + ex.Message);
                return null;
            }
        }

        private IEnumerator WaitForPhysicalRestoreAndFinalize(SaveLoadResult result,
            SaveGameDto rollbackSnapshot, ShoreSceneTransitCoordinator transit)
        {
            while (transit.IsBusy)
                yield return null;

            if (!string.IsNullOrEmpty(transit.LastError))
            {
                // Asynchronous failure during scene loading, navmesh sampling, or proxy resolution:
                // Roll back gameplay state so WorldSession and physical scene remain consistent!
                _session.RestoreSnapshot(rollbackSnapshot);
                SetStatus($"Load failed: Physical restoration rejected ({transit.LastError}).");
                IsBusy = false;
                yield break;
            }

            // Scene reentry actually succeeded: NOW report completed load
            SetStatus($"Loaded: {slotName}.sav (Rev {result.Value.State.Revision})" + (result.UsedBackup ? " [Backup]" : ""));
            RefreshCommander();
            IsBusy = false;
            LoadCompleted?.Invoke(result);
        }

        private void RefreshCommander()
        {
            var commander = FindAnyObjectByType<TacticalCommandController>();
            if (commander != null)
            {
                commander.ClearSelection();
                commander.RefreshSceneRegistrations();
            }
        }

        private void SetStatus(string msg)
        {
            _lastStatus = msg;
            _statusTimer = 5f;
            Debug.Log("[DiskSaveCoordinator] " + msg);
        }

        private void OnEnable()
        {
            var commander = GetComponentInChildren<TacticalCommandController>() ??
                            FindAnyObjectByType<TacticalCommandController>();
            if (commander != null)
                commander.AddPointerOverlayBlocker(IsPointerOverHud);
        }

        private bool IsPointerOverHud(Vector2 screenPoint)
        {
            if (!showGui) return false;
            var topLeft = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return new Rect(Screen.width - 220, 10, 210, 95).Contains(topLeft);
        }

        private void OnGUI()
        {
            if (!showGui) return;
            GUILayout.BeginArea(new Rect(Screen.width - 220, 10, 210, 95), GUI.skin.box);
            GUILayout.Label("FrostArk | Persistence");
            GUILayout.BeginHorizontal();
            GUI.enabled = !IsBusy;
            if (GUILayout.Button("Save (F5)"))
                QuickSave();
            if (GUILayout.Button("Load (F9)"))
                QuickLoad();
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_lastStatus))
                GUILayout.Label(_lastStatus);
            GUILayout.EndArea();
        }
    }
}
