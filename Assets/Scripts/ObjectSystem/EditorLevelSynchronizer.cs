using Eraflo.Common.ObjectSystem;
using Eraflo.Common.LevelSystem;
using UnityEngine;

namespace FallGuys.ObjectSystem
{
    /// <summary>
    /// Component that automatically hooks into BaseObject events to sync the LevelDatabase.
    /// This should only be active in the Editor project.
    /// </summary>
    public class EditorLevelSynchronizer : MonoBehaviour
    {
        [SerializeField] private LevelDatabase _database;

        private void OnEnable()
        {
            if (_database == null)
            {
                Debug.LogWarning("[EditorLevelSynchronizer] No LevelDatabase assigned. Auto-registration disabled.");
                return;
            }

            BaseObject.OnObjectCreated += HandleObjectCreated;
        }

        private void OnDisable()
        {
            BaseObject.OnObjectCreated -= HandleObjectCreated;
        }

        private void HandleObjectCreated(BaseObject baseObject)
        {
            if (_database.IsLoading) return;

            // Register the object's data into the current level
            if (baseObject.RuntimeData != null)
            {
                _database.AddObject(baseObject.RuntimeData);
                
                // We also need to handle deletion and movement
                var delHook = baseObject.gameObject.AddComponent<DeletionHook>();
                delHook.Initialize(baseObject.RuntimeData, _database);

                var moveHook = baseObject.gameObject.AddComponent<TransformationHook>();
                moveHook.Initialize(baseObject.RuntimeData);
            }
        }

        /// <summary>
        /// Simple helper to detect object destruction and unregister from database.
        /// </summary>
        private class DeletionHook : MonoBehaviour
        {
            private ObjectData _data;
            private LevelDatabase _db;

            public void Initialize(ObjectData data, LevelDatabase db)
            {
                _data = data;
                _db = db;
            }

            private void OnDestroy()
            {
                if (_db != null && _data != null)
                {
                    _db.RemoveObject(_data);
                }
            }
        }

        /// <summary>
        /// Simple helper to sync transform changes back to the RuntimeData.
        /// </summary>
        private class TransformationHook : MonoBehaviour
        {
            private ObjectData _data;

            public void Initialize(ObjectData data)
            {
                _data = data;
            }

            private void LateUpdate()
            {
                if (_data == null) return;

                // Sync current transform back to serializable data
                // This ensures that when we save, we get the moved/rotated values.
                if (transform.hasChanged)
                {
                    _data.Position = new Vector3Serializable(transform.position.x, transform.position.y, transform.position.z);
                    _data.Rotation = new QuaternionSerializable(transform.rotation.x, transform.rotation.y, transform.rotation.z, transform.rotation.w);
                    _data.Scale = new Vector3Serializable(transform.localScale.x, transform.localScale.y, transform.localScale.z);
                    
                    transform.hasChanged = false;
                }
            }
        }
    }
}
