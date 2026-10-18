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
        [SerializeField] TileBase[] tileSet;   // index 0 = lowest value, last = highest
        [SerializeField] float minValue = 0f;
        [SerializeField] float maxValue = 1f;
        int[] lastTileIndex; // previously drawn tile indices

        public void Draw(WorldGrid grid) // public method to update and draw the tilemap using world grid data
        {
            float[] values = grid.GetField(fieldName);

            if (lastTileIndex == null)
            {
                lastTileIndex = new int[grid.CellCount];
                for (int i = 0; i < lastTileIndex.Length; i++)
                    lastTileIndex[i] = -1;
            }

            for (int y = 0; y < grid.Height; y++) // loop through the grid
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int i = grid.Index(x, y); // convert 2D grid to array index
                    int tileIndex = ValueToTileIndex(values[i]); // maps cell value to tile index

                    if (tileIndex == lastTileIndex[i]) continue; // if unchanged, skip it

                    tilemap.SetTile(new Vector3Int(x, y, 0), tileSet[tileIndex]); // places tile onto the map at given position
                    lastTileIndex[i] = tileIndex; // updates recorded tile index for the cell position
                }
            }
        }

        int ValueToTileIndex(float value) // method to convert values into a tile in the tile index in the inspector
        {
            float t = Mathf.InverseLerp(minValue, maxValue, value); // normalize values bewteen the set points
            int index = Mathf.FloorToInt(t * (tileSet.Length - 1)); // scales the value to match the given tiles indexes
            return Mathf.Clamp(index, 0, tileSet.Length - 1); // clamps the final index to stay in bounds
        }
    }
}
