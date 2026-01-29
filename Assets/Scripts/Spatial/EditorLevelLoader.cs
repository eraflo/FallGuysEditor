using Eraflo.Common.LevelSystem;
using Eraflo.Common.ObjectSystem;
using UnityEngine;
using System.Collections.Generic;
using Spatial;

namespace FallGuys.Editor.Spatial
{
    /// <summary>
    /// Responsible for recreating the visual/editable scene in the Editor
    /// when a level is loaded from JSON into the LevelDatabase.
    /// </summary>
    public class EditorLevelLoader : MonoBehaviour
    {
        [SerializeField] private LevelDatabase _database;
        [SerializeField] private LayerMask _grabbableLayer;

        private void OnEnable()
        {
            ObjectRegistry.Initialize(); // Pre-load all configs
            if (_database != null)
            {
                _database.OnLevelChanged += HandleLevelChanged;
                Debug.Log($"[EditorLevelLoader] Registered to Database ID: {_database.GetInstanceID()}");
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
            Debug.Log($"[EditorLevelLoader] Database change detected ({_database.GetInstanceID()}). Reconstructing...");
            ReconstructLevel();
        }

        public void ReconstructLevel()
        {
            if (_database == null || _database.CurrentLevel == null) return;

            _isReconstructing = true;
            _database.IsLoading = true;
            Debug.Log("[EditorLevelLoader] Reconstructing level...");

            // 1. Clear existing objects via GridSystem
            if (GridSystem.Instance != null)
            {
                GridSystem.Instance.ClearAndDestroyObjects();
            }

            // 2. Spawn new objects
            var objects = _database.CurrentLevel.Objects;
            Debug.Log($"[EditorLevelLoader] Found {objects.Count} objects to spawn in the database.");
            
            foreach (var objData in objects)
            {
                SpawnEditorObject(objData);
            }
            _database.IsLoading = false;
            _isReconstructing = false;
        }

        private void ClearScene()
        {
            if (GridSystem.Instance != null)
            {
                GridSystem.Instance.ClearAndDestroyObjects();
            }
        }

        private void SpawnEditorObject(ObjectData data)
        {
            if (data == null) return;

            string logicKey = data.LogicKey;
            
            if (string.IsNullOrEmpty(logicKey)) return;

            // Use robust discovery from Common ObjectRegistry
            GameObject prefab = ObjectRegistry.GetPrefab(logicKey);

            if (prefab == null)
            {
                Debug.LogWarning($"[EditorLevelLoader] Prefab for LogicKey '{logicKey}' not found in ObjectRegistry.");
                return;
            }

            // 2. Instantiate at correct transform
            Vector3 pos = data.Position.ToVector3();
            Quaternion rot = data.Rotation.ToQuaternion();
            
            Debug.Log($"[EditorLevelLoader] Spawning '{logicKey}' (Prefab: {prefab.name}) at world {pos}");
            
            // Instantiate at root (null parent) to avoid UI conflicts
            GameObject instance = Instantiate(prefab, pos, rot, null);
            instance.transform.localScale = data.Scale.ToVector3();
            instance.name = $"EditorObj_{logicKey}";

            Debug.Log($"[EditorLevelLoader] Instance '{instance.name}' created at world {instance.transform.position}. Active: {instance.activeSelf}");

            // 3. Initialize BaseObject
            if (instance.TryGetComponent<BaseObject>(out var baseObj))
            {
                baseObj.Initialize(data);
            }

            // 4. Register in GridSystem immediately
            if (GridSystem.Instance != null)
            {
                Vector3Int cell = GridSystem.Instance.WorldToCell(pos);
                GridSystem.Instance.OccupyCell(cell, instance);
                Debug.Log($"[EditorLevelLoader] Registered '{logicKey}' in grid cell {cell}");
            }

            // 5. Apply "Placed" state (Mirror BuilderInteractor logic)
            // Layer
            SetLayerRecursive(instance.transform, GetLayerFromMask(_grabbableLayer));

            // Physics (Rigidbody must be frozen but NOT kinematic for ContinuousSpeculative)
            if (instance.TryGetComponent<Rigidbody>(out Rigidbody rb))
            {
                rb.isKinematic = false;
                rb.useGravity = false;
                rb.constraints = RigidbodyConstraints.FreezeAll;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                Debug.Log($"[EditorLevelLoader] Rigidbody for '{instance.name}' frozen and set to Speculative.");
            }

            // Interaction
            if (instance.TryGetComponent<GridLockable>(out var gl))
            {
                gl.SetLockState(true);
            }
        }

        private void SetLayerRecursive(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            foreach (Transform child in target) SetLayerRecursive(child, layer);
        }

        private int GetLayerFromMask(LayerMask mask)
        {
            for (int i = 0; i < 32; i++) { if (((1 << i) & mask.value) != 0) return i; }
            return 0;
        }

    }
}
