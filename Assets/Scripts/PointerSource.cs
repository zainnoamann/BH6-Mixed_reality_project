using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR;

/// <summary>
/// One place that answers "where is the user pointing" and "did they press select",
/// whether the answer comes from a mouse or from a Quest controller.
///
/// Nothing has to be wired in the scene: Resolve() creates one if the scene has none,
/// and it finds the "Right Controller" and "Left Controller" of the XR Origin by name.
///
/// Both controllers draw the same blue pointer (a line with a dot where it lands).
/// Either hand can point: the hand that last pulled its trigger or grip is the active
/// one, and select / place / pick up follow that hand.
///
/// Headset buttons:
///   Trigger (either hand)  - select / place / confirm
///   Grip (either hand)     - pick up the object you point at
///   Y (left)               - step up   (rotate right, or bigger while resizing)
///   X (left)               - step down (rotate left, or smaller while resizing)
///   B (right)              - cancel / deselect
///
/// A on the right controller is left alone: the XR rig uses it for Jump.
///
/// On desktop nothing changes: the ray comes from the mouse and select is left click.
/// </summary>
public class PointerSource : MonoBehaviour
{
    public static PointerSource Instance { get; private set; }

    [Header("XR")]
    [Tooltip("The right hand controller transform inside XR Origin. Found by name when empty.")]
    [SerializeField] private Transform rightHand;

    [Tooltip("The left hand controller transform inside XR Origin. Found by name when empty.")]
    [SerializeField] private Transform leftHand;

    [Tooltip("Force XR mode even when no headset is detected. For testing only.")]
    [SerializeField] private bool forceXr;

    [Header("Shared")]
    [SerializeField] private Camera viewCamera;

    [Header("Headset pointer")]
    [Tooltip("Draw a line and a dot from each controller.")]
    [SerializeField] private bool showPointer = true;

    [SerializeField] private Color pointerColour = new Color(0.3f, 0.9f, 1f, 1f);

    [SerializeField] private float pointerLength = 8f;

    [Tooltip("Hide XR Interaction Toolkit's own white ray on both controllers, so only " +
             "this pointer shows. Turn on only if both rays land on the same spot.")]
    [SerializeField] private bool hideToolkitRay;

    [Tooltip("How strongly hand shake is smoothed. Lower = steadier but slower to follow.")]
    [SerializeField] private float aimSmoothing = 18f;

    /// <summary>Everything that exists once per controller.</summary>
    private class HandState
    {
        public XRNode node;
        public string controllerName;
        public string originName;

        public Transform transform;
        public Transform xriOrigin;
        public float nextOriginSearch;

        public int aimFrame = -1;
        public bool aimStarted;
        public Vector3 aimPosition;
        public Vector3 aimDirection = Vector3.forward;

        public bool triggerNow;
        public bool triggerLast;
        public bool gripNow;
        public bool gripLast;

        public LineRenderer line;
        public Transform dot;
    }

    private readonly HandState right = new HandState
    {
        node = XRNode.RightHand,
        controllerName = "Right Controller",
        originName = "[Right CurveInteractionCaster] Stabilization Cast Origin",
    };

    private readonly HandState left = new HandState
    {
        node = XRNode.LeftHand,
        controllerName = "Left Controller",
        originName = "[Left CurveInteractionCaster] Stabilization Cast Origin",
    };

    /// <summary>The hand whose ray and trigger the app is listening to right now.</summary>
    private HandState active;

    private int lastFrame = -1;

    private bool stepUpNow;
    private bool stepUpLast;
    private bool stepDownNow;
    private bool stepDownLast;
    private bool cancelNow;
    private bool cancelLast;

    private float nextSearchTime;
    private float nextHideTime;
    private Material pointerMaterial;

    private int uiFrame = -1;
    private bool uiResult;

    private void Awake()
    {
        Instance = this;
        active = right;

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
        bool xr = showPointer && UsingXr;

        DrawPointer(right, xr);
        DrawPointer(left, xr);

        if (xr && hideToolkitRay)
        {
            HideToolkitRay();
        }
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

            if ((right.transform == null || left.transform == null) &&
                Time.unscaledTime >= nextSearchTime)
            {
                // The rig may have been enabled after Awake; look again, once a second.
                nextSearchTime = Time.unscaledTime + 1f;
                Configure(Camera.main);
            }

            return right.transform != null;
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

    /// <summary>The active controller transform, so callers can read its rotation.</summary>
    public Transform Hand => Active.transform;

    private HandState Active => active ?? right;

    public void Configure(Camera camera)
    {
        if (camera != null)
        {
            viewCamera = camera;
        }

        Transform rigRoot = viewCamera != null
            ? viewCamera.transform.root
            : null;

        right.transform = rightHand != null ? rightHand : FindController(rigRoot, right.controllerName);
        left.transform = leftHand != null ? leftHand : FindController(rigRoot, left.controllerName);

        if (right.transform == null)
        {
            Debug.LogWarning(
                "PointerSource could not find a Right Controller in the active XR rig.",
                this);
        }
    }

    private static Transform FindController(Transform rigRoot, string controllerName)
    {
        if (rigRoot != null)
        {
            foreach (Transform candidate in
                     rigRoot.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name == controllerName)
                {
                    return candidate;
                }
            }
        }

        foreach (Transform candidate in
                 FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (candidate.name == controllerName)
            {
                return candidate;
            }
        }

        return null;
    }

    // ------------------------------------------------------------------
    // Pointing
    // ------------------------------------------------------------------

    /// <summary>The ray the user is currently pointing along (the active hand in the headset).</summary>
    public bool TryGetRay(out Ray ray)
    {
        ray = default;

        if (UsingXr)
        {
            return TryGetHandRay(Active, out ray);
        }

        if (Mouse.current == null || ViewCamera == null)
            return false;

        ray = ViewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        return true;
    }

    private bool TryGetHandRay(HandState hand, out Ray ray)
    {
        ray = default;

        if (hand.transform == null || !hand.transform.gameObject.activeInHierarchy)
            return false;

        UpdateAim(hand);
        ray = new Ray(hand.aimPosition, hand.aimDirection);
        return true;
    }

    /// <summary>
    /// Works out one hand's pointing ray, once per frame.
    ///
    /// A hand is never perfectly still, and a small shake at the wrist becomes a big jump
    /// two metres away. So the ray is smoothed a little. When XR Interaction Toolkit has
    /// made its own stabilised ray origin (the one its visible UI ray uses), that is used
    /// instead, so this pointer and the UI ray point at exactly the same place.
    /// </summary>
    private void UpdateAim(HandState hand)
    {
        if (hand.aimFrame == Time.frameCount)
            return;

        hand.aimFrame = Time.frameCount;

        Vector3 position = hand.transform.position;
        Vector3 direction = hand.transform.forward;

        if (hand.xriOrigin == null && Time.unscaledTime >= hand.nextOriginSearch)
        {
            hand.nextOriginSearch = Time.unscaledTime + 1f;
            GameObject found = GameObject.Find(hand.originName);
            hand.xriOrigin = found != null ? found.transform : null;
        }

        // Only trust it while it really sits at the hand; otherwise it is stale.
        if (hand.xriOrigin != null && hand.xriOrigin.gameObject.activeInHierarchy &&
            (hand.xriOrigin.position - hand.transform.position).sqrMagnitude < 0.25f)
        {
            position = hand.xriOrigin.position;
            direction = hand.xriOrigin.forward;
        }

        if (!hand.aimStarted)
        {
            hand.aimStarted = true;
            hand.aimPosition = position;
            hand.aimDirection = direction;
            return;
        }

        float blend = 1f - Mathf.Exp(-aimSmoothing * Time.unscaledDeltaTime);
        hand.aimPosition = Vector3.Lerp(hand.aimPosition, position, blend);
        hand.aimDirection = Vector3.Slerp(hand.aimDirection, direction, blend).normalized;
    }

    // ------------------------------------------------------------------
    // The visible pointer
    // ------------------------------------------------------------------

    /// <summary>
    /// A thin line from the controller with a dot where it lands, so it is clear what
    /// the trigger will act on. Shown on the menu as well as in the room.
    /// </summary>
    private void DrawPointer(HandState hand, bool xr)
    {
        if (!xr || !TryGetHandRay(hand, out Ray aim))
        {
            if (hand.line != null) hand.line.enabled = false;
            if (hand.dot != null) hand.dot.gameObject.SetActive(false);
            return;
        }

        if (hand.line == null)
        {
            BuildPointer(hand);
        }

        Vector3 end = aim.origin + aim.direction * pointerLength;
        bool hitSomething;

        if (RayHitsUi(aim, out float uiDistance))
        {
            // The menu is drawn on top of the room, so the pointer stops on the menu.
            end = aim.origin + aim.direction * uiDistance;
            hitSomething = true;
        }
        else
        {
            hitSomething = Physics.Raycast(aim, out RaycastHit hit, pointerLength, ~0,
                                           QueryTriggerInteraction.Ignore);

            if (hitSomething)
            {
                end = hit.point;
            }
        }

        hand.line.enabled = true;
        hand.line.SetPosition(0, aim.origin);
        hand.line.SetPosition(1, end);

        hand.dot.gameObject.SetActive(hitSomething);
        hand.dot.position = end;

        // Keep the dot the same size to the eye, near or far.
        float distance = Vector3.Distance(aim.origin, end);
        hand.dot.localScale = Vector3.one * Mathf.Clamp(distance * 0.015f, 0.01f, 0.06f);
    }

    private void BuildPointer(HandState hand)
    {
        if (pointerMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");

            pointerMaterial = new Material(shader);
            pointerMaterial.color = pointerColour;
            if (pointerMaterial.HasProperty("_BaseColor"))
            {
                pointerMaterial.SetColor("_BaseColor", pointerColour);
            }
        }

        GameObject lineObject = new GameObject(hand.controllerName + " PointerLine");
        lineObject.transform.SetParent(transform, false);

        hand.line = lineObject.AddComponent<LineRenderer>();
        hand.line.useWorldSpace = true;
        hand.line.positionCount = 2;
        hand.line.startWidth = 0.006f;
        hand.line.endWidth = 0.003f;
        hand.line.sharedMaterial = pointerMaterial;
        hand.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        hand.line.receiveShadows = false;

        GameObject dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        dot.name = hand.controllerName + " PointerDot";
        dot.transform.SetParent(transform, false);

        // No collider: the dot must never block the ray it is showing.
        Collider dotCollider = dot.GetComponent<Collider>();
        if (dotCollider != null) Destroy(dotCollider);

        Renderer dotRenderer = dot.GetComponent<Renderer>();
        dotRenderer.sharedMaterial = pointerMaterial;
        dotRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        dotRenderer.receiveShadows = false;

        hand.dot = dot.transform;
    }

    /// <summary>
    /// Optional: switch off XR Interaction Toolkit's own line on the controllers, so only
    /// this pointer is seen. Found by component name, so this file needs no reference to
    /// the toolkit. Clicking the menu still works; only the drawing is hidden.
    /// </summary>
    private void HideToolkitRay()
    {
        if (Time.unscaledTime < nextHideTime)
            return;

        nextHideTime = Time.unscaledTime + 1f;

        HideToolkitRay(right.transform);
        HideToolkitRay(left.transform);
    }

    private static void HideToolkitRay(Transform controller)
    {
        if (controller == null)
            return;

        foreach (Behaviour behaviour in controller.GetComponentsInChildren<Behaviour>(true))
        {
            if (behaviour != null && behaviour.GetType().Name == "CurveVisualController" &&
                behaviour.gameObject.activeSelf)
            {
                behaviour.gameObject.SetActive(false);
            }
        }
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
                ? Active.triggerNow && !Active.triggerLast
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
                ? Active.triggerNow
                : Mouse.current != null && Mouse.current.leftButton.isPressed;
        }
    }

    public bool SelectReleased
    {
        get
        {
            Refresh();

            return UsingXr
                ? !Active.triggerNow && Active.triggerLast
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
                ? Active.gripNow && !Active.gripLast
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
                ? Active.gripNow
                : Keyboard.current != null && Keyboard.current.gKey.isPressed;
        }
    }

    public bool GrabReleased
    {
        get
        {
            Refresh();

            return UsingXr
                ? !Active.gripNow && Active.gripLast
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
    /// True when the active controller ray points at a UI element of a world-space canvas.
    /// EventSystem.IsPointerOverGameObject() only knows about the mouse, so in the
    /// headset a trigger press on a button would also select the furniture behind it.
    /// </summary>
    public bool RayOverUi()
    {
        if (uiFrame == Time.frameCount)
            return uiResult;

        uiFrame = Time.frameCount;
        uiResult = TryGetRay(out Ray ray) && RayHitsUi(ray, out _);
        return uiResult;
    }

    private static bool RayHitsUi(Ray ray, out float hitDistance)
    {
        hitDistance = 0f;

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
                {
                    hitDistance = distance;
                    return true;
                }
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

        ReadHand(right);
        ReadHand(left);

        // The hand that presses becomes the pointing hand. Its press still counts this
        // frame, because its own "last" value is false.
        HandState other = Active == right ? left : right;
        bool otherPressed = (other.triggerNow && !other.triggerLast) ||
                            (other.gripNow && !other.gripLast);
        bool activeBusy = Active.triggerNow || Active.gripNow;

        if (otherPressed && !activeBusy && other.transform != null)
        {
            active = other;
            uiFrame = -1; // the "over the menu" answer was for the other hand
        }

        stepUpLast = stepUpNow;
        stepDownLast = stepDownNow;
        cancelLast = cancelNow;

        UnityEngine.XR.InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        stepDownNow = leftDevice.isValid &&
                      leftDevice.TryGetFeatureValue(
                          UnityEngine.XR.CommonUsages.primaryButton, out bool xPressed) &&
                      xPressed;

        // Y, not A: the XR rig's Jump action is bound to A, so A made the player hop.
        stepUpNow = leftDevice.isValid &&
                    leftDevice.TryGetFeatureValue(
                        UnityEngine.XR.CommonUsages.secondaryButton, out bool yPressed) &&
                    yPressed;

        UnityEngine.XR.InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        cancelNow = rightDevice.isValid &&
                    rightDevice.TryGetFeatureValue(
                        UnityEngine.XR.CommonUsages.secondaryButton, out bool bPressed) &&
                    bPressed;
    }

    private static void ReadHand(HandState hand)
    {
        hand.triggerLast = hand.triggerNow;
        hand.gripLast = hand.gripNow;

        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(hand.node);

        if (!device.isValid)
        {
            hand.triggerNow = false;
            hand.gripNow = false;
            return;
        }

        bool triggerButton =
            device.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.triggerButton, out bool triggerPressed) &&
            triggerPressed;
        bool triggerAxis =
            device.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.trigger, out float triggerValue) &&
            triggerValue >= 0.5f;
        bool gripButton =
            device.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.gripButton, out bool gripPressed) &&
            gripPressed;
        bool gripAxis =
            device.TryGetFeatureValue(
                UnityEngine.XR.CommonUsages.grip, out float gripValue) &&
            gripValue >= 0.5f;

        hand.triggerNow = triggerButton || triggerAxis;
        hand.gripNow = gripButton || gripAxis;
    }
}
