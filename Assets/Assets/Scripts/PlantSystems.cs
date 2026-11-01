using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldSim
{
    // Systems for the Tellus Growth Model (see PlantModel.cs).
    //
    // Fast tick, every tick, in this order:   ThermalSystem, MoistureSystem, CarbonMixingSystem
    // Slow tick, every Nth tick, in order:     SuitabilitySystem, GrowthSystem, SpreadSystem, AlbedoSystem, AggregateSystem
    //
    // Field names: temperature, water, humidity, co2, insolation, albedo, biomass, pressure,
    //              suitability, tempEma, lightEma, plantGrowth (render stage 0-8)

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

    // ---------------------------------------------------------------- fast systems

    // T relaxes toward T_eq and diffuses. Stable while Lambda + DT <= 1.
    //   T_eq = T_off + T_span * S0 * (1 - A) + k_co2 * ln(C / C0) - k_cool * tau * B * min(1, W / 0.3)
    public class ThermalSystem : ITickSystem
    {
        readonly PlantModel model;
        public ThermalSystem(PlantModel model) { this.model = model; }

        public void Tick(WorldGrid g, int tickNumber)
        {
            EnvParams e = model.Env;
            float tau = model.Traits.Transpiration;

            float[] T = g.GetField("temperature");
            float[] Tn = g.GetNextField("temperature");
            float[] s0 = g.GetField("insolation");
            float[] albedo = g.GetField("albedo");
            float[] co2 = g.GetField("co2");
            float[] biomass = g.GetField("biomass");
            float[] water = g.GetField("water");

            for (int y = 0; y < g.Height; y++)
            {
                for (int x = 0; x < g.Width; x++)
                {
                    int i = g.Index(x, y);
                    float teq = e.TOff
                              + e.TSpan * s0[i] * (1f - albedo[i])
                              + e.KCo2 * Mathf.Log(Mathf.Max(co2[i], 1f) / e.C0)
                              - e.KCool * tau * biomass[i] * Mathf.Min(1f, water[i] / 0.3f);
                    float mean = (T[g.Left(x, y)] + T[g.Right(x, y)] + T[g.Up(x, y)] + T[g.Down(x, y)]) * 0.25f;
                    Tn[i] = T[i] + e.Lambda * (teq - T[i]) + e.DT * (mean - T[i]);
                }
            }
            g.SwapField("temperature");
        }
    }

    // Evaporation, transpiration and rain move water between W and H; then both diffuse.
    //   evap   = k_e * clip((T + 5) / 45, 0, 1) * W * (1 - 0.4 * c * B)
    //   transp = k_t * tau * c * B * W
    //   W -= evap + transp - rain;  H += evap + transp - rain;  rain = k_r * H
    // Sum(W + H) is constant.
    public class MoistureSystem : ITickSystem
    {
        readonly PlantModel model;
        public MoistureSystem(PlantModel model) { this.model = model; }

        public void Tick(WorldGrid g, int tickNumber)
        {
            EnvParams e = model.Env;
            PlantTraits tr = model.Traits;

            float[] T = g.GetField("temperature");
            float[] W = g.GetField("water");
            float[] H = g.GetField("humidity");
            float[] B = g.GetField("biomass");

            // Each tile's exchange only touches that tile, so it is safe to do in place.
            for (int i = 0; i < g.CellCount; i++)
            {
                float warm = Mathf.Clamp01((T[i] + 5f) / 45f);
                float cb = tr.Canopy * B[i];
                float evap = e.Ke * warm * W[i] * (1f - 0.4f * cb);
                float transp = e.Kt * tr.Transpiration * cb * W[i];
                float rain = e.Kr * H[i];
                W[i] += rain - evap - transp;
                H[i] += evap + transp - rain;
            }

            // Diffusion fetches the fields again: each swap replaces the array behind the name.
            FieldOps.Diffuse(g, "water", e.DW);
            FieldOps.Diffuse(g, "humidity", e.DH);
        }
    }

    public class CarbonMixingSystem : ITickSystem
    {
        readonly PlantModel model;
        public CarbonMixingSystem(PlantModel model) { this.model = model; }

        public void Tick(WorldGrid g, int tickNumber) => FieldOps.Diffuse(g, "co2", model.Env.DC);
    }

    // ---------------------------------------------------------------- suitability

    // One factor of suitability: 1 inside [OptLo, OptHi], smoothstep down to 0 at Lo and Hi.
    [Serializable]
    public struct FactorCurve
    {
        public float Lo, OptLo, OptHi, Hi, Weight;

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

    // Stores w * ln(f) so five lookups add up to ln(s) and only one Exp is needed per tile.
    public sealed class WeightedLogLut
    {
        readonly float[] table;
        readonly float min, scale;
        readonly int n;

        public WeightedLogLut(FactorCurve c, float min, float max, int n = 256)
        {
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
    }

    // First smooths temperature and light (x_ema += beta * (x - x_ema)) so a day/night cycle won't kill everything,
    // then s = exp(sum w_k ln f_k): a weighted geometric mean, so one lethal factor drives s to 0.
    public class SuitabilitySystem : ITickSystem
    {
        readonly PlantModel model;
        readonly WeightedLogLut lutT, lutW, lutL, lutC, lutH;

        public SuitabilitySystem(PlantModel model)
        {
            this.model = model;
            //                                     lo     optLo  optHi  hi     weight
            lutT = new WeightedLogLut(new FactorCurve(0f,    15f,   28f,   40f,   0.30f), -40f, 80f);        // temperature, deg C
            lutW = new WeightedLogLut(new FactorCurve(0.05f, 0.30f, 0.80f, 1.00f, 0.30f), 0f, 1f);           // water
            lutL = new WeightedLogLut(new FactorCurve(0.10f, 0.40f, 0.90f, 1.00f, 0.20f), 0f, 1f);           // light = S0 * (1 - albedo)
            lutC = new WeightedLogLut(new FactorCurve(150f,  300f,  1200f, 5000f, 0.10f), 0f, 6000f, 512);   // CO2, ppm
            lutH = new WeightedLogLut(new FactorCurve(0.02f, 0.10f, 0.60f, 1.00f, 0.10f), 0f, 1f);           // humidity
        }

        public void Tick(WorldGrid g, int tickNumber)
        {
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

                float ln = lutT.Get(tEma[i]) + lutW.Get(W[i]) + lutL.Get(lEma[i]) + lutC.Get(C[i]) + lutH.Get(H[i]);
                suit[i] = Mathf.Exp(ln);
            }
        }
    }

    // ---------------------------------------------------------------- growth

    // Occupied tiles grow logistically, scaled by suitability, and lose biomass in proportion to the square of
    // their suitability shortfall:
    //   dB = r * s * B * (1 - B) - mu * (1 - s)^2 * B,    r = r0 * canopy multiplier * spread multiplier
    // Below B_ext a tile goes extinct. Carbon moves between air and biomass (C -= k_c * dB).
    // Also adds this tick's DNA income to the evolution model.
    public class GrowthSystem : ITickSystem
    {
        readonly PlantModel model;
        public GrowthSystem(PlantModel model) { this.model = model; }

        public void Tick(WorldGrid g, int tickNumber)
        {
            GrowthParams p = model.Growth;
            PlantTraits tr = model.Traits;
            float rEff = p.R0 * tr.GrowthMult;

            float[] B = g.GetField("biomass");
            float[] S = g.GetField("suitability");
            float[] C = g.GetField("co2");

            float income = 0f;
            for (int i = 0; i < g.CellCount; i++)
            {
                float b = B[i];
                if (b <= 0f) continue;

                float s = S[i];
                float shortfall = 1f - s;
                float db = rEff * s * b * (1f - b) - p.Mu * shortfall * shortfall * b;
                float bn = Mathf.Clamp01(b + db);
                if (bn < p.BExt) bn = 0f; // extinct: all its carbon returns to the air via the line below

                C[i] -= p.Kc * (bn - b);
                B[i] = bn;
                if (bn > 0f) income += tr.Canopy * bn * (1 + tr.PigmentTier);
            }

            model.Evolution.AddIncome(income / g.CellCount);
        }
    }

    // Empty tiles collect seed pressure from their 4 edge neighbours; at pressure 1 they are seeded.
    //   gain = sigma * kappa * s * (mean neighbour biomass)   if B = 0 and s >= 0.15
    //   P = 0.9 * P + gain;   if P >= 1 then B = B_seed, P = 0
    // An accumulator instead of a random roll keeps runs repeatable. Two passes (collect, then apply) so
    // tile order can't bias which way the plant spreads.

    public class SpreadSystem : ITickSystem
    {
        readonly PlantModel model;
        readonly List<int> toSeed = new List<int>();

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

        public void Tick(WorldGrid g, int tickNumber)
        {
            GrowthParams p = model.Growth;
            float sigmaKappa = model.Traits.Spread * p.Kappa;

            float[] B = g.GetField("biomass");
            float[] S = g.GetField("suitability");
            float[] P = g.GetField("pressure");

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

                    // Local biomass creates a neighborhood seed source.
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

                    // More nearby vegetation means more seed pressure.
                    float localSource = neighborSum * 0.25f;

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


    // A = clamp(A0 + k_a * canopy * B * (pigmentation - p_ref), 0, A_max). Plants with pigmentation below p_ref
    // darken the ground; above it they brighten it.
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

            for (int i = 0; i < g.CellCount; i++)
                A[i] = Mathf.Clamp(e.A0 + e.Ka * tr.Canopy * B[i] * (tr.Pigmentation - e.PRef), 0f, e.AMax);
        }
    }

    // Last slow system: totals for the HUD, plus the 0-8 render stage the tilemap renderer and tile inspector read.
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
            float[] C = g.GetField("co2");
            float[] stage = g.GetField("plantGrowth");

            int occupied = 0;
            float sumT = 0f, sumS = 0f, sumW = 0f, sumC = 0f;
            for (int i = 0; i < g.CellCount; i++)
            {
                stage[i] = PlantModel.Stage(B[i]);
                sumC += C[i];
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
                MeanCo2 = sumC / g.CellCount,
            };
        }
    }
}
