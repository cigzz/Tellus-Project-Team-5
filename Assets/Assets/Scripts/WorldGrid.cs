using System;
using System.Collections.Generic;

namespace WorldSim
{
    public class WorldGrid
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int CellCount;
        public readonly float TileWorldSize;
        public readonly Random Rng;
        readonly Dictionary<string, float[]> fields = new Dictionary<string, float[]>();
        readonly Dictionary<string, float[]> nextFields = new Dictionary<string, float[]>(); // write buffers
        public WorldGrid(int width, int height, int seed = 0, float tileWorldSize = 1f)
        {
            Width = width;
            Height = height;
            CellCount = width * height;
            TileWorldSize = tileWorldSize;
            Rng = new Random(seed);
        }
        public int Index(int x, int y) => y * Width + x;
        // Gets a field's current values, creating it filled with defaultValue the first time anyone asks for that name.
        public float[] GetField(string name, float defaultValue = 0f)
        {
            if (!fields.TryGetValue(name, out float[] values))
            {
                values = new float[CellCount];
                if (defaultValue != 0f)
                {
                    for (int i = 0; i < values.Length; i++)
                        values[i] = defaultValue;
                }
                fields[name] = values;
            }
            return values;
        }

        // --- Double buffering ---
        // GetField (the stable snapshot for this tick), write results into
        // GetNextField, then call SwapField once at the end of your Tick().

        public float[] GetNextField(string name)
        {
            if (!nextFields.TryGetValue(name, out float[] values))
            {
                int length = GetField(name).Length; // ensures current field exists first
                values = new float[length];
                nextFields[name] = values;
            }
            return values;
        }
        // Makes the write buffer the new current values for this field.
        public void SwapField(string name)
        {
            if (!nextFields.TryGetValue(name, out float[] next)) return;
            (fields[name], nextFields[name]) = (next, fields[name]);
        }
        public bool HasField(string name) => fields.ContainsKey(name);
        // Every field name currently in use
        public IEnumerable<string> FieldNames => fields.Keys;
        // Neighbor lookups. X wraps around like a spherical planet would
        // Y stays clamped to the edge, like poles - there's a top and bottom
        public int Left(int x, int y) => Index((x - 1 + Width) % Width, y);
        public int Right(int x, int y) => Index((x + 1) % Width, y);
        public int Down(int x, int y) => Index(x, Math.Max(y - 1, 0));
        public int Up(int x, int y) => Index(x, Math.Min(y + 1, Height - 1));
    }
}
