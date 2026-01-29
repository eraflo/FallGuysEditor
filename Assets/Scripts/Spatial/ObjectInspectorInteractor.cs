using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using Unity.XR.CoreUtils;
using Eraflo.Common.ObjectSystem;
using FallGuys.UI;

namespace Spatial
{
    /// <summary>
    /// Handles the "point and press B" interaction to open the Object Inspector UI.
    /// </summary>
    [RequireComponent(typeof(XRRayInteractor))]
    public class ObjectInspectorInteractor : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputActionReference inspectAction; // B Button

        [Header("References")]
        [SerializeField] private ObjectInspectorPanel inspectorPanel;
        [SerializeField] private LockInteractor lockInteractor;
        [SerializeField] private GhostController ghostController;

        private XRRayInteractor _rayInteractor;
        private BaseObject _currentHoveredObject;
        private bool _isInspectorOpen;

        private void Awake()
        {
            _rayInteractor = GetComponent<XRRayInteractor>();

            if (inspectorPanel != null)
            {
                inspectorPanel.OnPanelClosed += HandlePanelClosed;
            }
        }

        private void OnEnable()
        {
            if (inspectAction != null)
            {
                inspectAction.action.Enable();
                inspectAction.action.performed += OnInspectPressed;
            }
        }

        private void OnDisable()
        {
            if (inspectAction != null)
            {
                inspectAction.action.performed -= OnInspectPressed;
                inspectAction.action.Disable();
            }
        }

        private void Update()
        {
            if (_isInspectorOpen) return;

            // Track the currently hovered object
            if (_rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            {
                _currentHoveredObject = hit.collider.GetComponentInParent<BaseObject>();
            }
            else
            {
                _currentHoveredObject = null;
            }
        }

        private void OnInspectPressed(InputAction.CallbackContext context)
        {
            if (_currentHoveredObject == null || _isInspectorOpen) return;

            // Only allow inspection if the object is placed in the grid
            if (GridSystem.Instance == null || !GridSystem.Instance.IsObjectInGrid(_currentHoveredObject.gameObject))
            {
                return;
            }

            OpenInspector(_currentHoveredObject);
        }

        private void OpenInspector(BaseObject target)
        {
            // Position the panel between the object and the camera to avoid clipping
            Transform cam = Camera.main != null ? Camera.main.transform : null;
            if (cam == null)
            {
                var xrCam = GetComponentInParent<XROrigin>()?.GetComponentInChildren<Camera>();
                if (xrCam != null) cam = xrCam.transform;
            }
            
            if (cam == null) cam = FindObjectOfType<Camera>()?.transform;
            if (cam == null) return; // Cannot position without camera

            _isInspectorOpen = true;

            // Pause conflicting interactions
            if (lockInteractor != null) lockInteractor.SetEnabled(false);
            Vector3 directionToCam = (cam.position - target.transform.position).normalized;
            // 0.8m towards camera: ensures it's well outside the object bounding box
            Vector3 panelPosition = target.transform.position + directionToCam * 0.8f + Vector3.up * 0.2f;
            
            inspectorPanel.Show(target, panelPosition, ghostController);
        }

        private void HandlePanelClosed()
        {
            _isInspectorOpen = false;
            _currentHoveredObject = null; // Clear selection to prevent immediate re-trigger or lock behaviors

            // Resume conflicting interactions
            if (lockInteractor != null) lockInteractor.SetEnabled(true);
            if (ghostController != null) ghostController.Hide();
        }
    }
}
