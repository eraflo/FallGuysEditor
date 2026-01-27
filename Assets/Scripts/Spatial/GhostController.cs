using UnityEngine;
using System.Collections.Generic;
using Eraflo.Common.ObjectSystem;

namespace Spatial
{
    /// <summary>
    /// Manages the visual preview of the object to be placed.
    /// Handles ghost material application and validity feedback.
    /// </summary>
    public class GhostController : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private Material ghostMaterial;
        [SerializeField] private Color validColor = new Color(0, 1, 0, 0.4f);
        [SerializeField] private Color invalidColor = new Color(1, 0, 0, 0.4f);

        private class GhostData
        {
            public GameObject obj;
            public Renderer[] renderers;
            public MaterialPropertyBlock mpb;
        }

        private List<GhostData> activeGhosts = new List<GhostData>();
        private List<GhostData> ghostPool = new List<GhostData>();
        private GameObject currentPrefab;
        private bool lastValidState = true;
        private static readonly int ColorProp = Shader.PropertyToID("_Color");
        private static readonly int BaseColorProp = Shader.PropertyToID("_BaseColor");

        /// <summary>
        /// Shows a ghost version of the specified prefab.
        /// </summary>
        public void Show(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            ShowMultiple(prefab, new List<Vector3> { position }, rotation);
        }

        public void ShowMultiple(GameObject prefab, List<Vector3> positions, Quaternion rotation)
        {
            if (prefab == null || positions == null || positions.Count == 0)
            {
                Hide();
                return;
            }

            if (currentPrefab != prefab)
            {
                ClearAll();
                currentPrefab = prefab;
            }

            // Sync number of active ghosts
            while (activeGhosts.Count > positions.Count)
            {
                var data = activeGhosts[activeGhosts.Count - 1];
                activeGhosts.RemoveAt(activeGhosts.Count - 1);
                data.obj.SetActive(false);
                ghostPool.Add(data);
            }

            while (activeGhosts.Count < positions.Count)
            {
                activeGhosts.Add(GetOrCreateGhost(prefab));
            }

            // Determine Target Scale from prefab
            Vector3 targetScale = Vector3.one;
            if (prefab.TryGetComponent<Eraflo.Common.ObjectSystem.BaseObject>(out var bo))
            {
                targetScale = bo.InitialScale;
            }

            // Update positions, rotations and ENFORCE target scale
            for (int i = 0; i < positions.Count; i++)
            {
                activeGhosts[i].obj.SetActive(true);
                activeGhosts[i].obj.transform.SetPositionAndRotation(positions[i], rotation);
                activeGhosts[i].obj.transform.localScale = targetScale;
            }
            
            UpdateColor(lastValidState);
        }

        private GhostData GetOrCreateGhost(GameObject prefab)
        {
            if (ghostPool.Count > 0)
            {
                var data = ghostPool[ghostPool.Count - 1];
                ghostPool.RemoveAt(ghostPool.Count - 1);
                return data;
            }

            return CreateGhostData(prefab);
        }

        private GhostData CreateGhostData(GameObject prefab)
        {
            GameObject ghost = Instantiate(prefab, transform);
            ghost.name = "GHOST_" + prefab.name;

            if (ghost.TryGetComponent<Rigidbody>(out Rigidbody rb))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            foreach (var col in ghost.GetComponentsInChildren<Collider>()) col.enabled = false;
            foreach (var mono in ghost.GetComponentsInChildren<MonoBehaviour>())
            {
                if (mono != this) mono.enabled = false;
            }

            GhostData data = new GhostData
            {
                obj = ghost,
                renderers = ghost.GetComponentsInChildren<Renderer>(true),
                mpb = new MaterialPropertyBlock()
            };

            foreach (var r in data.renderers)
            {
                r.enabled = true; // FORCE ENABLE: Clones might be disabled if source was hidden
                Material[] materials = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = ghostMaterial;
                r.materials = materials;
            }

            return data;
        }

        public void SetValid(bool isValid)
        {
            if (lastValidState == isValid) return;
            lastValidState = isValid;
            UpdateColor(isValid);
        }

        private void UpdateColor(bool isValid)
        {
            Color targetColor = isValid ? validColor : invalidColor;

            for (int i = 0; i < activeGhosts.Count; i++)
            {
                var data = activeGhosts[i];
                data.mpb.SetColor(ColorProp, targetColor);
                data.mpb.SetColor(BaseColorProp, targetColor);

                for (int j = 0; j < data.renderers.Length; j++)
                {
                    data.renderers[j].SetPropertyBlock(data.mpb);
                }
            }
        }

        public void Hide()
        {
            for (int i = 0; i < activeGhosts.Count; i++)
            {
                activeGhosts[i].obj.SetActive(false);
                ghostPool.Add(activeGhosts[i]);
            }
            activeGhosts.Clear();
        }

        private void ClearAll()
        {
            foreach (var d in activeGhosts) if (d.obj != null) Destroy(d.obj);
            foreach (var d in ghostPool) if (d.obj != null) Destroy(d.obj);
            activeGhosts.Clear();
            ghostPool.Clear();
        }

        public GameObject GetCurrentPrefab() => currentPrefab;
    }
}
