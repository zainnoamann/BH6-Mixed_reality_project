using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Swings a door open when the player comes near and closes it after they leave.
///
/// Every room door (Door_1 .. Door_11, Door_Front) gets one by itself after the scene
/// loads; nothing needs adding to the scene. Each door is a frame (a lining around the
/// opening, which stays put) and a panel with handles on both faces. The panel turns on a
/// hinge at the edge away from the handles, and always swings away from the player.
///
/// Works for the desktop Player and the headset alike: it follows the main camera.
/// </summary>
public class AutoDoor : MonoBehaviour
{
    [Tooltip("Horizontal distance from the doorway at which the door opens, in metres.")]
    [SerializeField] private float openDistance = 1.8f;

    [Tooltip("Extra distance before an open door closes again, so it doesn't flicker at the edge.")]
    [SerializeField] private float closeMargin = 0.4f;

    [SerializeField] private float openAngle = 90f;

    [Tooltip("Degrees per second.")]
    [SerializeField] private float swingSpeed = 200f;

    private static readonly Regex DoorName = new Regex(@"^Door_(\d+|Front)$");

    private Transform hinge;
    private Vector3 doorway;      // centre of the opening, at floor height
    private Vector3 swingTangent; // where the free edge goes when the angle increases
    private float doorHeight;
    private float angle;
    private float targetAngle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToDoors()
    {
        foreach (Transform t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (DoorName.IsMatch(t.name) && t.GetComponent<AutoDoor>() == null)
            {
                t.gameObject.AddComponent<AutoDoor>();
            }
        }
    }

    private void Start()
    {
        Transform panel = FindPanel();
        if (panel == null || !TryBounds(panel, out Bounds bounds))
        {
            Debug.LogWarning("AutoDoor: no door panel found under " + name + ".");
            enabled = false;
            return;
        }

        // The panel is thin along one horizontal axis and wide along the other.
        bool wideAlongX = bounds.size.x >= bounds.size.z;
        Vector3 across = wideAlongX ? Vector3.right : Vector3.forward;

        // Hinge on the edge furthest from the handles.
        Vector3 handles = HandleCentre(panel, bounds.center);
        float side = Vector3.Dot(handles - bounds.center, across) >= 0f ? -1f : 1f;
        Vector3 hingePoint = bounds.center + across * (side * Vector3.Dot(bounds.extents, across));
        hingePoint.y = bounds.min.y;

        hinge = new GameObject("Hinge").transform;
        hinge.SetParent(transform, false);
        hinge.position = hingePoint;
        hinge.rotation = Quaternion.identity;
        panel.SetParent(hinge, true);

        doorway = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        doorHeight = bounds.size.y;

        Vector3 arm = bounds.center - hingePoint;
        arm.y = 0f;
        swingTangent = (Quaternion.Euler(0f, 90f, 0f) * arm).normalized;
    }

    private void Update()
    {
        Camera viewer = Camera.main;
        if (viewer == null)
            return;

        Vector3 eye = viewer.transform.position;
        Vector3 flat = eye - doorway;
        flat.y = 0f;
        float distance = flat.magnitude;

        // Same storey only: the eye is between the door's floor and a little above its top.
        bool sameFloor = eye.y > doorway.y - 0.5f && eye.y < doorway.y + doorHeight + 0.6f;

        if (sameFloor && distance < openDistance)
        {
            if (Mathf.Approximately(targetAngle, 0f))
            {
                // Swing away from the player.
                targetAngle = Vector3.Dot(swingTangent, flat) > 0f ? -openAngle : openAngle;
            }
        }
        else if (!sameFloor || distance > openDistance + closeMargin)
        {
            targetAngle = 0f;
        }

        if (!Mathf.Approximately(angle, targetAngle))
        {
            angle = Mathf.MoveTowards(angle, targetAngle, swingSpeed * Time.deltaTime);
            hinge.localRotation = Quaternion.Euler(0f, angle, 0f);
        }
    }

    /// <summary>The child with the handles: it has more parts than the one-piece frame.</summary>
    private Transform FindPanel()
    {
        Transform best = null;
        int bestCount = 1;

        foreach (Transform child in transform)
        {
            int count = child.GetComponentsInChildren<MeshFilter>(true).Length;
            if (count > bestCount)
            {
                best = child;
                bestCount = count;
            }
        }

        return best;
    }

    private static Vector3 HandleCentre(Transform panel, Vector3 fallback)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;

        foreach (Renderer renderer in panel.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.bounds.size.magnitude < 0.35f)
            {
                sum += renderer.bounds.center;
                count++;
            }
        }

        return count > 0 ? sum / count : fallback;
    }

    private static bool TryBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool found = false;

        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }
}
