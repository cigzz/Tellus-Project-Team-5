using UnityEngine;

namespace WorldSim
{
    // Temperature. Every tick each tile steps toward a target temperature and blends with its neighbours.
    //
    //   1. Local target  = TOff + absorbed sun + CO2 greenhouse (planet mean) + water-vapour greenhouse
    //                      - transpiration cooling (transpiration trait * leaf area, less on dry soil) - altitude cooling
    //   2. Heat transport: the target is blended toward its latitude-row mean (ZonalMix; stronger near the poles,
    //                      where a row is a short circle) and then toward the planet mean (HeatTransport).
    //   3. Ice surfaces:  on ice/snow the target can't rise above IceSurfaceMax, because the heat goes into melting.
    //                      Keeps polar summers near freezing while the ice lasts.
    //   4. T += Lambda * (target - T) + DT * (neighbourMean - T)
    //
    // Mixing the target (not the temperature) makes ZonalMix and HeatTransport plain fractions that don't depend
    // on Lambda; Lambda only sets how fast tiles respond (thermal inertia).
    // Weather: a drifting Perlin field leans each tile's neighbour average toward one neighbour, forming patches.
    //
    // Cost: one pass over the grid. Row offsets are worked out once per row, x wraps without %, and the planet mean
    // used by HeatTransport is summed during the pass and applied on the next tick.
    public class TemperatureSystem : ITickSystem
    {
        // --- Tuning ---
        const int OuterRingSize = 3;            // Outer ring distance (above 4 may look odd)
        const float FarWeight = 0.75f;      // Outer ring strength (0 = off, 1 = same as direct neighbors)
        const float Smoothness = 0.5f;         // Random lean strength (0 = none, 0.2 subtle, 0.5+ jagged)
        const float NoiseScale = 0.25f;    // Patch size (smaller = bigger patches)
        const float NoiseDrift = 0.03f;    // Patch movement per tick (0 = frozen)
        const float NoiseContrast = 2f;    // Evens out direction picks (1 = off)
        const float LapseRate = 15f;       // deg C colder on the highest peak than at the lowest point
        const float IceSurfaceMax = 2f;    // deg C an ice-covered tile's target can reach (melting holds it near 0)

        readonly PlantModel model;
        readonly float iceFull;            // ice amount that fully covers a tile (AlbedoParams.IceFull)
        float noiseOffset;                 // Per-world noise start, set on first tick
        bool noiseOffsetSet;
        float[] rowTarget;                 // scratch: local targets for one row
        float planetMeanTarget;            // mean target from the previous tick, for HeatTransport

        public TemperatureSystem(PlantModel model, AlbedoParams albedo = null)
        {
            this.model = model;
            iceFull = Mathf.Max(albedo != null ? albedo.IceFull : 1.5f, 1e-3f);
        }

        public void Tick(WorldGrid grid, int tickNumber)
        {
            EnvParams env = model.Env;
            PlantTraits traits = model.Traits;

            float[] temperature = grid.GetField("temperature");
            float[] nextTemperature = grid.GetNextField("temperature");
            float[] sunlight = grid.GetField("insolation");
            float[] albedo = grid.GetField("albedo");
            float[] biomass = grid.GetField("biomass");
            float[] water = grid.GetField("water");
            float[] ice = grid.GetField("ice");
            float[] hNorm = TerrainFields.GetNormalizedHeight(grid);

            int width = grid.Width;
            int height = grid.Height;

            if (!noiseOffsetSet)
            {
                noiseOffset = (float)grid.Rng.NextDouble() * 1000f;
                noiseOffsetSet = true;

                // First tick: start the planet mean from the current temperatures.
                float sum = 0f;
                for (int i = 0; i < grid.CellCount; i++) sum += temperature[i];
                planetMeanTarget = sum / grid.CellCount;
            }
            if (rowTarget == null || rowTarget.Length != width) rowTarget = new float[width];

            // Planet-wide terms, once per tick. CO2 is well mixed, so its greenhouse uses the planet mean (CarbonSystem).
            float baseTarget = env.TOff
                             + env.KCo2 * Mathf.Log(Mathf.Max(model.MeanCo2, 1f) / env.C0)
                             + WaterSystem.Latest.VapourWarming;
            float heatTransport = Mathf.Clamp01(env.HeatTransport);
            float planetMean = planetMeanTarget;
            float targetSum = 0f;

            float totalWeight = 4f + 4f * FarWeight;
            float drift = tickNumber * NoiseDrift;

            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                int rowUp = Mathf.Min(y + 1, height - 1) * width;          // Y clamps at the poles
                int rowDown = Mathf.Max(y - 1, 0) * width;
                int rowFarUp = Mathf.Min(y + OuterRingSize, height - 1) * width;
                int rowFarDown = Mathf.Max(y - OuterRingSize, 0) * width;

                // Pass 1: local targets for this row and their mean.
                float rowSum = 0f;
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;
                    float t = baseTarget
                            + env.TSpan * sunlight[i] * (1f - albedo[i])
                            - env.KCool * traits.Transpiration * traits.LeafArea(biomass[i]) * Mathf.Min(1f, water[i] / 0.3f)
                            - LapseRate * hNorm[i];
                    rowTarget[x] = t;
                    rowSum += t;
                }
                float rowMean = rowSum / width;

                // A row is a circle of latitude whose length shrinks with cos(latitude), so it mixes faster near the poles.
                float lat = ((y + 0.5f) / height) * Mathf.PI - Mathf.PI / 2f;
                float zonal = Mathf.Clamp01(env.ZonalMix / Mathf.Max(Mathf.Cos(lat), 1e-3f));

                // Pass 2: blend the target, cap it on ice, step the temperature.
                for (int x = 0; x < width; x++)
                {
                    int i = row + x;

                    float target = rowTarget[x] + zonal * (rowMean - rowTarget[x]);
                    target += heatTransport * (planetMean - target);
                    float iceCover = Mathf.Clamp01(ice[i] / iceFull);
                    if (target > IceSurfaceMax) target += iceCover * (IceSurfaceMax - target);
                    targetSum += target;

                    // Neighbours: X wraps, Y clamps at poles
                    int xl = x == 0 ? width - 1 : x - 1;
                    int xr = x == width - 1 ? 0 : x + 1;
                    int xfl = x - OuterRingSize; if (xfl < 0) xfl += width;
                    int xfr = x + OuterRingSize; if (xfr >= width) xfr -= width;
                    int left = row + xl, right = row + xr, up = rowUp + x, down = rowDown + x;

                    // Weighted average of both rings
                    float near = temperature[left] + temperature[right] + temperature[up] + temperature[down];
                    float far  = temperature[row + xfl] + temperature[row + xfr] + temperature[rowFarUp + x] + temperature[rowFarDown + x];
                    float neighborMean = (near + FarWeight * far) / totalWeight;

                    // Noise picks a direction (0-3); nearby cells pick alike, forming drifting patches
                    float n = Mathf.PerlinNoise(x * NoiseScale + noiseOffset,
                                                y * NoiseScale + noiseOffset + drift);
                    n = Mathf.Clamp01((n - 0.5f) * NoiseContrast + 0.5f);
                    int direction = (int)(n * 3.999f);
                    int pick = direction == 0 ? left : direction == 1 ? right : direction == 2 ? up : down;

                    // Lean average toward the picked neighbor
                    neighborMean += Smoothness * (temperature[pick] - neighborMean);

                    // Step toward the target (Lambda) and toward neighbors (DT)
                    nextTemperature[i] = temperature[i]
                                       + env.Lambda * (target - temperature[i])
                                       + env.DT * (neighborMean - temperature[i]);
                }
            }

            planetMeanTarget = targetSum / grid.CellCount;
            grid.SwapField("temperature");
        }
    }
}
