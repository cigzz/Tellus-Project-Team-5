using UnityEngine;

namespace WorldSim
{
    // Kept for the simple seed-spread demo scenes. The main world uses the
    // PlantModel growth and spread systems in PlantSystems.cs.
    public class PlantGrowthSystem : ITickSystem
    {
        const float SpreadThreshold = 0.5f;
        const float SeedlingGrowth = 0.1f;

        public void Tick(WorldGrid grid, int tickNumber)
        {
            float[] growth = grid.GetField("plantGrowth");
            float[] nextGrowth = grid.GetNextField("plantGrowth");
            float[] water = grid.GetField("water");
            float[] temperature = grid.GetField("temperature", defaultValue: 20f);

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                int i = grid.Index(x, y);
                float value = growth[i];
                if (water[i] > 0f && temperature[i] > 10f)
                {
                    if (value > 0f)
                    {
                        value = Mathf.Min(value + 0.1f, 1f);
                    }
                    else
                    {
                        float neighborGrowth = Mathf.Max(
                            growth[grid.Left(x, y)], growth[grid.Right(x, y)],
                            growth[grid.Up(x, y)], growth[grid.Down(x, y)]);
                        if (neighborGrowth > SpreadThreshold)
                            value = SeedlingGrowth;
                    }
                }

                nextGrowth[i] = value;
            }

            grid.SwapField("plantGrowth");
        }
    }
}
