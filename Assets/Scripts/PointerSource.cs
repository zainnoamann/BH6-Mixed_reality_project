using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// One place that answers "where is the user pointing" and "did they press select",
/// whether the answer comes from a mouse or from a Quest controller.
///
/// Put this on the same object as MouseObjectSelector. Drag the right hand controller
/// from the XR Origin into Right Hand, and the headset camera into View Camera.
///
/// On desktop nothing changes: the ray comes from the mouse and select is left click.
/// On the headset the ray comes out of the controller and select is the trigger.
/// </summary>
public class PointerSource : MonoBehaviour
{
    public static PointerSource Instance { get; private set; }

    [Header("XR")]
    [Tooltip("The right hand controller transform inside XR Origin.")]
    [SerializeField] private Transform rightHand;

    [Tooltip("Force XR mode even when no headset is detected. For testing only.")]
    [SerializeField] private bool forceXr;

    [Header("Shared")]
    [SerializeField] private Camera viewCamera;

    private int lastFrame = -1;

    private bool triggerNow;
    private bool triggerLast;
    private bool gripNow;
    private bool gripLast;

    private void Awake()
    {
        Instance = this;

        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }

        Configure(viewCamera);
    }

    public static PointerSource Resolve()
    {
        if (Instance == null)
        {
            Instance = FindFirstObjectByType<PointerSource>();
        }

        return Instance;
    }

    /// <summary>True when a headset is connected and being worn.</summary>
    public bool UsingXr
    {
        get
        {
            if (forceXr)
                return true;

            UnityEngine.XR.InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);

            return head.isValid && rightHand != null;
        }
    }

    public Camera ViewCamera
    {
        get
        {
            if (viewCamera == null)
            {
                viewCamera = Camera.main;
            }

            return viewCamera;
        }
    }

    /// <summary>The controller transform, so callers can read its rotation.</summary>
    public Transform Hand => rightHand;

    public void Configure(Camera camera)
    {
        if (camera != null)
        {
            viewCamera = camera;
        }

        Transform rigRoot = viewCamera != null
            ? viewCamera.transform.root
            : null;

        rightHand = FindRightController(rigRoot);

        if (rightHand == null)
        {
            Debug.LogWarning(
                "PointerSource could not find a Right Controller in the active XR rig.",
                this);
        }
    }

    private static Transform FindRightController(Transform rigRoot)
    {
        if (rigRoot != null)
        {
            foreach (Transform candidate in
                     rigRoot.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == "Right Controller")
                {
                    return candidate;
                }
            }
        }

        foreach (Transform candidate in
                 FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (candidate.name == "Right Controller")
            {
                return candidate;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Pointing
    // ------------------------------------------------------------------

    /// <summary>The ray the user is currently pointing along.</summary>
    public bool TryGetRay(out Ray ray)
    {
        ray = default;

        if (UsingXr)
        {
            if (rightHand == null)
                return false;

            ray = new Ray(rightHand.position, rightHand.forward);
            return true;
        }

        if (Mouse.current == null || ViewCamera == null)
            return false;

        ray = ViewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        return true;
    }

    // ------------------------------------------------------------------
    // Select: left click on desktop, trigger on the headset
    // ------------------------------------------------------------------

    public bool SelectPressed
    {
        get
        {
            Refresh();

            return UsingXr
                ? triggerNow && !triggerLast
                : Mouse.current != null &&
                  Mouse.current.leftButton.wasPressedThisFrame;
        }
    }

    public bool SelectHeld
    {
        get
        {
            Refresh();

            return UsingXr
                ? triggerNow
                : Mouse.current != null && Mouse.current.leftButton.isPressed;
        }
    }

    public bool SelectReleased
    {
        get
        {
            Refresh();

            return UsingXr
                ? !triggerNow && triggerLast
                : Mouse.current != null &&
                  Mouse.current.leftButton.wasReleasedThisFrame;
        }
    }

    // ------------------------------------------------------------------
    // Grab: G key on desktop, grip button on the headset
    // ------------------------------------------------------------------

    public bool GrabPressed
    {
        get
        {
            Refresh();

            return UsingXr
                ? gripNow && !gripLast
                : Keyboard.current != null &&
                  Keyboard.current.gKey.wasPressedThisFrame;
        }
    }

    public bool GrabHeld
    {
        get
        {
            Refresh();

            return UsingXr
                ? gripNow
                : Keyboard.current != null && Keyboard.current.gKey.isPressed;
        }
    }

    public bool GrabReleased
    {
        get
        {
            Refresh();

            return UsingXr
                ? !gripNow && gripLast
                : Keyboard.current != null &&
                  Keyboard.current.gKey.wasReleasedThisFrame;
        }
    }

    /// <summary>Escape on desktop, the B button on the headset.</summary>
    public bool CancelPressed
    {
        get
        {
            if (UsingXr)
            {
                UnityEngine.XR.InputDevice hand =
                    InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                bool b = false;

                return hand.isValid &&
                       hand.TryGetFeatureValue(
                           UnityEngine.XR.CommonUsages.secondaryButton, out b) && b;
            }

            return Keyboard.current != null &&
                   Keyboard.current.escapeKey.wasPressedThisFrame;
        }
    }

    // ------------------------------------------------------------------

    private void Refresh()
    {
        if (lastFrame == Time.frameCount)
            return;

        lastFrame = Time.frameCount;

        triggerLast = triggerNow;
        gripLast = gripNow;

        UnityEngine.XR.InputDevice hand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (!hand.isValid)
        {
            triggerNow = false;
            gripNow = false;
            return;
        }

        bool triggerButton =
            hand.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.triggerButton, out bool triggerPressed) &&
            triggerPressed;
        bool triggerAxis =
            hand.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.trigger, out float triggerValue) &&
            triggerValue >= 0.5f;
        bool gripButton =
            hand.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.gripButton, out bool gripPressed) &&
            gripPressed;
        bool gripAxis =
            hand.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.grip, out float gripValue) &&
            gripValue >= 0.5f;

        triggerNow = triggerButton || triggerAxis;
        gripNow = gripButton || gripAxis;
    }
}
