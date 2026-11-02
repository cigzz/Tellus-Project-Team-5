using UnityEngine;

namespace WorldSim
{
    public class PlantGrowthSystem : ITickSystem
    {
        const float SpreadThreshold = 0.5f; // how grown a neighbor must be before it can spread
        const float SeedlingGrowth = 0.1f;  // starting growth for a newly spread cell

        public void Tick(WorldGrid grid, int tickNumber)
        {
            float[] growth = grid.GetField("plantGrowth");        // stable snapshot for this tick
            float[] nextGrowth = grid.GetNextField("plantGrowth"); // write buffer
            float[] water = grid.GetField("water");
            float[] temperature = grid.GetField("temperature", defaultValue: 20f);

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int i = grid.Index(x, y);
                    bool goodConditions = water[i] > 0f && temperature[i] > 10f;
                    float value = growth[i];

                    if (goodConditions)
                    {
                        if (value > 0f)
                        {
                            value = Mathf.Min(value + 0.1f, 1f); // existing plant grows
                        }
                        else
                        {
                            // Empty cell: only takes root if a strongly-grown neighbor spreads to it.
                            float neighborGrowth = Mathf.Max(
                                growth[grid.Left(x, y)], growth[grid.Right(x, y)],
                                growth[grid.Up(x, y)], growth[grid.Down(x, y)]);
                            if (neighborGrowth > SpreadThreshold)
                                value = SeedlingGrowth;
                        }
                    }

                    nextGrowth[i] = value; // double buffering requires writing every cell
                }
            }

            grid.SwapField("plantGrowth");
        }
    }
}
