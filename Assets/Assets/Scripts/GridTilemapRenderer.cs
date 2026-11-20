using UnityEngine;
using UnityEngine.Tilemaps;

namespace WorldSim
{
    // Draws one field of the WorldGrid onto a Tilemap, picking a tile from a tileset based on each cell's value.
    // mutiple will be used in the manager object to draw the filters and gameview
    public class GridTilemapRenderer : MonoBehaviour
    {
        [SerializeField] Tilemap tilemap;
        [SerializeField] string fieldName = "temperature";
        [SerializeField] TileBase[] tileSet;
        [SerializeField] float minValue = 0f;
        [SerializeField] float maxValue = 1f;
        [SerializeField] bool useOptimal;           // off: linear color from min - max. on: best at optimalValue, worse toward min and max
        [SerializeField] float optimalValue = 0.5f;
        [SerializeField] float optimalTolerance = 0f;   // half-width of the all-green band around optimalValue
        WorldGrid lastGrid;
        public string FieldName => fieldName;
        public float MinValue => minValue;
        public float MaxValue => maxValue;
        public TileBase[] TileSet => tileSet;
        public bool UseOptimal => useOptimal;
        public float OptimalValue => optimalValue;
        public float OptimalTolerance => optimalTolerance;
        int[] lastTileIndex; // previously drawn tile indices
        int[] lastDrawnValue;
        readonly System.Collections.Generic.List<Vector3Int> changedPositions = new System.Collections.Generic.List<Vector3Int>();
        readonly System.Collections.Generic.List<TileBase> changedTiles = new System.Collections.Generic.List<TileBase>();

        // Shows or hides this view. Hidden: stops drawing and hides its tilemap. Shown: repaints every cell.
        public void SetShown(bool shown)
        {
            enabled = shown;
            var tilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
            if (tilemapRenderer != null) tilemapRenderer.enabled = shown;
            if (!shown) return;

            if (lastTileIndex != null)
            {
                for (int i = 0; i < lastTileIndex.Length; i++)
                    lastTileIndex[i] = -1;
            }
            if (lastGrid != null) Draw(lastGrid);
        }

        public void SetField(string newFieldName, float newMin, float newMax, TileBase[] newTileSet = null,
                             bool newUseOptimal = false, float newOptimalValue = 0f, float newOptimalTolerance = 0f)
        {
            fieldName = newFieldName;
            minValue = newMin;
            maxValue = newMax;
            useOptimal = newUseOptimal;
            optimalValue = newOptimalValue;
            optimalTolerance = newOptimalTolerance;
            if (newTileSet != null && newTileSet.Length > 0) tileSet = newTileSet; // empty = keep the current tileset

            // forget what was drawn so every cell repaints with the new field and tileset
            if (lastTileIndex != null)
            {
                for (int i = 0; i < lastTileIndex.Length; i++)
                    lastTileIndex[i] = -1;
            }

            if (lastGrid != null) Draw(lastGrid);
        }
        public void Draw(WorldGrid grid) // public method to update and draw the tilemap using world grid data
        {
            lastGrid = grid;
            if (!enabled) return; // hidden view: don't draw
            float[] values = grid.GetField(fieldName);

            if (lastTileIndex == null)
            {
                lastTileIndex = new int[grid.CellCount];
                for (int i = 0; i < lastTileIndex.Length; i++)
                    lastTileIndex[i] = -1;
            }

            changedPositions.Clear();
            changedTiles.Clear();

            for (int y = 0; y < grid.Height; y++) // loop through the grid
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int i = grid.Index(x, y); // convert 2D grid to array index
                    int tileIndex = ValueToTileIndex(values[i]); // maps cell value to tile index

                    if (tileIndex == lastTileIndex[i]) continue; // if unchanged, skip it

                    changedPositions.Add(new Vector3Int(x, y, 0)); // queue only the cells that changed
                    changedTiles.Add(tileSet[tileIndex]);
                    lastTileIndex[i] = tileIndex; // updates recorded tile index for the cell position
                }
            }

            // one batched tilemap update instead of one SetTile call per cell
            if (changedPositions.Count > 0)
                tilemap.SetTiles(changedPositions.ToArray(), changedTiles.ToArray());
        }
        float Normalize(float value)
        {
            if (!useOptimal) return Mathf.InverseLerp(minValue, maxValue, value);

            float lowEdge = optimalValue - optimalTolerance;
            float highEdge = optimalValue + optimalTolerance;

            if (value < lowEdge) return Mathf.InverseLerp(lowEdge, minValue, value);
            if (value > highEdge) return Mathf.InverseLerp(highEdge, maxValue, value);
            return 0.001f; // inside the tolerance band
        }

        int ValueToTileIndex(float value) // method to convert values into a tile in the tile index in the inspector
        {
            float t = Normalize(value);
            int index = Mathf.FloorToInt(t * (tileSet.Length - 1)); // scales the value to match the given tiles indexes
            return Mathf.Clamp(index, 0, tileSet.Length - 1); // clamps the final index to stay in bounds
        }
    }
}
