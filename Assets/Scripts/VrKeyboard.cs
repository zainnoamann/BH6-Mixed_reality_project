using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// A simple on-screen keyboard for the headset.
///
/// Inside the Quest there is no keyboard, so the prompt field ("a wooden chair")
/// could not be filled in. When a text field is selected with the controller, this
/// keyboard appears at the bottom of the canvas; point at a key and pull the trigger.
///
///   letters, Space, Backspace, Clear, Done (hides the keyboard)
///
/// It only shows in the headset. On desktop the real keyboard is used as before.
/// Built in code, like SelectionToolbar, so the scene does not need re-wiring.
/// </summary>
public class VrKeyboard : MonoBehaviour
{
    [Tooltip("Show the keyboard on desktop too. For testing without a headset.")]
    [SerializeField] private bool showOnDesktop;

    private static readonly string[] Rows = { "qwertyuiop", "asdfghjkl", "zxcvbnm" };

    private const float KeySize = 46f;
    private const float KeyGap = 5f;

    private GameObject panel;
    private TMP_InputField target;

    /// <summary>Creates the keyboard under the given canvas. It starts hidden.</summary>
    public static VrKeyboard Create(Transform canvas)
    {
        GameObject root = new GameObject("VrKeyboard", typeof(RectTransform));
        root.transform.SetParent(canvas, false);

        // The root stays active so Update keeps running; only the panel is shown / hidden.
        RectTransform rootRect = (RectTransform)root.transform;
        rootRect.anchorMin = new Vector2(0.5f, 0f);
        rootRect.anchorMax = new Vector2(0.5f, 0f);
        rootRect.pivot = new Vector2(0.5f, 0f);
        // Below the bottom edge of the menu, so it does not cover the Generate button.
        rootRect.anchoredPosition = new Vector2(0f, -110f);
        rootRect.sizeDelta = Vector2.zero;

        VrKeyboard keyboard = root.AddComponent<VrKeyboard>();
        keyboard.Build();

        return keyboard;
    }

    private void Build()
    {
        panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(transform, false);

        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = Vector2.zero;

        Image background = panel.AddComponent<Image>();
        UiTheme.StylePanel(background);

        VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 14, 14);
        layout.spacing = KeyGap;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (string letters in Rows)
        {
            Transform row = CreateRow();

            foreach (char letter in letters)
            {
                string text = letter.ToString();
                CreateKey(row, text.ToUpperInvariant(), KeySize, UiTheme.Role.Key, () => Type(text));
            }
        }

        Transform actions = CreateRow();
        CreateKey(actions, "Clear", KeySize * 1.8f, UiTheme.Role.Neutral, Clear);
        CreateKey(actions, "Space", KeySize * 4.5f, UiTheme.Role.Key, () => Type(" "));
        CreateKey(actions, "Backspace", KeySize * 2.4f, UiTheme.Role.Neutral, Backspace);
        CreateKey(actions, "Done", KeySize * 1.8f, UiTheme.Role.Accent, Done);

        panel.SetActive(false);
    }

    private void Update()
    {
        EventSystem eventSystem = EventSystem.current;
        GameObject selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;

        // Remember the field: pressing a key moves the selection to that key.
        if (selected != null && selected.TryGetComponent(out TMP_InputField field))
        {
            target = field;
        }

        if (target != null && !target.isActiveAndEnabled)
        {
            target = null; // its panel was closed
        }

        PointerSource pointer = PointerSource.Resolve();
        bool headset = showOnDesktop || (pointer != null && pointer.UsingXr);
        bool show = headset && target != null;

        if (panel.activeSelf != show)
        {
            panel.SetActive(show);

            if (show)
            {
                transform.SetAsLastSibling(); // on top of the toolbar
            }
        }
    }

    // ------------------------------------------------------------------ keys

    private void Type(string text)
    {
        if (target == null)
        {
            return;
        }

        target.text += text;
        target.caretPosition = target.text.Length;
    }

    private void Backspace()
    {
        if (target == null || target.text.Length == 0)
        {
            return;
        }

        target.text = target.text.Substring(0, target.text.Length - 1);
        target.caretPosition = target.text.Length;
    }

    private void Clear()
    {
        if (target != null)
        {
            target.text = string.Empty;
        }
    }

    private void Done()
    {
        target = null;

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }
    }

    // ------------------------------------------------------------------ building

    private Transform CreateRow()
    {
        GameObject row = new GameObject("Row", typeof(RectTransform));
        row.transform.SetParent(panel.transform, false);

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = KeyGap;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        return row.transform;
    }

    private static void CreateKey(Transform row, string caption, float width, UiTheme.Role role,
                                  UnityEngine.Events.UnityAction onClick)
    {
        GameObject key = new GameObject(caption + "Key", typeof(RectTransform));
        key.transform.SetParent(row, false);

        Image image = key.AddComponent<Image>();

        Button button = key.AddComponent<Button>();
        button.targetGraphic = image;
        UiTheme.StyleButton(button, role);
        button.onClick.AddListener(onClick);

        LayoutElement layout = key.AddComponent<LayoutElement>();
        layout.minWidth = width;
        layout.preferredWidth = width;
        layout.minHeight = KeySize;
        layout.preferredHeight = KeySize;

        GameObject label = new GameObject("Text", typeof(RectTransform));
        label.transform.SetParent(key.transform, false);

        RectTransform labelRect = (RectTransform)label.transform;
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TextMeshProUGUI text = label.AddComponent<TextMeshProUGUI>();
        text.text = caption;
        text.fontSize = caption.Length == 1 ? 22f : 17f;
        text.color = UiTheme.Text;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
    }
}
