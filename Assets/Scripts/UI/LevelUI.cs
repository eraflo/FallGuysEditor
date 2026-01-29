using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using Eraflo.Common.LevelSystem;
using System.IO;
using TMPro;
using FallGuys.Editor.Spatial;
using Spatial;
using Eraflo.Common.ObjectSystem;

using Microsoft.MixedReality.Toolkit.Experimental.UI;
using FallGuys.UI;

namespace FallGuys.UI
{
    public class LevelUI : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SaveSystemManager _saveSystem;
        [SerializeField] private LevelDatabase _database;
        [SerializeField] private EditorLevelLoader _editorLevelLoader;

        [Header("UI - Save")]
        [SerializeField] private TMP_InputField _levelNameInput;
        [SerializeField] private Button _saveButton;
        [SerializeField] private TMP_Text _validationText;

        [Header("UI - Load (VR Browser)")]
        [SerializeField] private RectTransform _fileListContainer;
        [SerializeField] private GameObject _fileEntryPrefab;
        [SerializeField] private Button _refreshButton;

        [Header("VR Keyboard")]
        [SerializeField] private GameObject _keyboardPrefab;
        [SerializeField] private float _keyboardDistance = 0.5f;
        [SerializeField] private float _keyboardHeightOffset = -0.2f;
        [SerializeField] private float _followSpeed = 5f;

        private NonNativeKeyboard _keyboardInstance;

        private void Awake() 
        {
            ObjectRegistry.Initialize(); // Pre-load all configs
            Debug.Log($"[LevelUI] Database ID: {(_database != null ? _database.GetInstanceID() : 0)}");
            if (_database.CurrentLevel == null)
            {
                _database.CreateNewLevel();
            }

            _saveButton.onClick.AddListener(Save);
            _refreshButton.onClick.AddListener(RefreshFileList);
            
            if (_levelNameInput != null)
            {
                _levelNameInput.onSelect.AddListener((val) => OpenKeyboard());
                // Removed onDeselect to prevent closing when clicking keyboard buttons
                // _levelNameInput.onDeselect.AddListener((val) => CloseKeyboard());
            }
        }

        private void Start()
        {
            RefreshFileList();
        }

        private void Update()
        {
            if (_keyboardInstance != null && _keyboardInstance.gameObject.activeSelf)
            {
                Transform cam = Camera.main.transform;
                Vector3 targetPos = cam.position + cam.forward * _keyboardDistance + Vector3.up * _keyboardHeightOffset;
                
                // Smooth follow
                _keyboardInstance.transform.position = Vector3.Lerp(_keyboardInstance.transform.position, targetPos, Time.deltaTime * _followSpeed);
                
                // Always look at camera (NonNativeKeyboard usually needs to be rotated 180 or looked at efficiently)
                // The NonNativeKeyboard script has a LookAtTargetOrigin method, but we can do it manually slightly better for smooth follow
                _keyboardInstance.transform.LookAt(cam);
                _keyboardInstance.transform.Rotate(0, 180, 0);
            }
        }

        public void OpenKeyboard()
        {
            if (_keyboardPrefab == null) return;

            // 1. Get or Spawn Keyboard
            if (_keyboardInstance == null)
            {
                // check if already in scene static
                _keyboardInstance = NonNativeKeyboard.Instance;
                
                if (_keyboardInstance == null)
                {
                    GameObject go = Instantiate(_keyboardPrefab);
                    _keyboardInstance = go.GetComponent<NonNativeKeyboard>();
                }
            }

            // 2. Setup
            if (_keyboardInstance != null)
            {
                // Note: We do NOT overwrite _keyboardInstance.InputField anymore.
                // We let the keyboard use its own internal preview, and we sync via events.
                
                // Unsubscribe first to avoid duplicates
                _keyboardInstance.OnTextUpdated -= HandleKeyboardTextUpdated;
                _keyboardInstance.OnClosed -= HandleKeyboardClosed;
                _keyboardInstance.OnTextSubmitted -= HandleKeyboardClosed;

                _keyboardInstance.OnTextUpdated += HandleKeyboardTextUpdated;
                _keyboardInstance.OnClosed += HandleKeyboardClosed;
                _keyboardInstance.OnTextSubmitted += HandleKeyboardClosed;

                // Initialize with current text
                _keyboardInstance.PresentKeyboard(_levelNameInput.text);
                
                // Snap to position immediately on open so it doesn't fly in from zero
                Transform cam = Camera.main.transform;
                Vector3 targetPos = cam.position + cam.forward * _keyboardDistance + Vector3.up * _keyboardHeightOffset;
                _keyboardInstance.transform.position = targetPos;
                _keyboardInstance.transform.LookAt(cam);
                _keyboardInstance.transform.Rotate(0, 180, 0);
            }
        }

        public void CloseKeyboard()
        {
            if (_keyboardInstance != null)
            {
                _keyboardInstance.Close();
            }
        }

        private void HandleKeyboardTextUpdated(string text)
        {
            if (_levelNameInput != null)
            {
                _levelNameInput.text = text;
            }
        }

        private void HandleKeyboardClosed(object sender, EventArgs e)
        {
             // Optional: Validation logic when closed
        }

        public void Save()
        {
            if (_database == null) return;
            
            _database.IsLoading = true; // START GUARDING: Prevent any reconstruction during the whole save process
            
            string levelName = _levelNameInput != null && !string.IsNullOrEmpty(_levelNameInput.text) ? _levelNameInput.text : "Level_" + DateTime.Now.ToString("yyyyMMdd_HHmm");
            _database.SetLevelName(levelName);

            // 0. Build dynamic object list from Grid
            if (GridSystem.Instance != null)
            {
                
                var uniqueObjects = GridSystem.Instance.GetUniqueObjects();
                _database.CurrentLevel.Objects.Clear();
                int count = 0;
                foreach (var go in uniqueObjects)
                {
                    if (go.TryGetComponent<BaseObject>(out var bo))
                    {
                        bo.UpdateRuntimeDataTransform(); // SYNC: Capture VERY latest world state
                        
                        if (bo.RuntimeData != null)
                        {
                            _database.AddObject(bo.RuntimeData);
                            Debug.Log($"[LevelUI.Save] Saving object '{bo.RuntimeData.LogicKey}' at world pos {bo.transform.position}");
                            count++;
                        }
                    }
                }
                
                _database.IsLoading = false; 
                Debug.Log($"[LevelUI] Collected {count} objects from GridSystem for saving.");
            }

            // 1. Validation
            if (!_database.CurrentLevel.Validate(out string error))
            {
                _database.IsLoading = false; // UNBLOCK: Restore notifications on failure
                if (_validationText != null) _validationText.text = error;
                Debug.LogWarning($"[LevelUI] Validation Failed: {error}");
                return;
            }

            _database.IsLoading = false; // UNBLOCK: Capture is done, we can allow refreshes now or at the very end

            if (_validationText != null) _validationText.text = "<color=green>Level Valid! Saving...</color>";

            // 2. Pre-process (Calculations)
            _database.CurrentLevel.CalculateCheckpointIndices();

            // 3. Save
            string filename = levelName + ".json";
            _saveSystem.SaveToFile(filename, _database.CurrentLevel);

            // 4. Refresh VR browser immediately
            RefreshFileList();
        }

        public void RefreshFileList()
        {
            // Clear current list immediately
            List<GameObject> children = new List<GameObject>();
            foreach (Transform child in _fileListContainer) children.Add(child.gameObject);
            foreach (var child in children) DestroyImmediate(child);

            // Get files from SaveSystem
            var files = _saveSystem.GetAvailableSaveFiles();
            Debug.Log($"[LevelUI] Found {files.Count} save files.");

            foreach (var file in files)
            {
                GameObject entry = Instantiate(_fileEntryPrefab, _fileListContainer);
                var text = entry.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = file;

                var btn = entry.GetComponent<Button>();
                if (btn != null)
                {
                    btn.onClick.AddListener(() => LoadFile(file));
                }
            }

            // Force layout update for VR UI (container and parent/content)
            if (_fileListContainer != null)
            {
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_fileListContainer);
                
                // Content typically has the ContentSizeFitter
                if (_fileListContainer.TryGetComponent<RectTransform>(out var rt))
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
                }
                
                // Parent (Viewport/ScrollRect) might need a poke
                if (_fileListContainer.parent is RectTransform parentRT)
                {
                    LayoutRebuilder.ForceRebuildLayoutImmediate(parentRT);
                }
            }
        }

        private void LoadFile(string filename)
        {
            string json = _saveSystem.LoadFileContent(filename);
            if (!string.IsNullOrEmpty(json))
            {
                Debug.Log($"[LevelUI] Starting load for {filename}...");
                _database.LoadFromJson(json);
                
                if (_levelNameInput != null) _levelNameInput.text = _database.CurrentLevel.LevelName;
                Debug.Log($"[LevelUI] Finished loading {filename}. Database now has {_database.CurrentLevel.Objects.Count} objects.");
            }
        }
    }
}
