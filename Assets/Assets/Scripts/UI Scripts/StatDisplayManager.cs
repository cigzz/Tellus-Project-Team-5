using UnityEngine;
using WorldSim;
public class StatDisplayManager : MonoBehaviour
{
    [Header("Bar colors (shared by all displays)")]
    [SerializeField] Color redColor = new Color(0.85f, 0.2f, 0.2f);
    [SerializeField] Color middleColor = new Color(0.95f, 0.8f, 0.2f);
    [SerializeField] Color greenColor = new Color(0.25f, 0.8f, 0.3f);
    [Tooltip("Fade between colors between thresholds. Off = solid middle color.")]
    [SerializeField] bool blend = true;
    static PlantModel model;
    EnvironmentStatDisplay[] displays;
    public static void SetModel(PlantModel m) => model = m;

    void Awake() =>
        displays = FindObjectsByType<EnvironmentStatDisplay>(FindObjectsInactive.Include, FindObjectsSortMode.None);

    void LateUpdate()
    {
        if (model == null) return;
        PlantStats stats = model.Stats;
        foreach (var d in displays) d.Show(stats, this);
    }

    // t: 0 = bad (red), 1 = good (green)
    public Color ColorFor(float t)
    {
        if (t <= 0f) return redColor;
        if (t >= 1f) return greenColor;
        if (!blend) return middleColor;
        return t < 0.5f
            ? Color.Lerp(redColor, middleColor, t * 2f)
            : Color.Lerp(middleColor, greenColor, (t - 0.5f) * 2f);
    }
}
