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

        [Header("Plant model (Tellus Growth Model)")]
        // The doc's rates assume 10 fast ticks per second and a slow tick every 10th fast tick.
        // Set the TickEngine to Ticks Per Second = 10 and Slow Group Interval = 10 to match.
        [SerializeField] EnvParams env = new EnvParams();
        [SerializeField] GrowthParams growth = new GrowthParams();

        // Tiers, DNA and the latest planet stats live here. PlantPlacer and the evolution menu read it.
        public PlantModel Model { get; private set; }

        void Start()
        {
            var grid = new WorldGrid(width, height, seed: 12345);

            string heightmapPath = Path.Combine(Application.streamingAssetsPath, heightmapFileName);
            HeightmapLoader.Apply(grid, heightmapPath, minHeight, maxHeight);

            // Creates and fills every field the plant model uses (temperature starts at its bare-ground equilibrium).
            Model = new PlantModel(env, growth);
            Model.InitGrid(grid);

            var tickEngine = GetComponent<TickEngine>();
            tickEngine.Init(grid);

            // every tick, order matters
            tickEngine.AddSystem(new TemperatureSystem(Model));
            tickEngine.AddSystem(new WaterSystem(Model));
            tickEngine.AddSystem(new HumiditySystem(Model));
            tickEngine.AddSystem(new CarbonMixingSystem(Model));

            // slow tick: decides who lives, order matters
            tickEngine.AddSlowSystem(new SuitabilitySystem(Model));
            tickEngine.AddSlowSystem(new GrowthSystem(Model));
            tickEngine.AddSlowSystem(new SpreadSystem(Model));
            tickEngine.AddSlowSystem(new AlbedoSystem(Model));
            tickEngine.AddSlowSystem(new AggregateSystem(Model));
            StatDisplayManager.SetModel(Model);

            // Redraw both views every tick.
            tickEngine.OnTick += () =>
            {
                if (renderer != null) renderer.Draw(grid);
                if (heightRenderer != null) heightRenderer.Draw(grid);
            };
        }
    }
}
