using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldSim
{
    // Systems for the Tellus Growth Model (see PlantModel.cs).
    //
    // Fast tick, every tick, in this order:   InsolationSystem, TemperatureSystem, WaterSystem, HumiditySystem
    // Slow tick, every Nth tick, in order:     SuitabilitySystem, GrowthSystem, SpreadSystem, CarbonSystem,
    //                                          TerrainAlbedoSystem, AggregateSystem
    //
    // Field names: temperature, water, humidity, ice, groundwater, co2, deadCarbon, insolation, height, albedo,
    //              baseAlbedo, biomass, pressure, suitability, tempEma, lightEma, plantGrowth (render stage 0-8)

    static class FieldOps
    {
        // x += d * (mean of the 4 edge neighbours - x). Conserves the total on this grid:
        // x wraps, and y is clamped so every tile still counts as a neighbour exactly 4 times.
        public static void Diffuse(WorldGrid g, string name, float d)
        {
            float[] cur = g.GetField(name);
            float[] nxt = g.GetNextField(name);
            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    int i = g.Index(x, y);
                    float mean = (cur[g.Left(x, y)] + cur[g.Right(x, y)] + cur[g.Up(x, y)] + cur[g.Down(x, y)]) * 0.25f;
                    nxt[i] = cur[i] + d * (mean - cur[i]); // double buffer requires writing every cell
                }
            }
            g.SwapField(name);
        }
    }

    // ---------------------------------------------------------------- carbon

    // The carbon cycle apart from plant uptake. Slow tick, after GrowthSystem and SpreadSystem.
    // The only other places carbon moves:
    //   GrowthSystem, growth:              C    -= Kc * dB     (dB > 0)
    //   GrowthSystem, die-back/extinction: dead += Kc * -dB    (dB < 0)
    //   PlantModel.SeedTile:               C    -= Kc * BSeed
    // Each slow tick:
    //   1. Decay: dead carbon returns to the air at DeadCarbonDecay * f(temperature) * f(soil water) * f(humidity)
    //      (DecomposerActivity). Warm, moist tiles rot fast; frozen ground (permafrost) holds its carbon and waterlogged
    //      soil (bogs) rots slowly. Also totals the air for the planet mean.
    //   2. Mixing, one double-buffered step that is a weighted average of the old values (stable, conserves carbon):
    //        C += NeighbourBlend * (4-neighbour mean - C)       smooths colony edges
    //           + zonal          * (latitude-row mean - C)      zonal = GlobalMix / cos(lat), as in TemperatureSystem
    //           + GlobalMix      * (planet mean - C)
    //      zonal is capped so the three weights never sum past 1.
    // air + Kc * biomass + dead carbon stays constant (up to float rounding).
    // The planet mean goes to model.MeanCo2. TemperatureSystem's greenhouse term and the HUD read that one value,
    // so there is one Log per tick instead of one per tile, and no extra pass.
    public class CarbonSystem : ITickSystem
    {
        const float ReleaseAllBelow = 1e-3f; // ppm; smaller leftovers are released at once so tiles reach exactly 0
        const float NeighbourBlend = 0.5f;   // fixed local smoothing; 0.5 halves edge steps each slow tick

        readonly PlantModel model;
        float[] rowMean;                     // scratch: mean CO2 of each latitude row
        float[] rowZonal;                    // cached zonal weight per row (needs Cos); rebuilt when GlobalMix changes
        float rowZonalMix = -1f;
        public CarbonSystem(PlantModel model) { this.model = model; }

        // Decomposer activity 0-1 = f(temperature) * f(soil water) * f(humidity). Each factor is a smoothstep ramp:
        //   temperature: 0 at <= 0 C, rises to 1 at 30 C, flat to 45 C, back to 0 at 60 C
        //   soil water:  0 at <= 0.02 (dry), 1 from 0.25 to 0.7, back to 0 at 1.2 (waterlogged)
        //   humidity:    0 at 0, rises to 1 at 0.5 and stays there
        // Written out (not FactorCurve.Eval) and returning early on a 0 factor: most cold, dry or flooded tiles skip
        // the other two factors. This made the decay pass about twice as fast in benchmarks.
        static float DecomposerActivity(float t, float w, float h)
        {
            float ft = t <= 0f ? 0f : t < 30f ? Smooth(t / 30f) : t <= 45f ? 1f : Smooth((60f - t) / 15f);
            if (ft <= 0f) return 0f;
            float fw = w <= 0.02f ? 0f : w < 0.25f ? Smooth((w - 0.02f) / 0.23f) : w <= 0.7f ? 1f : Smooth((1.2f - w) / 0.5f);
            if (fw <= 0f) return 0f;
            float fh = h >= 0.5f ? 1f : h <= 0f ? 0f : Smooth(h / 0.5f);
            return ft * fw * fh;
        }

        static float Smooth(float u) => u >= 1f ? 1f : u <= 0f ? 0f : u * u * (3f - 2f * u);

        public void Tick(WorldGrid g, int tickNumber)
        {
            float[] C = g.GetField("co2");
            float[] next = g.GetNextField("co2");
            float[] dead = g.GetField("deadCarbon");
            float[] T = g.GetField("temperature");
            float[] W = g.GetField("water");
            float[] H = g.GetField("humidity");
            float decayRate = Mathf.Clamp01(model.Env.DeadCarbonDecay);
            float mix = Mathf.Clamp(model.Env.GlobalMix, 0f, 1f - NeighbourBlend);
            int width = g.Width, height = g.Height;

            if (rowMean == null || rowMean.Length != height)
            {
                rowMean = new float[height];
                rowZonal = new float[height];
                rowZonalMix = -1f;
            }
            if (mix != rowZonalMix)
            {
                for (int y = 0; y < height; y++)
                {
                    float lat = ((y + 0.5f) / height) * Mathf.PI - Mathf.PI / 2f;
                    rowZonal[y] = Mathf.Min(mix / Mathf.Max(Mathf.Cos(lat), 1e-3f), 1f - NeighbourBlend - mix);
                }
                rowZonalMix = mix;
            }

            // 1. decay dead carbon into the air; total each row and the planet
            double sum = 0.0;
            for (int y = 0; y < height; y++)
            {
                int row = y * width, end = row + width;
                double rowSum = 0.0;
                for (int i = row; i < end; i++)
                {
                    float d = dead[i];
                    if (d > 0f)
                    {
                        float release = d < ReleaseAllBelow ? d : d * decayRate * DecomposerActivity(T[i], W[i], H[i]);
                        dead[i] = d - release;
                        C[i] += release;
                    }
                    rowSum += C[i];
                }
                rowMean[y] = (float)(rowSum / width);
                sum += rowSum;
            }
            float mean = (float)(sum / g.CellCount);

            // 2. neighbour + row + planet blend in one pass (x wraps, y clamps). The first and last column are done
            //    outside the inner loop so it has no wrap checks.
            const float q = NeighbourBlend * 0.25f;
            for (int y = 0; y < height; y++)
            {
                int row = y * width, last = row + width - 1;
                int up = (y < height - 1 ? y + 1 : y) * width;
                int dn = (y > 0 ? y - 1 : y) * width;
                float z = rowZonal[y], rm = rowMean[y];
                float c;

                c = C[row];
                next[row] = c + (q * (C[last] + C[row + 1] + C[up] + C[dn]) - NeighbourBlend * c) + z * (rm - c) + mix * (mean - c);

                for (int i = row + 1, u = up + 1, d = dn + 1; i < last; i++, u++, d++)
                {
                    c = C[i];
                    next[i] = c + (q * (C[i - 1] + C[i + 1] + C[u] + C[d]) - NeighbourBlend * c) + z * (rm - c) + mix * (mean - c);
                }

                c = C[last];
                next[last] = c + (q * (C[last - 1] + C[row] + C[up + width - 1] + C[dn + width - 1]) - NeighbourBlend * c) + z * (rm - c) + mix * (mean - c);
            }
            g.SwapField("co2");

            model.MeanCo2 = mean; // every blend conserves the total, so this is also the mean after mixing
        }
    }

    // ---------------------------------------------------------------- suitability

    // One factor of suitability: 1 inside [OptLo, OptHi], smoothstep down to 0 at Lo and Hi.
    // Weight is applied by WeightedLogLut, not by Eval.
    [Serializable]
    public struct FactorCurve
    {
        [InspectorLabel("Lower Limit")]
        [Tooltip("At or below this value the factor is 0, which makes the tile unsuitable.")]
        public float Lo;
        [InspectorLabel("Ideal Range Start")]
        [Tooltip("From here to Ideal Range End the factor is 1 (no penalty). Between Lower Limit and here it rises smoothly from 0 to 1.")]
        public float OptLo;
        [InspectorLabel("Ideal Range End")]
        [Tooltip("Above this the factor falls smoothly, reaching 0 at Upper Limit.")]
        public float OptHi;
        [InspectorLabel("Upper Limit")]
        [Tooltip("At or above this value the factor is 0, which makes the tile unsuitable.")]
        public float Hi;
        [InspectorLabel("Importance (weight)")]
        [Tooltip("How strongly this factor pulls suitability down when outside its ideal range. 0 = ignored (never fatal). " +
                 "The five weights sum to 1 by default; raising all of them lowers suitability everywhere.")]
        public float Weight;

        public FactorCurve(float lo, float optLo, float optHi, float hi, float weight)
        {
            Lo = lo; OptLo = optLo; OptHi = optHi; Hi = hi; Weight = weight;
        }

        public float Eval(float x)
        {
            float up = Smooth((x - Lo) / (OptLo - Lo));
            float dn = Smooth((Hi - x) / (Hi - OptHi));
            return Mathf.Min(up, dn);
        }

        static float Smooth(float u) { u = Mathf.Clamp01(u); return u * u * (3f - 2f * u); }
    }

    // Lookup table of w * ln(f), sampled at n points over [min, max] and linearly interpolated (inputs outside the
    // range are clamped). The factors' lookups add up to ln(s), so each tile needs only one Exp.
    // f is floored at 1e-9 to keep the log finite; Kills() catches f = 0 exactly so suitability can be set to 0.
    public sealed class WeightedLogLut
    {
        readonly float[] table;
        readonly float min, scale;
        readonly int n;
        readonly float killLo, killHi; // f is exactly 0 at or beyond these
        readonly bool canKill;         // a zero-weight factor is ignored, so it never kills
        public readonly FactorCurve Curve; // kept so a death can be blamed on the low or high side of the optimum

        public WeightedLogLut(FactorCurve c, float min, float max, int n = 256)
        {
            Curve = c;
            killLo = c.Lo;
            killHi = c.Hi;
            canKill = c.Weight > 0f;
            this.n = n;
            this.min = min;
            scale = (n - 1) / (max - min);
            table = new float[n];
            for (int i = 0; i < n; i++)
                table[i] = c.Weight * Mathf.Log(Mathf.Max(c.Eval(min + i / scale), 1e-9f));
        }

        public float Get(float x)
        {
            float u = Mathf.Clamp((x - min) * scale, 0f, n - 1.001f);
            int i = (int)u;
            return table[i] + (table[i + 1] - table[i]) * (u - i);
        }

        // True where the factor is exactly 0 (at or beyond Lo or Hi).
        public bool Kills(float x) => canKill && (x <= killLo || x >= killHi);
    }

    // Smooths temperature and light first (x_ema += Beta * (x - x_ema)) so brief swings don't kill tiles outright,
    // then s = exp(sum w_k ln f_k), a weighted geometric mean of the five factors (weights sum to 1 by default).
    // Any factor at 0 (at or beyond its Lo or Hi) makes s exactly 0. The curves are set in GrowthParams (Inspector).
    public class SuitabilitySystem : ITickSystem
    {
        readonly PlantModel model;
        WeightedLogLut lutT, lutW, lutL, lutC, lutH;

        public SuitabilitySystem(PlantModel model)
        {
            this.model = model;
            RefreshLuts();
            model.Suitability = this; // lets GrowthSystem ask why a tile died
        }

        public void Tick(WorldGrid g, int tickNumber)
        {
            RefreshLuts();
            float beta = model.Growth.Beta;
            float[] T = g.GetField("temperature");
            float[] W = g.GetField("water");
            float[] H = g.GetField("humidity");
            float[] C = g.GetField("co2");
            float[] s0 = g.GetField("insolation");
            float[] albedo = g.GetField("albedo");
            float[] tEma = g.GetField("tempEma");
            float[] lEma = g.GetField("lightEma");
            float[] suit = g.GetField("suitability");

            for (int i = 0; i < g.CellCount; i++)
            {
                tEma[i] += beta * (T[i] - tEma[i]);
                lEma[i] += beta * (s0[i] * (1f - albedo[i]) - lEma[i]);

                float t = tEma[i], w = W[i], l = lEma[i], c = C[i], h = H[i];
                if (lutT.Kills(t) || lutW.Kills(w) || lutL.Kills(l) || lutC.Kills(c) || lutH.Kills(h))
                    suit[i] = 0f; // one fatal factor, however good the others are
                else
                    suit[i] = Mathf.Exp(lutT.Get(t) + lutW.Get(w) + lutL.Get(l) + lutC.Get(c) + lutH.Get(h));
            }
        }

        FactorCurve builtT, builtW, builtL, builtC, builtH; // curves the LUTs were last built from

        // Rebuilds the lookup tables only when a factor curve was edited in the Inspector.
        void RefreshLuts()
        {
            GrowthParams p = model.Growth;
            if (lutT != null && Same(p.TemperatureFactor, builtT) && Same(p.WaterFactor, builtW) && Same(p.LightFactor, builtL)
                && Same(p.Co2Factor, builtC) && Same(p.HumidityFactor, builtH)) return;

            builtT = p.TemperatureFactor; builtW = p.WaterFactor; builtL = p.LightFactor;
            builtC = p.Co2Factor; builtH = p.HumidityFactor;

            lutT = new WeightedLogLut(Safe(builtT), -40f, 80f);
            lutW = new WeightedLogLut(Safe(builtW), 0f, 1f);
            lutL = new WeightedLogLut(Safe(builtL), 0f, 1f);
            lutC = new WeightedLogLut(Safe(builtC), 0f, 6000f, 512);
            lutH = new WeightedLogLut(Safe(builtH), 0f, 1f);
        }

        static bool Same(FactorCurve a, FactorCurve b) =>
            a.Lo == b.Lo && a.OptLo == b.OptLo && a.OptHi == b.OptHi && a.Hi == b.Hi && a.Weight == b.Weight;

        // Keeps Lo < OptLo <= OptHi < Hi and Weight >= 0, so a half-typed Inspector value can't divide by zero.
        static FactorCurve Safe(FactorCurve c)
        {
            const float eps = 1e-4f;
            c.OptLo = Mathf.Max(c.OptLo, c.Lo + eps);
            c.OptHi = Mathf.Max(c.OptHi, c.OptLo);
            c.Hi = Mathf.Max(c.Hi, c.OptHi + eps);
            c.Weight = Mathf.Max(c.Weight, 0f);
            return c;
        }

        enum Factor { Temperature, Water, Light, Co2, Humidity }

        // Called by GrowthSystem only for a tile that just died, so recomputing five lookups here is cheap.
        // Each lookup is w * ln(f) <= 0, so the most negative one is the factor that pulled suitability down the most.
        // A factor at exactly 0 is blamed ahead of any partial shortfall.
        // Which side of that factor's optimum the tile is on picks between the paired causes (cold vs heat, etc.).
        public PlantDeathCause DiagnoseDeath(WorldGrid g, int i)
        {
            float t = g.GetField("tempEma")[i];
            float w = g.GetField("water")[i];
            float l = g.GetField("lightEma")[i];
            float c = g.GetField("co2")[i];
            float h = g.GetField("humidity")[i];

            Factor worst = Factor.Temperature;
            float worstLn = Score(lutT, t);
            Consider(Factor.Water, Score(lutW, w), ref worst, ref worstLn);
            Consider(Factor.Light, Score(lutL, l), ref worst, ref worstLn);
            Consider(Factor.Co2, Score(lutC, c), ref worst, ref worstLn);
            Consider(Factor.Humidity, Score(lutH, h), ref worst, ref worstLn);

            if (worstLn > -1e-3f) return PlantDeathCause.Unknown; // every factor was near its optimum

            switch (worst)
            {
                case Factor.Temperature: return Below(t, lutT) ? PlantDeathCause.Cold : PlantDeathCause.Heat;
                case Factor.Water:       return Below(w, lutW) ? PlantDeathCause.Drought : PlantDeathCause.Waterlogged;
                case Factor.Light:       return Below(l, lutL) ? PlantDeathCause.Darkness : PlantDeathCause.Scorch;
                case Factor.Co2:         return Below(c, lutC) ? PlantDeathCause.CarbonStarved : PlantDeathCause.CarbonOverload;
                case Factor.Humidity:    return Below(h, lutH) ? PlantDeathCause.DryAir : PlantDeathCause.Humid;
                default:                 return PlantDeathCause.Unknown;
            }
        }

        static float Score(WeightedLogLut lut, float x) => lut.Kills(x) ? float.NegativeInfinity : lut.Get(x);

        static void Consider(Factor f, float ln, ref Factor worst, ref float worstLn)
        {
            if (ln < worstLn) { worst = f; worstLn = ln; }
        }

        // True if x is below the middle of the factor's optimal band.
        static bool Below(float x, WeightedLogLut lut) => x < (lut.Curve.OptLo + lut.Curve.OptHi) * 0.5f;
    }

    // ---------------------------------------------------------------- growth

    // Occupied tiles grow logistically, scaled by suitability, and lose biomass in proportion to the square of
    // their suitability shortfall:
    //   dB = r * s * B * (1 - B) - mu * (1 - s)^2 * B,    r = r0 * canopy multiplier * spread multiplier
    // Below B_ext a tile goes extinct. Growth takes carbon from the air (C -= k_c * dB); lost biomass goes into
    // the tile's dead carbon (dead += k_c * -dB), which CarbonSystem releases back to the air over time.
    // Also pays this tick's DNA income: total leaf area * the pigmentation DNA multiplier / tile count (see EvolutionModel).
    public class GrowthSystem : ITickSystem
    {
        readonly PlantModel model;
        readonly PlantDeathReport deathReport = new PlantDeathReport();
        public GrowthSystem(PlantModel model) { this.model = model; }

        public void Tick(WorldGrid g, int tickNumber)
        {
            GrowthParams p = model.Growth;
            PlantTraits tr = model.Traits;
            float rEff = p.R0 * tr.GrowthMult;
            model.UpdateSurvivalThreshold(); // for the read-only Inspector field

            float[] B = g.GetField("biomass");
            float[] S = g.GetField("suitability");
            float[] C = g.GetField("co2");
            float[] dead = g.GetField("deadCarbon");

            float leafArea = 0f; // planet total, for DNA income
            deathReport.Begin(tickNumber);
            for (int i = 0; i < g.CellCount; i++)
            {
                float b = B[i];
                if (b <= 0f) continue;
                deathReport.AliveBefore++;

                float s = S[i];
                float shortfall = 1f - s;
                float db = rEff * s * b * (1f - b) - p.Mu * shortfall * shortfall * b;
                float bn = Mathf.Clamp01(b + db);
                if (bn < p.BExt)
                {
                    bn = 0f; // extinct: all its carbon goes to dead carbon via the lines below
                    deathReport.Add(model.Suitability != null ? model.Suitability.DiagnoseDeath(g, i) : PlantDeathCause.Unknown);
                }

                float dCarbon = p.Kc * (bn - b);
                if (dCarbon > 0f) C[i] -= dCarbon;   // growth: taken from the air now
                else dead[i] -= dCarbon;             // loss: stored as dead carbon, released slowly by CarbonSystem
                B[i] = bn;
                if (bn > 0f) leafArea += tr.LeafArea(bn);
            }

            model.RaisePlantDeaths(deathReport); // sent every slow tick, even with 0 deaths, so the alert window keeps moving
            model.Evolution.AddIncome(leafArea * tr.DnaMult / g.CellCount);
        }
    }

    // Empty, viable tiles collect seed pressure from their 4 edge neighbours and are seeded at pressure 1:
    //   gain = spread trait * Kappa * SpreadScale * s * (steered mean neighbour biomass) * variation
    //   P = PressureDecay * P + gain;   if P >= 1 then B = BSeed, P = 0
    // Tiles below MinSeedSuit or failing PlantModel.CanGrowAt have P reset to 0.
    // An accumulator instead of a random roll keeps runs repeatable. Two passes (collect, then apply) so
    // tile order can't bias which way the plant spreads.
    //
    // Steering: suitability is averaged within SteeringRadius of each tile ("habitat"). The direction in which that
    // average improves is the preferred direction, and seed pressure travelling that way is weighted up (to the
    // ceiling) while seed pressure travelling toward worse land is weighted down (to the floor). Where suitability is
    // flat there is no preferred direction and spread is the same in every direction.
    public class SpreadSystem : ITickSystem
    {
        readonly PlantModel model;
        readonly List<int> toSeed = new List<int>();
        float[] habitat, blurTemp; // smoothed suitability, allocated on first use

        // Fixed seed makes the same world produce the same spread pattern.
        const uint RandomSeed = 1234567u;

        public SpreadSystem(PlantModel model)
        {
            this.model = model;
        }

        // Deterministic hash: no global Unity random state is needed.
        static float Hash01(int index, int tick)
        {
            unchecked
            {
                uint h = RandomSeed;
                h ^= (uint)index * 374761393u;
                h ^= (uint)tick * 668265263u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;

                return (h & 0x00FFFFFFu) / 16777216f;
            }
        }

        // Average of suitability over a (2r+1) x (2r+1) window, done as two 1D passes.
        // x wraps around the planet and y clamps at the poles, like the neighbour tables.
        void BlurSuitability(WorldGrid g, float[] S, int radius)
        {
            if (habitat == null || habitat.Length != g.CellCount)
            {
                habitat = new float[g.CellCount];
                blurTemp = new float[g.CellCount];
            }

            int w = g.Width, h = g.Height;
            float inv = 1f / (2 * radius + 1);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    for (int d = -radius; d <= radius; d++)
                        sum += S[g.Index(((x + d) % w + w) % w, y)];
                    blurTemp[g.Index(x, y)] = sum * inv;
                }
            }

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float sum = 0f;
                    for (int d = -radius; d <= radius; d++)
                        sum += blurTemp[g.Index(x, Mathf.Clamp(y + d, 0, h - 1))];
                    habitat[g.Index(x, y)] = sum * inv;
                }
            }
        }

        public void Tick(WorldGrid g, int tickNumber)
        {
            GrowthParams p = model.Growth;
            float sigmaKappa = model.Traits.Spread * p.Kappa * p.SpreadScale;

            float[] B = g.GetField("biomass");
            float[] S = g.GetField("suitability");
            float[] P = g.GetField("pressure");

            bool steer = p.SteeringStrength > 0f;
            if (steer) BlurSuitability(g, S, Mathf.Max(1, p.SteeringRadius));

            toSeed.Clear();

            // Pass 1: calculate pressure without changing biomass.
            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    int i = g.Index(x, y);

                    if (B[i] > 0f)
                    {
                        P[i] = 0f;
                        continue;
                    }

                    // Don't accumulate seeds where plants cannot establish.
                    if (S[i] < p.MinSeedSuit || !model.CanGrowAt(S[i]))
                    {
                        P[i] = 0f;
                        continue;
                    }

                    float left = B[g.Left(x, y)];
                    float right = B[g.Right(x, y)];
                    float up = B[g.Up(x, y)];
                    float down = B[g.Down(x, y)];

                    float neighborSum = left + right + up + down;

                    if (neighborSum <= 0f)
                    {
                        P[i] *= p.PressureDecay;
                        continue;
                    }

                    float localSource;
                    if (steer)
                    {
                        // (gx, gy) points toward better land. Dividing by (length + flat threshold) gives a direction whose
                        // size is ~0 on flat ground and ~1 on a clear slope.
                        float gx = (habitat[g.Right(x, y)] - habitat[g.Left(x, y)]) * 0.5f;
                        float gy = (habitat[g.Up(x, y)] - habitat[g.Down(x, y)]) * 0.5f;
                        float inv = 1f / (Mathf.Sqrt(gx * gx + gy * gy) + p.SteeringFlatBelow);
                        float ux = gx * inv, uy = gy * inv;

                        float k = p.SteeringStrength, lo = p.SteeringFloor, hi = p.SteeringCeiling;

                        // The left neighbour sends seed rightward, so it is weighted up when better land lies to the right (ux > 0), etc.
                        localSource = (left * Mathf.Clamp(1f + k * ux, lo, hi)
                                     + right * Mathf.Clamp(1f - k * ux, lo, hi)
                                     + up * Mathf.Clamp(1f - k * uy, lo, hi)
                                     + down * Mathf.Clamp(1f + k * uy, lo, hi)) * 0.25f;
                    }
                    else
                    {
                        // No steering: plain mean of the 4 neighbours.
                        localSource = neighborSum * 0.25f;
                    }

                    // Small deterministic variation creates an irregular frontier.
                    float variation = 0.75f + 0.5f * Hash01(i, tickNumber);

                    float gain =
                        sigmaKappa *
                        S[i] *
                        localSource *
                        variation;

                    P[i] = p.PressureDecay * P[i] + gain;

                    if (P[i] >= 1f)
                        toSeed.Add(i);
                }
            }

            // Pass 2: apply seeds after all pressures have been calculated.
            for (int k = 0; k < toSeed.Count; k++)
                model.SeedTile(g, toSeed[k]);
        }
    }


    // A = clamp(A0 + Ka * (pigmentation - PRef) * ground cover, 0, A_max). Plants with pigmentation below PRef
    // darken the ground; above it they brighten it. Ground cover saturates (PlantTraits.GroundCover), so a bigger
    // canopy reaches the full effect at a lower biomass.
    // Not registered by WorldBootstrap: TerrainAlbedoSystem replaces it and includes this plant term.
    public class AlbedoSystem : ITickSystem
    {
        readonly PlantModel model;
        public AlbedoSystem(PlantModel model) { this.model = model; }

        public void Tick(WorldGrid g, int tickNumber)
        {
            EnvParams e = model.Env;
            PlantTraits tr = model.Traits;
            float[] B = g.GetField("biomass");
            float[] A = g.GetField("albedo");

            float plantGain = e.Ka * (tr.Pigmentation - e.PRef);
            for (int i = 0; i < g.CellCount; i++)
                A[i] = Mathf.Clamp(e.A0 + plantGain * tr.GroundCover(B[i]), 0f, e.AMax);
        }
    }

    // Runs after the other plant systems: totals for the HUD, plus the 0-8 render stage the tilemap renderer and tile inspector read.
    public class AggregateSystem : ITickSystem
    {
        readonly PlantModel model;
        public AggregateSystem(PlantModel model) { this.model = model; }

        public void Tick(WorldGrid g, int tickNumber)
        {
            float[] B = g.GetField("biomass");
            float[] S = g.GetField("suitability");
            float[] T = g.GetField("temperature");
            float[] W = g.GetField("water");
            float[] stage = g.GetField("plantGrowth");

            int occupied = 0;
            float sumT = 0f, sumS = 0f, sumW = 0f;
            for (int i = 0; i < g.CellCount; i++)
            {
                stage[i] = PlantModel.Stage(B[i]);
                if (B[i] <= 0f) continue;
                occupied++;
                sumT += T[i];
                sumS += S[i];
                sumW += W[i];
            }

            float inv = occupied > 0 ? 1f / occupied : 0f;
            model.Stats = new PlantStats
            {
                Occupied = occupied,
                Coverage = (float)occupied / g.CellCount,
                MeanTemp = sumT * inv,
                MeanSuitability = sumS * inv,
                MeanWater = sumW * inv,
                MeanCo2 = model.MeanCo2, // totalled by CarbonSystem
            };
        }
    }
}