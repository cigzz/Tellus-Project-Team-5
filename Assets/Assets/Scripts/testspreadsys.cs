using System;
using UnityEngine;

namespace WorldSim
{

    [Serializable]
    public class SpreadSettings
    {
        [Range(1, 6)] public int jumpRange = 3;
        [Range(0f, 1f)] public float spreadChance = 0.5f;
        [Range(0f, 1f)] public float distanceFalloff = 0.5f;
        public bool dirtBlocksJumps = true;
        public bool infectGrass = true;
        public bool infectDeadGrass = true;
    }

    public class SpreadSystem : ITickSystem
    {
        public const float Dirt = 0f, Grass = 1f, DeadGrass = 2f, SpreadCode = 3f;

        readonly SpreadSettings s;
        bool[] done; 

        public SpreadSystem(SpreadSettings settings) { s = settings; }

        public void Tick(WorldGrid grid, int tickNumber)
        {
            float[] terrain = grid.GetField("terrain");
            float[] resistance = grid.GetField("resistance");
            float[] spread = grid.GetField("spread");
            float[] next = grid.GetNextField("spread");
            Array.Copy(spread, next, spread.Length); 

            if (done == null || done.Length != spread.Length) done = new bool[spread.Length];

            int W = grid.Width, H = grid.Height;
            int R = s.jumpRange;
            int R2 = R * R;

            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    int i = grid.Index(x, y);

                    if (spread[i] < 0.5f || done[i]) continue;
                    bool anyCandidate = false;
                    for (int dy = -R; dy <= R; dy++)
                    {
                        int ny = y + dy;

                        if (ny < 0 || ny >= H) continue;    
                        for (int dx = -R; dx <= R; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int dist2 = dx * dx + dy * dy;

                            if (dist2 > R2) continue;
                            int nx = ((x + dx) % W + W) % W;   
                            int j = grid.Index(nx, ny);

                            if (!IsInfectable(terrain[j]) || spread[j] >= 0.5f) continue;
                            anyCandidate = true;

                            if (next[j] >= 0.5f) continue;
                            float dist = Mathf.Sqrt(dist2);
                            float chance = s.spreadChance
                                         * Mathf.Pow(s.distanceFalloff, dist - 1f)
                                         * (1f - resistance[j]);
                            if ((float)grid.Rng.NextDouble() >= chance) continue;

                            if (s.dirtBlocksJumps && dist2 > 2 && LineBlocked(grid, terrain, x, y, dx, dy)) continue;
                            next[j] = 1f;
                        }
                    }

                    if (!anyCandidate) done[i] = true;
                }
            }

            grid.SwapField("spread");
        }

        bool IsInfectable(float terrainCode)
        {
            int c = Mathf.RoundToInt(terrainCode);
            return (c == 1 && s.infectGrass) || (c == 2 && s.infectDeadGrass);
        }

        bool LineBlocked(WorldGrid grid, float[] terrain, int x, int y, int dx, int dy)
        {
            int steps = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
            for (int k = 1; k < steps; k++)
            {
                float t = k / (float)steps;
                int sx = ((x + Mathf.RoundToInt(dx * t)) % grid.Width + grid.Width) % grid.Width;
                int sy = y + Mathf.RoundToInt(dy * t);
                if (!IsInfectable(terrain[grid.Index(sx, sy)])) return true;
            }
            return false;
        }

        //display field GridTilemapRenderer draws
        public static void UpdateDisplay(WorldGrid grid)
        {
            float[] terrain = grid.GetField("terrain");
            float[] spread = grid.GetField("spread");
            float[] display = grid.GetField("display");
            for (int i = 0; i < display.Length; i++)
                display[i] = (spread[i] >= 0.5f ? SpreadCode : terrain[i]) + 0.1f;
        }
    }
}