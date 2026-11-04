using UnityEngine;
using UnityEngine.UI;
using TMPro;
using WorldSim;

// Applied to each UI component needed as a component
public class EnvironmentStatDisplay : MonoBehaviour
{
    public enum StatType { Coverage, MeanTemp, MeanSuitability, MeanWater, MeanCo2, Occupied }

    [SerializeField] StatType stat;

    [Header("Text")]
    [SerializeField] TMP_Text textBox;
    [SerializeField] string prefix = "Value: ";
    [SerializeField] string suffix = "";
    [Tooltip("100 shows 0-1 values as a percent.")]
    [SerializeField] float displayMultiplier = 1f;
    [SerializeField, Range(0, 4)] int decimals = 1;

    [Header("Bar")]
    [SerializeField] Image fillImage;
    [Tooltip("Optional. Replaces the Image's sprite.")]
    [SerializeField] Sprite fillSprite;
    [SerializeField] float minValue = 0f;  // empty bar
    [SerializeField] float maxValue = 1f;  // full bar

    [Header("Thresholds (raw values)")]
    [Tooltip("Set higher than Green when low values are good (e.g. heat).")]
    [SerializeField] float redThreshold = 0.25f;
    [SerializeField] float greenThreshold = 0.75f;

    void Awake()
    {
        if (fillImage == null) return;
        if (fillSprite != null) fillImage.sprite = fillSprite;
        fillImage.type = Image.Type.Filled;
    }

    public void Show(PlantStats s, StatDisplayManager manager)
    {
        float value = stat switch
        {
            StatType.Coverage        => s.Coverage,
            StatType.MeanTemp        => s.MeanTemp,
            StatType.MeanSuitability => s.MeanSuitability,
            StatType.MeanWater       => s.MeanWater,
            StatType.MeanCo2         => s.MeanCo2,
            _                        => s.Occupied,
        };

        if (textBox != null)
            textBox.text = prefix + (value * displayMultiplier).ToString("F" + decimals) + suffix;

        if (fillImage != null)
        {
            fillImage.fillAmount = Mathf.InverseLerp(minValue, maxValue, value);
            // 0 at red threshold, 1 at green; works in either direction.
            fillImage.color = manager.ColorFor(Mathf.InverseLerp(redThreshold, greenThreshold, value));
        }
    }
}
