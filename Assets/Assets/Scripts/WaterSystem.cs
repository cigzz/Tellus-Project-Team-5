using UnityEngine;

namespace WorldSim
{
    // Exchanges water with humidity through evaporation, plant transpiration,
    // and rain. The local exchange conserves the combined water + humidity pool.
    public class WaterSystem : ITickSystem
    {
        readonly PlantModel model;

        public WaterSystem(PlantModel model) { this.model = model; }

        public void Tick(WorldGrid grid, int tickNumber)
        {
            EnvParams env = model.Env;
            PlantTraits traits = model.Traits;

            float[] temperature = grid.GetField("temperature");
            float[] water = grid.GetField("water");
            float[] humidity = grid.GetField("humidity");
            float[] biomass = grid.GetField("biomass");

            for (int i = 0; i < grid.CellCount; i++)
            {
                float warmth = Mathf.Clamp01((temperature[i] + 5f) / 45f);
                float canopyBiomass = traits.Canopy * biomass[i];
                float evaporation = env.Ke * warmth * water[i] * (1f - 0.4f * canopyBiomass);
                float transpiration = env.Kt * traits.Transpiration * canopyBiomass * water[i];
                float rain = env.Kr * humidity[i];

                water[i] += rain - evaporation - transpiration;
                humidity[i] += evaporation + transpiration - rain;
            }

            FieldOps.Diffuse(grid, "water", env.DW);
        }
    }
}
