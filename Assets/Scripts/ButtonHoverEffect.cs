using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// Small hover feedback for wood UI buttons: scales up slightly and brightens the wood
// tint on pointer-enter, reverts on pointer-exit. Unity's built-in Button ColorTint
// transition can't do a scale change, so this replaces that transition entirely (see
// GameUIHelper.CreateWoodButton, which sets transition to None before adding this) to
// avoid the two systems fighting over the same Image color.
public class ButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private const float HoverScale = 1.05f;
    private const float HoverBrighten = 1.15f;

    private Vector3 baseScale;
    private Color baseColor;
    private Image image;

    void Awake()
    {
        baseScale = transform.localScale;
        image = GetComponent<Image>();
        if (image != null) baseColor = image.color;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        transform.localScale = baseScale * HoverScale;
        if (image != null) image.color = baseColor * HoverBrighten;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        transform.localScale = baseScale;
        if (image != null) image.color = baseColor;
    }
}
