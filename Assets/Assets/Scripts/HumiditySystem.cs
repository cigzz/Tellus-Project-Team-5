namespace WorldSim
{
    // Spreads atmospheric moisture between neighboring cells after each cell's
    // local evaporation/transpiration/rain exchange has run in WaterSystem.
    public class HumiditySystem : ITickSystem
    {
        readonly PlantModel model;

        public HumiditySystem(PlantModel model) { this.model = model; }

        public void Tick(WorldGrid grid, int tickNumber)
        {
            FieldOps.Diffuse(grid, "humidity", model.Env.DH);
        }
    }
}
