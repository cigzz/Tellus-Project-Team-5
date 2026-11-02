using UnityEngine;

namespace WorldSim
{
    // Relative humidity (0..1) gains moisture from evaporation and plants,
    // diffuses between neighboring cells, then condenses excess into rain.
    public class HumiditySystem : ITickSystem
    {
        const float VaporDiffusion = 0.02f;

        public void Tick(WorldGrid grid, int tickNumber)
        {
            float[] humidity = grid.GetField("humidity", defaultValue: 0.55f);
            float[] next = grid.GetNextField("humidity");
            float[] water = grid.GetField("water");
            float[] soil = grid.GetField("soilMoisture", defaultValue: 25f);
            float[] temperature = grid.GetField("temperature", defaultValue: 15f);
            float[] plants = grid.GetField("plantGrowth");
            float[] precipitation = grid.GetField("precipitation");

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                int i = grid.Index(x, y);
                float neighborMean = (humidity[grid.Left(x, y)] + humidity[grid.Right(x, y)] +
                                      humidity[grid.Up(x, y)] + humidity[grid.Down(x, y)]) * 0.25f;
                float warmth = Mathf.Clamp01((temperature[i] + 5f) / 35f);
                float evaporation = (water[i] * 0.00012f + soil[i] * 0.000012f) * warmth;
                float transpiration = plants[i] * 0.000015f * warmth;
                float vapor = Mathf.Clamp01(humidity[i] + (neighborMean - humidity[i]) * VaporDiffusion + evaporation + transpiration);

                float saturation = Mathf.Clamp(0.78f + temperature[i] * 0.004f, 0.45f, 0.98f);
                float excess = Mathf.Max(0f, vapor - saturation);
                precipitation[i] = excess * 0.08f;
                next[i] = Mathf.Clamp01(vapor - excess * 0.35f);
            }

            grid.SwapField("humidity");
        }
    }
}
