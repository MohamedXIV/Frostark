using VContainer;
using System;
using System.IO;
using FrostArk.Core.Application;
using FrostArk.Core.Save;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostArk.Unity
{
    /// <summary>
    /// Minimal player-accessible disk Save/Load coordinator.
    /// Provides hotkey (F5/F9) and compact on-screen GUI access to SaveRepository
    /// using existing persistence systems without requiring a full menu framework.
    /// </summary>
    public sealed class DiskSaveCoordinator : MonoBehaviour
    {
        [SerializeField] private bool showGui = true;
        [SerializeField] private string slotName = "slot_0";

        private WorldSession _session;
        private SaveRepository _repository;
        private ISaveBlobStore _store;
        private string _lastStatus = "";
        private float _statusTimer = 0f;

        public string LastStatus => _lastStatus;
        public ISaveBlobStore Store => _store;

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

                // Rehydrate the active WorldSession in-place
                _session.RestoreSnapshot(result.Value);
                SetStatus($"Loaded: {slotName}.sav (Rev {result.Value.State.Revision})" + (result.UsedBackup ? " [Backup]" : ""));

                // Restore physical scene alignment and actor positions
                if (result.Value.SceneCheckpoint != null)
                {
                    var transit = FindAnyObjectByType<ShoreSceneTransitCoordinator>();
                    if (transit == null)
                    {
                        SetStatus("Load failed: Missing ShoreSceneTransitCoordinator for physical restoration.");
                        return new SaveLoadResult(null, result.UsedBackup, SaveDecodeError.InvalidData);
                    }
                    if (!transit.TryRestorePhysicalScene())
                    {
                        SetStatus($"Load failed: Physical restoration rejected ({transit.LastError}).");
                        return new SaveLoadResult(null, result.UsedBackup, SaveDecodeError.InvalidData);
                    }
                }

                // Refresh commander registrations and visuals
                var commander = FindAnyObjectByType<TacticalCommandController>();
                if (commander != null)
                {
                    commander.ClearSelection();
                    commander.RefreshSceneRegistrations();
                }

                return result;
            }
            catch (Exception ex)
            {
                SetStatus("Load failed: " + ex.Message);
                return null;
            }
        }

        private void SetStatus(string msg)
        {
            _lastStatus = msg;
            _statusTimer = 5f;
            Debug.Log("[DiskSaveCoordinator] " + msg);
        }

        private void OnGUI()
        {
            if (!showGui) return;
            GUILayout.BeginArea(new Rect(Screen.width - 220, 10, 210, 95), GUI.skin.box);
            GUILayout.Label("FrostArk | Persistence");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save (F5)"))
                QuickSave();
            if (GUILayout.Button("Load (F9)"))
                QuickLoad();
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_lastStatus))
                GUILayout.Label(_lastStatus);
            GUILayout.EndArea();
        }
    }
}
