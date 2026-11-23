using UnityEngine;

namespace WorldSim
{
    // Shared terrain helpers used by the water and albedo systems.
    //
    // "heightNorm" is the heightmap remapped to 0 (lowest tile on the planet) .. 1 (highest tile),
    // so tuning values don't depend on the min/max heights chosen in WorldBootstrap.
    public static class TerrainFields
    {
        public const string HeightNorm = "heightNorm";

        // Builds "heightNorm" from "height" once and returns it. Safe to call from several systems.
        // A flat or missing heightmap gives 0.5 everywhere so nothing divides by zero.
        public static float[] GetNormalizedHeight(WorldGrid grid)
        {
            if (grid.HasField(HeightNorm)) return grid.GetField(HeightNorm);

            float[] height = grid.GetField("height");
            float[] norm = grid.GetField(HeightNorm);

            float min = float.MaxValue, max = float.MinValue;
            for (int i = 0; i < grid.CellCount; i++)
            {
                if (height[i] < min) min = height[i];
                if (height[i] > max) max = height[i];
            }

            float range = max - min;
            for (int i = 0; i < grid.CellCount; i++)
                norm[i] = range > 1e-5f ? (height[i] - min) / range : 0.5f;

            return norm;
        }

        // 0 when x <= a, 1 when x >= b, linear in between.
        public static float Ramp(float x, float a, float b) => Mathf.Clamp01((x - a) / (b - a));
    }
}