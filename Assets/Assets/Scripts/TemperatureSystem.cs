using UnityEngine;

namespace WorldSim
{
    // Relaxes temperature toward a greenhouse/albedo/transpiration equilibrium,
    // then exchanges heat with the four neighboring cells.
    public class TemperatureSystem : ITickSystem
    {
        readonly PlantModel model;

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

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                int i = grid.Index(x, y);
                float equilibrium = env.TOff
                                 + env.TSpan * sunlight[i] * (1f - albedo[i])
                                 + env.KCo2 * Mathf.Log(Mathf.Max(co2[i], 1f) / env.C0)
                                 - env.KCool * transpirationTrait * biomass[i] * Mathf.Min(1f, water[i] / 0.3f);
                float neighborMean = (temperature[grid.Left(x, y)] + temperature[grid.Right(x, y)] +
                                      temperature[grid.Up(x, y)] + temperature[grid.Down(x, y)]) * 0.25f;
                nextTemperature[i] = temperature[i]
                                   + env.Lambda * (equilibrium - temperature[i])
                                   + env.DT * (neighborMean - temperature[i]);
            }

            grid.SwapField("temperature");
        }
    }
}
