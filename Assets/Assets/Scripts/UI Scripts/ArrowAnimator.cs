using UnityEngine;

///Rotation animation to flip arrow buttons for showing/hiding UI bars
[DisallowMultipleComponent]
public class ArrowFlip : MonoBehaviour
{
    public enum RotationAxis { X, Y, Z }

    [Header("Link")]
    [SerializeField] private SlideMenu menu;
    [SerializeField] private bool startClosed = false;
    [Header("Rotation")]
    [SerializeField] private RotationAxis axis = RotationAxis.Z;
    [SerializeField] private float openAngle = 0f;
    [SerializeField] private float closedAngle = 180f;
    [SerializeField] private float duration = 0.25f;
    [SerializeField] private AnimationCurve easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Quaternion baseRotation;
    private float progress;
    private bool closed;

    private void Awake()
    {
        baseRotation = transform.localRotation;
    }

    private void Start()
    {
        closed = menu != null ? !menu.IsOpen : startClosed;
        progress = closed ? 1f : 0f;
        Apply();
    }

    public void Flip()
    {
        closed = !closed;
    }

    private void Update()
    {
        if (menu != null) closed = !menu.IsOpen;

        float goal = closed ? 1f : 0f;
        if (Mathf.Approximately(progress, goal)) return;

        float step = duration > 0f ? Time.unscaledDeltaTime / duration : 1f;
        progress = Mathf.MoveTowards(progress, goal, step);
        Apply();
    }

    private void Apply()
    {
        float angle = Mathf.LerpUnclamped(openAngle, closedAngle, easing.Evaluate(progress));
        transform.localRotation = baseRotation * Quaternion.AngleAxis(angle, GetAxisVector());
    }

    private Vector3 GetAxisVector()
    {
        switch (axis)
        {
            case RotationAxis.X: return Vector3.right;
            case RotationAxis.Y: return Vector3.up;
            default: return Vector3.forward;
        }
    }
}