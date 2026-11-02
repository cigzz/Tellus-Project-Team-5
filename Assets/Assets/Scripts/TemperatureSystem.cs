using System;
using UnityEngine;

namespace WorldSim
{
    public class TemperatureSystem : ITickSystem
    {
        const double TicksPerDay = 8.0 * 86400.0;
        const double DaysPerYear = 365.0;
        const float HeatDiffusion = 0.012f;

        public void Tick(WorldGrid grid, int tickNumber)
        {
            float[] temperature = grid.GetField("temperature", defaultValue: 15f);
            float[] next = grid.GetNextField("temperature");
            float[] height = grid.GetField("height");
            float[] soil = grid.GetField("soilMoisture", defaultValue: 25f);
            float[] water = grid.GetField("water");
            float[] humidity = grid.GetField("humidity", defaultValue: 0.55f);

            double yearPhase = 2.0 * Math.PI * (tickNumber / TicksPerDay) / DaysPerYear;
            double dayPhase = 2.0 * Math.PI * ((tickNumber % TicksPerDay) / TicksPerDay);

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                int i = grid.Index(x, y);
                float signedLatitude = ((y + 0.5f) / grid.Height) * 2f - 1f;
                float absoluteLatitude = Mathf.Abs(signedLatitude);
                float hemisphere = Mathf.Sign(signedLatitude);

                float annualMean = 28f - 42f * absoluteLatitude;
                float seasonalSwing = 22f * absoluteLatitude * hemisphere * (float)Math.Cos(yearPhase);
                float dailySwing = 5f * (1f - absoluteLatitude) * (float)Math.Cos(dayPhase);
                float target = annualMean + seasonalSwing + dailySwing;
                target -= Mathf.Max(0f, height[i]) * 0.0065f; // about 6.5 C per kilometre

                float wetness = Mathf.Clamp01((soil[i] + water[i]) / 100f);
                float thermalInertia = Mathf.Lerp(0.003f, 0.0008f, wetness);
                float cloudCooling = Mathf.Clamp01(humidity[i] - 0.7f) * 2.5f;
                target -= cloudCooling;

                float neighborMean = (temperature[grid.Left(x, y)] + temperature[grid.Right(x, y)] +
                                      temperature[grid.Up(x, y)] + temperature[grid.Down(x, y)]) * 0.25f;
                float diffusion = (neighborMean - temperature[i]) * HeatDiffusion;
                next[i] = Mathf.Clamp(temperature[i] + (target - temperature[i]) * thermalInertia + diffusion, -90f, 65f);
            }

            grid.SwapField("temperature");
        }
    }
}
