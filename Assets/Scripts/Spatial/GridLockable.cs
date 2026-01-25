using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

namespace Spatial
{
    /// <summary>
    /// Component that allows an object to be locked in place within the grid.
    /// When locked, the object's XRGrabInteractable is disabled.
    /// </summary>
    public class GridLockable : MonoBehaviour
    {
        [Header("State")]
        [SerializeField] private bool isLocked = false;

        [Header("Visual Feedback")]
        [SerializeField] private GameObject lockVisual; // Optional: a small lock icon or effect
        [SerializeField] private Color lockedTint = new Color(0.7f, 0.7f, 1f, 1f);

        [Header("Audio Feedback")]
        [SerializeField] private AudioClip lockSound;
        [SerializeField] private AudioClip unlockSound;
        [SerializeField] private AudioSource audioSource;

        private XRGrabInteractable grabInteractable;
        private Renderer[] renderers;
        private Color[] originalColors;

        public bool IsLocked => isLocked;

        private void Awake()
        {
            grabInteractable = GetComponent<XRGrabInteractable>();
            renderers = GetComponentsInChildren<Renderer>();

            if (audioSource == null) audioSource = GetComponent<AudioSource>();

            // Store original colors
            originalColors = new Color[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i].material.HasProperty("_BaseColor"))
                    originalColors[i] = renderers[i].material.GetColor("_BaseColor");
                else if (renderers[i].material.HasProperty("_Color"))
                    originalColors[i] = renderers[i].material.color;
            }

            ApplyLockState(false); // Initial apply without sound
        }

        public void SetLockState(bool locked)
        {
            if (isLocked == locked) return;

            isLocked = locked;
            ApplyLockState(true);

            Debug.Log($"Object {gameObject.name} is now {(isLocked ? "Locked" : "Unlocked")}");
        }

        private void ApplyLockState(bool playEffects)
        {
            if (grabInteractable != null) grabInteractable.enabled = !isLocked;
            if (lockVisual != null) lockVisual.SetActive(isLocked);

            // Audio feedback
            if (playEffects && audioSource != null)
            {
                AudioClip clip = isLocked ? lockSound : unlockSound;
                if (clip != null) audioSource.PlayOneShot(clip);
            }

            // Visual feedback: tint
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;

                Color target = isLocked ? lockedTint : originalColors[i];
                if (renderers[i].material.HasProperty("_BaseColor"))
                    renderers[i].material.SetColor("_BaseColor", target);
                else if (renderers[i].material.HasProperty("_Color"))
                    renderers[i].material.color = target;
            }
        }
    }
}
