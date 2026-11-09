using UnityEngine;

namespace WorldSim
{
    // Switches a GridTilemapRenderer between the field it started with and an alternate field
    // OnClick at FieldViewToggle.Toggle
    // the original is whatever the renderer is set to in the inspector
    public class FieldViewToggle : MonoBehaviour
    {
        [SerializeField] GridTilemapRenderer targetRenderer;

        [Header("Alternate view")]
        [SerializeField] string alternateFieldName = "water";
        [SerializeField] float alternateMinValue = 0f;   // value that maps to the first tile
        [SerializeField] float alternateMaxValue = 10f;  // value that maps to the last tile

        string originalFieldName;
        float originalMin, originalMax;
        bool showingAlternate;

        void Awake()
        {
            // Remember the renderer's starting setup so we can switch back to it.
            originalFieldName = targetRenderer.FieldName;
            originalMin = targetRenderer.MinValue;
            originalMax = targetRenderer.MaxValue;
        }

        public void Toggle()
        {
            showingAlternate = !showingAlternate;

            if (showingAlternate)
                targetRenderer.SetField(alternateFieldName, alternateMinValue, alternateMaxValue);
            else
                targetRenderer.SetField(originalFieldName, originalMin, originalMax);
        }
    }
}
