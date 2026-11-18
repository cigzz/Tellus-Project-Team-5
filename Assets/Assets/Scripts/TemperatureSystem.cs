using UnityEngine;

namespace WorldSim
{
    // spreads temperature toward equilibrium and spreads heat to nearby cells
    // uses perlin noise for blob and weather
    public class TemperatureSystem : ITickSystem
    {
        // --- Tuning ---
        const int OuterRingSize = 2;            // Outer ring distance (above 4 may look odd)
        const float FarWeight = 0.5f;      // Outer ring strength (0 = off, 1 = same as direct neighbors)
        const float Smoothness = 0.5f;         // Random lean strength (0 = none, 0.2 subtle, 0.5+ jagged)
        const float NoiseScale = 0.15f;    // Patch size (smaller = bigger patches)
        const float NoiseDrift = 0.05f;    // Patch movement per tick (0 = frozen)
        const float NoiseContrast = 2f;    // Evens out direction picks (1 = off)

        readonly PlantModel model;
        float noiseOffset;                 // Per-world noise start, set on first tick
        bool noiseOffsetSet;

        public TemperatureSystem(PlantModel model) { this.model = model; }

        public void Tick(WorldGrid grid, int tickNumber)
        {
            EnvParams env = model.Env;
            float transpirationTrait = model.Traits.Transpiration;

            float[] temperature = grid.GetField("temperature");
            float[] nextTemperature = grid.GetNextField("temperature");
            float[] sunlight = grid.GetField("insolation");
            float[] albedo = grid.GetField("albedo");
            float[] co2 = grid.GetField("co2");
            float[] biomass = grid.GetField("biomass");
            float[] water = grid.GetField("water");

            if (!noiseOffsetSet)
            {
                noiseOffset = (float)grid.Rng.NextDouble() * 1000f;
                noiseOffsetSet = true;
            }

            int width = grid.Width;
            int height = grid.Height;
            float totalWeight = 4f + 4f * FarWeight;
            float drift = tickNumber * NoiseDrift;

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int i = grid.Index(x, y);

                // Target temp: base + absorbed sun + CO2 warming - plant cooling
                float equilibrium = env.TOff
                                 + env.TSpan * sunlight[i] * (1f - albedo[i])
                                 + env.KCo2 * Mathf.Log(Mathf.Max(co2[i], 1f) / env.C0)
                                 - env.KCool * transpirationTrait * biomass[i] * Mathf.Min(1f, water[i] / 0.3f);

                // Neighbors: X wraps, Y clamps at poles
                int left  = grid.Left(x, y);
                int right = grid.Right(x, y);
                int up    = grid.Up(x, y);
                int down  = grid.Down(x, y);
                int farLeft  = grid.Index((x - OuterRingSize + width) % width, y);
                int farRight = grid.Index((x + OuterRingSize) % width, y);
                int farUp    = grid.Index(x, Mathf.Min(y + OuterRingSize, height - 1));
                int farDown  = grid.Index(x, Mathf.Max(y - OuterRingSize, 0));

                // Weighted average of both rings
                float near = temperature[left] + temperature[right] + temperature[up] + temperature[down];
                float far  = temperature[farLeft] + temperature[farRight] + temperature[farUp] + temperature[farDown];
                float neighborMean = (near + FarWeight * far) / totalWeight;

                // Noise picks a direction (0-3); nearby cells pick alike, forming drifting patches
                float n = Mathf.PerlinNoise(x * NoiseScale + noiseOffset,
                                            y * NoiseScale + noiseOffset + drift);
                n = Mathf.Clamp01((n - 0.5f) * NoiseContrast + 0.5f);
                int direction = (int)(n * 3.999f);
                int pick = direction == 0 ? left : direction == 1 ? right : direction == 2 ? up : down;

                // Lean average toward the picked neighbor
                neighborMean += Smoothness * (temperature[pick] - neighborMean);

                // Step toward equilibrium (Lambda) and toward neighbors (DT)
                nextTemperature[i] = temperature[i]
                                   + env.Lambda * (equilibrium - temperature[i])
                                   + env.DT * (neighborMean - temperature[i]);
            }

            grid.SwapField("temperature");
        }
    }
}