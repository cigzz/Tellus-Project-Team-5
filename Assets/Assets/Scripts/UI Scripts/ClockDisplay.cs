using UnityEngine;
using TMPro;

public class ClockDisplayUI : MonoBehaviour
{
    [SerializeField] WorldClock clock;
    [SerializeField] TMP_Text label;
    void Update()
    {
        if (clock != null) label.text = clock.TimeString;
    }
}