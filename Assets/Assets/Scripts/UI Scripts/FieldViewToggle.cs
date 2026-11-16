using UnityEngine;
using UnityEngine.Tilemaps;

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
        [SerializeField] TileBase[] alternateTileSet;    // leave empty to keep the renderer's current tileset
        [SerializeField] bool alternateUseOptimal;       // true: min and max become the "worst" bounds
        [SerializeField] float alternateOptimalValue = 20f;
        [SerializeField] float alternateTolerance = 2f;  // half-width of the optimal band (20 +/- 2 = 18 to 22)

        string originalFieldName;
        float originalMin, originalMax;
        TileBase[] originalTileSet;
        bool originalUseOptimal;
        float originalOptimalValue;
        float originalTolerance;
        bool showingAlternate;

        void Awake()
        {
            // Remember the renderer's starting setup so we can switch back to it.
            originalFieldName = targetRenderer.FieldName;
            originalMin = targetRenderer.MinValue;
            originalMax = targetRenderer.MaxValue;
            originalTileSet = targetRenderer.TileSet;
            originalUseOptimal = targetRenderer.UseOptimal;
            originalOptimalValue = targetRenderer.OptimalValue;
            originalTolerance = targetRenderer.OptimalTolerance;
        }

        public void Toggle()
        {
            showingAlternate = !showingAlternate;

            if (showingAlternate)
                targetRenderer.SetField(alternateFieldName, alternateMinValue, alternateMaxValue, alternateTileSet,
                                        alternateUseOptimal, alternateOptimalValue, alternateTolerance);
            else
                targetRenderer.SetField(originalFieldName, originalMin, originalMax, originalTileSet,
                                        originalUseOptimal, originalOptimalValue, originalTolerance);
        }
    }
}
