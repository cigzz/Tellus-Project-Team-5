using System.Collections;
using UnityEngine;
using UnityEngine.Events;

///Slide animation to show/hide main UI bars
[DisallowMultipleComponent]
public class SlideMenu : MonoBehaviour
{
    public enum SlideDirection { Left, Right, Up, Down }

    [Header("Setup")]
    [SerializeField] private RectTransform panel;
    [SerializeField] private bool startOpen = true;
    [Header("Slide")]
    [SerializeField] private SlideDirection hideDirection = SlideDirection.Left;
    [Tooltip("How far the panel moves when hiding, 0 = panel's width/height to hide it fully.")]
    [SerializeField] private float distance = 0f;
    [SerializeField] private float duration = 0.3f;
    [SerializeField] private AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [Header("Events")]
    public UnityEvent onFinished;

    public bool IsOpen { get; private set; }

    private Vector2 openPosition;
    private Vector2 hiddenPosition;
    private Coroutine routine;

    private void Reset()
    {
        panel = GetComponent<RectTransform>();
    }

    private void Awake()
    {
        if (panel == null) panel = (RectTransform)transform;

        openPosition = panel.anchoredPosition;
        hiddenPosition = openPosition + GetDirectionVector() * GetSlideDistance();

        IsOpen = startOpen;
        panel.anchoredPosition = startOpen ? openPosition : hiddenPosition;
    }

    private void OnValidate()
    {
        distance = Mathf.Max(0f, distance);
        duration = Mathf.Max(0f, duration);
    }

    public void Toggle()
    {
        SetOpen(!IsOpen);
    }

    public void Show()
    {
        SetOpen(true);
    }

    public void Hide()
    {
        SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        if (IsOpen == open) return;
        IsOpen = open;

        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Slide(open ? openPosition : hiddenPosition));
    }

    private IEnumerator Slide(Vector2 target)
    {
        Vector2 from = panel.anchoredPosition;

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float eased = easing.Evaluate(t / duration);
            panel.anchoredPosition = Vector2.LerpUnclamped(from, target, eased);
            yield return null;
        }

        panel.anchoredPosition = target;
        routine = null;
        onFinished.Invoke();
    }

    private Vector2 GetDirectionVector()
    {
        switch (hideDirection)
        {
            case SlideDirection.Left: return Vector2.left;
            case SlideDirection.Right: return Vector2.right;
            case SlideDirection.Up: return Vector2.up;
            default: return Vector2.down;
        }
    }

    private float GetSlideDistance()
    {
        if (distance > 0f) return distance;

        bool horizontal = hideDirection == SlideDirection.Left || hideDirection == SlideDirection.Right;
        return horizontal ? panel.rect.width : panel.rect.height;
    }
}