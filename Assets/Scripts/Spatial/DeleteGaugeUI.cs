using UnityEngine;
using UnityEngine.UI;

namespace Spatial
{
    /// <summary>
    /// UI for the deletion process. Includes a radial gauge and a validation button.
    /// </summary>
    public class DeleteGaugeUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private Image fillImage;

        [Header("Validation Button Settings")]
        [SerializeField] private GameObject validationButton; // Must have a Collider
        [SerializeField] private Color normalColor = Color.white;
        [SerializeField] private Color highlightColor = Color.red;

        private Transform mainCameraTransform;
        private Image validationImage;
        private Vector3 baseLocalPosition;

        private void Awake()
        {
            if (Camera.main != null)
                mainCameraTransform = Camera.main.transform;

            if (validationButton != null)
            {
                validationImage = validationButton.GetComponent<Image>();
                baseLocalPosition = validationButton.transform.localPosition;
            }

            Hide();
        }

        private void LateUpdate()
        {
            if (canvas.enabled && mainCameraTransform != null)
            {
                // Face the camera
                transform.LookAt(transform.position + mainCameraTransform.rotation * Vector3.forward,
                                 mainCameraTransform.rotation * Vector3.up);
            }
        }

        public void Show(Vector3 worldPos, float progress, bool showValidation)
        {
            if (!canvas.enabled) canvas.enabled = true;

            transform.position = worldPos;
            fillImage.fillAmount = progress;

            if (validationButton != null)
            {
                validationButton.SetActive(showValidation);
                SetValidationHighlight(false); // Reset highlight by default
            }
        }

        public void SetValidationHighlight(bool highlighted)
        {
            if (validationImage != null)
            {
                validationImage.color = highlighted ? highlightColor : normalColor;
                validationButton.transform.localScale = Vector3.one * (highlighted ? 1.2f : 1.0f);
            }
        }

        public void SetValidationShake(float intensity)
        {
            if (validationButton != null && intensity > 0)
            {
                Vector3 randomOffset = Random.insideUnitSphere * intensity * 5f;
                validationButton.transform.localPosition = baseLocalPosition + randomOffset;
            }
            else if (validationButton != null)
            {
                validationButton.transform.localPosition = baseLocalPosition;
            }
        }

        public void Hide()
        {
            if (canvas.enabled) canvas.enabled = false;
            if (validationButton != null) validationButton.SetActive(false);
        }

        public GameObject GetValidationButton() => validationButton;
    }
}
