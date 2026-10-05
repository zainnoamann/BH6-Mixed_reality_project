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
///   Y (left)         - step up   (rotate right, or bigger while resizing)
///   X (left)         - step down (rotate left, or smaller while resizing)
///
/// A on the right controller is left alone: the XR rig uses it for Jump.
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

    [Header("Headset pointer")]
    [Tooltip("Draw a line and a dot from the right controller.")]
    [SerializeField] private bool showPointer = true;

    [SerializeField] private Color pointerColour = new Color(0.3f, 0.9f, 1f, 1f);

    [SerializeField] private float pointerLength = 8f;

    [Tooltip("How strongly hand shake is smoothed. Lower = steadier but slower to follow.")]
    [SerializeField] private float aimSmoothing = 18f;

    private Transform xriOrigin;
    private float nextOriginSearch;
    private int aimFrame = -1;
    private bool aimStarted;
    private Vector3 aimPosition;
    private Vector3 aimDirection = Vector3.forward;

    private LineRenderer pointerLine;
    private Transform pointerDot;
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

    private void LateUpdate()
    {
        DrawPointer();
    }

    // ------------------------------------------------------------------
    // Aim: a steady ray, and a visible pointer
    // ------------------------------------------------------------------

    /// <summary>
    /// Works out the pointing ray once per frame.
    ///
    /// A hand is never perfectly still, and a small shake at the wrist becomes a big jump
    /// two metres away. So the ray is smoothed a little. When XR Interaction Toolkit has
    /// made its own stabilised ray origin (the one its visible UI ray uses), that is used
    /// instead, so this pointer and the UI ray point at exactly the same place.
    /// </summary>
    private void UpdateAim()
    {
        if (aimFrame == Time.frameCount)
            return;

        aimFrame = Time.frameCount;

        Vector3 position = rightHand.position;
        Vector3 direction = rightHand.forward;

        if (xriOrigin == null && Time.unscaledTime >= nextOriginSearch)
        {
            nextOriginSearch = Time.unscaledTime + 1f;
            GameObject found = GameObject.Find("[Right CurveInteractionCaster] Stabilization Cast Origin");
            xriOrigin = found != null ? found.transform : null;
        }

        // Only trust it while it really sits at the hand; otherwise it is stale.
        if (xriOrigin != null && xriOrigin.gameObject.activeInHierarchy &&
            (xriOrigin.position - rightHand.position).sqrMagnitude < 0.25f)
        {
            position = xriOrigin.position;
            direction = xriOrigin.forward;
        }

        if (!aimStarted)
        {
            aimStarted = true;
            aimPosition = position;
            aimDirection = direction;
            return;
        }

        float blend = 1f - Mathf.Exp(-aimSmoothing * Time.unscaledDeltaTime);
        aimPosition = Vector3.Lerp(aimPosition, position, blend);
        aimDirection = Vector3.Slerp(aimDirection, direction, blend).normalized;
    }

    /// <summary>
    /// A thin line from the controller with a dot where it lands, so it is clear what
    /// the trigger will act on. Hidden on desktop, and while the ray is on the menu
    /// (XR Interaction Toolkit draws its own ray there).
    /// </summary>
    private void DrawPointer()
    {
        bool show = showPointer && UsingXr && TryGetRay(out Ray ray) && !RayOverUi();

        if (!show)
        {
            if (pointerLine != null) pointerLine.enabled = false;
            if (pointerDot != null) pointerDot.gameObject.SetActive(false);
            return;
        }

        if (pointerLine == null)
        {
            BuildPointer();
        }

        TryGetRay(out Ray aim);

        Vector3 end = aim.origin + aim.direction * pointerLength;
        bool hitSomething = Physics.Raycast(aim, out RaycastHit hit, pointerLength, ~0,
                                            QueryTriggerInteraction.Ignore);

        if (hitSomething)
        {
            end = hit.point;
        }

        pointerLine.enabled = true;
        pointerLine.SetPosition(0, aim.origin);
        pointerLine.SetPosition(1, end);

        pointerDot.gameObject.SetActive(hitSomething);
        pointerDot.position = end;

        // Keep the dot the same size to the eye, near or far.
        float distance = Vector3.Distance(aim.origin, end);
        pointerDot.localScale = Vector3.one * Mathf.Clamp(distance * 0.015f, 0.01f, 0.06f);
    }

    private void BuildPointer()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        Material material = new Material(shader);
        material.color = pointerColour;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", pointerColour);

        GameObject lineObject = new GameObject("PointerLine");
        lineObject.transform.SetParent(transform, false);

        pointerLine = lineObject.AddComponent<LineRenderer>();
        pointerLine.useWorldSpace = true;
        pointerLine.positionCount = 2;
        pointerLine.startWidth = 0.006f;
        pointerLine.endWidth = 0.003f;
        pointerLine.material = material;
        pointerLine.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pointerLine.receiveShadows = false;

        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.name = "PointerDot";
        dot.transform.SetParent(transform, false);

        // No collider: the dot must never block the ray it is showing.
        Collider dotCollider = dot.GetComponent<Collider>();
        if (dotCollider != null) Destroy(dotCollider);

        Renderer dotRenderer = dot.GetComponent<Renderer>();
        dotRenderer.sharedMaterial = material;
        dotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dotRenderer.receiveShadows = false;

        pointerDot = dot.transform;
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

            UpdateAim();
            ray = new Ray(aimPosition, aimDirection);
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

    /// <summary>Headset only: Y on the left controller. Rotate right / make bigger.</summary>
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

        // Y, not A: the XR rig's Jump action is bound to A, so A made the player hop.
        stepUpNow = left.isValid &&
                    left.TryGetFeatureValue(
                        UnityEngine.XR.CommonUsages.secondaryButton, out bool yPressed) &&
                    yPressed;

        UnityEngine.XR.InputDevice hand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (!hand.isValid)
        {
            triggerNow = false;
            gripNow = false;
            cancelNow = false;
            return;
        }

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
