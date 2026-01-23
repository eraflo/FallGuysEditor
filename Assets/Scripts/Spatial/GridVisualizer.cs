using System.Collections.Generic;
using UnityEngine;

namespace Spatial
{
    /// <summary>
    /// Visualizes the grid using a pool of LineRenderers in 3D.
    /// Draws a volumetric cage around the player for air placement.
    /// </summary>
    public class GridVisualizer : MonoBehaviour
    {
        [Header("Visual Settings")]
        [SerializeField] private Material lineMaterial;
        [SerializeField] private float lineWidth = 0.02f;
        [SerializeField] private Color lineColor = new Color(1, 1, 1, 0.3f);
        [SerializeField] private float visibilityRadius = 5f;
        [SerializeField] private bool isVisible = true;

        [Header("References")]
        [SerializeField] private Transform playerTransform;

        private List<LineRenderer> xLines = new List<LineRenderer>();
        private List<LineRenderer> yLines = new List<LineRenderer>();
        private List<LineRenderer> zLines = new List<LineRenderer>();

        private GridSystem grid;

        private Vector3Int currentCenterCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        private Vector3Int lastPlayerCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
        private float sqrMoveThreshold;

        private bool isLogicActive = true;

        public Vector3Int CurrentCenterCell => currentCenterCell;
        public int VisibilityHalfRange => Mathf.CeilToInt(visibilityRadius / grid.CellSize);
        public bool IsVisible => isVisible;

        private void Start()
        {
            grid = GridSystem.Instance;
            if (grid == null)
            {
                Debug.LogError("GridSystem instance not found!");
                return;
            }

            if (playerTransform == null && Camera.main != null)
            {
                playerTransform = Camera.main.transform;
            }

            GenerateGridLines();

            // Setup threshold cache (120% of visibility radius in cell units, squared)
            float step = grid.CellSize;
            float threshold = (visibilityRadius / step) * 1.2f;
            sqrMoveThreshold = threshold * threshold;

            // Force initial display
            Vector3 localPos = playerTransform.position - grid.Origin;
            Vector3Int playerCell = new Vector3Int(
                Mathf.RoundToInt(localPos.x / step),
                Mathf.RoundToInt(localPos.y / step),
                Mathf.RoundToInt(localPos.z / step)
            );
            UpdateVolumetricGrid(playerCell);
        }

        public void SetEnabled(bool isEnabled)
        {
            isLogicActive = isEnabled;
            UpdateVisualState();

            if (isEnabled)
            {
                // Reset center to force a fresh update when re-enabled
                currentCenterCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            }
        }

        public void SetVisible(bool visible)
        {
            isVisible = visible;
            UpdateVisualState();
        }

        public void ToggleVisibility()
        {
            isVisible = !isVisible;
            UpdateVisualState();
        }

        private void UpdateVisualState()
        {
            bool shouldShow = isLogicActive && isVisible;
            if (!shouldShow)
            {
                // Hide all lines strictly to avoid artifacts
                foreach (var lr in xLines) if (lr != null) lr.gameObject.SetActive(false);
                foreach (var lr in yLines) if (lr != null) lr.gameObject.SetActive(false);
                foreach (var lr in zLines) if (lr != null) lr.gameObject.SetActive(false);
            }
            else
            {
                // Force a refresh of the volumetric grid on the next update 
                // so lines reappear instantly at the correct location.
                lastPlayerCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
                currentCenterCell = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);
            }
        }

        private void GenerateGridLines()
        {
            // Calculate pool size based on diameter + some buffer
            int countPerAxis = Mathf.CeilToInt((visibilityRadius * 2 / grid.CellSize) + 4);
            int sqPool = countPerAxis * countPerAxis;

            for (int i = 0; i < sqPool; i++) xLines.Add(CreateLine("X_Line"));
            for (int i = 0; i < sqPool; i++) yLines.Add(CreateLine("Y_Line"));
            for (int i = 0; i < sqPool; i++) zLines.Add(CreateLine("Z_Line"));
        }

        private LineRenderer CreateLine(string name)
        {
            GameObject obj = new GameObject(name);
            obj.transform.SetParent(transform);
            LineRenderer lr = obj.AddComponent<LineRenderer>();

            lr.sharedMaterial = lineMaterial;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.startColor = lineColor;
            lr.endColor = lineColor;
            lr.positionCount = 2;
            lr.useWorldSpace = true;

            obj.SetActive(false);
            return lr;
        }

        private void Update()
        {
            // Logic must run even if invisible so that BuilderInteractor's 
            // reach check (based on currentCenterCell) remains accurate.
            if (playerTransform == null || !isLogicActive) return;

            float step = grid.CellSize;
            Vector3 localPos = playerTransform.position - grid.Origin;
            Vector3Int playerCell = new Vector3Int(
                Mathf.RoundToInt(localPos.x / step),
                Mathf.RoundToInt(localPos.y / step),
                Mathf.RoundToInt(localPos.z / step)
            );

            if (playerCell == lastPlayerCell) return;
            lastPlayerCell = playerCell;

            // Update internal logic center
            if ((playerCell - currentCenterCell).sqrMagnitude > sqrMoveThreshold || currentCenterCell.x == int.MinValue)
            {
                currentCenterCell = playerCell;

                // Only render the lines if visible
                if (isVisible)
                {
                    UpdateVolumetricGrid(playerCell);
                }
            }
        }

        private void UpdateVolumetricGrid(Vector3Int center)
        {
            if (!isLogicActive) return;

            currentCenterCell = center;
            float step = grid.CellSize;
            Vector3 origin = grid.Origin;
            int halfRange = Mathf.CeilToInt(visibilityRadius / step);

            // Draw X-aligned lines (vary on Y/Z)
            int xLineIdx = 0;
            for (int y = center.y - halfRange; y <= center.y + halfRange; y++)
            {
                for (int z = center.z - halfRange; z <= center.z + halfRange; z++)
                {
                    if (xLineIdx >= xLines.Count) break;
                    LineRenderer lr = xLines[xLineIdx++];
                    lr.gameObject.SetActive(true);
                    lr.SetPosition(0, new Vector3(origin.x + (center.x - halfRange) * step, origin.y + y * step, origin.z + z * step));
                    lr.SetPosition(1, new Vector3(origin.x + (center.x + halfRange) * step, origin.y + y * step, origin.z + z * step));
                }
            }
            DisableUnusedLines(xLines, xLineIdx);

            // Draw Y-aligned lines (vary on X/Z)
            int yLineIdx = 0;
            for (int x = center.x - halfRange; x <= center.x + halfRange; x++)
            {
                for (int z = center.z - halfRange; z <= center.z + halfRange; z++)
                {
                    if (yLineIdx >= yLines.Count) break;
                    LineRenderer lr = yLines[yLineIdx++];
                    lr.gameObject.SetActive(true);
                    lr.SetPosition(0, new Vector3(origin.x + x * step, origin.y + (center.y - halfRange) * step, origin.z + z * step));
                    lr.SetPosition(1, new Vector3(origin.x + x * step, origin.y + (center.y + halfRange) * step, origin.z + z * step));
                }
            }
            DisableUnusedLines(yLines, yLineIdx);

            // Draw Z-aligned lines (vary on X/Y)
            int zLineIdx = 0;
            for (int x = center.x - halfRange; x <= center.x + halfRange; x++)
            {
                for (int y = center.y - halfRange; y <= center.y + halfRange; y++)
                {
                    if (zLineIdx >= zLines.Count) break;
                    LineRenderer lr = zLines[zLineIdx++];
                    lr.gameObject.SetActive(true);
                    lr.SetPosition(0, new Vector3(origin.x + x * step, origin.y + y * step, origin.z + (center.z - halfRange) * step));
                    lr.SetPosition(1, new Vector3(origin.x + x * step, origin.y + y * step, origin.z + (center.z + halfRange) * step));
                }
            }
            DisableUnusedLines(zLines, zLineIdx);
        }

        private void DisableUnusedLines(List<LineRenderer> pool, int startIdx)
        {
            for (int i = startIdx; i < pool.Count; i++)
            {
                if (pool[i].gameObject.activeSelf) pool[i].gameObject.SetActive(false);
            }
        }
    }
}
