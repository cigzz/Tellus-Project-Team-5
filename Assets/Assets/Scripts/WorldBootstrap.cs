using System.IO;
using UnityEngine;

namespace WorldSim
{
    [RequireComponent(typeof(TickEngine))]
    public class WorldBootstrap : MonoBehaviour
    {
        [SerializeField] int width = 245;
        [SerializeField] int height = 245;
        [SerializeField] GridTilemapRenderer renderer;        // discrete tiles, e.g. plant growth
        [SerializeField] GridTextureRenderer heightRenderer;  // continuous grayscale, for height

        [Header("Heightmap")]
        [SerializeField] string heightmapFileName = "heightmap.png"; // relative to StreamingAssets
        [SerializeField] float minHeight = 0f;   // height for pure black
        [SerializeField] float maxHeight = 100f; // height for pure white

        void Start()
        {
            var grid = new WorldGrid(width, height, seed: 12345);

            // Height must be loaded before any system reads it - WaterSystem reads
            // "height" from its very first tick to know where water should pool.
            string heightmapPath = Path.Combine(Application.streamingAssetsPath, heightmapFileName);
            HeightmapLoader.Apply(grid, heightmapPath, minHeight, maxHeight);

            // Start with stable climate fields; the simulation evolves them each tick.
            grid.GetField("temperature", defaultValue: 15f);
            grid.GetField("humidity", defaultValue: 0.55f);
            grid.GetField("soilMoisture", defaultValue: 25f);
            grid.GetField("precipitation");

            var tickEngine = GetComponent<TickEngine>();
            tickEngine.Init(grid);

            // World clock - counts 1 in-game second every 8 ticks
            var clock = GetComponent<WorldClock>();
            if (clock == null) clock = gameObject.AddComponent<WorldClock>();
            tickEngine.AddSystem(clock);

            // every tick, order matters
            tickEngine.AddSystem(new WaterSystem());
            tickEngine.AddSystem(new TemperatureSystem());
            tickEngine.AddSystem(new HumiditySystem());

            // slow tick only runs every 3rd tick (set on the TickEngine component)
            tickEngine.AddSlowSystem(new PlantGrowthSystem());

            // Redraw both views every tick.
            tickEngine.OnTick += () =>
            {
                if (renderer != null) renderer.Draw(grid);
                if (heightRenderer != null) heightRenderer.Draw(grid);
            };
        }
    }
}
