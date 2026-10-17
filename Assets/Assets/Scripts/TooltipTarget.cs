using UnityEngine;
using UnityEngine.EventSystems;

// Identifies when mouse enters the object it's attached to and tells it to show the tooltip
public class TooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public string title;
    [TextArea(2, 6)] public string body;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (TooltipSystem.Instance != null)
            TooltipSystem.Instance.Show(title, body);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (TooltipSystem.Instance != null)
            TooltipSystem.Instance.Hide();
    }

    void OnDisable()
    {
        if (TooltipSystem.Instance != null)
            TooltipSystem.Instance.Hide();
    }
}
