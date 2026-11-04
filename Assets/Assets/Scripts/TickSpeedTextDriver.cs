using UnityEngine;
using UnityEngine.UI;
using TMPro;
using WorldSim;

[RequireComponent(typeof(Scrollbar))]
public class TickSpeedTextDriver : MonoBehaviour
{
    public TickEngine tickEngine;
    public TMP_Text label;
    Scrollbar slider;
    public float referenceTicksPerSecondValue=8f, min = 0.8f, max = 32f;
 
    void Start()
    {
        slider = GetComponent<Scrollbar>();
        slider.value = Mathf.InverseLerp(min, max, tickEngine.GetTickRate());
        slider.onValueChanged.AddListener(OnSliderChanged);
        OnSliderChanged(slider.value);
    }
 
    void OnSliderChanged(float value)
    {
        value = Mathf.Lerp(min, max, value);
        tickEngine.SetTickRate(value);
        if (label != null) label.text = "Speed: " + (value/referenceTicksPerSecondValue).ToString("0.00");
    }
}
