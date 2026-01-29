using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
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

            OpenInspector(_currentHoveredObject);
        }

        private void OpenInspector(BaseObject target)
        {
            _isInspectorOpen = true;

            // Pause conflicting interactions
            if (lockInteractor != null) lockInteractor.SetEnabled(false);

            // Position the panel near the object
            Vector3 panelPosition = target.transform.position + Vector3.up * 0.5f + Camera.main.transform.right * 0.3f;
            inspectorPanel.Show(target, panelPosition, ghostController);
        }

        private void HandlePanelClosed()
        {
            _isInspectorOpen = false;

            // Resume conflicting interactions
            if (lockInteractor != null) lockInteractor.SetEnabled(true);
            if (ghostController != null) ghostController.Hide();
        }
    }
}
