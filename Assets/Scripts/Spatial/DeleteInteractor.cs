using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace Spatial
{
    /// <summary>
    /// Handles the "Commit-to-Delete" interaction for unplaced objects.
    /// Stage 1: Point to charge. Stage 2: Move ray to the side-button to confirm.
    /// </summary>
    [RequireComponent(typeof(XRRayInteractor))]
    public class DeleteInteractor : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float chargeDuration = 1.5f;
        [SerializeField] private float hapticAmplitude = 0.3f;
        [Header("UI Reference")]
        [SerializeField] private GameObject deleteUIPrefab;

        [Header("Effects")]
        [SerializeField] private GameObject deleteEffectPrefab;
        [SerializeField] private AudioClip deleteSound;
        [SerializeField] private AudioSource audioSource;

        private XRRayInteractor rayInteractor;
        private XRBaseController controller;
        private DeleteGaugeUI deleteUIInstance;
        private GameObject currentTarget;
        private float currentChargeTime = 0f;
        private bool isCharged = false;
        private GridSystem grid;

        private float confirmationDwellTimer = 0f;
        private const float confirmationDwellDuration = 0.25f;
        private float graceTimer = 0f;
        private const float graceDuration = 0.5f;
        private Vector3 originalTargetScale;

        private void Awake()
        {
            rayInteractor = GetComponent<XRRayInteractor>();
            controller = GetComponent<XRBaseController>();
            grid = GridSystem.Instance;

            if (audioSource == null) audioSource = GetComponent<AudioSource>();

            if (deleteUIPrefab != null)
            {
                // Instantiate the prefab (or use the scene instance if it's already there)
                GameObject uiObj;
                if (!deleteUIPrefab.scene.IsValid())
                {
                    uiObj = Instantiate(deleteUIPrefab);
                    uiObj.name = "DeleteGaugeUI_Instance";
                }
                else
                {
                    uiObj = deleteUIPrefab;
                }

                deleteUIInstance = uiObj.GetComponent<DeleteGaugeUI>();
                if (deleteUIInstance == null)
                {
                    deleteUIInstance = uiObj.GetComponentInChildren<DeleteGaugeUI>();
                }
            }
        }

        private void Update()
        {
            if (grid == null) grid = GridSystem.Instance;
            if (grid == null) return;

            bool isHittingButton = false;

            // 1. PRIORITY: Check for validation button (UI or Physics)
            // 1.1 Check UI Raycast
            if (rayInteractor.TryGetCurrentUIRaycastResult(out UnityEngine.EventSystems.RaycastResult uiHit))
            {
                if (IsHitOnValidationButton(uiHit.gameObject))
                {
                    isHittingButton = true;
                }
            }

            // 1.2 Check Physics Raycast for the button
            if (!isHittingButton && rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            {
                if (IsHitOnValidationButton(hit.collider.gameObject))
                {
                    isHittingButton = true;
                }
            }

            // 1.3 Process Button Hit
            if (isHittingButton)
            {
                if (isCharged && currentTarget != null)
                {
                    ProcessConfirmationDwell();
                }
                graceTimer = 0f;
                return; // Stop here, we found the button
            }

            // 2. SECONDARY: Check for objects to charge
            bool isHittingObject = false;
            if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit objHit))
            {
                isHittingObject = CheckForObjectHit(objHit.collider.gameObject, objHit);
            }

            // 3. CLEANUP: Reset or start grace period
            if (!isHittingObject)
            {
                if (isCharged && currentTarget != null)
                {
                    graceTimer += Time.deltaTime;
                    if (graceTimer >= graceDuration)
                    {
                        ResetInteraction();
                    }
                }
                else
                {
                    ResetInteraction();
                }
            }
            else
            {
                graceTimer = 0f;
            }
        }

        private bool CheckForObjectHit(GameObject hitObj, RaycastHit hit)
        {
            // Only check for objects to charge
            XRGrabInteractable grab = hit.collider.GetComponentInParent<XRGrabInteractable>();
            if (grab != null)
            {
                GameObject root = grab.gameObject;
                if (!grid.IsObjectInGrid(root) && !grab.isSelected)
                {
                    HandleCharging(root, hit);
                    return true;
                }
            }
            return false;
        }

        private void HandleCharging(GameObject obj, RaycastHit hit)
        {
            // If we're charging a different object, reset the interaction
            if (currentTarget != obj)
            {
                ResetInteraction();
                currentTarget = obj;
                originalTargetScale = currentTarget.transform.localScale;
            }

            // If we're not charged, charge the object
            if (!isCharged)
            {
                currentChargeTime += Time.deltaTime;
                if (currentChargeTime >= chargeDuration)
                {
                    isCharged = true;
                    if (controller != null) controller.SendHapticImpulse(0.8f, 0.2f);
                }

                // Only update visual position while charging
                if (deleteUIInstance != null)
                {
                    Vector3 uiPos = hit.point + hit.normal * 0.05f;
                    deleteUIInstance.Show(uiPos, Mathf.Clamp01(currentChargeTime / chargeDuration), isCharged);
                }
            }
            else
            {
                // Once charged, we keep the UI visible but we don't update its position based on the ray
                // This allows the user to 'leave' the object center and point to the validation button
                if (deleteUIInstance != null)
                {
                    deleteUIInstance.Show(deleteUIInstance.transform.position, 1f, true);
                }
            }
        }

        private void DeleteCurrentTarget()
        {
            if (currentTarget != null)
            {
                // Play particle effect
                if (deleteEffectPrefab != null)
                {
                    Instantiate(deleteEffectPrefab, currentTarget.transform.position, Quaternion.identity);
                }

                // Play sound
                if (audioSource != null && deleteSound != null)
                {
                    audioSource.PlayOneShot(deleteSound);
                }

                // Haptic feedback
                if (controller != null) controller.SendHapticImpulse(1.0f, 0.3f);

                Destroy(currentTarget);
                ResetInteraction();
            }
        }

        private void ResetInteraction()
        {
            // Reset the target scale if we were charging
            if (currentTarget != null && isCharged)
            {
                currentTarget.transform.localScale = originalTargetScale;
            }

            // Reset the interaction state
            currentTarget = null;
            currentChargeTime = 0f;
            isCharged = false;
            confirmationDwellTimer = 0f;

            // Reset the UI
            if (deleteUIInstance != null)
            {
                deleteUIInstance.SetValidationShake(0);
                deleteUIInstance.Hide();
            }
        }

        private bool IsObjectGrabbed(GameObject obj)
        {
            var interactable = obj.GetComponent<XRGrabInteractable>();
            return interactable != null && interactable.isSelected;
        }

        private bool IsHitOnValidationButton(GameObject hitObj)
        {
            if (deleteUIInstance == null) return false;
            GameObject button = deleteUIInstance.GetValidationButton();
            if (button == null) return false;

            // Check if hit object is the button or a child of the button
            return hitObj == button || hitObj.transform.IsChildOf(button.transform);
        }

        private void ProcessConfirmationDwell()
        {
            if (deleteUIInstance == null || currentTarget == null) return;

            // Highlight the button and start dwell timer
            deleteUIInstance.SetValidationHighlight(true);
            confirmationDwellTimer += Time.deltaTime;

            // Update UI shake and object shrink
            float ratio = Mathf.Clamp01(confirmationDwellTimer / confirmationDwellDuration);
            deleteUIInstance.SetValidationShake(ratio);
            currentTarget.transform.localScale = Vector3.Lerp(originalTargetScale, Vector3.zero, ratio);

            // Progressive haptics
            if (controller != null) controller.SendHapticImpulse(Mathf.Lerp(0.2f, 1.0f, ratio), 0.05f);

            if (confirmationDwellTimer >= confirmationDwellDuration)
            {
                DeleteCurrentTarget();
            }
        }
    }
}
