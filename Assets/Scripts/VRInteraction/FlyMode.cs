using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Samples.StarterAssets;

public class FlyMode : MonoBehaviour
{
    [SerializeField] private InputActionReference enableFly;
    [SerializeField] private ActionBasedControllerManager actionBasedControllerManager;
    [SerializeField] private TeleportationProvider teleportationProvider;
    [SerializeField] private DynamicMoveProvider dynamicMoveProvider;

    private bool flyEnabled = false;

    private void Start()
    {
        enableFly.action.performed += OnToggleFly;
    }

    private void OnToggleFly(InputAction.CallbackContext ctx)
    {
        if (!ctx.performed) return;

        flyEnabled = !flyEnabled;

        actionBasedControllerManager.smoothMotionEnabled = flyEnabled;
        teleportationProvider.gameObject.SetActive(!flyEnabled);
        dynamicMoveProvider.gameObject.SetActive(flyEnabled);

    }

}
