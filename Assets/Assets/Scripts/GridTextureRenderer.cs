using UnityEngine;

namespace WorldSim
{
    // Draws one field of the WorldGrid as continuous grayscale
    //
    //   Height SpriteRenderer: Sorting Layer "Terrain", Order 0
    //   Tilemap Renderer:      Sorting Layer "Terrain", Order 1
    [RequireComponent(typeof(SpriteRenderer))]
    public class GridTextureRenderer : MonoBehaviour
    {
        // public
        [SerializeField] string fieldName = "height";
        [SerializeField] float minValue = 0f;
        [SerializeField] float maxValue = 100f;

        // One gradient stop: a color and the normalized height (0-1) where it sits
        [System.Serializable]
        public struct ColorStop
        {
            public Color color;
            [Range(0f, 1f)] public float point;
        }

        // 5 gradient source colors, low to high. Keep points in ascending order
        [SerializeField] ColorStop[] colorStops = new ColorStop[5]
        {
            new ColorStop { color = new Color(0.05f, 0.15f, 0.40f), point = 0.00f }, // deep water
            new ColorStop { color = new Color(0.20f, 0.45f, 0.75f), point = 0.30f }, // shallow water
            new ColorStop { color = new Color(0.85f, 0.80f, 0.55f), point = 0.40f }, // sand
            new ColorStop { color = new Color(0.25f, 0.55f, 0.20f), point = 0.60f }, // grass
            new ColorStop { color = new Color(0.95f, 0.95f, 0.95f), point = 1.00f }, // peaks
        };

        [SerializeField, Range(0f, 0.1f)] float noiseAmount = 0.02f; // max random offset added to each pixel's height (0-1 scale)
        // private
        SpriteRenderer spriteRenderer;
        Texture2D texture;
        Color32[] pixels; // color storage before update
        float[] noise;    // fixed per-pixel random values (-1 to 1) so noise doesn't flicker between draws

        public void Draw(WorldGrid grid) // public method for rendering world grid data
        {
            if (texture == null)
            {
                spriteRenderer = GetComponent<SpriteRenderer>();

                texture = new Texture2D(grid.Width, grid.Height, TextureFormat.RGB24, false) // create texture with grid dimensions
                {
                    filterMode = FilterMode.Point, // sets scaling filter to nearest-neighbor
                    wrapMode = TextureWrapMode.Clamp // clamps texture coordinates
                };

                var sprite = Sprite.Create(texture, new Rect(0, 0, grid.Width, grid.Height),
                    new Vector2(0f, 0f), pixelsPerUnit: 1f); // sets scale to one pixel per unit
                spriteRenderer.sprite = sprite; 

                pixels = new Color32[grid.CellCount];

                noise = new float[grid.CellCount]; // generate noise once, scaled by noiseAmount each draw
                for (int i = 0; i < noise.Length; i++) noise[i] = Random.Range(-1f, 1f);
            }

            float[] values = grid.GetField(fieldName); //fetches desired field's array from grid

            for (int i = 0; i < values.Length; i++) // loop through field's array data
            {
                float t = Mathf.InverseLerp(minValue, maxValue, values[i]); // normalize value to 0-1
                t = Mathf.Clamp01(t + noise[i] * noiseAmount); // add small random noise
                pixels[i] = EvaluateGradient(t); // pick blended color from the 5 stops
            }

            texture.SetPixels32(pixels); // uploads pixels color data to the texture
            texture.Apply(false); // applies texture without updating mipmaps
        }

        // Returns the color at t by blending between the two stops it falls between
        Color EvaluateGradient(float t)
        {
            if (t <= colorStops[0].point) return colorStops[0].color; // below first stop

            for (int i = 1; i < colorStops.Length; i++)
            {
                if (t <= colorStops[i].point)
                {
                    float blend = Mathf.InverseLerp(colorStops[i - 1].point, colorStops[i].point, t); // 0-1 position between the two stops
                    return Color.Lerp(colorStops[i - 1].color, colorStops[i].color, blend);
                }
            }

            return colorStops[colorStops.Length - 1].color; // above last stop
        }
    }
}
