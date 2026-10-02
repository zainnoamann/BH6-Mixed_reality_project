using System.Collections.Generic;

/// <summary>
/// Real-world heights for common furniture, so a generated "vase" starts vase-sized
/// and a "wardrobe" starts wardrobe-sized, instead of everything being the same height.
///
/// The longest matching word wins, so "table lamp" beats "table" and "lamp".
/// Heights are in metres. Add more rows freely.
/// </summary>
public static class TypicalSizes
{
    private static readonly Dictionary<string, float> Heights = new Dictionary<string, float>
    {
        // seating
        { "chair", 0.9f }, { "armchair", 1.0f }, { "office chair", 1.1f }, { "stool", 0.45f },
        { "bar stool", 0.75f }, { "sofa", 0.85f }, { "couch", 0.85f }, { "bench", 0.45f },
        { "ottoman", 0.42f }, { "beanbag", 0.7f },

        // tables and storage
        { "table", 0.75f }, { "desk", 0.75f }, { "coffee table", 0.45f }, { "side table", 0.55f },
        { "nightstand", 0.55f }, { "bedside table", 0.55f }, { "dresser", 0.85f },
        { "cabinet", 1.0f }, { "bookshelf", 1.8f }, { "bookcase", 1.8f }, { "shelf", 1.6f },
        { "wardrobe", 2.0f }, { "drawer", 0.8f }, { "tv stand", 0.5f },

        // beds
        { "bed", 0.6f }, { "crib", 0.9f },

        // lighting
        { "lamp", 1.5f }, { "floor lamp", 1.6f }, { "table lamp", 0.5f }, { "desk lamp", 0.45f },
        { "lantern", 0.35f }, { "candle", 0.2f },

        // decor
        { "vase", 0.4f }, { "flower", 0.45f }, { "plant", 0.8f }, { "potted plant", 0.8f },
        { "cactus", 0.35f }, { "pot", 0.35f }, { "sculpture", 0.5f }, { "statue", 0.6f },
        { "clock", 0.3f }, { "mirror", 1.2f }, { "painting", 0.6f }, { "frame", 0.35f },
        { "cushion", 0.4f }, { "pillow", 0.4f }, { "basket", 0.35f }, { "bowl", 0.1f },
        { "mug", 0.1f }, { "cup", 0.1f }, { "book", 0.25f }, { "globe", 0.4f },

        // electronics
        { "tv", 0.7f }, { "television", 0.7f }, { "monitor", 0.45f }, { "laptop", 0.25f },
        { "speaker", 0.4f }, { "fan", 1.2f },
    };

    /// <summary>Height in metres for the best matching word in the prompt, or -1 if none match.</summary>
    public static float HeightFor(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return -1f;
        }

        string lower = " " + prompt.ToLowerInvariant() + " ";
        string best = null;

        foreach (string key in Heights.Keys)
        {
            // Match whole words only, and the plural ("chairs"): "potato" is not "pot".
            bool found = lower.Contains(" " + key + " ") || lower.Contains(" " + key + "s ");

            if (found && (best == null || key.Length > best.Length))
            {
                best = key;
            }
        }

        return best != null ? Heights[best] : -1f;
    }

    /// <summary>
    /// Scene units per metre, from the room's ceiling height. A normal room is 2 to 4.5 m
    /// tall, so in that range the scene is already in metres. Otherwise the room was
    /// exported at another scale, and 2.6 m is assumed as its real ceiling height.
    /// </summary>
    public static float UnitsPerMetre(float ceilingUnits)
    {
        if (ceilingUnits <= 0f)
        {
            return 1f;
        }

        return ceilingUnits >= 2f && ceilingUnits <= 4.5f ? 1f : ceilingUnits / 2.6f;
    }
}
