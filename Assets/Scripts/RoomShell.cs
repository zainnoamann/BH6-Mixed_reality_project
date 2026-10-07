/// <summary>
/// The parts of the room that are not furniture: walls, floor, ceiling, windows, doors,
/// railings.
///
/// They are locked: they cannot be selected, moved, or block a placement.
/// Matching is by name, the same rule FurnitureDragger used before, so it keeps
/// working when the room model is swapped for another export.
/// </summary>
public static class RoomShell
{
    private static readonly string[] FixedParts =
    {
        "wall", "floor", "ceiling", "window", "door", "roof", "railing"
    };

    public static bool IsFixed(string objectName)
    {
        if (string.IsNullOrEmpty(objectName))
        {
            return false;
        }

        string lower = objectName.ToLowerInvariant();

        foreach (string part in FixedParts)
        {
            if (lower.Contains(part))
            {
                return true;
            }
        }

        return false;
    }
}
