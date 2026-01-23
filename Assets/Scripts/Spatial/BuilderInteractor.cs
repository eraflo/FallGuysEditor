using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

namespace Spatial
{
    /// <summary>
    /// Orchestrates VR input, grid snapping, ghost preview, and object placement.
    /// Handles rotation via joystick and haptic feedback.
    /// </summary>
    public class BuilderInteractor : MonoBehaviour
    {
        #region Structs & Enums

        [System.Serializable]
        public struct HapticSettings
        {
            public float amplitude;
            public float duration;
            public HapticSettings(float a, float d) { amplitude = a; duration = d; }
        }

        #endregion

        #region Serialized Fields

        [Header("References")]
        [SerializeField] private GhostController ghostController;
        [SerializeField] private GridVisualizer gridVisualizer;
        [SerializeField] private XRBaseInteractor leftInteractor;
        [SerializeField] private XRBaseInteractor rightInteractor;
        [SerializeField] private LocomotionProvider[] locomotionProviders;
        [SerializeField] private XRBaseInteractor[] teleportInteractors;

        [Header("Input Actions")]
        [SerializeField] private InputActionReference toggleAction;
        [SerializeField] private InputActionReference leftRotateAction;
        [SerializeField] private InputActionReference rightRotateAction;
        [SerializeField] private InputActionReference leftPlaceAction;
        [SerializeField] private InputActionReference rightPlaceAction;
        [SerializeField] private InputActionReference cancelAction;

        [Header("Placement Settings")]
        [SerializeField] private LayerMask gridLayer;
        [SerializeField] private LayerMask ignoreGrabLayers;
        [SerializeField] private LayerMask grabbableLayer; // Layer for objects placed on the grid
        [SerializeField] private float depthSensitivity = 2f;
        [SerializeField] private bool isSystemEnabled = true;

        [Header("Haptics")]
        [SerializeField] private HapticSettings rotationHaptics = new HapticSettings(0.3f, 0.1f);
        [SerializeField] private HapticSettings placementHaptics = new HapticSettings(0.7f, 0.2f);

        [Header("Rotation Timings")]
        [SerializeField] private float rotationRepeatDelay = 0.4f;
        [SerializeField] private float rotationRepeatRate = 0.1f;

        #endregion

        #region Private Fields

        private GridSystem grid;
        private float currentRotationY = 0f;
        private float currentRotationX = 0f;

        private float nextRotationStepTime = 0f;
        private bool isCurrentlyRotating = false;

        private Vector3 lastLookPos;
        private Vector3 cachedSnapPos;
        private int autoLayerIndex;

        private float togglePressStartTime;
        private bool isLongPressTriggered;
        private const float longPressDuration = 0.4f;

        private bool isWaitingForTriggerRelease = false;
        private bool isInsideGridZone = true;
        private List<Renderer> grabbedRenderers = new List<Renderer>();
        private XRBaseController leftController;
        private XRBaseController rightController;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            grid = GridSystem.Instance;
            if (grid == null) grid = UnityEngine.Object.FindFirstObjectByType<GridSystem>();

            // Auto-find all locomotion providers in scene (active + inactive)
            if (locomotionProviders == null || locomotionProviders.Length == 0)
                locomotionProviders = UnityEngine.Object.FindObjectsByType<LocomotionProvider>(FindObjectsInactive.Include, FindObjectsSortMode.None);

            // Extract the first active layer from the grid layer mask
            int mask = gridLayer.value;
            for (int i = 0; i < 32; i++)
            {
                if ((mask & (1 << i)) != 0) { autoLayerIndex = i; break; }
            }
        }

        private void OnEnable()
        {
            // --- Input Setup ---
            // We use Started/Canceled for the toggle to handle Short vs Long press logic.
            if (toggleAction != null)
            {
                toggleAction.action.Enable();
                toggleAction.action.started += OnToggleStarted;
                toggleAction.action.canceled += OnToggleCanceled;
            }

            // Standard event-based triggers
            EnableAction(leftPlaceAction, OnPlace);
            EnableAction(rightPlaceAction, OnPlace);
            EnableAction(cancelAction, OnCancel);

            // XR Listeners for physical grab events
            if (leftInteractor != null)
            {
                leftInteractor.selectEntered.AddListener(OnGrab);
                leftInteractor.selectExited.AddListener(OnRelease);
            }
            if (rightInteractor != null)
            {
                rightInteractor.selectEntered.AddListener(OnGrab);
                rightInteractor.selectExited.AddListener(OnRelease);
            }

            UpdateSystemState();

            // Cache controller references once to avoid GetComponent in haptics
            if (leftInteractor != null) leftController = leftInteractor.GetComponent<XRBaseController>() ?? leftInteractor.GetComponentInParent<XRBaseController>();
            if (rightInteractor != null) rightController = rightInteractor.GetComponent<XRBaseController>() ?? rightInteractor.GetComponentInParent<XRBaseController>();
        }

        private void OnDisable()
        {
            if (toggleAction != null)
            {
                toggleAction.action.started -= OnToggleStarted;
                toggleAction.action.canceled -= OnToggleCanceled;
                toggleAction.action.Disable();
            }

            DisableAction(leftPlaceAction, OnPlace);
            DisableAction(rightPlaceAction, OnPlace);
            DisableAction(cancelAction, OnCancel);

            if (leftInteractor != null)
            {
                leftInteractor.selectEntered.RemoveListener(OnGrab);
                leftInteractor.selectExited.RemoveListener(OnRelease);
            }
            if (rightInteractor != null)
            {
                rightInteractor.selectEntered.RemoveListener(OnGrab);
                rightInteractor.selectExited.RemoveListener(OnRelease);
            }

            // SAFETY: Force all locomotion and anchor control back on when the builder script is disabled
            SetLocomotionEnabled(true);
            ToggleRayAnchorControl(true);
        }

        private void Update()
        {
            // PERFORMANCE: Cache the grabbed object once per frame
            GameObject grabbedObj = GetGrabbedObject();
            bool isGrabbingInGrid = isSystemEnabled && grabbedObj != null;

            if (isGrabbingInGrid)
            {
                HandleRotationContinuous(grabbedObj);
                UpdatePlacementPreview(grabbedObj);
            }
            else
            {
                ghostController.Hide();
            }

            // --- Locomotion & Trigger Management ---
            float leftT = leftPlaceAction != null ? leftPlaceAction.action.ReadValue<float>() : 0f;
            float rightT = rightPlaceAction != null ? rightPlaceAction.action.ReadValue<float>() : 0f;
            bool isTriggerActive = leftT > 0.1f || rightT > 0.1f;

            // Block TP/Turn only during the grab or just after placement
            bool shouldDisableLoco = isGrabbingInGrid || isWaitingForTriggerRelease;
            SetLocomotionEnabled(!shouldDisableLoco);

            if (isWaitingForTriggerRelease)
            {
                if (leftT < 0.1f && rightT < 0.1f) isWaitingForTriggerRelease = false;
            }

            // --- Long Press Detection ---
            if (togglePressStartTime > 0 && !isLongPressTriggered)
            {
                if (Time.time - togglePressStartTime >= longPressDuration)
                {
                    isLongPressTriggered = true;
                    if (isSystemEnabled && gridVisualizer != null)
                    {
                        gridVisualizer.ToggleVisibility();
                        SendHapticPulse(new HapticSettings(0.2f, 0.05f));
                    }
                }
            }
        }

        #endregion

        #region System Management

        private void UpdateSystemState()
        {
            if (gridVisualizer != null) gridVisualizer.SetEnabled(isSystemEnabled);
            if (!isSystemEnabled) ghostController.Hide();

            if (!isSystemEnabled)
            {
                isWaitingForTriggerRelease = false;
                SetLocomotionEnabled(true);
            }

            UpdateInputOverrides(isSystemEnabled);
            ToggleRayAnchorControl(!isSystemEnabled);
        }

        private void UpdateInputOverrides(bool enabled)
        {
            if (enabled)
            {
                if (leftRotateAction != null)
                    leftRotateAction.action.ApplyBindingOverride(new InputBinding { overrideProcessors = "ScaleVector2(y=1)" });
                if (rightRotateAction != null)
                    rightRotateAction.action.ApplyBindingOverride(new InputBinding { overrideProcessors = "ScaleVector2(y=1)" });
            }
            else
            {
                if (leftRotateAction != null) leftRotateAction.action.RemoveAllBindingOverrides();
                if (rightRotateAction != null) rightRotateAction.action.RemoveAllBindingOverrides();
            }
        }

        private void SetLocomotionEnabled(bool enable)
        {
            if (locomotionProviders != null)
                foreach (var lp in locomotionProviders) if (lp != null) lp.enabled = enable;

            // Also disable the interactors (rays) to prevent buffering input/selection
            if (teleportInteractors != null)
                foreach (var interactor in teleportInteractors) if (interactor != null) interactor.enabled = enable;
        }

        private void ToggleRayAnchorControl(bool enable)
        {
            if (leftInteractor is XRRayInteractor leftRay) leftRay.allowAnchorControl = enable;
            if (rightInteractor is XRRayInteractor rightRay) rightRay.allowAnchorControl = enable;
        }

        #endregion

        #region Interaction Handlers

        private void OnGrab(SelectEnterEventArgs args)
        {
            // Enforcement: Force release of any already held object
            GameObject existing = GetGrabbedObject();
            if (existing != null && existing != args.interactableObject.transform.gameObject)
            {
                DeselectObject(existing);
            }

            GameObject grabbed = args.interactableObject.transform.gameObject;
            grid.ClearObject(grabbed);

            isWaitingForTriggerRelease = false;
            SetLocomotionEnabled(false);

            // PERFORMANCE: Cache all renderers once to avoid expensive per-frame recursion
            grabbedRenderers.Clear();
            grabbed.GetComponentsInChildren<Renderer>(true, grabbedRenderers);

            // Initial visibility state based on distance (Cubic check to match GridVisualizer)
            Vector3 centerPos = grid.Origin + (Vector3)gridVisualizer.CurrentCenterCell * grid.CellSize;
            Vector3 diff = grabbed.transform.position - centerPos;
            float cubicDist = Mathf.Max(Mathf.Abs(diff.x), Mathf.Max(Mathf.Abs(diff.y), Mathf.Abs(diff.z)));
            float visRadius = gridVisualizer.VisibilityHalfRange * grid.CellSize;

            isInsideGridZone = cubicDist < visRadius;
            if (isSystemEnabled) ToggleGrabbedMeshVisibility(!isInsideGridZone);

            if (grabbed.TryGetComponent<Rigidbody>(out Rigidbody rb))
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.None;
                rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        private void OnRelease(SelectExitEventArgs args)
        {
            // Restore visibility immediately in case it was hidden
            ToggleGrabbedMeshVisibility(true);
            grabbedRenderers.Clear();

            if (isSystemEnabled)
            {
                isWaitingForTriggerRelease = true;
            }
        }

        private void OnToggleCanceled(InputAction.CallbackContext context)
        {
            if (!isLongPressTriggered)
            {
                float pressDuration = Time.time - togglePressStartTime;
                if (pressDuration < longPressDuration)
                {
                    isSystemEnabled = !isSystemEnabled;
                    UpdateSystemState();
                }
            }
            togglePressStartTime = 0f;
            isLongPressTriggered = false;
        }

        private void OnToggleStarted(InputAction.CallbackContext context) => togglePressStartTime = Time.time;

        private void OnCancel(InputAction.CallbackContext context)
        {
            GameObject grabbedObj = GetGrabbedObject();
            if (grabbedObj != null)
            {
                DeselectObject(grabbedObj);
                if (ghostController != null) ghostController.Hide();
            }
        }

        private void OnPlace(InputAction.CallbackContext context)
        {
            if (!isSystemEnabled) return;

            bool isLeftAction = leftPlaceAction != null && context.action == leftPlaceAction.action;
            bool isRightAction = rightPlaceAction != null && context.action == rightPlaceAction.action;
            bool isLeftHolding = leftInteractor != null && leftInteractor.interactablesSelected.Count > 0;
            bool isRightHolding = rightInteractor != null && rightInteractor.interactablesSelected.Count > 0;

            if ((isLeftAction && !isLeftHolding) || (isRightAction && !isRightHolding)) return;

            GameObject grabbedObj = GetGrabbedObject();
            if (grabbedObj == null) return;

            Vector3Int cell = grid.WorldToCell(grabbedObj.transform.position);
            Vector3Int center = gridVisualizer.CurrentCenterCell;
            int halfRange = gridVisualizer.VisibilityHalfRange;

            if (Mathf.Abs(cell.x - center.x) > halfRange || Mathf.Abs(cell.y - center.y) > halfRange || Mathf.Abs(cell.z - center.z) > halfRange) return;

            Vector3Int snappedCell = grid.WorldToCell(cachedSnapPos);
            if (!grid.IsCellOccupied(snappedCell, grabbedObj))
            {
                Quaternion rotation = grid.GetQuantizedRotation(currentRotationX, currentRotationY, 0f);
                grabbedObj.transform.SetPositionAndRotation(cachedSnapPos, rotation);

                if (grabbedObj.TryGetComponent<Rigidbody>(out Rigidbody rb))
                {
                    rb.isKinematic = false;
                    rb.useGravity = false;
                    rb.constraints = RigidbodyConstraints.FreezeAll;
                }

                SetLayerRecursive(grabbedObj.transform, GetLayerFromMask(grabbableLayer));
                DeselectObject(grabbedObj);
                grid.OccupyCell(snappedCell, grabbedObj);
                SendHapticPulse(placementHaptics);
            }
        }

        #endregion

        #region Logic & Calculations

        private void HandleRotationContinuous(GameObject grabbedObj)
        {
            XRBaseInteractor activeHand = null;
            InputActionReference activeRotateAction = null;
            InputActionReference inactiveRotateAction = null;

            if (rightInteractor != null && rightInteractor.interactablesSelected.Count > 0)
            {
                activeHand = rightInteractor;
                activeRotateAction = rightRotateAction;
                inactiveRotateAction = leftRotateAction;
            }
            else if (leftInteractor != null && leftInteractor.interactablesSelected.Count > 0)
            {
                activeHand = leftInteractor;
                activeRotateAction = leftRotateAction;
                inactiveRotateAction = rightRotateAction;
            }

            if (activeHand == null) return;

            Vector2 activeInput = activeRotateAction != null ? activeRotateAction.action.ReadValue<Vector2>() : Vector2.zero;
            bool hasRotationInput = Mathf.Abs(activeInput.x) > 0.3f || Mathf.Abs(activeInput.y) > 0.3f;

            if (!hasRotationInput) isCurrentlyRotating = false;
            else
            {
                if (!isCurrentlyRotating) { PerformRotationStep(activeInput); nextRotationStepTime = Time.time + rotationRepeatDelay; isCurrentlyRotating = true; }
                else if (Time.time >= nextRotationStepTime) { PerformRotationStep(activeInput); nextRotationStepTime = Time.time + rotationRepeatRate; }
            }

            if (inactiveRotateAction != null)
            {
                Vector2 inactiveInput = inactiveRotateAction.action.ReadValue<Vector2>();
                if (Mathf.Abs(inactiveInput.y) > 0.1f) HandleDistanceTranslation(activeHand as XRRayInteractor, inactiveInput.y);
            }
        }

        private void HandleDistanceTranslation(XRRayInteractor interactor, float verticalInput)
        {
            if (interactor == null || interactor.attachTransform == null) return;
            float speed = 2.0f;
            Transform attach = interactor.attachTransform;
            attach.localPosition += Vector3.forward * verticalInput * Time.deltaTime * speed;
            float dist = attach.localPosition.z;
            attach.localPosition = new Vector3(attach.localPosition.x, attach.localPosition.y, Mathf.Clamp(dist, 0.5f, 50f));
        }

        private void PerformRotationStep(Vector2 input)
        {
            bool changed = false;
            float absX = Mathf.Abs(input.x);
            float absY = Mathf.Abs(input.y);

            if (absX > absY)
            {
                if (absX > 0.3f) { currentRotationY += Mathf.Sign(input.x) * grid.RotationStep; changed = true; }
            }
            else
            {
                if (absY > 0.3f) { currentRotationX -= Mathf.Sign(input.y) * grid.RotationStep; changed = true; }
            }

            if (changed) SendHapticPulse(rotationHaptics);
        }

        private void UpdatePlacementPreview(GameObject target)
        {
            Vector3 targetPos = target.transform.position;
            Vector3 centerPos = grid.Origin + (Vector3)gridVisualizer.CurrentCenterCell * grid.CellSize;
            Vector3 diff = targetPos - centerPos;

            // --- Cubic Distance Logic ---
            // Matches the visual cage: distance is the maximum axial difference
            float cubicDist = Mathf.Max(Mathf.Abs(diff.x), Mathf.Max(Mathf.Abs(diff.y), Mathf.Abs(diff.z)));
            float visRadius = gridVisualizer.VisibilityHalfRange * grid.CellSize;

            bool previouslyInside = isInsideGridZone;

            // Invisible 0-100%, Visible outside.
            if (cubicDist > visRadius) isInsideGridZone = false;
            else if (cubicDist < visRadius * 0.9f) isInsideGridZone = true; // 10% Hysteresis

            if (isSystemEnabled && (previouslyInside != isInsideGridZone))
            {
                ToggleGrabbedMeshVisibility(!isInsideGridZone);
                if (!isInsideGridZone) ghostController.Hide();
            }

            if (!isInsideGridZone) return;

            // PERFORMANCE: Use squared magnitude for threshold check
            if ((targetPos - lastLookPos).sqrMagnitude > 0.0001f)
            {
                lastLookPos = targetPos;
                cachedSnapPos = grid.GetClosestGridPoint(targetPos);
            }

            Quaternion rotation = grid.GetQuantizedRotation(currentRotationX, currentRotationY, 0f);
            bool isOccupied = grid.IsCellOccupied(grid.WorldToCell(cachedSnapPos), target);

            ghostController.Show(target, cachedSnapPos, rotation);
            ghostController.SetValid(!isOccupied);
        }

        #endregion

        #region Helpers

        private void DeselectObject(GameObject target)
        {
            var interactable = target.GetComponent<IXRSelectInteractable>();
            if (interactable == null) interactable = target.GetComponentInParent<IXRSelectInteractable>();

            if (interactable != null && interactable.isSelected)
            {
                if (rightInteractor != null && rightInteractor.interactablesSelected.Contains(interactable))
                    rightInteractor.interactionManager.SelectExit(rightInteractor, interactable);
                else if (leftInteractor != null && leftInteractor.interactablesSelected.Contains(interactable))
                    leftInteractor.interactionManager.SelectExit(leftInteractor, interactable);
            }
        }

        private void ToggleGrabbedMeshVisibility(bool visible)
        {
            int count = grabbedRenderers.Count;
            for (int i = 0; i < count; i++)
            {
                if (grabbedRenderers[i] != null) grabbedRenderers[i].enabled = visible;
            }
        }

        private void SetLayerRecursive(Transform target, int layer)
        {
            target.gameObject.layer = layer;
            foreach (Transform child in target) SetLayerRecursive(child, layer);
        }

        private GameObject GetGrabbedObject()
        {
            GameObject target = null;
            if (rightInteractor != null && rightInteractor.interactablesSelected.Count > 0)
                target = rightInteractor.interactablesSelected[0].transform.gameObject;
            else if (leftInteractor != null && leftInteractor.interactablesSelected.Count > 0)
                target = leftInteractor.interactablesSelected[0].transform.gameObject;

            if (target != null && ((1 << target.layer) & ignoreGrabLayers.value) == 0) return target;
            return null;
        }

        private int GetLayerFromMask(LayerMask mask)
        {
            for (int i = 0; i < 32; i++) { if (((1 << i) & mask.value) != 0) return i; }
            return 0;
        }

        private void SendHapticPulse(HapticSettings settings)
        {
            if (rightController != null) rightController.SendHapticImpulse(settings.amplitude, settings.duration);
            if (leftController != null) leftController.SendHapticImpulse(settings.amplitude, settings.duration);
        }

        private void EnableAction(InputActionReference action, Action<InputAction.CallbackContext> callback)
        {
            if (action == null) return; action.action.Enable(); action.action.performed += callback;
        }

        private void DisableAction(InputActionReference action, Action<InputAction.CallbackContext> callback)
        {
            if (action == null) return; action.action.performed -= callback; action.action.Disable();
        }

        #endregion
    }
}
