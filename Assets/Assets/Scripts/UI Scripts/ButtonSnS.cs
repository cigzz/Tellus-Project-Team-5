using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

///Animate buttons to squash and stretch on clicks
[DisallowMultipleComponent]
public class SquashStretchButton : MonoBehaviour, IPointerClickHandler
{
    [Header("Scale multipliers (x, y)")]
    [SerializeField] private Vector2 squash = new Vector2(1.2f, 0.8f);   
    [SerializeField] private Vector2 stretch = new Vector2(0.9f, 1.1f);  

    [Header("Timing (seconds)")]
    [SerializeField] private float squashTime = 0.08f;
    [SerializeField] private float stretchTime = 0.10f;
    [SerializeField] private float settleTime = 0.10f;

    [Header("Events")]
    public UnityEvent onFinished;
    private Vector3 baseScale;
    private Coroutine routine;

    private void Awake()
    {
        baseScale = transform.localScale;
    }

    private void OnDisable()
    {
        routine = null;
        transform.localScale = baseScale;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Play();
    }

    public void Play()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Animate());
    }

    private IEnumerator Animate()
    {
        yield return ScaleTo(squash, squashTime);
        yield return ScaleTo(stretch, stretchTime);
        yield return ScaleTo(Vector2.one, settleTime);
        routine = null;
        onFinished.Invoke();
    }

    private IEnumerator ScaleTo(Vector2 multiplier, float duration)
    {
        Vector3 from = transform.localScale;
        Vector3 to = new Vector3(baseScale.x * multiplier.x, baseScale.y * multiplier.y, baseScale.z);

        for (float t = 0f; t < duration; t += Time.unscaledDeltaTime)
        {
            float eased = Mathf.SmoothStep(0f, 1f, t / duration);
            transform.localScale = Vector3.Lerp(from, to, eased);
            yield return null;
        }

        transform.localScale = to;
    }
}