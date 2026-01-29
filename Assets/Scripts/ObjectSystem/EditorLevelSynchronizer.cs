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
            // Register the object's data into the current level
            if (baseObject.RuntimeData != null)
            {
                _database.AddObject(baseObject.RuntimeData);
                
                // We also need to handle deletion
                var hook = baseObject.gameObject.AddComponent<DeletionHook>();
                hook.Initialize(baseObject.RuntimeData, _database);
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
    }
}
