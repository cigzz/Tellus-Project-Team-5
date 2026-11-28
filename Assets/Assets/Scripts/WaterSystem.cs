using System;
using UnityEngine;

namespace WorldSim
{
    // Tuning for the water cycle. Shown in the WorldBootstrap Inspector under "Water cycle".
    // Starting values, not playtested calibration.
    [Serializable]
    public class WaterParams
    {
        [Header("Difficulty")]
        [Tooltip("Scales evaporation, ground seepage and drought strength. 0.5 = gentle, 1 = normal, 1.5+ = harsh.")]
        [Range(0f, 2f)] public float Difficulty = 1f;

        [Header("Air capacity (warm air holds more vapour)")]
        public float CapacityAt15C = 0.45f;   // humidity the air can hold at 15 deg C
        public float CapacityGrowth = 0.06f;  // per deg C (~doubles every 11.5 deg C)
        public float CapacityMin = 0.04f;
        public float CapacityMax = 1.0f;

        [Header("Evaporation")]
        public float EvapGrowth = 0.05f;      // evaporation speeds up this much per deg C above 15
        public float EvapMaxBoost = 4f;       // cap on that speed-up
        public float FrozenEvapFactor = 0.15f;// evaporation multiplier at or below 0 deg C
        public float Sublimation = 1f;        // ice turning straight to vapour, relative to water evaporation
        [Tooltip("Sublimation always runs at least this share of its dry-air rate, so saturated polar air can't stop it.")]
        [Range(0f, 1f)] public float SublimationMinDeficit = 0.3f;

        [Header("Rain and snow")]
        public float OverflowRain = 0.35f;    // share of vapour above capacity that falls each tick

        [Header("Ice")]
        public float FreezeRate = 0.04f;      // share of surface water frozen per tick at -10 deg C
        public float MeltRate = 0.05f;        // share of ice melted per tick at +10 deg C
        [Tooltip("Glacier flow: share of the ice difference between two tiles moved per tick once the ice is thick. Keep <= 0.25.")]
        public float IceFlow = 0.05f;
        [Tooltip("Ice thickness at which flow reaches full IceFlow speed. Thinner ice flows proportionally slower.")]
        public float IceFlowFullAt = 1.5f;

        [Header("Runoff (water flows downhill)")]
        public float RunoffRate = 0.2f;       // keep <= 0.25 so 4 neighbours can't overshoot
        public float WaterDepthScale = 4f;    // height units one unit of water fills (pooled water levels out)
        public float MaxRunoffShare = 0.5f;   // most of a tile's free water that can leave in one tick
        public float SoilRetention = 0.3f;    // water the soil holds against runoff (slopes stay marginal, not bone dry)

        [Header("Groundwater")]
        public float Infiltration = 0.004f;   // share of surface water lost into the ground per tick
        public float SpringRate = 0.01f;      // share of groundwater that resurfaces per tick (lowlands only)
        public float GroundwaterDiffusion = 0.1f;
        [Tooltip("Aquifer water per tile at the start. Added to the planet's budget once, on the first tick.")]
        public float StartGroundwater = 0.4f;

        [Header("Global effects")]
        [Tooltip("Planet-wide warming from water vapour: deg C per ln(mean humidity / reference).")]
        public float VapourGreenhouse = 3f;
        public float VapourReference = 0.10f;
        [Tooltip("deg C per unit of water evaporated (cooling) or condensed (warming) on a tile.")]
        public float LatentHeat = 6f;
        [Tooltip("How much a drought phase cuts rain planet-wide (0 = never, 0.6 = rain drops 60% at the worst).")]
        public float DroughtAmplitude = 0.35f;
        public int DroughtPeriodTicks = 1200;
    }

    // Planet-wide water numbers, refreshed every tick. Read by HUDs via WaterSystem.Latest.
    public struct WaterStats
    {
        public float SurfaceWater, Vapour, Ice, Groundwater; // planet totals
        public float Total => SurfaceWater + Vapour + Ice + Groundwater;
        public float MeanHumidity;
        public float IceCoverage;       // share of tiles with meaningful ice
        public float VapourWarming;     // deg C currently added planet-wide by water vapour
        public float RainMultiplier;    // 1 = normal, below 1 = drought
    }

    // Water cycle. Every tick, in order:
    //   1. Global state: mean humidity -> vapour greenhouse warming; drought cycle -> rain multiplier.
    //   2. Per tile: evaporation, transpiration, rain/snow, freeze/melt, seepage, springs, latent heat.
    //   3. Runoff: surface water flows to lower neighbours (heights + water depth), so basins pool and slopes dry.
    //   4. Groundwater spreads slowly underground; surface water seeps sideways (env.DW).
    //
    // Water is conserved: surface water + humidity + ice + groundwater stays constant after the first tick
    // (StartGroundwater is added once). Only heat is added or removed.
    //
    // What makes it harder than before:
    //   - Hot tiles evaporate much faster, and evaporation stops once the air is saturated.
    //   - Highlands drain and are colder; cold tiles lock water away as ice.
    //   - A share of surface water seeps underground and resurfaces as springs, mostly in lowlands.
    //   - Polar and mountain cold traps pull vapour out of the air as snow.
    //   - Planet-wide drought phases cut rain everywhere at once.
    public class WaterSystem : ITickSystem
    {
        readonly PlantModel model;
        readonly WaterParams p;

        readonly int[] neighbours = new int[4];   // scratch for Runoff
        readonly float[] drops = new float[4];

        public WaterStats Stats { get; private set; }
        public static WaterStats Latest { get; private set; }

        public WaterSystem(PlantModel model, WaterParams parameters = null)
        {
            this.model = model;
            p = parameters ?? new WaterParams();
        }

        // Most vapour air at this surface temperature can hold.
        public float Capacity(float surfaceTemp)
            => Mathf.Clamp(p.CapacityAt15C * Mathf.Exp(p.CapacityGrowth * (surfaceTemp - 15f)), p.CapacityMin, p.CapacityMax);

        public void Tick(WorldGrid grid, int tickNumber)
        {
            EnvParams env = model.Env;
            PlantTraits traits = model.Traits;
            float difficulty = p.Difficulty;

            float[] temperature = grid.GetField("temperature");
            float[] water = grid.GetField("water");
            float[] humidity = grid.GetField("humidity");
            float[] biomass = grid.GetField("biomass");
            float[] ice = grid.GetField("ice");
            bool firstTick = !grid.HasField("groundwater");
            float[] ground = grid.GetField("groundwater", firstTick ? p.StartGroundwater : 0f);
            float[] hNorm = TerrainFields.GetNormalizedHeight(grid);

            // ---- 1. global state
            float meanHumidity = 0f;
            for (int i = 0; i < grid.CellCount; i++) meanHumidity += humidity[i];
            meanHumidity /= grid.CellCount;

            float vapourWarming = p.VapourGreenhouse * Mathf.Log(Mathf.Max(meanHumidity, 0.005f) / p.VapourReference);
            // TemperatureSystem adds this to every tile's target temperature (read from WaterSystem.Latest).
            float rainMult = DroughtMultiplier(tickNumber, difficulty);

            // ---- 2. local exchange
            for (int i = 0; i < grid.CellCount; i++)
            {
                float temp = temperature[i];
                float cap = Capacity(temp);
                float h = humidity[i];
                float w = water[i];
                float deficit = Mathf.Clamp01(1f - h / cap); // dry air pulls water up, saturated air doesn't

                // Evaporation: grows exponentially with warmth, scaled by difficulty, cut by up to 40% under full leaf cover.
                float boost = Mathf.Min(Mathf.Exp(p.EvapGrowth * (temp - 15f)), p.EvapMaxBoost);
                if (temp <= 0f) boost *= p.FrozenEvapFactor;
                float b = biomass[i];
                float evaporation = env.Ke * difficulty * boost * w * deficit * (1f - 0.4f * traits.GroundCover(b));
                // Transpiration: plants draw soil water up through their leaves, so it scales with leaf area (canopy * biomass).
                float transpiration = env.Kt * traits.Transpiration * traits.LeafArea(b) * w * deficit;
                float lift = Mathf.Min(evaporation + transpiration, w);
                // Ice sublimates slowly into dry air, so cold traps don't swallow the planet's water for good.
                float subDeficit = Mathf.Max(deficit, p.SublimationMinDeficit);
                float sublimate = Mathf.Min(p.Sublimation * env.Ke * difficulty * boost * ice[i] * subDeficit, ice[i]);

                // Rain: steady drizzle that grows as the air nears saturation, plus a downpour of
                // anything above capacity. Air pushed up a mountain cools, loses capacity and rains out.
                float drizzle = env.Kr * h * Mathf.Min(h / cap, 2f);
                float overflow = p.OverflowRain * Mathf.Max(0f, h - cap);
                float rain = Mathf.Min((drizzle + overflow) * rainMult, h);

                h += lift + sublimate - rain;
                w -= lift;

                // Below freezing it falls as snow (ice); above, as water.
                float iceAmount = ice[i] - sublimate;
                if (temp < 0f) iceAmount += rain; else w += rain;

                // Freeze / melt.
                if (temp < 0f)
                {
                    float freeze = p.FreezeRate * TerrainFields.Ramp(-temp, 0f, 10f) * w;
                    w -= freeze; iceAmount += freeze;
                }
                else if (iceAmount > 0f)
                {
                    float melt = p.MeltRate * TerrainFields.Ramp(temp, 0f, 10f) * iceAmount;
                    iceAmount -= melt; w += melt;
                }

                // Seepage into the ground; springs bring it back up, mostly in lowlands.
                float seep = p.Infiltration * difficulty * w;
                float spring = p.SpringRate * (1f - hNorm[i]) * ground[i];
                w += spring - seep;
                ground[i] += seep - spring;

                // Latent heat: evaporating cools the tile, condensing warms it.
                temperature[i] += p.LatentHeat * (rain - lift - sublimate);

                water[i] = Mathf.Max(w, 0f);
                humidity[i] = Mathf.Max(h, 0f);
                ice[i] = Mathf.Max(iceAmount, 0f);
            }

            // ---- 3. runoff, then 4. lateral spreading
            Runoff(grid, water);
            FieldOps.Diffuse(grid, "water", env.DW);
            FieldOps.Diffuse(grid, "groundwater", p.GroundwaterDiffusion);
            IceFlow(grid, ice);

            Stats = CollectStats(grid, meanHumidity, vapourWarming, rainMult);
            Latest = Stats;
        }

        float[] iceDelta; // scratch for IceFlow, allocated on first use

        // Glacier flow: ice moves from thicker to thinner neighbours. Speed scales with the shared thickness of
        // each pair, so thin frost barely moves while thick sheets spread outward toward warmer tiles and melt.
        // Each edge is handled once and the amount leaving one tile is added to the other, so water is conserved.
        void IceFlow(WorldGrid grid, float[] ice)
        {
            if (p.IceFlow <= 0f) return;
            if (iceDelta == null || iceDelta.Length != grid.CellCount) iceDelta = new float[grid.CellCount];
            Array.Clear(iceDelta, 0, iceDelta.Length);

            float rate = Mathf.Min(p.IceFlow, 0.25f);   // 4 neighbours: above 0.25 a tile could overshoot
            float fullAt = Mathf.Max(p.IceFlowFullAt, 1e-3f);

            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int i = grid.Index(x, y);
                    Exchange(i, grid.Right(x, y));
                    int up = grid.Up(x, y);
                    if (up != i) Exchange(i, up);       // top row clamps to itself: no edge
                }
            }

            for (int i = 0; i < grid.CellCount; i++) ice[i] = Mathf.Max(ice[i] + iceDelta[i], 0f);

            void Exchange(int a, int b)
            {
                float diff = ice[a] - ice[b];
                if (diff == 0f) return;
                float thickness = Mathf.Clamp01(0.5f * (ice[a] + ice[b]) / fullAt);
                float flow = rate * thickness * diff;
                iceDelta[a] -= flow;
                iceDelta[b] += flow;
            }
        }

        // Smooth, slightly irregular planet-wide wet/dry cycle. 1 = normal rain.
        float DroughtMultiplier(int tick, float difficulty)
        {
            if (p.DroughtPeriodTicks <= 0 || p.DroughtAmplitude <= 0f) return 1f;
            float a = 2f * Mathf.PI * tick / p.DroughtPeriodTicks;
            float wave = 0.65f * Mathf.Sin(a) + 0.35f * Mathf.Sin(2.3f * a + 1.7f); // -1 .. 1
            float dryness = Mathf.Clamp01(0.5f * (1f + wave));                     // 0 .. 1
            return 1f - Mathf.Clamp01(p.DroughtAmplitude * difficulty) * dryness;
        }

        // Each tile sends its free water (above SoilRetention) to lower neighbours in proportion to the drop in water-surface level
        // (terrain height + water depth). Collect all flows first, then apply, so tile order doesn't matter.
        // Conserves water exactly: every amount removed from one tile is added to another.
        void Runoff(WorldGrid grid, float[] water)
        {
            if (p.RunoffRate <= 0f) return;

            float[] height = grid.GetField("height");
            float[] next = grid.GetNextField("water");
            Array.Copy(water, next, grid.CellCount);

            float depthScale = Mathf.Max(p.WaterDepthScale, 1e-3f);
            int[] nb = neighbours;
            float[] drop = drops;

            for (int y = 0; y < grid.Height; y++)
                for (int x = 0; x < grid.Width; x++)
                {
                    int i = grid.Index(x, y);
                    float w = water[i];
                    float free = w - p.SoilRetention; // only water above what the soil holds can run off
                    if (free <= 1e-6f) continue;

                    float level = height[i] + w * depthScale;
                    nb[0] = grid.Left(x, y); nb[1] = grid.Right(x, y); nb[2] = grid.Up(x, y); nb[3] = grid.Down(x, y);

                    float total = 0f;
                    for (int k = 0; k < 4; k++)
                    {
                        int j = nb[k];
                        // At the poles Up/Down return the tile itself: drop is 0, so nothing flows.
                        float d = level - (height[j] + water[j] * depthScale);
                        // Moving m water closes the gap by 2 * m * depthScale; RunoffRate <= 0.25 keeps it from overshooting.
                        drop[k] = d > 0f ? p.RunoffRate * d / (2f * depthScale) : 0f;
                        total += drop[k];
                    }
                    if (total <= 0f) continue;

                    float scale = Mathf.Min(1f, free * p.MaxRunoffShare / total);
                    for (int k = 0; k < 4; k++)
                    {
                        if (drop[k] <= 0f) continue;
                        float m = drop[k] * scale;
                        next[i] -= m;
                        next[nb[k]] += m;
                    }
                }

            grid.SwapField("water");
        }

        WaterStats CollectStats(WorldGrid grid, float meanHumidity, float vapourWarming, float rainMult)
        {
            float[] water = grid.GetField("water");
            float[] humidity = grid.GetField("humidity");
            float[] ice = grid.GetField("ice");
            float[] ground = grid.GetField("groundwater");

            var s = new WaterStats { MeanHumidity = meanHumidity, VapourWarming = vapourWarming, RainMultiplier = rainMult };
            int iced = 0;
            for (int i = 0; i < grid.CellCount; i++)
            {
                s.SurfaceWater += water[i];
                s.Vapour += humidity[i];
                s.Ice += ice[i];
                s.Groundwater += ground[i];
                if (ice[i] > 0.05f) iced++;
            }
            s.IceCoverage = (float)iced / grid.CellCount;
            return s;
        }
    }
}