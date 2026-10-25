using UnityEngine;
using UnityEngine.Tilemaps;

namespace WorldSim
{
    //test bootstrap for variable tile spread
    [RequireComponent(typeof(TickEngine))]
    public class SpreadTestBootstrap : MonoBehaviour
    {
        public enum ResistanceMode { Scattered, Clumpy }

        [Header("Grid")]
        [SerializeField] int width = 100;
        [SerializeField] int height = 100;
        [SerializeField] int seed = 12345;
        [SerializeField] GridTilemapRenderer tileRenderer; 

        [Header("Use Existing Tilemap")]
        [SerializeField] bool readTilemap = true;  
        [SerializeField] Tilemap sourceTilemap;               
        [SerializeField] TileBase dirtTile, grassTile, deadGrassTile; 

        [Header("Generated Terrain")]
        [SerializeField] float terrainScale = 0.08f;
        [SerializeField, Range(0f, 1f)] float dirtBelow = 0.3f;       
        [SerializeField, Range(0f, 1f)] float deadGrassBelow = 0.45f; 

        [Header("Resistance")]
        [SerializeField] ResistanceMode resistanceMode = ResistanceMode.Scattered;
        [SerializeField, Range(0f, 0.95f)] float maxResistance = 0.8f;
        [SerializeField] float clumpScale = 0.3f; 

        [Header("Spread")]
        [SerializeField] SpreadSettings settings = new SpreadSettings(); 
        [SerializeField] bool centerStart = true;

        void Start()
        {
            //click a grid tile to start spread
            var grid = new WorldGrid(width, height, seed);
            if (readTilemap) ReadTilemap(grid);
            else BuildTerrain(grid);
            BuildResistance(grid);
            grid.GetField("spread"); 
            grid.GetField("display");
            if (centerStart && !readTilemap)
            {
                int cx = width / 2, cy = height / 2;
                float[] terrain = grid.GetField("terrain");
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        terrain[grid.Index(cx + dx, cy + dy)] = SpreadSystem.Grass; 
                grid.GetField("spread")[grid.Index(cx, cy)] = 1f;
            }
            //update grid on ticks
            var tickEngine = GetComponent<TickEngine>();
            tickEngine.Init(grid);
            tickEngine.AddSystem(new SpreadSystem(settings));
            tickEngine.OnTick += () =>
            {
                SpreadSystem.UpdateDisplay(grid);
                tileRenderer.Draw(grid);
            };
            SpreadSystem.UpdateDisplay(grid);
            tileRenderer.Draw(grid);
        }

        //skip any unrecognized tiles
        void ReadTilemap(WorldGrid grid)
        {
            if (sourceTilemap == null)
            {
                Debug.LogError("SpreadTestBootstrap: sourceTilemap is not assigned.");
                return;
            }

            float[] terrain = grid.GetField("terrain");
            int unrecognized = 0;

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    TileBase t = sourceTilemap.GetTile(new Vector3Int(x, y, 0));
                    float code;
                    if (t == grassTile) code = SpreadSystem.Grass;
                    else if (t == deadGrassTile) code = SpreadSystem.DeadGrass;
                    else
                    {
                        code = SpreadSystem.Dirt;
                        if (t != dirtTile) unrecognized++;
                    }
                    terrain[grid.Index(x, y)] = code;
                }
            }

            if (unrecognized > 0)
                Debug.LogWarning($"SpreadTestBootstrap: {unrecognized} cells were empty.");
        }

        void BuildTerrain(WorldGrid grid)
        {
            float[] terrain = grid.GetField("terrain");
            float offX = (float)grid.Rng.NextDouble() * 1000f;
            float offY = (float)grid.Rng.NextDouble() * 1000f;

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    float n = Mathf.PerlinNoise(x * terrainScale + offX, y * terrainScale + offY);
                    terrain[grid.Index(x, y)] = n < dirtBelow ? SpreadSystem.Dirt
                                              : n < deadGrassBelow ? SpreadSystem.DeadGrass
                                              : SpreadSystem.Grass;
                }
            }
        }

        void BuildResistance(WorldGrid grid)
        {
            float[] resistance = grid.GetField("resistance");
            float offX = (float)grid.Rng.NextDouble() * 1000f;
            float offY = (float)grid.Rng.NextDouble() * 1000f;

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    float r = resistanceMode == ResistanceMode.Clumpy
                        ? Mathf.PerlinNoise(x * clumpScale + offX, y * clumpScale + offY)
                        : (float)grid.Rng.NextDouble();
                    resistance[grid.Index(x, y)] = r * maxResistance;
                }
            }
        }
    }
}