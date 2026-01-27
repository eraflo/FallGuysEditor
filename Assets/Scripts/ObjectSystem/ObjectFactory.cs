using Eraflo.Common.ObjectSystem;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using System.Collections.Generic;
using Spatial;

namespace ObjectSystem
{
    /// <summary>
    /// Component that spawns a prefab and replaces it as soon as it is grabbed.
    /// Perfect for making a 3D shelf of infinite objects.
    /// </summary>
    public class ObjectFactory : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private GameObject _prefab;
        [SerializeField] private Transform _spawnPoint;
        [SerializeField] private Vector3 _scaleInZone = new Vector3(0.2f, 0.2f, 0.2f);
        [SerializeField] private Vector3 _zoneSize = Vector3.one;

        // Track EVERYTHING this factory spawns to ensure absolute isolation.
        // We remember the original scale of each object we own.
        private Dictionary<BaseObject, Vector3> _ownedObjects = new Dictionary<BaseObject, Vector3>();
        private List<BaseObject> _toCleanup = new List<BaseObject>();

        private GameObject _currentInstance;
        private BaseObject _currentBaseObject;
        private GridLockable _currentGridLockable;
        private Vector3 _initialScale;

        private void Start()
        {
            if (_spawnPoint == null) _spawnPoint = transform;
            SpawnInstance();
        }

        private void Update()
        {
            // Center of our detection zone in world space
            Vector3 zoneCenter = _spawnPoint.position;
            
            // Scaled sizes for Hysteresis
            Vector3 lossy = transform.lossyScale;
            Vector3 baseSize = Vector3.Scale(_zoneSize, lossy);
            
            Vector3 internalSize = baseSize * 0.85f; // Must enter this to become SMALL
            Vector3 externalSize = baseSize * 1.15f; // Must leave this to become BIG

            bool shouldSpawnReplacement = false;

            foreach (var kvp in _ownedObjects)
            {
                BaseObject bo = kvp.Key;
                if (bo == null) { _toCleanup.Add(bo); continue; }

                Vector3 objCenter = bo.transform.position; // Simplest: pivot center
                Vector3 localPos = Quaternion.Inverse(transform.rotation) * (objCenter - zoneCenter);
                
                bool isInsideInternal = AbsMax(localPos, internalSize / 2f);
                bool isOutsideExternal = !AbsMax(localPos, externalSize / 2f);

                // --- STATE MACHINE PER OBJECT ---
                // If it's small, it only becomes big by leaving the EXTERNAL zone
                // If it's big, it only becomes small by entering the INTERNAL zone
                
                float currentScale = bo.transform.localScale.x; // Uniform check
                float smallScale = Vector3.Scale(_scaleInZone, lossy).x;

                if (isInsideInternal)
                {
                    // FORCE SMALL & SHELF STATE
                    Vector3 targetSmall = Vector3.Scale(_scaleInZone, lossy);
                    if (bo.transform.localScale != targetSmall) bo.transform.localScale = targetSmall;
                    
                    if (bo.enabled) bo.enabled = false;
                    if (bo.TryGetComponent<GridLockable>(out var gl) && gl.enabled) gl.enabled = false;
                }
                else if (isOutsideExternal)
                {
                    // RESTORE BIG & ACTIVE STATE
                    if (bo.transform.localScale != kvp.Value)
                    {
                        bo.transform.localScale = kvp.Value;
                        bo.enabled = true;
                        if (bo.TryGetComponent<GridLockable>(out var gl)) gl.enabled = true;

                        Debug.Log($"[ObjectFactory] {bo.name} is CLEAR of zone. Restoring original scale {kvp.Value}");

                        // If the currently tracked "shelf object" just became big, we need to spawn its replacement
                        if (bo.gameObject == _currentInstance)
                        {
                            shouldSpawnReplacement = true;
                        }
                    }
                }
            }

            // Cleanup destroyed objects
            if (_toCleanup.Count > 0)
            {
                foreach (var bo in _toCleanup) _ownedObjects.Remove(bo);
                _toCleanup.Clear();
            }

            // Deferred spawn to avoid dictionary modification issues
            if (shouldSpawnReplacement)
            {
                _currentInstance = null;
                _currentBaseObject = null;
                _currentGridLockable = null;
                SpawnInstance();
            }
        }

        private bool AbsMax(Vector3 localPos, Vector3 halfExtents)
        {
            return Mathf.Abs(localPos.x) <= halfExtents.x && 
                   Mathf.Abs(localPos.y) <= halfExtents.y && 
                   Mathf.Abs(localPos.z) <= halfExtents.z;
        }

        private void SpawnInstance()
        {
            if (_prefab == null) return;

            _currentInstance = Instantiate(_prefab, _spawnPoint.position, _spawnPoint.rotation);
            _initialScale = _currentInstance.transform.localScale;
            
            // Forced to local small scale relative to factory lossyScale
            _currentInstance.transform.localScale = Vector3.Scale(_scaleInZone, transform.lossyScale);

            if (_currentInstance.TryGetComponent<BaseObject>(out _currentBaseObject))
            {
                _currentBaseObject.enabled = false;
                // PERSISTENT OWNERSHIP: This factory owns this object forever
                _ownedObjects.TryAdd(_currentBaseObject, _initialScale);
            }

            if (_currentInstance.TryGetComponent<GridLockable>(out _currentGridLockable))
            {
                _currentGridLockable.enabled = false;
            }
        }

        private void OnDrawGizmos()
        {
            if (_spawnPoint == null) return;

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.3f);
            Gizmos.matrix = Matrix4x4.TRS(_spawnPoint.position, transform.rotation, transform.lossyScale);
            Gizmos.DrawCube(Vector3.zero, _zoneSize);
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
            Gizmos.DrawWireCube(Vector3.zero, _zoneSize);
        }
    }
}
