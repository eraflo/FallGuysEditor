using UnityEngine;
using UnityEngine.UI;

namespace Spatial
{
    /// <summary>
    /// Controls the circular gauge UI for locking/unlocking objects.
    /// </summary>
    public class LockGaugeUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private Image fillImage;
        [SerializeField] private Image backgroundIcon;
        [SerializeField] private Sprite lockSprite;
        [SerializeField] private Sprite unlockSprite;
        [Range(0.1f, 1f)][SerializeField] private float iconScale = 0.6f;

        private Transform mainCameraTransform;

        private void Awake()
        {
            if (Camera.main != null)
                mainCameraTransform = Camera.main.transform;

            if (backgroundIcon != null)
            {
                backgroundIcon.transform.localScale = Vector3.one * iconScale;
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

        public void Show(Vector3 worldPos, float progress, bool isCurrentlyLocked)
        {
            if (!canvas.enabled) canvas.enabled = true;

            transform.position = worldPos;
            fillImage.fillAmount = progress;

            if (backgroundIcon != null)
            {
                backgroundIcon.sprite = isCurrentlyLocked ? unlockSprite : lockSprite;
            }
        }

        public void SetColor(Color color)
        {
            if (fillImage != null) fillImage.color = color;
        }

        public void Hide()
        {
            if (canvas.enabled) canvas.enabled = false;
        }

        public void SetProgress(float progress)
        {
            fillImage.fillAmount = progress;
        }
    }
}
