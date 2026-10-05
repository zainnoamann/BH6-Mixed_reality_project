using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Keeps the world-space menu canvas comfortable to use in the headset.
///
///   - It floats in the room instead of being glued to the head, so you can turn
///     your head to look at a button.
///   - When you turn or walk away, it glides back in front of you.
///   - It sits a little below eye level and is drawn bigger than before.
///   - It never goes through a wall: near a wall it comes closer to you instead.
///   - It is drawn on top of the room, so even when an edge does reach a wall or a
///     piece of furniture, the buttons stay visible instead of disappearing behind it.
///
/// Added automatically to the world-space canvas by MouseObjectSelector. It only
/// acts in the headset; on desktop the canvas is left exactly as it is in the scene.
/// The numbers below are the ones to tune if the menu feels too big, small, near or far.
/// </summary>
public class VrMenuPlacer : MonoBehaviour
{
    [Tooltip("Metres in front of the eyes.")]
    public float distance = 1.2f;

    [Tooltip("Metres above (+) or below (-) eye level for the middle of the menu.")]
    public float heightOffset = -0.15f;

    [Tooltip("Canvas scale. 0.001 = 1 canvas unit is 1 mm. Bigger number = bigger menu.")]
    public float canvasScale = 0.0013f;

    [Tooltip("The menu comes back in front of you after you turn this many degrees away.")]
    public float followAngle = 40f;

    [Tooltip("Gap kept between the middle of the menu and the walls, in metres.")]
    public float wallMargin = 0.3f;

    [Tooltip("How fast it glides back. Higher = faster.")]
    public float followSpeed = 6f;

    [Tooltip("Name of the room root, used to find the walls.")]
    public string roomRootName = "Model";

    private Camera head;
    private bool started;
    private bool moving;
    private Bounds room;
    private bool roomKnown;
    private Vector3 lastForward = Vector3.forward;

    private readonly HashSet<Graphic> onTop = new HashSet<Graphic>();
    private Material onTopMaterial;
    private float nextOnTopCheck;
    private static readonly int ZTestMode = Shader.PropertyToID("unity_GUIZTestMode");

    private void LateUpdate()
    {
        PointerSource pointer = PointerSource.Resolve();

        if (pointer == null || !pointer.UsingXr)
        {
            return; // desktop: leave the canvas alone
        }

        if (head == null)
        {
            head = Camera.main;

            if (head == null)
            {
                return;
            }
        }

        if (!started)
        {
            Begin();
        }

        if (Time.unscaledTime >= nextOnTopCheck)
        {
            // Again every half second: the toolbar, keyboard and panels appear later.
            nextOnTopCheck = Time.unscaledTime + 0.5f;
            DrawOnTop();
        }

        Vector3 eye = head.transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.01f)
        {
            forward = lastForward; // looking straight up or down: keep the last direction
        }
        else
        {
            forward.Normalize();
            lastForward = forward;
        }

        Vector3 wanted = ClampToRoom(eye + forward * distance, eye);
        wanted.y = eye.y + heightOffset;

        Vector3 toMenu = Vector3.ProjectOnPlane(transform.position - eye, Vector3.up);
        float gap = toMenu.magnitude;

        bool lookedAway = gap < 0.05f || Vector3.Angle(forward, toMenu) > followAngle;
        bool wrongDistance = gap < distance * 0.4f || gap > distance * 1.7f;
        bool throughWall = roomKnown && !InsideRoom(transform.position, wallMargin * 0.5f) &&
                           InsideRoom(eye, 0f);

        if (lookedAway || wrongDistance || throughWall)
        {
            moving = true;
        }

        if (moving)
        {
            float blend = 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
            transform.position = Vector3.Lerp(transform.position, wanted, blend);

            if ((transform.position - wanted).sqrMagnitude < 0.0004f)
            {
                moving = false; // arrived: rest here until the user looks away again
            }
        }

        // Always face the user. A canvas is readable when its forward points away from the eyes.
        Vector3 away = Vector3.ProjectOnPlane(transform.position - eye, Vector3.up);

        if (away.sqrMagnitude > 0.0001f)
        {
            transform.rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
        }
    }

    /// <summary>First frame in the headset: unglue from the head and jump into place.</summary>
    private void Begin()
    {
        started = true;

        // If the scene uses XR Interaction Toolkit's Lazy Follow on this canvas, the two
        // would fight over the position. Looked up by name so this file needs no XRI reference.
        Behaviour lazyFollow = GetComponent("LazyFollow") as Behaviour;
        if (lazyFollow != null)
        {
            lazyFollow.enabled = false;
        }

        transform.SetParent(null, true);
        transform.localScale = Vector3.one * canvasScale;

        roomKnown = RoomMetrics.TryMeasure(gameObject.scene, roomRootName, out room);

        Vector3 eye = head.transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up);
        forward = forward.sqrMagnitude < 0.01f ? Vector3.forward : forward.normalized;
        lastForward = forward;

        Vector3 start = ClampToRoom(eye + forward * distance, eye);
        start.y = eye.y + heightOffset;
        transform.position = start;
    }

    /// <summary>
    /// Makes every image and text of the menu ignore the depth of the room, so walls and
    /// furniture can never hide it. UI shaders read their depth test from the
    /// unity_GUIZTestMode value; setting it to Always on the material does this.
    /// </summary>
    private void DrawOnTop()
    {
        foreach (Graphic graphic in GetComponentsInChildren<Graphic>(true))
        {
            if (graphic == null || !onTop.Add(graphic))
            {
                continue;
            }

            if (graphic is TMP_Text text)
            {
                // fontMaterial is this text's own copy, so the shared font asset is not changed.
                text.fontMaterial.SetInt(ZTestMode, (int)CompareFunction.Always);
                continue;
            }

            if (graphic.material != Graphic.defaultGraphicMaterial)
            {
                continue; // a custom material: leave it alone
            }

            if (onTopMaterial == null)
            {
                onTopMaterial = new Material(Graphic.defaultGraphicMaterial);
                onTopMaterial.SetInt(ZTestMode, (int)CompareFunction.Always);
            }

            graphic.material = onTopMaterial;
        }
    }

    /// <summary>Keeps the point inside the walls. Skipped when the user is outside the room.</summary>
    private Vector3 ClampToRoom(Vector3 point, Vector3 eye)
    {
        if (!roomKnown || !InsideRoom(eye, 0f))
        {
            return point;
        }

        float minX = room.min.x + wallMargin;
        float maxX = room.max.x - wallMargin;
        float minZ = room.min.z + wallMargin;
        float maxZ = room.max.z - wallMargin;

        point.x = Mathf.Clamp(point.x, minX, Mathf.Max(minX, maxX));
        point.z = Mathf.Clamp(point.z, minZ, Mathf.Max(minZ, maxZ));

        return point;
    }

    private bool InsideRoom(Vector3 point, float margin)
    {
        return point.x > room.min.x + margin && point.x < room.max.x - margin &&
               point.z > room.min.z + margin && point.z < room.max.z - margin;
    }
}
