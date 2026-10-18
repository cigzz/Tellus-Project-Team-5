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
        // private
        SpriteRenderer spriteRenderer;
        Texture2D texture;
        Color32[] pixels; // color storage before update

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
            }

            float[] values = grid.GetField(fieldName); //fetches desired field's array from grid

            for (int i = 0; i < values.Length; i++) // loop through field's array data
            {
                byte gray = (byte)(Mathf.InverseLerp(minValue, maxValue, values[i]) * 255f); // Normalize greyscale values to color value
                pixels[i] = new Color32(gray, gray, gray, 255); // assignes normalized color to pixels array
            }

            texture.SetPixels32(pixels); // uploads pixels color data to the texture
            texture.Apply(false); // applies texture without updating mipmaps
        }
    }
}
