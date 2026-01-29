using Eraflo.Common.LevelSystem;
using Eraflo.Common.ObjectSystem;
using UnityEngine;
using System.Collections.Generic;

namespace FallGuys.Editor.Spatial
{
    /// <summary>
    /// Responsible for recreating the visual/editable scene in the Editor
    /// when a level is loaded from JSON into the LevelDatabase.
    /// </summary>
    public class EditorLevelLoader : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private LevelDatabase _database;
        [SerializeField] private Transform _objectsContainer;

        private void OnEnable()
        {
            if (_database != null)
            {
                _database.OnLevelChanged += HandleLevelChanged;
            }
        }

        private void OnDisable()
        {
            if (_database != null)
            {
                _database.OnLevelChanged -= HandleLevelChanged;
            }
        }

        private bool _isReconstructing = false;

        private void HandleLevelChanged()
        {
            if (_isReconstructing) return;
        }

        public void ReconstructLevel()
        {
            if (_database == null || _database.CurrentLevel == null) return;

            _isReconstructing = true;
            _database.IsLoading = true;
            Debug.Log("[EditorLevelLoader] Reconstructing level...");

            // 1. Clear existing objects in container
            ClearScene();
            _database.Clear(); // Ensure database is clean before re-injection

            // 2. Spawn new objects
            foreach (var objData in _database.CurrentLevel.Objects)
            {
                SpawnEditorObject(objData);
            }
            _database.IsLoading = false;
            _isReconstructing = false;
        }

        private void ClearScene()
        {
            if (_objectsContainer == null) _objectsContainer = transform;

            // We need to be careful not to delete the container itself or non-level objects.
            // Level objects should probably have a specific tag or be children of _objectsContainer.
            List<GameObject> toDestroy = new List<GameObject>();
            foreach (Transform child in _objectsContainer)
            {
                toDestroy.Add(child.gameObject);
            }

            foreach (var obj in toDestroy)
            {
                DestroyImmediate(obj);
            }
        }

        private void SpawnEditorObject(ObjectData data)
        {
            string logicKey = data.Config != null ? data.Config.LogicKey : data.LogicKey;
            if (string.IsNullOrEmpty(logicKey)) return;

            // 1. Load Common Prefab directly (searching in subfolders)
            GameObject prefab = GetCommonPrefab(logicKey);
            if (prefab == null)
            {
                Debug.LogWarning($"[EditorLevelLoader] Common prefab '{logicKey}' not found in subfolders.");
                return;
            }

            // 2. Instantiate at correct transform
            GameObject instance = Instantiate(prefab, data.Position.ToVector3(), data.Rotation.ToQuaternion(), _objectsContainer);
            instance.transform.localScale = data.Scale.ToVector3();
            instance.name = $"EditorObj_{logicKey}";

            // 3. Initialize BaseObject
            if (instance.TryGetComponent<BaseObject>(out var baseObj))
            {
                baseObj.Initialize(data);
            }
        }

        private GameObject GetCommonPrefab(string logicKey)
        {
            // Try root and categories
            GameObject prefab = Resources.Load<GameObject>($"Prefabs/{logicKey}");
            if (prefab != null) return prefab;

            string[] categories = { "Trap", "Platform", "Area" };
            foreach (var category in categories)
            {
                prefab = Resources.Load<GameObject>($"Prefabs/{category}/{logicKey}");
                if (prefab != null) return prefab;
            }

            return null;
        }
    }
}
