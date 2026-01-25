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

            bool isHittingSomethingValid = false;

            if (rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
            {
                isHittingSomethingValid = CheckHit(hit.collider.gameObject, hit);
            }

            // 1.5 NEW: If physics hit failed or wasn't the button, check UI raycast
            if (!isHittingSomethingValid && rayInteractor.TryGetCurrentUIRaycastResult(out UnityEngine.EventSystems.RaycastResult uiHit))
            {
                if (deleteUIInstance != null && deleteUIInstance.GetValidationButton() == uiHit.gameObject)
                {
                    if (isCharged && currentTarget != null)
                    {
                        // HIGHLIGHT: Show feedback that we are on the button
                        deleteUIInstance.SetValidationHighlight(true);

                        // Increment dwell time for intentional deletion
                        confirmationDwellTimer += Time.deltaTime;
                        float ratio = confirmationDwellTimer / confirmationDwellDuration;

                        // UI SHAKE
                        deleteUIInstance.SetValidationShake(ratio);

                        // OBJECT SHRINK: Sucking the object into oblivion
                        currentTarget.transform.localScale = Vector3.Lerp(originalTargetScale, Vector3.zero, ratio);

                        // PROGRESSIVE HAPTICS: Vibrate more as we get closer to deletion
                        if (controller != null)
                        {
                            controller.SendHapticImpulse(Mathf.Lerp(0.2f, 1.0f, ratio), 0.05f);
                        }

                        if (confirmationDwellTimer >= confirmationDwellDuration)
                        {
                            DeleteCurrentTarget();
                            return;
                        }
                    }
                    isHittingSomethingValid = true;
                }
            }

            if (!isHittingSomethingValid)
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
                graceTimer = 0f; // Reset grace if we hit something valid
            }
        }

        private bool CheckHit(GameObject hitObj, RaycastHit hit)
        {
            // 1. Check if we hit the validation button via physics
            if (deleteUIInstance != null && deleteUIInstance.GetValidationButton() == hitObj)
            {
                if (isCharged && currentTarget != null)
                {
                    deleteUIInstance.SetValidationHighlight(true);
                    confirmationDwellTimer += Time.deltaTime;
                    float ratio = confirmationDwellTimer / confirmationDwellDuration;
                    deleteUIInstance.SetValidationShake(ratio);
                    currentTarget.transform.localScale = Vector3.Lerp(originalTargetScale, Vector3.zero, ratio);
                    if (controller != null) controller.SendHapticImpulse(Mathf.Lerp(0.2f, 1.0f, ratio), 0.05f);

                    if (confirmationDwellTimer >= confirmationDwellDuration)
                    {
                        DeleteCurrentTarget();
                    }
                }
                return true;
            }

            // 2. Check if we hit a potential object to delete
            confirmationDwellTimer = 0f;
            if (deleteUIInstance != null) deleteUIInstance.SetValidationShake(0);

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
            if (currentTarget != obj)
            {
                ResetInteraction();
                currentTarget = obj;
                originalTargetScale = currentTarget.transform.localScale;
            }

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
            if (currentTarget != null && isCharged)
            {
                currentTarget.transform.localScale = originalTargetScale;
            }

            currentTarget = null;
            currentChargeTime = 0f;
            isCharged = false;
            confirmationDwellTimer = 0f;
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
    }
}
