using UnityEngine;
using UnityEngine.Tilemaps;

namespace WorldSim
{
    // Lets the player click a tile to plant the first seed. Works with PlantGrowthSystem
    public class PlantPlacer : MonoBehaviour
    {
        [SerializeField] TickEngine tickEngine;
        [SerializeField] Tilemap tilemap; // only used to convert a click into a grid cell
        [SerializeField] float seedValue = 1f;

        void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return; // return if no mouse input
            if (tickEngine.Grid == null) return; // grid not created yet

            Vector3 worldPoint = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector3Int cell = tilemap.WorldToCell(worldPoint);

            WorldGrid grid = tickEngine.Grid;
            if (cell.x < 0 || cell.x >= grid.Width || cell.y < 0 || cell.y >= grid.Height) return;

            int index = grid.Index(cell.x, cell.y);
            grid.GetField("plantGrowth")[index] = seedValue;
        }
    }
}
