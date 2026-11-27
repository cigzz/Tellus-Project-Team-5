using TMPro;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

namespace WorldSim
{
    [RequireComponent(typeof(TickEngine))]
    public class WorldBootstrap : MonoBehaviour
    {
        public TMP_Text clockTextGO;
        [SerializeField, Min(0.01f)] float referenceTicksPerSecond = 8f; // ticks per in-game clock second
        [SerializeField] int width = 245;
        [SerializeField] int height = 245;
        [SerializeField] GridTilemapRenderer renderer;        // discrete tiles, e.g. plant growth
        [SerializeField] GridTextureRenderer heightRenderer;  // continuous grayscale, for height
        [SerializeField] ColorBlendTilemapRenderer colorRenderer; // blended-color field view

        [Header("Heightmap")]
        [SerializeField] string heightmapFileName = "heightmap.png"; // relative to StreamingAssets
        [SerializeField] float minHeight = 0f;   // height for pure black
        [SerializeField] float maxHeight = 100f; // height for pure white

        [Header("Plant model (Tellus Growth Model)")]
        [SerializeField] EnvParams env = new EnvParams();
        [SerializeField] GrowthParams growth = new GrowthParams();

        [Header("Water cycle")]
        [SerializeField] WaterParams water = new WaterParams();

        [Header("Albedo")]
        [SerializeField] AlbedoParams albedo = new AlbedoParams();

        [Header("Coverage victory")]
            [SerializeField, Range(0f, 1f)] float victoryMinCoverage = 0.4f;
        EndGameSystem endGameSystem;

        [Header("Tutorial")]
        [SerializeField] bool showTutorial;

        // Tiers, DNA and the latest planet stats live here. PlantPlacer and the evolution menu read it.
        public PlantModel Model { get; private set; }

        void Start()
        {
            var grid = new WorldGrid(width, height, seed: 12345);

            string heightmapPath = Path.Combine(Application.streamingAssetsPath, heightmapFileName);
            HeightmapLoader.Apply(grid, heightmapPath, minHeight, maxHeight);

            // Terrain albedo first: InitGrid keeps an existing "albedo" field, so starting temperatures
            // are computed from the height-based albedo instead of a flat A0.
            TerrainAlbedoSystem.Initialize(grid, albedo, env.AMax);

            // Creates and fills every field the plant model uses (temperature starts at its bare-ground equilibrium).
            Model = new PlantModel(env, growth);
            Model.InitGrid(grid);

            var tickEngine = GetComponent<TickEngine>();
            tickEngine.Init(grid);

            // every tick, order matters
            tickEngine.AddSystem(new InsolationSystem(Model));   // first: everything below reads this tick's sunlight
            tickEngine.AddSystem(new TemperatureSystem(Model));
            tickEngine.AddSystem(new WaterSystem(Model, water));
            tickEngine.AddSystem(new HumiditySystem(Model));
            tickEngine.AddSystem(new CarbonMixingSystem(Model));

            // slow tick: decides who lives, order matters
            tickEngine.AddSlowSystem(new SuitabilitySystem(Model));
            tickEngine.AddSlowSystem(new GrowthSystem(Model));
            tickEngine.AddSlowSystem(new SpreadSystem(Model));
            tickEngine.AddSlowSystem(new TerrainAlbedoSystem(Model, albedo));
            tickEngine.AddSlowSystem(new AggregateSystem(Model));
            tickEngine.AddSlowSystem(new WorldClock(clockTextGO, referenceTicksPerSecond));
            StatDisplayManager.SetModel(Model);

            // Check coverage
            endGameSystem = new EndGameSystem(Model, tickEngine, victoryMinCoverage);
            tickEngine.AddSlowSystem(endGameSystem);

            // Redraw both views every tick.
            tickEngine.OnTick += () =>
            {
                if (renderer != null) renderer.Draw(grid);
                if (heightRenderer != null) heightRenderer.Draw(grid);
                if (colorRenderer != null) colorRenderer.Draw(grid);
            };

            if (showTutorial)
                GameplayTutorial.TryStart(this, victoryMinCoverage);
        }
    
//PRESS F9 TO WIN OR F10 TO LOSE OR IN GAMEMANAGER MENU
#if UNITY_EDITOR
        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f9Key.wasPressedThisFrame) PreviewVictory();
            if (keyboard.f10Key.wasPressedThisFrame) PreviewExtinction();
        }

        [ContextMenu("Preview Victory (Play Mode)")]
        void PreviewVictory() => PreviewEnding(true);

        [ContextMenu("Preview Extinction (Play Mode)")]
        void PreviewExtinction() => PreviewEnding(false);

        void PreviewEnding(bool won)
        {
            if (!Application.isPlaying || endGameSystem == null)
            {
                Debug.LogWarning("Enter Play mode and let GameManager initialize before previewing an ending.", this);
                return;
            }

            endGameSystem.PreviewEnding(won);
        }
#endif
    }
}
