using System;
using UnityEngine;

namespace WorldSim
{
    // Surface water and soil moisture use millimetres of water per cell.
    public class WaterSystem : ITickSystem
    {
        const float RunoffRate = 0.08f;
        const float MaxRunoffPerEdge = 2f;
        const float SoilCapacityMm = 150f;
        float[] outgoing = Array.Empty<float>();

        public void Tick(WorldGrid grid, int tickNumber)
        {
            float[] height = grid.GetField("height");
            float[] water = grid.GetField("water");
            float[] soil = grid.GetField("soilMoisture", defaultValue: 25f);
            float[] nextWater = grid.GetNextField("water");
            float[] nextSoil = grid.GetNextField("soilMoisture");
            if (outgoing.Length != grid.CellCount) Array.Resize(ref outgoing, grid.CellCount);
            Array.Clear(nextWater, 0, nextWater.Length);
            Array.Clear(outgoing, 0, outgoing.Length);

            // Route runoff over each shared edge once. X wraps like a planet;
            // the north and south edges are closed boundaries.
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width - 1; x++)
                {
                    int i = grid.Index(x, y);
                    FlowPair(i, grid.Index(x + 1, y), height, water, nextWater);
                }
                if (grid.Width > 2)
                    FlowPair(grid.Index(grid.Width - 1, y), grid.Index(0, y), height, water, nextWater);
            }
            for (int y = 0; y < grid.Height - 1; y++)
            for (int x = 0; x < grid.Width; x++)
                FlowPair(grid.Index(x, y), grid.Index(x, y + 1), height, water, nextWater);

            float[] rain = grid.GetField("precipitation");
            float[] temperature = grid.GetField("temperature", defaultValue: 15f);
            for (int i = 0; i < grid.CellCount; i++)
            {
                float surface = Mathf.Max(0f, water[i] + nextWater[i] + rain[i]);
                float soilBeforeInfiltration = Mathf.Min(SoilCapacityMm, soil[i]);
                float infiltration = Mathf.Min(surface, (SoilCapacityMm - soilBeforeInfiltration) * 0.004f);
                surface -= infiltration;
                float soilAfterInfiltration = soilBeforeInfiltration + infiltration;

                float warmth = Mathf.Clamp01((temperature[i] + 5f) / 35f);
                float surfaceEvaporation = Mathf.Min(surface, surface * 0.00015f * warmth);
                float soilEvaporation = Mathf.Min(soilAfterInfiltration, soilAfterInfiltration * 0.000025f * warmth);
                nextWater[i] = Mathf.Max(0f, surface - surfaceEvaporation);
                nextSoil[i] = Mathf.Clamp(soilAfterInfiltration - soilEvaporation, 0f, SoilCapacityMm);
            }

            grid.SwapField("water");
            grid.SwapField("soilMoisture");
        }

        void FlowPair(int a, int b, float[] height, float[] water, float[] change)
        {
            float surfaceA = height[a] + water[a] * 0.001f;
            float surfaceB = height[b] + water[b] * 0.001f;
            if (Mathf.Approximately(surfaceA, surfaceB)) return;

            int source = surfaceA > surfaceB ? a : b;
            int target = source == a ? b : a;
            float headMm = Mathf.Abs(surfaceA - surfaceB) * 1000f;
            float available = Mathf.Max(0f, water[source] - outgoing[source]);
            float moved = Mathf.Min(available, Mathf.Min(MaxRunoffPerEdge, headMm * RunoffRate));
            change[source] -= moved;
            change[target] += moved;
            outgoing[source] += moved;
        }

        public static float TotalVolume(WorldGrid grid)
        {
            float[] water = grid.GetField("water");
            float total = 0f;
            for (int i = 0; i < water.Length; i++) total += water[i];
            return total;
        }
    }
}
