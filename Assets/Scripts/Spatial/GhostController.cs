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
            public BaseObject baseObj;
            public VisualPreviewDrawer drawer;
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

            // Feature: Always show preview on ghosts for better UX during placement
            bool showPreview = bo != null;

            // Handle local suppression to avoid duplicate visualizations
            if (bo != null) bo.SuppressLocalPreview = showPreview;

            for (int i = 0; i < positions.Count; i++)
            {
                var data = activeGhosts[i];
                data.obj.SetActive(true);

                // Sync overrides and config from source to ghost in real-time
                if (bo != null && data.baseObj != null)
                {
                    data.baseObj.Initialize(bo.RuntimeData);
                }

                // Restore snapping position/rotation (Initialize might have moved it to source transform)
                data.obj.transform.SetPositionAndRotation(positions[i], rotation);
                data.obj.transform.localScale = targetScale;

                // Feature: Real-time visualization on ghost
                if (showPreview && data.baseObj != null && data.baseObj.Config != null)
                {
                    if (data.drawer == null)
                    {
                        GameObject dGo = new GameObject("GhostPreviewDrawer");
                        dGo.transform.SetParent(data.obj.transform, false);
                        data.drawer = dGo.AddComponent<VisualPreviewDrawer>();
                    }
                    
                    data.drawer.gameObject.SetActive(true);
                    data.drawer.Clear();
                    data.baseObj.Config.DrawRuntimePreview(data.baseObj, data.drawer);
                }
                else if (data.drawer != null && data.drawer.gameObject.activeSelf)
                {
                    data.drawer.Clear();
                    data.drawer.gameObject.SetActive(false);
                }
            }
            
            UpdateColor(lastValidState);
        }

        private GhostData GetOrCreateGhost(GameObject prefab)
        {
            while (ghostPool.Count > 0)
            {
                var data = ghostPool[ghostPool.Count - 1];
                ghostPool.RemoveAt(ghostPool.Count - 1);

                if (data.obj != null)
                {
                    return data;
                }
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

            // Clean up ANY existing VisualPreviewDrawer that might have been cloned from the active source
            foreach (var existingDrawer in ghost.GetComponentsInChildren<VisualPreviewDrawer>(true))
            {
                Destroy(existingDrawer.gameObject);
            }

            foreach (var col in ghost.GetComponentsInChildren<Collider>()) col.enabled = false;
            foreach (var mono in ghost.GetComponentsInChildren<MonoBehaviour>())
            {
                // We keep VisualPreviewDrawer enabled on the ghost so it can draw its own lines
                if (mono != this && !(mono is VisualPreviewDrawer)) mono.enabled = false;
            }

            GhostData data = new GhostData
            {
                obj = ghost,
                baseObj = ghost.GetComponent<BaseObject>(),
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

                if (data.obj == null) continue;

                for (int j = 0; j < data.renderers.Length; j++)
                {
                    if (data.renderers[j] != null)
                    {
                        data.renderers[j].SetPropertyBlock(data.mpb);
                    }
                }
            }
        }

        public void Hide()
        {
            if (currentPrefab != null && currentPrefab.TryGetComponent<BaseObject>(out var bo))
            {
                bo.SuppressLocalPreview = false;
            }

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
