using System;
using FrostArk.Core.Application;
using FrostArk.Core.Save;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FrostArk.Unity
{
    /// <summary>
    /// Player-accessible F5/F9 persistence for the persistent run root. The
    /// runtime rebuilds its generated ship/actors from the restored snapshot;
    /// no second session or scene-swap coordinator is introduced.
    /// </summary>
    public sealed class PersistentWorldSaveCoordinator : MonoBehaviour
    {
        [SerializeField] private string slotName = "e12-persistent";
        [SerializeField] private bool showGui = true;

        private PersistentWorldRuntime _runtime;
        private WorldSession _session;
        private SaveRepository _repository;
        private ISaveBlobStore _store;
        private TacticalCommandController _boundCommander;
        private string _status = "";
        private float _statusTimer;
        private bool _loadPending;

        public bool IsBusy => _runtime != null && _runtime.IsRestoring;
        public string LastStatus => _status;

        public void Bind(PersistentWorldRuntime runtime)
        {
            _runtime = runtime;
            _session = runtime != null ? runtime.Session : null;
            EnsureRepository();
        }

        private void Start() => EnsureBound();

        private void OnEnable() => EnsureBound();

        private void Update()
        {
            EnsureBound();
            if (_runtime != null && _runtime.Commander != _boundCommander)
            {
                _boundCommander = _runtime.Commander;
                _boundCommander?.SetPointerOverlayBlocker(IsPointerOverHud);
            }

            if (_statusTimer > 0f)
            {
                _statusTimer -= Time.deltaTime;
                if (_statusTimer <= 0f)
                    _status = "";
            }

            if (_loadPending && (_runtime == null || !_runtime.IsRestoring))
            {
                _loadPending = false;
                SetStatus(string.IsNullOrEmpty(_runtime?.LastRestoreError)
                    ? "Loaded " + slotName + ".sav"
                    : "Load failed: " + _runtime.LastRestoreError);
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.f5Key.wasPressedThisFrame)
                QuickSave();
            else if (keyboard.f9Key.wasPressedThisFrame)
                QuickLoad();
        }

        private void EnsureBound()
        {
            if (_runtime == null)
                _runtime = GetComponent<PersistentWorldRuntime>() ??
                    FindAnyObjectByType<PersistentWorldRuntime>();
            if (_runtime != null && _runtime.Session != null)
                _session = _runtime.Session;
            EnsureRepository();
        }

        private void EnsureRepository()
        {
            if (_repository != null)
                return;
            _store = new LocalDiskSaveBlobStore(slotName: slotName);
            _repository = new SaveRepository(_store);
        }

        public bool QuickSave()
        {
            if (IsBusy)
            {
                SetStatus("Save rejected: load is running.");
                return false;
            }
            if (_runtime != null && _runtime.IsTransitBusy)
            {
                SetStatus("Save rejected: transit in progress.");
                return false;
            }
            if (_session == null || _repository == null)
            {
                SetStatus("Save failed: world not ready.");
                return false;
            }
            try
            {
                var snapshot = _runtime != null
                    ? _runtime.CaptureSnapshot()
                    : _session.Capture();
                _repository.Save(snapshot);
                SetStatus("Saved " + slotName + ".sav (Rev " +
                    snapshot.State.Revision + ", Min " +
                    snapshot.State.Minute.Value + ")");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Save failed: " + ex.Message);
                return false;
            }
        }

        public bool QuickLoad()
        {
            if (IsBusy)
            {
                SetStatus("Load rejected: load is running.");
                return false;
            }
            if (_runtime != null && _runtime.IsTransitBusy)
            {
                SetStatus("Load rejected: transit in progress.");
                return false;
            }
            if (_session == null || _repository == null || _runtime == null)
            {
                SetStatus("Load failed: world not ready.");
                return false;
            }
            try
            {
                var result = _repository.Load();
                if (!result.Success)
                {
                    SetStatus("Load failed: " + result.Error);
                    return false;
                }
                if (!_runtime.RestoreFromSnapshot(result.Value))
                {
                    SetStatus("Load failed: " +
                        (_runtime.LastRestoreError ?? "world rebuild rejected."));
                    return false;
                }
                _loadPending = true;
                SetStatus("Loading " + slotName + ".sav...");
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("Load failed: " + ex.Message);
                return false;
            }
        }

        private bool IsPointerOverHud(Vector2 screenPoint)
        {
            if (!showGui)
                return false;
            var topLeft = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return new Rect(Screen.width - 236, 10, 226, 108).Contains(topLeft);
        }

        private void OnGUI()
        {
            if (!showGui)
                return;
            GUILayout.BeginArea(new Rect(Screen.width - 236, 10, 226, 108),
                GUI.skin.box);
            GUILayout.Label("FrostArk E12 | Persistence");
            GUILayout.BeginHorizontal();
            GUI.enabled = !IsBusy;
            if (GUILayout.Button("Save (F5)"))
                QuickSave();
            if (GUILayout.Button("Load (F9)"))
                QuickLoad();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label(_status);
            GUILayout.EndArea();
        }

        private void SetStatus(string message)
        {
            _status = message;
            _statusTimer = 6f;
            Debug.Log("[FrostArk][E12] " + message);
        }
    }
}
