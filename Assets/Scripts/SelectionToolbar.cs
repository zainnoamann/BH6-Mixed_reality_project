using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small bar at the bottom of the screen that appears when an object is selected.
///
///   [ Selected: table ]  [ Move ]  [ Rotate ]  [ Resize ]  [ Change Texture ]  [ Close ]
///
/// Clicking an object only selects it. The texture panel opens only from here,
/// so a click in the room no longer pops up the Change Texture form every time.
///
/// Built in code so the scene does not need re-wiring. It stays in one place on
/// screen on purpose: panels that follow the object made the UI jump around.
/// </summary>
public class SelectionToolbar : MonoBehaviour
{
    public event Action MoveClicked;
    public event Action ResizeClicked;
    public event Action RotateClicked;
    public event Action TextureClicked;
    public event Action CloseClicked;

    private GameObject panel;
    private TMP_Text label;
    private GameObject moveButton;
    private GameObject resizeButton;
    private GameObject rotateButton;
    private GameObject textureButton;

    private static readonly Color PanelColour = new Color(0.12f, 0.12f, 0.14f, 0.92f);
    private static readonly Color ButtonColour = new Color(0.13f, 0.55f, 0.13f, 1f);
    private static readonly Color CloseColour = new Color(0.35f, 0.35f, 0.38f, 1f);

    public bool IsVisible => panel != null && panel.activeSelf;

    /// <summary>Creates the toolbar under the given canvas and returns it hidden.</summary>
    public static SelectionToolbar Create(Transform canvas)
    {
        GameObject root = new GameObject("SelectionToolbar", typeof(RectTransform));
        root.transform.SetParent(canvas, false);

        SelectionToolbar toolbar = root.AddComponent<SelectionToolbar>();
        toolbar.Build();
        toolbar.Hide();

        return toolbar;
    }

    private void Build()
    {
        panel = gameObject;

        RectTransform rect = (RectTransform)transform;
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 24f);

        Image background = panel.AddComponent<Image>();
        background.color = PanelColour;

        HorizontalLayoutGroup layout = panel.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 8, 8);
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = panel.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        label = CreateText(panel.transform, "Label", "", 18f, TextAlignmentOptions.Left);
        LayoutElement labelLayout = label.gameObject.AddComponent<LayoutElement>();
        // No fixed width: the label grows with its text, so long hints are not cut off.
        labelLayout.minWidth = 180f;

        moveButton = CreateButton("Move", ButtonColour, () => MoveClicked?.Invoke());
        rotateButton = CreateButton("Rotate", ButtonColour, () => RotateClicked?.Invoke());
        resizeButton = CreateButton("Resize", ButtonColour, () => ResizeClicked?.Invoke());
        textureButton = CreateButton("Change Texture", ButtonColour, () => TextureClicked?.Invoke());
        CreateButton("Close", CloseColour, () => CloseClicked?.Invoke());
    }

    /// <summary>Normal state: object name plus all actions.</summary>
    public void Show(string objectName)
    {
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        label.text = "Selected: " + objectName;
        moveButton.SetActive(true);
        rotateButton.SetActive(true);
        resizeButton.SetActive(true);
        textureButton.SetActive(true);
    }

    /// <summary>While moving: instructions only, actions hidden.</summary>
    public void ShowHint(string text)
    {
        panel.SetActive(true);
        panel.transform.SetAsLastSibling();
        label.text = text;
        moveButton.SetActive(false);
        rotateButton.SetActive(false);
        resizeButton.SetActive(false);
        textureButton.SetActive(false);
    }

    public void Hide()
    {
        if (panel != null)
        {
            panel.SetActive(false);
        }
    }

    private GameObject CreateButton(string text, Color colour, Action onClick)
    {
        GameObject go = new GameObject(text + "Button", typeof(RectTransform));
        go.transform.SetParent(panel.transform, false);

        Image image = go.AddComponent<Image>();
        image.color = colour;

        Button button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(() => onClick());

        LayoutElement layout = go.AddComponent<LayoutElement>();
        layout.minWidth = 110f;
        layout.minHeight = 38f;
        layout.preferredHeight = 38f;

        TMP_Text caption = CreateText(go.transform, "Text", text, 17f, TextAlignmentOptions.Center);
        RectTransform captionRect = caption.rectTransform;
        captionRect.anchorMin = Vector2.zero;
        captionRect.anchorMax = Vector2.one;
        captionRect.offsetMin = new Vector2(10f, 0f);
        captionRect.offsetMax = new Vector2(-10f, 0f);

        // Size the button to its caption.
        layout.preferredWidth = Mathf.Max(110f, caption.GetPreferredValues(text).x + 24f);

        return go;
    }

    private static TMP_Text CreateText(Transform parent, string name, string text, float size,
                                       TextAlignmentOptions alignment)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = alignment;
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        return tmp;
    }
}
