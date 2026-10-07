using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One place for the look of the whole menu. The style follows Meta's UI Set
/// (the look of the Quest system menus):
///
///   dark grey backplate with large rounded corners and a soft fade,
///   pill-shaped buttons: blue Primary, grey Secondary, red Destructive,
///   and Borderless (no background until you point at it),
///   buttons get brighter and a little bigger on hover, smaller when pressed.
///
/// Apply(canvas) restyles the panels that were made in the scene (MenuPanel,
/// SelectionPanel, LoadingPopup ...) when Play starts, so the scene file does
/// not need to be edited by hand. SelectionToolbar and VrKeyboard, which are
/// built in code, use the same look through StylePanel / StyleButton.
///
/// To try another theme, change only the colours below.
/// </summary>
public static class UiTheme
{
    /// <summary>Accent = Primary, Neutral = Secondary, Danger = Destructive in Meta's words.</summary>
    public enum Role { Accent, Neutral, Danger, Key, Borderless }

    // ---- colours: change these to change the whole theme ----
    public static readonly Color PanelTop = new Color(0.17f, 0.18f, 0.20f, 0.97f);
    public static readonly Color PanelBottom = new Color(0.09f, 0.10f, 0.11f, 0.97f);
    public static readonly Color Field = new Color(0.06f, 0.07f, 0.08f, 1f);
    public static readonly Color Accent = new Color(0.00f, 0.39f, 0.88f, 1f);       // Meta blue
    public static readonly Color AccentBright = new Color(0.25f, 0.62f, 1.00f, 1f);
    public static readonly Color Neutral = new Color(0.27f, 0.28f, 0.31f, 1f);
    public static readonly Color Danger = new Color(0.86f, 0.20f, 0.22f, 1f);
    public static readonly Color Key = new Color(0.22f, 0.23f, 0.26f, 1f);
    public static readonly Color Text = new Color(0.96f, 0.97f, 0.98f, 1f);
    public static readonly Color TextDim = new Color(0.62f, 0.65f, 0.70f, 1f);

    private const int SpriteSize = 64;

    private static Sprite pill;
    private static Sprite card;
    private static Sprite small;

    /// <summary>Fully round ends, for buttons.</summary>
    public static Sprite Pill => pill != null ? pill : (pill = BuildRoundedSprite("UiThemePill", 31));

    /// <summary>Large rounded corners, for panels (backplates).</summary>
    public static Sprite Card => card != null ? card : (card = BuildRoundedSprite("UiThemeCard", 24));

    /// <summary>Small rounded corners, for keyboard keys and text fields.</summary>
    public static Sprite Small => small != null ? small : (small = BuildRoundedSprite("UiThemeSmall", 12));

    public static Color ColourFor(Role role)
    {
        switch (role)
        {
            case Role.Neutral: return Neutral;
            case Role.Danger: return Danger;
            case Role.Key: return Key;
            case Role.Borderless: return Color.white;
            default: return Accent;
        }
    }

    /// <summary>Turns an image into a dark rounded backplate with a soft fade.</summary>
    public static void StylePanel(Image image)
    {
        if (image == null)
        {
            return;
        }

        image.sprite = Card;
        image.type = Image.Type.Sliced;
        image.color = Color.white; // the real colours come from the fade below

        UiGradient fade = image.GetComponent<UiGradient>();
        if (fade == null)
        {
            fade = image.gameObject.AddComponent<UiGradient>();
        }

        fade.Set(PanelTop, PanelBottom);
    }

    /// <summary>Shapes and colours a button, with hover and press feedback.</summary>
    public static void StyleButton(Button button, Role role)
    {
        if (button == null)
        {
            return;
        }

        if (button.targetGraphic is Image image)
        {
            if (CanReplaceSprite(image))
            {
                image.sprite = role == Role.Key ? Small : Pill;
                image.type = Image.Type.Sliced;
            }

            image.color = ColourFor(role);
        }

        ColorBlock colours = button.colors;

        if (role == Role.Borderless)
        {
            // No background until the pointer is on it.
            colours.colorMultiplier = 1f;
            colours.normalColor = new Color(1f, 1f, 1f, 0f);
            colours.highlightedColor = new Color(1f, 1f, 1f, 0.16f);
            colours.selectedColor = new Color(1f, 1f, 1f, 0f);
            colours.pressedColor = new Color(1f, 1f, 1f, 0.28f);
            colours.disabledColor = new Color(1f, 1f, 1f, 0f);
        }
        else
        {
            // The button colour is multiplied by these. With a multiplier of 1.5,
            // 0.67 means "unchanged", higher is brighter and lower is darker.
            colours.colorMultiplier = 1.5f;
            colours.normalColor = Grey(0.67f);
            colours.highlightedColor = Grey(0.84f);
            colours.selectedColor = Grey(0.67f);
            colours.pressedColor = Grey(0.52f);
            colours.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.6f);
        }

        colours.fadeDuration = 0.08f;
        button.colors = colours;
        button.transition = Selectable.Transition.ColorTint;

        if (button.GetComponent<UiButtonScale>() == null)
        {
            button.gameObject.AddComponent<UiButtonScale>();
        }
    }

    /// <summary>Restyles every panel, button, text field and text under the canvas.</summary>
    public static void Apply(Transform canvas)
    {
        if (canvas == null)
        {
            return;
        }

        HashSet<Graphic> done = new HashSet<Graphic>();

        foreach (Button button in canvas.GetComponentsInChildren<Button>(true))
        {
            StyleButton(button, RoleFor(button.name));

            if (button.targetGraphic != null)
            {
                done.Add(button.targetGraphic);
            }

            foreach (TMP_Text caption in button.GetComponentsInChildren<TMP_Text>(true))
            {
                caption.color = Text;
                done.Add(caption);
            }
        }

        foreach (TMP_InputField input in canvas.GetComponentsInChildren<TMP_InputField>(true))
        {
            if (input.targetGraphic is Image box && CanReplaceSprite(box))
            {
                box.sprite = Small;
                box.type = Image.Type.Sliced;
                box.color = Field;
                done.Add(box);
            }

            if (input.textComponent != null)
            {
                input.textComponent.color = Text;
                done.Add(input.textComponent);
            }

            if (input.placeholder is TMP_Text hint)
            {
                hint.color = TextDim;
                done.Add(hint);
            }

            input.selectionColor = new Color(AccentBright.r, AccentBright.g, AccentBright.b, 0.35f);
            input.caretColor = AccentBright;
            input.customCaretColor = true;
        }

        foreach (Image image in canvas.GetComponentsInChildren<Image>(true))
        {
            if (done.Contains(image) || !CanReplaceSprite(image))
            {
                continue; // icons, the spinner and generated pictures keep their look
            }

            string name = image.name.ToLowerInvariant();

            if (name.EndsWith("panel") || name.EndsWith("popup"))
            {
                StylePanel(image);
            }
            else if (name == "fill")
            {
                // The loading bar grows with "fill amount", which only works on a
                // Filled image. Keep that type; only round it when it is not Filled.
                if (image.type != Image.Type.Filled)
                {
                    image.sprite = Pill;
                    image.type = Image.Type.Sliced;
                }

                image.color = AccentBright;
            }
            else if (name == "background" || name == "loadingbar")
            {
                image.sprite = Pill;
                image.type = Image.Type.Sliced;
                image.color = Field;
            }
        }

        foreach (TMP_Text text in canvas.GetComponentsInChildren<TMP_Text>(true))
        {
            if (!done.Contains(text))
            {
                text.color = Text;
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private static Role RoleFor(string buttonName)
    {
        string name = buttonName.ToLowerInvariant();

        if (name.Contains("delete") || name.Contains("exit"))
        {
            return Role.Danger;
        }

        if (name.Contains("close"))
        {
            return Role.Borderless; // the small X in the corner of a panel
        }

        if (name.Contains("cancel") || name.Contains("undo") ||
            name.Contains("regenerate"))
        {
            return Role.Neutral;
        }

        return Role.Accent;
    }

    /// <summary>True for empty images and Unity's built-in grey boxes; false for real pictures.</summary>
    private static bool CanReplaceSprite(Image image)
    {
        if (image.name == "GeneratedImage")
        {
            return false;
        }

        if (image.sprite == null)
        {
            return true;
        }

        string sprite = image.sprite.name;
        return sprite.StartsWith("UiTheme") || sprite == "UISprite" || sprite == "Background" || sprite == "InputFieldBackground";
    }

    private static Color Grey(float value)
    {
        return new Color(value, value, value, 1f);
    }

    private static Sprite BuildRoundedSprite(string name, int radius)
    {
        Texture2D texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false);
        texture.name = name;
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        Color32[] pixels = new Color32[SpriteSize * SpriteSize];
        float r = radius;

        for (int y = 0; y < SpriteSize; y++)
        {
            for (int x = 0; x < SpriteSize; x++)
            {
                // Distance from the pixel centre to the nearest corner circle centre.
                float px = x + 0.5f;
                float py = y + 0.5f;
                float cx = Mathf.Clamp(px, r, SpriteSize - r);
                float cy = Mathf.Clamp(py, r, SpriteSize - r);
                float distance = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy));

                // Soft one-pixel edge so the corner is not jagged.
                float alpha = Mathf.Clamp01(r - distance + 0.5f);
                pixels[y * SpriteSize + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();

        // The border is the part that must not stretch (the corners).
        Vector4 border = new Vector4(radius, radius, radius, radius);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, SpriteSize, SpriteSize),
                                      new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        sprite.name = name;
        return sprite;
    }
}
