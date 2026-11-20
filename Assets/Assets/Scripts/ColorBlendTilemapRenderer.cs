using UnityEngine;
using UnityEngine.Tilemaps;

namespace WorldSim
{
    // Modified GridTilemapRenderer: paints one template tile on every cell and tints it
    // with a color blended between three set points (low, optimal, high) based on the cell's value.
    // The template tile's sprite should be white (or light gray) so the tint shows true.
    public class ColorBlendTilemapRenderer : MonoBehaviour
    {
        [SerializeField] Tilemap tilemap;
        [SerializeField] string fieldName = "temperature";
        [SerializeField] TileBase templateTile;

        [Header("Set points")]
        [SerializeField] float minValue = 0f;           // value that is fully lowColor
        [SerializeField] float optimalValue = 0.5f;     // value that is fully optimalColor
        [SerializeField] float maxValue = 1f;           // value that is fully highColor
        [SerializeField] float optimalTolerance = 0f;   // half-width of the solid optimalColor band around optimalValue

        [Header("Colors")]
        [SerializeField] Color lowColor = Color.blue;
        [SerializeField] Color optimalColor = Color.green;
        [SerializeField] Color highColor = Color.red;
        [SerializeField, Min(0)] int colorSteps = 32;   // color bands; fewer = fewer redraws. 0 = smooth

        public string FieldName => fieldName;
        public float MinValue => minValue;
        public float OptimalValue => optimalValue;
        public float MaxValue => maxValue;
        public float OptimalTolerance => optimalTolerance;
        public Color LowColor => lowColor;
        public Color OptimalColor => optimalColor;
        public Color HighColor => highColor;

        WorldGrid lastGrid;
        Color32[] lastColor;    // previously drawn color per cell
        bool[] tilePlaced;      // whether the template tile is already on the cell
        readonly System.Collections.Generic.List<TileChangeData> changes = new System.Collections.Generic.List<TileChangeData>();

        // Shows or hides this view. Hidden: stops drawing and hides its tilemap. Shown: repaints every cell.
        public void SetShown(bool shown)
        {
            enabled = shown;
            var tilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
            if (tilemapRenderer != null) tilemapRenderer.enabled = shown;
            if (!shown) return;

            lastColor = null; // forget what was drawn so every cell repaints
            if (lastGrid != null) Draw(lastGrid);
        }

        public void SetField(string newFieldName, float newMin, float newOptimal, float newMax, float newTolerance,
                             Color newLowColor, Color newOptimalColor, Color newHighColor)
        {
            fieldName = newFieldName;
            minValue = newMin;
            optimalValue = newOptimal;
            maxValue = newMax;
            optimalTolerance = newTolerance;
            lowColor = newLowColor;
            optimalColor = newOptimalColor;
            highColor = newHighColor;

            lastColor = null; // forget what was drawn so every cell repaints
            if (lastGrid != null) Draw(lastGrid);
        }

        public void Draw(WorldGrid grid)
        {
            lastGrid = grid;
            if (!enabled) return; // hidden view: don't draw
            float[] values = grid.GetField(fieldName);

            if (lastColor == null || lastColor.Length != grid.CellCount) // first draw or forced repaint
            {
                lastColor = new Color32[grid.CellCount];
                tilePlaced = new bool[grid.CellCount];
            }

            changes.Clear();
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int i = grid.Index(x, y);
                    Color32 color = ValueToColor(values[i]);

                    if (tilePlaced[i] && color.Equals(lastColor[i])) continue; // unchanged, skip

                    changes.Add(new TileChangeData(new Vector3Int(x, y, 0), templateTile, color, Matrix4x4.identity));
                    lastColor[i] = color;
                    tilePlaced[i] = true;
                }
            }

            // one batched update for only the changed cells; true = tint even if the tile has Lock Color
            if (changes.Count > 0)
                tilemap.SetTiles(changes.ToArray(), true);
        }

        Color ValueToColor(float value)
        {
            float lowEdge = optimalValue - optimalTolerance;
            float highEdge = optimalValue + optimalTolerance;

            if (value < lowEdge)  // blend low -> optimal
                return Color.Lerp(lowColor, optimalColor, Step(Mathf.InverseLerp(minValue, lowEdge, value)));
            if (value > highEdge) // blend optimal -> high
                return Color.Lerp(optimalColor, highColor, Step(Mathf.InverseLerp(highEdge, maxValue, value)));
            return optimalColor;  // inside the tolerance band
        }

        // snaps the blend to colorSteps bands so tiny value drift doesn't repaint the tile
        float Step(float t) => colorSteps > 0 ? Mathf.Round(t * colorSteps) / colorSteps : t;
    }
}
