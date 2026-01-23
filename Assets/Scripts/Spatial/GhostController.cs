using UnityEngine;

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

        private GameObject currentGhost;
        private GameObject currentPrefab;
        private Renderer[] ghostRenderers;
        private bool lastValidState = true;

        /// <summary>
        /// Shows a ghost version of the specified prefab.
        /// Reuses the existing ghost if it's the same prefab (Optimization).
        /// </summary>
        public void Show(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return;

            if (currentGhost == null || currentPrefab != prefab)
            {
                CreateGhost(prefab);
            }

            currentGhost.SetActive(true);
            UpdatePosition(position, rotation);
        }

        private void CreateGhost(GameObject prefab)
        {
            if (currentGhost != null)
            {
                Destroy(currentGhost);
            }

            currentPrefab = prefab;

            // Clone the object (works for prefabs and scene objects)
            currentGhost = Instantiate(prefab, transform);
            currentGhost.name = "GHOST_" + prefab.name;

            // Ensure the ghost is properly positioned relative to the container
            currentGhost.transform.localPosition = Vector3.zero;
            currentGhost.transform.localRotation = Quaternion.identity;

            // Disable physics on ghost
            if (currentGhost.TryGetComponent<Rigidbody>(out Rigidbody rb))
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            }

            // Disable all colliders on ghost
            foreach (var col in currentGhost.GetComponentsInChildren<Collider>())
            {
                col.enabled = false;
            }

            // Disable scripts (optional, depending on components)
            foreach (var mono in currentGhost.GetComponentsInChildren<MonoBehaviour>())
            {
                if (mono != this) mono.enabled = false;
            }

            ghostRenderers = currentGhost.GetComponentsInChildren<Renderer>(true);
            foreach (var r in ghostRenderers) r.enabled = true;

            if (ghostRenderers == null || ghostRenderers.Length == 0)
            {
                Debug.LogWarning($"GhostController: '{prefab.name}' has no Renderers in children! Ghost will be invisible.");
            }

            ApplyGhostMaterial();
        }

        private void ApplyGhostMaterial()
        {
            foreach (var renderer in ghostRenderers)
            {
                Material[] materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = ghostMaterial;
                }
                renderer.materials = materials;
            }
            UpdateColor(lastValidState);
        }

        public void UpdatePosition(Vector3 position, Quaternion rotation)
        {
            if (currentGhost != null)
            {
                currentGhost.transform.SetPositionAndRotation(position, rotation);
            }
        }

        public void SetValid(bool isValid)
        {
            if (lastValidState == isValid) return;

            lastValidState = isValid;
            UpdateColor(isValid);
        }

        private void UpdateColor(bool isValid)
        {
            if (ghostRenderers == null) return;

            Color targetColor = isValid ? validColor : invalidColor;
            foreach (var renderer in ghostRenderers)
            {
                foreach (var mat in renderer.materials)
                {
                    if (mat.HasProperty("_Color"))
                    {
                        mat.SetColor("_Color", targetColor);
                    }
                    else if (mat.HasProperty("_BaseColor")) // URP
                    {
                        mat.SetColor("_BaseColor", targetColor);
                    }
                }
            }
        }

        public void Hide()
        {
            if (currentGhost != null)
            {
                currentGhost.SetActive(false);
            }
        }

        public GameObject GetCurrentPrefab() => currentPrefab;
    }
}
