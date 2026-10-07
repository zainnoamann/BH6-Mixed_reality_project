using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;

/// <summary>
/// Makes the user stand on the room's floor in the headset.
///
/// Two things went wrong before:
///   1. The XR Origin did not ask for "Floor" tracking, so the headset could use the
///      place where it was switched on as height zero. Standing up then felt wrong.
///   2. The XR Origin sat at height 0, but after the room was scaled the top of the
///      floor is not exactly at 0.
///
/// This script asks for Floor tracking and moves the XR Origin onto the floor under
/// it. It also writes the room height to the Console, so the team can check that the
/// room has a real-life size (a normal ceiling is about 2.4 to 2.7 metres).
///
/// Added automatically by MouseObjectSelector in the headset scene.
/// </summary>
public class XrFloorAligner : MonoBehaviour
{
    public string roomRootName = "Model";

    [Tooltip("Ask the headset to measure height from the real floor.")]
    public bool useFloorTracking = true;

    [Tooltip("Move the XR Origin up or down onto the room's floor at start.")]
    public bool snapToFloor = true;

    private IEnumerator Start()
    {
        XROrigin origin = FindFirstObjectByType<XROrigin>();

        if (origin == null)
        {
            yield break; // desktop scene: nothing to do
        }

        if (useFloorTracking)
        {
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        }

        // Wait until InteractionSetup has added the room colliders.
        yield return null;
        yield return null;
        Physics.SyncTransforms();

        if (snapToFloor && TryFindFloor(origin.transform.position, out float floorY))
        {
            CharacterController body = origin.GetComponent<CharacterController>();
            bool hadBody = body != null && body.enabled;

            if (hadBody) body.enabled = false; // it would undo the move otherwise

            Vector3 position = origin.transform.position;
            Debug.Log($"XrFloorAligner: floor is at height {floorY:F2}, XR Origin was at {position.y:F2}.");
            position.y = floorY + 0.01f;
            origin.transform.position = position;

            if (hadBody) body.enabled = true;
        }
        else if (snapToFloor)
        {
            Debug.LogWarning("XrFloorAligner: no floor found under the XR Origin. " +
                             "Is the XR Origin inside the room?");
        }

        if (RoomMetrics.TryMeasure(gameObject.scene, roomRootName, out Bounds room))
        {
            Debug.Log($"XrFloorAligner: the room is {room.size.y:F2} m high, " +
                      $"{room.size.x:F2} m by {room.size.z:F2} m wide. " +
                      "A real room is about 2.4 to 2.7 m high. If this number is much bigger, " +
                      "the furniture will feel too big and you will feel small.");
        }
    }

    /// <summary>Looks straight down through the XR Origin for the floor.</summary>
    private static bool TryFindFloor(Vector3 around, out float floorY)
    {
        floorY = 0f;

        Ray down = new Ray(new Vector3(around.x, around.y + 50f, around.z), Vector3.down);
        RaycastHit[] hits = Physics.RaycastAll(down, 100f, ~0, QueryTriggerInteraction.Ignore);

        bool found = false;
        bool foundNamedFloor = false;

        foreach (RaycastHit hit in hits)
        {
            if (hit.normal.y < 0.7f)
            {
                continue; // not a surface you can stand on
            }

            bool namedFloor = IsFloor(hit.collider.transform);

            // A piece called "floor" always wins. Otherwise take the lowest surface,
            // so we do not end up standing on a table or on the roof.
            if (namedFloor && !foundNamedFloor)
            {
                floorY = hit.point.y;
                foundNamedFloor = true;
                found = true;
            }
            else if (namedFloor == foundNamedFloor && (!found || hit.point.y < floorY))
            {
                floorY = hit.point.y;
                found = true;
            }
        }

        return found;
    }

    private static bool IsFloor(Transform piece)
    {
        for (Transform t = piece; t != null; t = t.parent)
        {
            if (t.name.ToLowerInvariant().Contains("floor"))
            {
                return true;
            }
        }

        return false;
    }
}
