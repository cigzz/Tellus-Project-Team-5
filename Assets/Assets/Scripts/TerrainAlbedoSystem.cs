using System;
using UnityEngine;

namespace WorldSim
{
    // Tuning for surface albedo. Shown in the WorldBootstrap Inspector under "Albedo".
    [Serializable]
    public class AlbedoParams
    {
        [Header("Terrain (from the heightmap)")]
        [Tooltip("Albedo of the lowest ground on the planet (dark, damp lowland soil).")]
        public float LowlandAlbedo = 0.14f;
        [Tooltip("Albedo of the highest ground on the planet (bare, bright rock).")]
        public float HighlandAlbedo = 0.40f;
        [Tooltip("1 = linear with height. >1 keeps most land dark and brightens only the peaks. <1 brightens earlier.")]
        public float HeightCurve = 1.2f;

        [Header("Surface water darkens the ground")]
        public float WaterAlbedo = 0.06f;
        public float WetFrom = 0.4f;        // water amount where darkening starts
        public float WetFull = 1.5f;        // water amount where the tile reads as open water
        [Range(0f, 1f)] public float MaxWaterCover = 0.8f;

        [Header("Ice and snow brighten it (global ice-albedo feedback)")]
        public float IceAlbedo = 0.55f;
        public float IceFull = 1.5f;        // ice amount that fully covers a bare tile (low = runaway ice ages)
        [Range(0f, 1f)] public float CanopyHidesSnow = 0.5f; // dense canopy pokes through snow

        [Header("Limits")]
        public float MinAlbedo = 0.04f;     // the top limit is EnvParams.AMax
    }

    // Replaces the flat-A0 AlbedoSystem in PlantSystems.cs.
    //
    //   base   = lerp(LowlandAlbedo, HighlandAlbedo, heightNorm ^ HeightCurve)      low = dark, high = bright
    //   wet    = lerp(base, WaterAlbedo, waterCover)                                pooled water darkens basins
    //   plants = wet + Ka * canopy * B * (pigmentation - PRef)                     same plant term as before
    //   final  = lerp(plants, IceAlbedo, iceCover * (1 - CanopyHidesSnow * canopy * B))
    //
    // Call Initialize() BEFORE PlantModel.InitGrid so starting temperatures use the terrain albedo.
    public class TerrainAlbedoSystem : ITickSystem
    {
        public const string BaseAlbedoField = "baseAlbedo";

        readonly PlantModel model;
        readonly AlbedoParams p;

        public TerrainAlbedoSystem(PlantModel model, AlbedoParams parameters = null)
        {
            this.model = model;
            p = parameters ?? new AlbedoParams();
        }

        // Builds the static height-based albedo and seeds the live "albedo" field with it.
        public static void Initialize(WorldGrid grid, AlbedoParams p, float maxAlbedo = 0.9f)
        {
            p ??= new AlbedoParams();
            float[] hNorm = TerrainFields.GetNormalizedHeight(grid);
            float[] baseAlbedo = grid.GetField(BaseAlbedoField);
            float[] albedo = grid.GetField("albedo");

            for (int i = 0; i < grid.CellCount; i++)
            {
                float t = Mathf.Pow(Mathf.Clamp01(hNorm[i]), Mathf.Max(p.HeightCurve, 0.01f));
                baseAlbedo[i] = Mathf.Clamp(Mathf.Lerp(p.LowlandAlbedo, p.HighlandAlbedo, t), p.MinAlbedo, maxAlbedo);
                albedo[i] = baseAlbedo[i];
            }
        }

        public void Tick(WorldGrid g, int tickNumber)
        {
            EnvParams e = model.Env;
            PlantTraits tr = model.Traits;

            if (!g.HasField(BaseAlbedoField)) Initialize(g, p, e.AMax);

            float[] baseAlbedo = g.GetField(BaseAlbedoField);
            float[] B = g.GetField("biomass");
            float[] W = g.GetField("water");
            float[] ice = g.GetField("ice");
            float[] A = g.GetField("albedo");

            float plantGain = e.Ka * tr.Canopy * (tr.Pigmentation - e.PRef);

            for (int i = 0; i < g.CellCount; i++)
            {
                float a = baseAlbedo[i];

                float waterCover = p.MaxWaterCover * TerrainFields.Ramp(W[i], p.WetFrom, p.WetFull);
                a = Mathf.Lerp(a, p.WaterAlbedo, waterCover);

                a += plantGain * B[i];

                float canopy = Mathf.Clamp01(tr.Canopy * B[i]);
                float iceCover = Mathf.Clamp01(ice[i] / p.IceFull) * (1f - p.CanopyHidesSnow * canopy);
                a = Mathf.Lerp(a, p.IceAlbedo, iceCover);

                A[i] = Mathf.Clamp(a, p.MinAlbedo, e.AMax);
            }
        }
    }
}