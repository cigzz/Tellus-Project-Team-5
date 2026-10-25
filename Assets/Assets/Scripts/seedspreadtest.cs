using UnityEngine;
using UnityEngine.Tilemaps;

namespace WorldSim
{
    public class SeedSpreadTest : MonoBehaviour
    {
        [SerializeField] TickEngine tickEngine;
        [SerializeField] Tilemap tilemap;
        [SerializeField] GridTilemapRenderer tileRenderer; 

        void Update()
        {
            //click a grid tile to start spread
            if (tickEngine.Grid == null) return;
            if (!Input.GetMouseButtonDown(0)) return;
            Vector3 worldPoint = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector3Int cell = tilemap.WorldToCell(worldPoint);

            //filter out dirt tiles
            WorldGrid grid = tickEngine.Grid;
            if (cell.x < 0 || cell.x >= grid.Width || cell.y < 0 || cell.y >= grid.Height) return;
            int index = grid.Index(cell.x, cell.y);
            if (Mathf.RoundToInt(grid.GetField("terrain")[index]) == 0) return;
            grid.GetField("spread")[index] = 1f;
            SpreadSystem.UpdateDisplay(grid);
            tileRenderer.Draw(grid);
        }
    }
}