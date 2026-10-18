using System.IO;
using UnityEngine;

namespace WorldSim
{
    public static class HeightmapLoader
    {
        public static void Apply(WorldGrid grid, string filePath, float minHeight = 0f, float maxHeight = 100f)
        {
            if (!File.Exists(filePath))
            {
                Debug.LogError($"HeightmapLoader: no file at '{filePath}'. All heights are 0!");
                return;
            }
            // read png file from disk byte by byte
            byte[] bytes = File.ReadAllBytes(filePath);
            // create temp texture and load bytes into an image
            var image = new Texture2D(2, 2);
            image.LoadImage(bytes);
            // ask for hieght fields from world grid
            float[] height = grid.GetField("height");
            // foreach cell on world grid
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    float w = (x + 0.5f) / grid.Width;
                    float v = (y + 0.5f) / grid.Height;
                    // pull color value from the image
                    float gray = image.GetPixelBilinear(w, v).grayscale; 
                    // Map the grayscale value to height range (0-100)
                    height[grid.Index(x, y)] = Mathf.Lerp(minHeight, maxHeight, gray);
                }
            }
            // clean up texture data
            Object.Destroy(image);
        }
    }
}
