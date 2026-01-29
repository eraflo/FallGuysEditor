using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;
using Eraflo.Common.LevelSystem;
using System.IO;
using TMPro;

namespace FallGuys.UI
{
    public class LevelUI : MonoBehaviour
    {   
        [Header("References")]
        [SerializeField] private SaveSystemManager _saveSystem;
        [SerializeField] private LevelDatabase _database;
        [SerializeField] private Spatial.EditorLevelLoader _editorLevelLoader;

        [Header("UI - Save")]
        [SerializeField] private TMP_InputField _levelNameInput;
        [SerializeField] private Button _saveButton;
        [SerializeField] private TMP_Text _validationText;

        [Header("UI - Load (VR Browser)")]
        [SerializeField] private RectTransform _fileListContainer;
        [SerializeField] private GameObject _fileEntryPrefab;
        [SerializeField] private Button _refreshButton;

        private void Awake() 
        {
            if (_database.CurrentLevel == null)
            {
                _database.CreateNewLevel();
            }

            _saveButton.onClick.AddListener(Save);
            _refreshButton.onClick.AddListener(RefreshFileList);
            
            RefreshFileList();
        }

        public void Save()
        {
            string levelName = _levelNameInput != null ? _levelNameInput.text : "Level_" + DateTime.Now.ToString("yyyyMMdd_HHmm");
            _database.SetLevelName(levelName);

            // 1. Validation
            if (!_database.CurrentLevel.Validate(out string error))
            {
                if (_validationText != null) _validationText.text = error;
                Debug.LogWarning($"[LevelUI] Validation Failed: {error}");
                return;
            }

            if (_validationText != null) _validationText.text = "<color=green>Level Valid! Saving...</color>";

            // 2. Pre-process (Calculations)
            _database.CurrentLevel.CalculateCheckpointIndices();

            // 3. Save
            string filename = levelName + ".json";
            _saveSystem.SaveToFile(filename, _database.CurrentLevel);
        }

        public void RefreshFileList()
        {
            // Clear current list
            foreach (Transform child in _fileListContainer)
            {
                Destroy(child.gameObject);
            }

            // Get files from SaveSystem
            var files = _saveSystem.GetAvailableSaveFiles();

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
        }

        private void LoadFile(string filename)
        {
            string json = _saveSystem.LoadFileContent(filename);
            if (!string.IsNullOrEmpty(json))
            {
                _database.LoadFromJson(json);
                if (_levelNameInput != null) _levelNameInput.text = _database.CurrentLevel.LevelName;
                Debug.Log($"[LevelUI] Loaded {filename}");
                
                if (_editorLevelLoader != null)
                {
                    _editorLevelLoader.ReconstructLevel();
                }
            }
        }
    }
}
