using UnityEngine;

namespace WorldSim
{
    // Switches a ColorBlendTilemapRenderer between the field it started with and an alternate field
    // OnClick at FieldViewToggle.Toggle
    // the original is whatever the renderer is set to in the inspector
    public class FieldViewToggle : MonoBehaviour
    {
        [SerializeField] ColorBlendTilemapRenderer targetRenderer;
        [SerializeField] GridTilemapRenderer otherRenderer; // turned off while the color view is on (optional)

        [Header("Alternate view")]
        [SerializeField] string alternateFieldName = "water";
        [SerializeField] float alternateMinValue = 0f;      // value that is fully the low color
        [SerializeField] float alternateOptimalValue = 5f;  // value that is fully the optimal color
        [SerializeField] float alternateMaxValue = 10f;     // value that is fully the high color
        [SerializeField] float alternateTolerance = 1f;    // half-width of the optimal band (5 +/- 1 = 4 to 6)

        [Header("Alternate colors")]
        [SerializeField] Color alternateLowColor = new Color(0.55f, 0.35f, 0.15f); // dry
        [SerializeField] Color alternateOptimalColor = Color.green;
        [SerializeField] Color alternateHighColor = Color.blue;                     // flooded

        string originalFieldName;
        float originalMin, originalOptimal, originalMax, originalTolerance;
        Color originalLowColor, originalOptimalColor, originalHighColor;
        bool showingAlternate;

        void Awake()
        {
            // Remember the renderer's starting setup so we can switch back to it.
            originalFieldName = targetRenderer.FieldName;
            originalMin = targetRenderer.MinValue;
            originalOptimal = targetRenderer.OptimalValue;
            originalMax = targetRenderer.MaxValue;
            originalTolerance = targetRenderer.OptimalTolerance;
            originalLowColor = targetRenderer.LowColor;
            originalOptimalColor = targetRenderer.OptimalColor;
            originalHighColor = targetRenderer.HighColor;

            ShowColorView(false); // start on the other view
        }

        void ShowColorView(bool on)
        {
            if (otherRenderer == null) return;
            // hide first, then show, so a shared tilemap ends up visible
            if (on) { otherRenderer.SetShown(false); targetRenderer.SetShown(true); }
            else    { targetRenderer.SetShown(false); otherRenderer.SetShown(true); }
        }

        public void Toggle()
        {
            showingAlternate = !showingAlternate;

            if (showingAlternate)
                targetRenderer.SetField(alternateFieldName, alternateMinValue, alternateOptimalValue, alternateMaxValue,
                                        alternateTolerance, alternateLowColor, alternateOptimalColor, alternateHighColor);
            else
                targetRenderer.SetField(originalFieldName, originalMin, originalOptimal, originalMax,
                                        originalTolerance, originalLowColor, originalOptimalColor, originalHighColor);

            ShowColorView(showingAlternate);
        }
        public void ChangeFieldFilter(string fieldName)
        {
            this.alternateFieldName = fieldName;
        }
    }
}
