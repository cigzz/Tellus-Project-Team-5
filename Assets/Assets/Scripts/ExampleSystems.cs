using UnityEngine;

namespace WorldSim
{
    public class WaterSystem : ITickSystem
    {
        const float FlowRate = 0.2f; // how quickly surface level equalizes with neighbors each tick

        public void Tick(WorldGrid grid, int tickNumber)
        {
            float[] height = grid.GetField("height");       // static terrain, set by HeightmapLoader
            float[] water = grid.GetField("water");         // stable snapshot for this tick
            float[] nextWater = grid.GetNextField("water"); // write buffer

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int i = grid.Index(x, y);
                    int l = grid.Left(x, y), r = grid.Right(x, y), u = grid.Up(x, y), d = grid.Down(x, y);

                    float surface = height[i] + water[i];
                    float neighborSurfaceAvg = (
                        (height[l] + water[l]) + (height[r] + water[r]) +
                        (height[u] + water[u]) + (height[d] + water[d])
                    ) / 4f;

                    float newSurface = surface + (neighborSurfaceAvg - surface) * FlowRate;
                    float newWater = newSurface - height[i];
                    if (newWater < 0f) newWater = 0f; // can't flow below the ground

                    if (grid.Rng.NextDouble() < 0.01)
                        newWater += 1f; // small chance of rain

                    nextWater[i] = newWater; // double buffer requires writing every cell
                }
            }

            grid.SwapField("water");
        }
        public static float TotalVolume(WorldGrid grid)
        {
            float[] water = grid.GetField("water");
            float total = 0f;
            for (int i = 0; i < water.Length; i++)
                total += water[i];
            return total;
        }
    }
    public class TemperatureSystem : ITickSystem
    {
        public void Tick(WorldGrid grid, int tickNumber)
        {
            float[] temperature = grid.GetField("temperature", defaultValue: 20f);
            float[] water = grid.GetField("water"); // reads a field another system owns

            for (int i = 0; i < grid.CellCount; i++)
            {
                temperature[i] -= water[i] * 0.01f;            // water cools a cell down slightly
                temperature[i] += (20f - temperature[i]) * 0.05f; // drift toward baseline
            }
        }
    }
}
