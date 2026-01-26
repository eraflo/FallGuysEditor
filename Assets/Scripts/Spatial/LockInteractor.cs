using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace Spatial
{
    /// <summary>
    /// Handles the "point-and-charge" interaction to lock/unlock objects.
    /// Should be placed on a GameObject with an XRRayInteractor.
    /// </summary>
    [RequireComponent(typeof(XRRayInteractor))]
    public class LockInteractor : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float chargeDuration = 1.2f;
        [SerializeField] private float cooldownDuration = 5.0f;
        [SerializeField] private float hapticAmplitude = 0.2f;
        [SerializeField] private float hapticDuration = 0.05f;

        [Header("UI Reference")]
        [SerializeField] private LockGaugeUI gaugeUI;

        private XRRayInteractor rayInteractor;
        private XRBaseController controller;
        private GridLockable currentTarget;
        private float currentChargeTime = 0f;
        private float lastInteractionTime = -10f;
        private GridLockable lastInteractedObject;
        private GridSystem grid;

        private void Awake()
        {
            rayInteractor = GetComponent<XRRayInteractor>();
            controller = GetComponent<XRBaseController>();
            grid = GridSystem.Instance;

            // If gaugeUI is a prefab, instantiate it so it exists in the scene
            if (gaugeUI != null && !gaugeUI.gameObject.scene.IsValid())
            {
                gaugeUI = Instantiate(gaugeUI);
                gaugeUI.gameObject.name = "LockGaugeUI_Instance";
            }
        }

        private void Update()
        {
            if (grid == null) grid = GridSystem.Instance;
            if (grid == null) return;

            if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            {
                GridLockable lockable = hit.collider.GetComponentInParent<GridLockable>();

                // Only allow locking if the object is actually placed in the grid
                if (lockable != null && grid.IsObjectInGrid(lockable.gameObject))
                {
                    HandleCharging(lockable, hit);
                    return;
                }
            }

            ResetCharge();
        }

        private void HandleCharging(GridLockable lockable, RaycastHit hit)
        {
            // Check cooldown if it's the same object we just interacted with
            if (lastInteractedObject == lockable && Time.time < lastInteractionTime + cooldownDuration)
            {
                if (gaugeUI != null)
                {
                    Vector3 offsetPos = hit.point + hit.normal * 0.05f;
                    gaugeUI.Show(offsetPos, 0, lockable.IsLocked);
                    gaugeUI.SetColor(Color.red); // Visual indicator for cooldown
                }
                return;
            }

            if (currentTarget != lockable)
            {
                currentTarget = lockable;
                currentChargeTime = 0f;
            }

            currentChargeTime += Time.deltaTime;
            float progress = Mathf.Clamp01(currentChargeTime / chargeDuration);

            if (gaugeUI != null)
            {
                // Offset the UI by 5cm along the surface normal to prevent clipping
                Vector3 offsetPos = hit.point + hit.normal * 0.05f;
                gaugeUI.Show(offsetPos, progress, currentTarget.IsLocked);
                gaugeUI.SetColor(Color.white); // Normal charging color
            }

            // Optional: subtle haptic pulse while charging
            if (controller != null && progress > 0.1f)
            {
                // Send pulse every ~10% progress
                if (Mathf.FloorToInt(progress * 10) > Mathf.FloorToInt((progress - (Time.deltaTime / chargeDuration)) * 10))
                {
                    controller.SendHapticImpulse(hapticAmplitude * progress, hapticDuration);
                }
            }

            if (currentChargeTime >= chargeDuration)
            {
                ToggleLock();
            }
        }

        private void ToggleLock()
        {
            if (currentTarget != null)
            {
                bool newState = !currentTarget.IsLocked;
                currentTarget.SetLockState(newState);

                // Track interaction for cooldown
                lastInteractionTime = Time.time;
                lastInteractedObject = currentTarget;

                // Stronger haptic feedback on completion
                if (controller != null)
                {
                    controller.SendHapticImpulse(0.7f, 0.2f);
                }

                // Reset charge
                currentChargeTime = 0f;
            }
        }

        private void ResetCharge()
        {
            currentTarget = null;
            currentChargeTime = 0f;
            if (gaugeUI != null) gaugeUI.Hide();
        }
    }
}
