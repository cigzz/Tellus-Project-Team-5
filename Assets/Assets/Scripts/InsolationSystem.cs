using UnityEngine;

namespace WorldSim
{
    // Sunlight reaching each tile, with seasons. Writes the "insolation" field (0-1).
    //
    // Uses the standard daily-mean insolation for latitude phi and solar declination delta, normalised so the
    // equator at equinox is 1:
    //   cos h0 = -tan(phi) * tan(delta)          h0 = half the day length (clamped: polar day / polar night)
    //   Q      = h0 * sin(phi) * sin(delta) + cos(phi) * cos(delta) * sin(h0)
    // With no tilt Q = cos(phi), the old static map. The declination swings through the year:
    //   delta = SeasonTilt * sin(2 pi * tick / SeasonLengthTicks)
    // so one hemisphere gets long summer days and a high sun while the other gets winter, then they swap.
    // Q is shaped the same way the old map was, so existing tuning carries over:
    //   insolation = lerp(PoleSun, 1, Q^CosWeight * SunGain), capped to 0-1
    // CosWeight < 1 flattens the drop-off toward the poles; 1 leaves the cosine as is.
    //
    // Sunlight depends only on latitude, so it is worked out once per row. Runs first in the fast group so
    // temperature, water and suitability all see this tick's sunlight.
    //   SeasonTilt        = intensity (0 turns seasons off)
    //   SeasonLengthTicks = frequency (ticks per full cycle)
    public class InsolationSystem : ITickSystem
    {
        readonly PlantModel model;

        public InsolationSystem(PlantModel model) { this.model = model; }

        // For HUDs and clocks.
        public float Declination { get; private set; }  // degrees; positive = northern (top-row) summer
        public float YearFraction { get; private set; } // 0-1 through the cycle; 0 = northern spring equinox

        public void Tick(WorldGrid grid, int tickNumber)
        {
            Fill(grid, model.Env, tickNumber, out float declination, out float yearFraction);
            Declination = declination;
            YearFraction = yearFraction;
        }

        // Fills the insolation field for a given tick. PlantModel.InitGrid calls this with tick 0.
        public static void Fill(WorldGrid grid, EnvParams env, int tickNumber) => Fill(grid, env, tickNumber, out _, out _);

        static void Fill(WorldGrid grid, EnvParams env, int tickNumber, out float declinationDeg, out float yearFraction)
        {
            int length = Mathf.Max(env.SeasonLengthTicks, 1);
            yearFraction = (tickNumber % length) / (float)length;

            float delta = env.SeasonTilt * Mathf.Deg2Rad * Mathf.Sin(2f * Mathf.PI * yearFraction);
            declinationDeg = delta * Mathf.Rad2Deg;
            float sinD = Mathf.Sin(delta), cosD = Mathf.Cos(delta), tanD = Mathf.Tan(delta);

            float[] s0 = grid.GetField("insolation");
            for (int y = 0; y < grid.Height; y++)
            {
                // latitude runs from -90 (bottom row) to +90 (top row); row centres never hit the poles exactly
                float lat = ((y + 0.5f) / grid.Height) * Mathf.PI - Mathf.PI / 2f;

                float h0 = Mathf.Acos(Mathf.Clamp(-Mathf.Tan(lat) * tanD, -1f, 1f));
                float q = h0 * Mathf.Sin(lat) * sinD + Mathf.Cos(lat) * cosD * Mathf.Sin(h0);
                q = Mathf.Pow(Mathf.Max(q, 0f), env.CosWeight);
                float sun = Mathf.Lerp(env.PoleSun, 1f, q * env.SunGain); // Mathf.Lerp caps t to 0-1

                int row = grid.Index(0, y);
                for (int x = 0; x < grid.Width; x++) s0[row + x] = sun;
            }
        }
    }
}
