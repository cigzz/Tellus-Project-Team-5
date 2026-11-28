using System;
using UnityEngine;

namespace WorldSim
{
    // Implements the "Tellus Growth Model" design doc (Team Spores, week 8).
    // Constants start from the doc's values; the tier tables have since been extended from 6 to 8 tiers (see TierTable).
    // None are calibrated against playtests.

    // Environment, heat, water, carbon and albedo constants.
    [Serializable]
    public class EnvParams
    {
        [Header("Heat")]
        public float TOff = -25f;     // deg C offset of the equilibrium-temperature line
        public float TSpan = 65f;     // deg C gained from full sunlight
        public float KCo2 = 4.3f;     // deg C per ln(CO2 / C0)
        public float C0 = 280f;       // reference CO2, ppm
        public float KCool = 8f;      // transpiration cooling, deg C, at transpiration trait 1 and leaf area 1 on moist soil
        public float Lambda = 0.15f;  // response speed toward the target temperature per fast tick (thermal inertia only)
        public float DT = 0.20f;      // temperature diffusion. Keep Lambda + DT <= 1
        [Tooltip("East-west heat mixing along each latitude row (0 = none, 1 = every row uniform). Evens out regional " +
                 "hot/cold spots without flattening the equator-to-pole gradient. Automatically stronger near the poles.")]
        [Range(0f, 1f)] public float ZonalMix = 0.13f;
        [Tooltip("Equator-to-pole heat transport (0 = none, 1 = whole planet one temperature). Pulls every tile's target " +
                 "this share of the way toward the planet mean. Independent of Lambda.")]
        [Range(0f, 1f)] public float HeatTransport = 0.35f;

        [Header("Sunlight and seasons (InsolationSystem)")]
        [Tooltip("Sunlight floor: what the poles get during polar night (0 = none, 1 = same as the equator).")]
        [Range(0f, 1f)] public float PoleSun = 0.25f;
        [Tooltip("Multiplies sunlight before it is capped at 1. Above 1 widens the fully lit tropical band.")]
        public float SunGain = 1.3f;
        [Tooltip("Weight on the cosine falloff toward the poles: 1 = pure cosine, lower = gentler drop-off (more polar sun), higher = sharper. Shapes the yearly-average profile only; seasons are added on top unchanged.")]
        [Range(0.1f, 2f)] public float CosWeight = 1f;
        [Tooltip("Season intensity: axial tilt in degrees. 0 = no seasons, 23.4 = Earth, 45+ = extreme.")]
        [Range(0f, 60f)] public float SeasonTilt = 23.44f;
        [Tooltip("Season frequency: ticks per full cycle of spring, summer, autumn and winter.")]
        [Min(1)] public int SeasonLengthTicks = 600;

        [Header("Water")]
        public float Ke = 0.02f;      // evaporation
        public float Kt = 0.03f;      // transpiration
        public float Kr = 0.05f;      // rain from humidity
        public float DW = 0.35f;      // water diffusion
        public float DH = 0.20f;      // humidity diffusion

        [Header("CO2 (CarbonSystem, slow tick)")]
        public float StartCo2 = 600f; // ppm
        [Tooltip("How fast dead carbon rots back into CO2: share released per slow tick on an ideal tile (warm, moist, humid). " +
                 "Cold, waterlogged or bone-dry tiles release only a fraction of this.")]
        [Range(0f, 1f)] public float DeadCarbonDecay = 0.06f;
        [Tooltip("CO2 mixing per slow tick (0 = local smoothing only). Each tile moves this share toward the planet mean, and " +
                 "toward its latitude-row mean by the same amount divided by cos(latitude), so polar rows mix faster. Keep small (0.01-0.1).")]
        [Range(0f, 1f)] public float GlobalMix = 0.03f;

        [Header("Start values")]
        public float StartWater = 0.5f;
        public float StartHumidity = 0.10f;

        [Header("Albedo")]
        public float A0 = 0.30f;      // flat bare-ground albedo; only a fallback, TerrainAlbedoSystem sets the real one first
        [Tooltip("Albedo change per unit of (pigmentation - PRef) where leaves fully cover the ground.")]
        public float Ka = 0.30f;
        [Tooltip("Pigmentation that leaves the ground's albedo unchanged. 0.2 = the starting tier, so starting plants are " +
                 "albedo-neutral; lower tiers darken the ground, higher tiers brighten it. 0 gives the proposal's original formula.")]
        public float PRef = 0.2f;
        public float AMax = 0.9f;     // highest albedo any tile can reach
    }

    // Growth and spread constants (slow tick). Names in brackets are the design doc's symbols.
    [Serializable]
    public class GrowthParams
    {
        [InspectorLabel("Survival Threshold (live)", ReadOnly = true)]
        [Tooltip("Read-only. Lowest suitability at which a colony grows instead of dying back, worked out from " +
                 "Base Growth Rate, Die-back Rate and the current tiers. Updated every slow tick; before Play it assumes default tiers.")]
        public float SurvivalThreshold;

        [Header("Growth and die-back")]
        [InspectorLabel("Base Growth Rate (r0)")]
        [Tooltip("Growth per slow tick on a perfectly suitable tile, before the canopy and spread tier multipliers. " +
                 "Growth slows as a tile fills up.")]
        public float R0 = 0.20f;
        [InspectorLabel("Die-back Rate (mu)")]
        [Tooltip("Biomass lost per slow tick on unsuitable land, scaled by (1 - suitability)^2. Higher = plants die faster on poor land.")]
        public float Mu = 0.12f;
        [InspectorLabel("Seedling Biomass")]
        [Tooltip("Biomass (0-1) a newly seeded tile starts with.")]
        public float BSeed = 0.05f;
        [InspectorLabel("Extinction Biomass")]
        [Tooltip("A tile whose biomass falls below this dies out.")]
        public float BExt = 0.01f;
        [InspectorLabel("CO2 per Biomass (ppm)")]
        [Tooltip("CO2 locked away per unit of biomass. Growing takes it from the air; dying puts it into dead carbon, " +
                 "which rots back into the air.")]
        public float Kc = 150f;

        [Header("Seeding")]
        [InspectorLabel("Seed Pressure Gain (kappa)")]
        [Tooltip("How fast empty tiles next to plants build seed pressure. A tile is seeded when its pressure reaches 1.")]
        public float Kappa = 1.6f;
        [InspectorLabel("Seed Pressure Kept")]
        [Tooltip("Share of seed pressure an empty tile keeps each slow tick (0.9 = loses 10% per slow tick).")]
        public float PressureDecay = 0.9f;
        [InspectorLabel("Min Suitability to Seed")]
        [Tooltip("Empty tiles below this suitability build no seed pressure.")]
        public float MinSeedSuit = 0.15f;

        [Header("Evolution strength")]
        [InspectorLabel("Trait Strength")]
        [Tooltip("How hard the evolution tiers hit. 1 = the TierTable values exactly. Higher pushes every tier further " +
                 "from the starting tier (3), so upgrades and downgrades matter more. Very high values make tiers 0 and 7 extreme.")]
        public float TraitStrength = 1.5f;

        [Header("Spread steering (toward more suitable land)")]
        [InspectorLabel("Spread Speed Multiplier")]
        [Tooltip("Multiplies Seed Pressure Gain. Steering slows the spreading front; this brings the pace back up.")]
        public float SpreadScale = 1.5f;
        [InspectorLabel("Steering Strength")]
        [Tooltip("0 = spread equally in every direction. Higher = favour the direction in which suitability improves.")]
        public float SteeringStrength = 1.0f;
        [InspectorLabel("Min Steering Weight")]
        [Tooltip("Smallest weight on seed pressure heading toward worse land. Higher lets plants still push into worse land; " +
                 "lower makes them pickier.")]
        public float SteeringFloor = 0.6f;
        [InspectorLabel("Max Steering Weight")]
        [Tooltip("Largest weight on seed pressure heading toward better land.")]
        public float SteeringCeiling = 2.0f;
        [InspectorLabel("Steering Radius (tiles)")]
        [Tooltip("Suitability is averaged within this many tiles before the direction is read, so gentle trends " +
                 "across several tiles are sensed, not just tile-to-tile differences.")]
        public int SteeringRadius = 3;
        [InspectorLabel("Flat Below")]
        [Tooltip("Suitability changes smaller than this per tile count as flat ground: no preferred direction.")]
        public float SteeringFlatBelow = 0.002f;

        [Header("Suitability factors")]
        // Suitability is a weighted geometric mean of the five factors below (see SuitabilitySystem). Edits apply on the
        // next slow tick, in Play mode too. Default curves:        Lo     OptLo  OptHi  Hi     Weight
        [InspectorLabel("Climate Smoothing (beta)")]
        [Tooltip("How quickly suitability follows changes in temperature and light, per slow tick (0-1). " +
                 "Lower = slower, so brief swings don't kill plants outright.")]
        public float Beta = 0.3f;
        [InspectorLabel("Temperature (deg C)")]
        [Tooltip("Air temperature, smoothed by Climate Smoothing. Values outside -40 to 80 are read as -40 or 80.")]
        public FactorCurve TemperatureFactor = new FactorCurve(0f,    15f,   28f,   40f,   0.30f);
        [InspectorLabel("Soil Water (0-1)")]
        [Tooltip("Water in the tile.")]
        public FactorCurve WaterFactor = new FactorCurve(      0.05f, 0.30f, 0.80f, 1.00f, 0.30f);
        [InspectorLabel("Light (0-1)")]
        [Tooltip("Sunlight absorbed: sunlight * (1 - albedo), smoothed by Climate Smoothing.")]
        public FactorCurve LightFactor = new FactorCurve(      0.10f, 0.40f, 0.90f, 1.00f, 0.20f);
        [InspectorLabel("CO2 (ppm)")]
        [Tooltip("CO2 above the tile. Values outside 0 to 6000 are read as 0 or 6000.")]
        public FactorCurve Co2Factor = new FactorCurve(        150f,  300f,  1200f, 5000f, 0.10f);
        [InspectorLabel("Humidity (0-1)")]
        [Tooltip("Moisture in the air.")]
        public FactorCurve HumidityFactor = new FactorCurve(   0.02f, 0.10f, 0.60f, 1.00f, 0.10f);
    }

    public enum PlantAttribute { Canopy = 0, Transpiration = 1, Pigmentation = 2, Spread = 3 }

    // The numbers the simulation actually uses, looked up from the player's tiers (EvolutionModel.BuildTraits).
    public struct PlantTraits
    {
        // Ground cover per unit of leaf area (see GroundCover). 2.5 lets a starting canopy cover ~90% of its tile at
        // full biomass, the same as the old linear canopy * biomass term, while bigger canopies get there sooner.
        public const float CoverPerLeafArea = 2.5f;

        public float Canopy, Transpiration, Pigmentation, Spread;
        public float GrowthMult; // canopy growth multiplier * spread growth multiplier
        public float DnaMult;    // DNA income multiplier from the pigmentation tier

        // Leaf area: canopy * biomass. Unbounded, so every canopy tier adds transpiration (water use and cooling).
        public float LeafArea(float biomass) => Canopy * biomass;

        // Share of the tile (0-1) under leaves: 1 - e^(-CoverPerLeafArea * leaf area). It saturates, so a bigger canopy
        // reaches full cover, and with it the full pigmentation albedo effect, shade and snow cover, at a lower biomass.
        public float GroundCover(float biomass) =>
            biomass > 0f ? 1f - Mathf.Exp(-CoverPerLeafArea * LeafArea(biomass)) : 0f;
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

    // Tier lookups. Index = tier (0-7); every trait starts at tier 3.
    // The design doc's 6-tier tables, extended by one tier below the start and one above. Steps are sized so each
    // trait's total range (tier 0 to tier 7) is about 30% wider than the doc's. Tier 3 keeps the doc's starting values
    // except for two retunes: Pigmentation starts at 0.2 instead of 0.4, and Spread is shifted up so its slowest tier
    // is the old starting speed.
    // These are the values before GrowthParams.TraitStrength stretches them (see EvolutionModel.BuildTraits).
    public static class TierTable
    {
        public const int MaxTier = 7;
        public const int DefaultTier = 3;

        //                                         tier:    0       1       2       3       4       5       6       7
        public static readonly float[] Canopy           = { 0.34f,  0.53f,  0.71f,  0.90f,  1.09f,  1.27f,  1.46f,  1.64f };
        public static readonly float[] Transpiration    = { 0.34f,  0.53f,  0.71f,  0.90f,  1.09f,  1.27f,  1.46f,  1.64f };
        // Enters albedo only as (pigmentation - EnvParams.PRef), so a value below 0 just means "darker still".
        public static readonly float[] Pigmentation     = { -0.36f, -0.17f, 0.01f,  0.20f,  0.39f,  0.57f,  0.76f,  0.94f };
        public static readonly float[] Spread           = { 1.10f,  1.38f,  1.66f,  1.94f,  2.22f,  2.50f,  2.78f,  3.06f };
        public static readonly float[] GrowthFromCanopy = { 0.74f,  0.81f,  0.89f,  0.96f,  1.03f,  1.11f,  1.18f,  1.26f };
        public static readonly float[] GrowthFromSpread = { 1.05f,  0.99f,  0.94f,  0.88f,  0.82f,  0.77f,  0.71f,  0.66f };
        // The doc's (1 + tier), moved up one tier so the start still pays 3x; the new bottom tier pays half.
        public static readonly float[] DnaFromPigment   = { 0.5f,   1f,     2f,     3f,     4f,     5f,     6f,     7f    };
        // The doc's max(1, tier), moved up one tier so returning to the start still costs 2.
        public static readonly int[] SegmentsToMoveOnto = { 1,      1,      1,      2,      3,      4,      5,      6     };
    }

    // The player's tiers, DNA and banked segments.
    //   DNA income per slow tick = sum(canopy * biomass) * DnaFromPigment[pigmentation tier] / tile count (GrowthSystem)
    //   the next segment costs 10 * 1.15^(segments banked); up to 10 can be banked
    //   moving onto tier t costs SegmentsToMoveOnto[t] segments, one tier up or down at a time
    public class EvolutionModel
    {
        public const float FirstSegmentCost = 10f;
        public const float PriceGrowth = 1.15f;
        public const int MaxSegments = 10;

        // indexed by PlantAttribute
        readonly int[] tiers = { TierTable.DefaultTier, TierTable.DefaultTier, TierTable.DefaultTier, TierTable.DefaultTier };

        public float Dna { get; private set; }    // DNA toward the next segment
        public int Segments { get; private set; } // segments banked, 0 to MaxSegments

        public event Action Changed;              // raised when a tier changes

        public float NextSegmentCost => FirstSegmentCost * Mathf.Pow(PriceGrowth, Segments);

        // Share of the next segment earned so far (0-1). 1 once the bank is full.
        public float SegmentProgress => Segments >= MaxSegments ? 1f : Mathf.Clamp01(Dna / NextSegmentCost);

        public int GetTier(PlantAttribute a) => tiers[(int)a];
        public int CostToMoveOnto(int tier) => TierTable.SegmentsToMoveOnto[Mathf.Clamp(tier, 0, TierTable.MaxTier)];

        // Banks every segment the DNA pays for. While the bank is full, DNA keeps accumulating for later.
        public void AddIncome(float income)
        {
            Dna += income;
            while (Segments < MaxSegments && Dna >= NextSegmentCost)
            {
                Dna -= NextSegmentCost;
                Segments++;
            }
        }

        // True if target is one tier away from the current tier, within 0 to MaxTier, and affordable.
        public bool CanMove(PlantAttribute a, int target)
        {
            if (target < 0 || target > TierTable.MaxTier) return false;
            if (Mathf.Abs(target - GetTier(a)) != 1) return false;
            return Segments >= CostToMoveOnto(target);
        }

        public bool TrySetTier(PlantAttribute a, int target)
        {
            if (!CanMove(a, target)) return false;
            Segments -= CostToMoveOnto(target);
            tiers[(int)a] = target;
            Changed?.Invoke();
            return true;
        }

        // Looks the current tiers up in TierTable. strength = 1 uses the table values exactly. Above 1 each value is
        // pushed further from the starting tier's value: multiplied traits use a power law around it (so they stay
        // positive), and pigmentation, which only enters albedo as (pigmentation - PRef), has its distance from it scaled.
        public PlantTraits BuildTraits(float strength = 1f)
        {
            int c = GetTier(PlantAttribute.Canopy);
            int t = GetTier(PlantAttribute.Transpiration);
            int p = GetTier(PlantAttribute.Pigmentation);
            int s = GetTier(PlantAttribute.Spread);
            return new PlantTraits
            {
                Canopy = Stretch(TierTable.Canopy, c, strength),
                Transpiration = Stretch(TierTable.Transpiration, t, strength),
                Pigmentation = Shift(TierTable.Pigmentation, p, strength),
                Spread = Stretch(TierTable.Spread, s, strength),
                GrowthMult = Stretch(TierTable.GrowthFromCanopy, c, strength) * Stretch(TierTable.GrowthFromSpread, s, strength),
                DnaMult = TierTable.DnaFromPigment[p],
            };
        }

        // start * (table[tier] / start) ^ strength, where start = table[DefaultTier]. For positive values only.
        static float Stretch(float[] table, int tier, float strength)
        {
            float start = table[TierTable.DefaultTier];
            return start * Mathf.Pow(table[tier] / start, strength);
        }

        // start + strength * (table[tier] - start). For values that can be 0 or negative.
        static float Shift(float[] table, int tier, float strength)
        {
            float start = table[TierTable.DefaultTier];
            return start + strength * (table[tier] - start);
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

        // Planet-mean atmospheric CO2 (ppm). Set by CarbonSystem every slow tick; drives the greenhouse term.
        public float MeanCo2 { get; internal set; }

        // Raised by GrowthSystem once per slow tick with that tick's deaths counted by cause (see PlantDeathAlertDriver).
        // The report object is reused every tick: copy what you need, don't keep it.
        public event Action<PlantDeathReport> PlantDeathsReported;
        internal SuitabilitySystem Suitability; // set by SuitabilitySystem so GrowthSystem can ask why a tile died
        internal void RaisePlantDeaths(PlantDeathReport report) => PlantDeathsReported?.Invoke(report);

        // Cached: systems read this every tick (CanGrowAt even reads it per tile), and building it uses Pow.
        // Rebuilt when a tier changes or TraitStrength is edited in the Inspector.
        PlantTraits traits;
        bool traitsDirty = true;
        float traitsBuiltWithStrength;

        public PlantTraits Traits
        {
            get
            {
                if (traitsDirty || traitsBuiltWithStrength != Growth.TraitStrength)
                {
                    traits = Evolution.BuildTraits(Growth.TraitStrength);
                    traitsBuiltWithStrength = Growth.TraitStrength;
                    traitsDirty = false;
                }
                return traits;
            }
        }

        public PlantModel(EnvParams env, GrowthParams growth)
        {
            Env = env;
            Growth = growth;
            Evolution.Changed += () => traitsDirty = true;
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
            grid.GetField("deadCarbon");   // soil carbon from dead biomass (ppm still to be released), see CarbonSystem
            MeanCo2 = Env.StartCo2;
            grid.GetField("water", Env.StartWater);
            grid.GetField("humidity", Env.StartHumidity);
            grid.GetField("biomass");
            grid.GetField("pressure");
            grid.GetField("suitability");
            grid.GetField("plantGrowth");

            // Tick 0 is the northern spring equinox, so both hemispheres start evenly lit.
            InsolationSystem.Fill(grid, Env, 0);

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int i = grid.Index(x, y);
                    float insolation = s0[i];
                    // start every tile at its bare-ground equilibrium so the first ticks don't swing wildly
                    float teq = Env.TOff + Env.TSpan * insolation * (1f - albedo[i]) + Env.KCo2 * Mathf.Log(Env.StartCo2 / Env.C0);
                    temperature[i] = teq;
                    tempEma[i] = teq;
                    lightEma[i] = insolation * (1f - albedo[i]);
                }
            }
        }

        // True if a seedling on a tile with this suitability would grow instead of dying out:
        // r * s > mu * (1 - s)^2 with r = R0 * tier growth multiplier, i.e. s > SurvivalThreshold.
        public bool CanGrowAt(float suitability)
        {
            float shortfall = 1f - suitability;
            return Growth.R0 * Traits.GrowthMult * suitability > Growth.Mu * shortfall * shortfall;
        }

        // Lowest suitability at which a colony grows instead of dying back: the smaller root of r * s = mu * (1 - s)^2.
        // About 0.32 at default values and tiers (r = 0.2 * 0.845). The doc's 0.297 assumed r = 0.2.
        public static float SurvivalThreshold(float r, float mu)
        {
            if (mu <= 0f) return 0f; // no die-back: any suitability above 0 grows
            if (r <= 0f) return 1f;  // no growth: nothing survives
            float b = 2f * mu + r;   // mu s^2 - (2 mu + r) s + mu = 0
            return (b - Mathf.Sqrt(b * b - 4f * mu * mu)) / (2f * mu);
        }

        // Writes the live threshold (current tiers) into GrowthParams so the Inspector shows it.
        public void UpdateSurvivalThreshold() =>
            Growth.SurvivalThreshold = SurvivalThreshold(Growth.R0 * Traits.GrowthMult, Growth.Mu);

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