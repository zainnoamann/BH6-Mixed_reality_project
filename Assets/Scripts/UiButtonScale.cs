using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Makes a button grow a little when the pointer is on it and shrink a little
/// when it is pressed, like the buttons in Meta's UI Set. Added by UiTheme.
/// </summary>
public class UiButtonScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
                             IPointerDownHandler, IPointerUpHandler
{
    public float hoverScale = 1.05f;
    public float pressScale = 0.96f;
    public float speed = 18f;

    private bool hovering;
    private bool pressing;

    public void OnPointerEnter(PointerEventData eventData) { hovering = true; }
    public void OnPointerExit(PointerEventData eventData) { hovering = false; pressing = false; }
    public void OnPointerDown(PointerEventData eventData) { pressing = true; }
    public void OnPointerUp(PointerEventData eventData) { pressing = false; }

    private void OnDisable()
    {
        // The panel was closed while the pointer was on the button.
        hovering = false;
        pressing = false;
        transform.localScale = Vector3.one;
    }

    private void Update()
    {
        float wanted = pressing ? pressScale : hovering ? hoverScale : 1f;
        float blend = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        float now = Mathf.Lerp(transform.localScale.x, wanted, blend);
        transform.localScale = new Vector3(now, now, 1f);
    }
}
