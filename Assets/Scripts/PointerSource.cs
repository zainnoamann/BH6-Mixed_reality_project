using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// One place that answers "where is the user pointing" and "did they press select",
/// whether the answer comes from a mouse or from a Quest controller.
///
/// Nothing has to be wired in the scene: Resolve() creates one if the scene has none,
/// and it finds the "Right Controller" of the XR Origin by name.
///
/// Headset buttons:
///   Trigger (right)  - select / place / confirm
///   Grip (right)     - pick up the object you point at
///   A (right)        - step up   (rotate right, or bigger while resizing)
///   X (left)         - step down (rotate left, or smaller while resizing)
///   B (right)        - cancel / deselect
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
    private bool stepUpNow;
    private bool stepUpLast;
    private bool stepDownNow;
    private bool stepDownLast;
    private bool cancelNow;
    private bool cancelLast;

    private float nextSearchTime;
    private int uiFrame = -1;
    private bool uiResult;

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

        if (Instance == null)
        {
            // No one added it to the scene: make one, so desktop and VR scenes both work.
            Instance = new GameObject("PointerSource").AddComponent<PointerSource>();
        }

        return Instance;
    }

    private void Update()
    {
        // Read the buttons every frame, so "pressed this frame" is always correct
        // even when nobody asked last frame.
        Refresh();
    }

    /// <summary>True when a headset is connected and being worn.</summary>
    public bool UsingXr
    {
        get
        {
            if (forceXr)
                return true;

            UnityEngine.XR.InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);

            if (!head.isValid)
                return false;

            if (rightHand == null && Time.unscaledTime >= nextSearchTime)
            {
                // The rig may have been enabled after Awake; look again, once a second.
                nextSearchTime = Time.unscaledTime + 1f;
                Configure(Camera.main);
            }

            return rightHand != null;
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

    /// <summary>Escape on desktop, the B button on the headset (once per press).</summary>
    public bool CancelPressed
    {
        get
        {
            Refresh();

            return UsingXr
                ? cancelNow && !cancelLast
                : Keyboard.current != null &&
                  Keyboard.current.escapeKey.wasPressedThisFrame;
        }
    }

    /// <summary>Headset only: A on the right controller. Rotate right / make bigger.</summary>
    public bool StepUpPressed
    {
        get
        {
            Refresh();
            return UsingXr && stepUpNow && !stepUpLast;
        }
    }

    /// <summary>Headset only: X on the left controller. Rotate left / make smaller.</summary>
    public bool StepDownPressed
    {
        get
        {
            Refresh();
            return UsingXr && stepDownNow && !stepDownLast;
        }
    }

    // ------------------------------------------------------------------
    // UI: is the controller ray on a menu or button?
    // ------------------------------------------------------------------

    /// <summary>
    /// True when the controller ray points at a UI element of a world-space canvas.
    /// EventSystem.IsPointerOverGameObject() only knows about the mouse, so in the
    /// headset a trigger press on a button would also select the furniture behind it.
    /// </summary>
    public bool RayOverUi()
    {
        if (uiFrame == Time.frameCount)
            return uiResult;

        uiFrame = Time.frameCount;
        uiResult = ComputeRayOverUi();
        return uiResult;
    }

    private bool ComputeRayOverUi()
    {
        if (!TryGetRay(out Ray ray))
            return false;

        foreach (Canvas canvas in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (!canvas.isRootCanvas || canvas.renderMode != RenderMode.WorldSpace)
                continue;

            Plane plane = new Plane(canvas.transform.forward, canvas.transform.position);

            if (!plane.Raycast(ray, out float distance))
                continue;

            Vector3 world = ray.GetPoint(distance);

            foreach (Graphic graphic in canvas.GetComponentsInChildren<Graphic>(false))
            {
                if (!graphic.raycastTarget || !graphic.isActiveAndEnabled)
                    continue;

                RectTransform rect = graphic.rectTransform;
                Vector2 local = rect.InverseTransformPoint(world);

                if (rect.rect.Contains(local))
                    return true;
            }
        }

        return false;
    }

    // ------------------------------------------------------------------

    private void Refresh()
    {
        if (lastFrame == Time.frameCount)
            return;

        lastFrame = Time.frameCount;

        triggerLast = triggerNow;
        gripLast = gripNow;
        stepUpLast = stepUpNow;
        stepDownLast = stepDownNow;
        cancelLast = cancelNow;

        UnityEngine.XR.InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        stepDownNow = left.isValid &&
                      left.TryGetFeatureValue(
                          UnityEngine.XR.CommonUsages.primaryButton, out bool xPressed) &&
                      xPressed;

        UnityEngine.XR.InputDevice hand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (!hand.isValid)
        {
            triggerNow = false;
            gripNow = false;
            stepUpNow = false;
            cancelNow = false;
            return;
        }

        stepUpNow =
            hand.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.primaryButton, out bool aPressed) &&
            aPressed;
        cancelNow =
            hand.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.secondaryButton, out bool bPressed) &&
            bPressed;

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
