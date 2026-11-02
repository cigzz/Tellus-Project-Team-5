using System;
using UnityEngine;

namespace WorldSim
{
    // Implements the "Tellus Growth Model" design doc (Team Spores, week 8).
    // All numbers are the doc's starting values; none are calibrated against playtests.

    // Environment, heat, water, carbon and albedo constants.
    [Serializable]
    public class EnvParams
    {
        [Header("Heat")]
        public float TOff = -25f;     // deg C offset of the equilibrium-temperature line
        public float TSpan = 65f;     // deg C gained from full sunlight
        public float KCo2 = 4.3f;     // deg C per ln(CO2 / C0)
        public float C0 = 280f;       // reference CO2, ppm
        public float KCool = 8f;      // max transpiration cooling, deg C
        public float Lambda = 0.15f;  // relaxation toward equilibrium per fast tick
        public float DT = 0.20f;      // temperature diffusion. Keep Lambda + DT <= 1

        [Header("Water")]
        public float Ke = 0.02f;      // evaporation
        public float Kt = 0.03f;      // transpiration
        public float Kr = 0.05f;      // rain from humidity
        public float DW = 0.03f;      // water diffusion
        public float DH = 0.20f;      // humidity diffusion

        [Header("CO2")]
        public float DC = 0.30f;      // CO2 mixing
        public float StartCo2 = 600f; // ppm

        [Header("Start values")]
        public float StartWater = 0.5f;
        public float StartHumidity = 0.10f;

        [Header("Albedo")]
        public float A0 = 0.30f;      // bare-ground albedo
        public float Ka = 0.30f;
        public float PRef = 0.4f;     // reference pigmentation (tier 2). 0 gives the proposal's original formula
        public float AMax = 0.9f;
    }

    // Growth and spread constants (slow tick).
    [Serializable]
    public class GrowthParams
    {
        public float R0 = 0.20f;            // base growth rate
        public float Mu = 0.12f;            // stress die-back rate
        public float BSeed = 0.05f;         // biomass of a new seedling
        public float BExt = 0.01f;          // below this a tile goes extinct
        public float Kc = 150f;             // ppm of CO2 locked per unit of biomass
        public float Kappa = 1.6f;          // seed pressure gain
        public float PressureDecay = 0.9f;  // seed pressure kept each slow tick
        public float MinSeedSuit = 0.15f;   // tiles below this suitability collect no seed pressure
        public float Beta = 0.3f;           // smoothing for temperature and light before suitability reads them
    }

    public enum PlantAttribute { Canopy = 0, Transpiration = 1, Pigmentation = 2, Spread = 3 }

    // The numbers the simulation actually uses, looked up from the player's tiers.
    public struct PlantTraits
    {
        public float Canopy, Transpiration, Pigmentation, Spread;
        public float GrowthMult; // canopy multiplier * spread multiplier
        public int PigmentTier;
    }

    // Planet-wide numbers for the HUD. Refreshed every slow tick by AggregateSystem.
    public struct PlantStats
    {
        public int Occupied;
        public float Coverage;        // occupied / all tiles (see doc: weight by cos(latitude) if you go spherical)
        public float MeanTemp;        // occupied tiles
        public float MeanSuitability; // occupied tiles
        public float MeanWater;       // occupied tiles
        public float MeanCo2;         // whole planet
    }

    // Tier 0-5 lookups, straight from the doc. Index = tier.
    public static class TierTable
    {
        public static readonly float[] Canopy = { 0.5f, 0.7f, 0.9f, 1.1f, 1.3f, 1.5f };
        public static readonly float[] Transpiration = { 0.5f, 0.7f, 0.9f, 1.1f, 1.3f, 1.5f };
        public static readonly float[] Pigmentation = { 0.0f, 0.2f, 0.4f, 0.6f, 0.8f, 1.0f };
        public static readonly float[] Spread = { 0.5f, 0.8f, 1.1f, 1.4f, 1.7f, 2.0f };
        public static readonly float[] GrowthFromCanopy = { 0.80f, 0.88f, 0.96f, 1.04f, 1.12f, 1.20f };
        public static readonly float[] GrowthFromSpread = { 1.00f, 0.94f, 0.88f, 0.82f, 0.76f, 0.70f };
        public static readonly int[] SegmentsToMoveOnto = { 1, 1, 2, 3, 4, 5 };

        public const int MaxTier = 5;
        public const int DefaultTier = 2;
    }

    // Tiers, DNA and segments.
    //   income per slow tick I = sum(canopy * biomass * (1 + pigmentation tier)) / tile count
    //   next segment costs 10 * 1.15^segments held; up to 8 can be held
    //   moving onto tier t costs max(1, t) segments, one step up or down at a time
    public class EvolutionModel
    {
        public const float FirstSegmentCost = 10f;
        public const float PriceGrowth = 1.15f;
        public const int MaxSegments = 8;

        public readonly int[] Tiers = { 2, 2, 2, 2 }; // indexed by PlantAttribute
        public float Dna { get; private set; }        // progress toward the next segment
        public int Segments { get; private set; }     // segments currently banked

        public event Action Changed;

        public float NextSegmentCost => FirstSegmentCost * Mathf.Pow(PriceGrowth, Segments);
        public int GetTier(PlantAttribute a) => Tiers[(int)a];

        public void AddIncome(float income)
        {
            Dna += income;
            while (Segments < MaxSegments && Dna >= NextSegmentCost)
            {
                Dna -= NextSegmentCost;
                Segments++;
            }
        }

        public int CostToMoveOnto(int tier) => TierTable.SegmentsToMoveOnto[Mathf.Clamp(tier, 0, TierTable.MaxTier)];

        public bool CanMove(PlantAttribute a, int target)
        {
            int cur = Tiers[(int)a];
            if (target < 0 || target > TierTable.MaxTier) return false;
            if (Mathf.Abs(target - cur) != 1) return false; // one step at a time
            return Segments >= CostToMoveOnto(target);
        }

        public bool TrySetTier(PlantAttribute a, int target)
        {
            if (!CanMove(a, target)) return false;
            Segments -= CostToMoveOnto(target);
            Tiers[(int)a] = target;
            Changed?.Invoke();
            return true;
        }

        public PlantTraits BuildTraits()
        {
            int c = Tiers[(int)PlantAttribute.Canopy];
            int t = Tiers[(int)PlantAttribute.Transpiration];
            int p = Tiers[(int)PlantAttribute.Pigmentation];
            int s = Tiers[(int)PlantAttribute.Spread];
            return new PlantTraits
            {
                Canopy = TierTable.Canopy[c],
                Transpiration = TierTable.Transpiration[t],
                Pigmentation = TierTable.Pigmentation[p],
                Spread = TierTable.Spread[s],
                GrowthMult = TierTable.GrowthFromCanopy[c] * TierTable.GrowthFromSpread[s],
                PigmentTier = p,
            };
        }
    }

    // Owns the parameters, the player's evolution state and the latest stats.
    // WorldBootstrap creates one and hands it to every system.
    public class PlantModel
    {
        public readonly EnvParams Env;
        public readonly GrowthParams Growth;
        public readonly EvolutionModel Evolution = new EvolutionModel();
        public PlantStats Stats;

        public PlantTraits Traits => Evolution.BuildTraits();

        public PlantModel(EnvParams env, GrowthParams growth)
        {
            Env = env;
            Growth = growth;
        }

        // The tilemap renderer shows "plantGrowth" as 0-8 (0 = empty, 1-8 = Plant1..Plant8 sprites).
        // Biomass 0-1 maps onto stages 1-8 so even a fresh seedling shows Plant1.
        public static float Stage(float biomass) => biomass > 0f ? 1f + biomass * 7f : 0f;

        // Creates every field the model uses and gives it its starting value.
        // Call once, after the heightmap is loaded and before the engine starts ticking.
        public void InitGrid(WorldGrid grid)
        {
            float[] s0 = grid.GetField("insolation");
            float[] albedo = grid.GetField("albedo", Env.A0);
            float[] temperature = grid.GetField("temperature");
            float[] tempEma = grid.GetField("tempEma");
            float[] lightEma = grid.GetField("lightEma");
            grid.GetField("co2", Env.StartCo2);
            grid.GetField("water", Env.StartWater);
            grid.GetField("humidity", Env.StartHumidity);
            grid.GetField("biomass");
            grid.GetField("pressure");
            grid.GetField("suitability");
            grid.GetField("plantGrowth");

            for (int y = 0; y < grid.Height; y++)
            {
                // latitude runs from -90 (bottom row) to +90 (top row); insolation = cos(latitude)
                float lat = ((y + 0.5f) / grid.Height) * Mathf.PI - Mathf.PI / 2f;
                float insolation = Mathf.Cos(lat);
                for (int x = 0; x < grid.Width; x++)
                {
                    int i = grid.Index(x, y);
                    s0[i] = insolation;
                    // start every tile at its bare-ground equilibrium so the first ticks don't swing wildly
                    float teq = Env.TOff + Env.TSpan * insolation * (1f - albedo[i]) + Env.KCo2 * Mathf.Log(Env.StartCo2 / Env.C0);
                    temperature[i] = teq;
                    tempEma[i] = teq;
                    lightEma[i] = insolation * (1f - albedo[i]);
                }
            }
        }

        // True if a seedling on a tile with this suitability would grow instead of dying out.
        // From the doc: a colony is viable when r * s > mu * (1 - s)^2 (s_crit = 0.297 at default tiers' r0 = 0.2).
        public bool CanGrowAt(float suitability)
        {
            float shortfall = 1f - suitability;
            return Growth.R0 * Traits.GrowthMult * suitability > Growth.Mu * shortfall * shortfall;
        }

        // Plants one seedling. Takes its carbon from the air so the carbon total stays constant.
        // Returns false if the tile is already occupied.
        public bool SeedTile(WorldGrid grid, int index)
        {
            float[] biomass = grid.GetField("biomass");
            if (biomass[index] > 0f) return false;

            biomass[index] = Growth.BSeed;
            grid.GetField("pressure")[index] = 0f;
            grid.GetField("co2")[index] -= Growth.Kc * Growth.BSeed;
            grid.GetField("plantGrowth")[index] = Stage(Growth.BSeed); // show it right away, not on the next slow tick
            return true;
        }
    }
}