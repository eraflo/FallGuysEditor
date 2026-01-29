using System.Collections.Generic;
using UnityEngine;

namespace Spatial
{
    /// <summary>
    /// Singleton handle for grid logic, conversions, and occupation tracking.
    /// </summary>
    public class GridSystem : MonoBehaviour
    {
        public static GridSystem Instance { get; private set; }

        [Header("Grid Settings")]
        [SerializeField] private float cellSize = 1f;
        [SerializeField] private Vector3Int gridDimensions = new Vector3Int(50, 10, 50);
        [SerializeField] private Vector3 origin = Vector3.zero;

        [Header("Rotation Settings")]
        [SerializeField] private float rotationStep = 15f;

        // Note: For advanced spatial searches (e.g., finding all objects in a radius), 
        // a Octree would be better than a Dictionary.
        // For simple O(1) point-to-object lookup, the Dictionary is already optimal.
        private Dictionary<Vector3Int, GameObject> occupiedCells = new Dictionary<Vector3Int, GameObject>();

        public float CellSize => cellSize;
        public Vector3Int GridDimensions => gridDimensions;
        public Vector3 Origin => origin;
        public float RotationStep => rotationStep;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// Snaps a world position to the center of the closest grid cell on the Y plane of the origin.
        /// </summary>
        public Vector3 GetClosestGridPoint(Vector3 worldPos)
        {
            Vector3Int cell = WorldToCell(worldPos);
            return CellToWorld(cell);
        }

        /// <summary>
        /// Converts world position to grid cell coordinates.
        /// </summary>
        public Vector3Int WorldToCell(Vector3 worldPos)
        {
            Vector3 localPos = worldPos - origin;
            int x = Mathf.FloorToInt(localPos.x / cellSize);
            int y = Mathf.FloorToInt(localPos.y / cellSize);
            int z = Mathf.FloorToInt(localPos.z / cellSize);
            return new Vector3Int(x, y, z);
        }

        /// <summary>
        /// Converts grid cell coordinates to world position (center of cell).
        /// </summary>
        public Vector3 CellToWorld(Vector3Int cell)
        {
            Vector3 pos = new Vector3(
                (cell.x * cellSize) + (cellSize * 0.5f),
                (cell.y * cellSize) + (cellSize * 0.5f),
                (cell.z * cellSize) + (cellSize * 0.5f)
            );
            return pos + origin;
        }

        /// <summary>
        /// Quantizes a 3D rotation based on the defined step.
        /// </summary>
        public Quaternion GetQuantizedRotation(float angleX, float angleY, float angleZ)
        {
            float snappedX = Mathf.Round(angleX / rotationStep) * rotationStep;
            float snappedY = Mathf.Round(angleY / rotationStep) * rotationStep;
            float snappedZ = Mathf.Round(angleZ / rotationStep) * rotationStep;
            return Quaternion.Euler(snappedX, snappedY, snappedZ);
        }

        /// <summary>
        /// Quantizes a 2D rotation based on the defined step.
        /// </summary>
        public Quaternion GetQuantizedRotation(float angleX, float angleY)
        {
            float snappedX = Mathf.Round(angleX / rotationStep) * rotationStep;
            float snappedY = Mathf.Round(angleY / rotationStep) * rotationStep;
            return Quaternion.Euler(snappedX, snappedY, 0);
        }

        /// <summary>
        /// Quantizes a single axis rotation based on the defined step.
        /// </summary>
        public Quaternion GetQuantizedRotation(float currentAngle)
        {
            float snappedAngle = Mathf.Round(currentAngle / rotationStep) * rotationStep;
            return Quaternion.Euler(0, snappedAngle, 0);
        }

        /// <summary>
        /// Checks if a cell is occupied.
        /// </summary>
        public bool IsCellOccupied(Vector3Int cell)
        {
            return occupiedCells.ContainsKey(cell);
        }

        /// <summary>
        /// Checks if a cell is occupied, ignoring a specific object.
        /// </summary>
        public bool IsCellOccupied(Vector3Int cell, GameObject ignoreObj)
        {
            if (occupiedCells.TryGetValue(cell, out GameObject existing))
            {
                return existing != ignoreObj;
            }
            return false;
        }

        /// <summary>
        /// Marks a cell as occupied.
        /// </summary>
        public void OccupyCell(Vector3Int cell, GameObject obj)
        {
            if (!occupiedCells.ContainsKey(cell))
            {
                occupiedCells.Add(cell, obj);
            }
        }

        /// <summary>
        /// Clears an object from any cell it occupies.
        /// </summary>
        public void ClearObject(GameObject obj)
        {
            Vector3Int? foundCell = null;
            foreach (var kvp in occupiedCells)
            {
                if (kvp.Value == obj)
                {
                    foundCell = kvp.Key;
                    break;
                }
            }
            if (foundCell.HasValue) occupiedCells.Remove(foundCell.Value);
        }

        /// <summary>
        /// Checks if a GameObject is currently registered in any grid cell.
        /// </summary>
        public bool IsObjectInGrid(GameObject obj)
        {
            if (obj == null) return false;
            foreach (var val in occupiedCells.Values)
            {
                if (val == obj) return true;
            }
            return false;
        }

        /// <summary>
        /// Returns all unique GameObjects currently registered in the grid.
        /// Useful for building the level save list.
        /// </summary>
        public List<GameObject> GetUniqueObjects()
        {
            HashSet<GameObject> uniqueObjects = new HashSet<GameObject>();
            foreach (var obj in occupiedCells.Values)
            {
                if (obj != null) uniqueObjects.Add(obj);
            }
            return new List<GameObject>(uniqueObjects);
        }
    }
}
