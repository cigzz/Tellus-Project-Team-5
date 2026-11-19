using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

///Hover animation for buttons
[DisallowMultipleComponent]
public class HoverGrowButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public enum MoveAxis { X, Y }

    [Header("Hover look")]
    public float hoverScale = 1.1f;

    [Tooltip("Axis to move:")]
    public MoveAxis moveAxis = MoveAxis.Y;
    [FormerlySerializedAs("moveUp")]
    public float moveAmount = 8f;

    [Header("Timing")]
    [SerializeField] private float duration = 0.12f;

    private Vector3 basePosition;
    private Vector3 baseScale;
    private float progress;
    private bool hovered;

    private void OnEnable()
    {
        basePosition = transform.localPosition;
        baseScale = transform.localScale;
        progress = 0f;
        hovered = false;
    }

    private void OnDisable()
    {
        transform.localPosition = basePosition;
        transform.localScale = baseScale;
        progress = 0f;
        hovered = false;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovered = true;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovered = false;
    }

    private void Update()
    {
        float goal = hovered ? 1f : 0f;
        if (Mathf.Approximately(progress, goal)) return;

        float step = duration > 0f ? Time.unscaledDeltaTime / duration : 1f;
        progress = Mathf.MoveTowards(progress, goal, step);

        float eased = Mathf.SmoothStep(0f, 1f, progress);
        transform.localScale = Vector3.Lerp(baseScale, baseScale * hoverScale, eased);
        Vector3 offset = moveAxis == MoveAxis.X
            ? new Vector3(moveAmount * eased, 0f, 0f)
            : new Vector3(0f, moveAmount * eased, 0f);
        transform.localPosition = basePosition + offset;
    }
}