using UnityEngine;
using UnityEngine.Tilemaps;

namespace WorldSim
{
    // Draws one field of the WorldGrid onto a Tilemap, using each cell's value as the tile index.
    // Optional: plant formula (suitability + biomass), biomass shading, water and ice overlays.
    // mutiple will be used in the manager object to draw the filters and gameview
    public class GridTilemapRenderer : MonoBehaviour
    {
        [SerializeField] Tilemap tilemap;
        [SerializeField] string fieldName = "temperature";
        [SerializeField] TileBase[] tileSet;        // index 0 = empty. fieldName values are used as the tile index (0 = tileSet[0])

        public enum PlantCombine { WeightedSum, Multiply }
        [Header("Plant formula (picks the tile from suitability + biomass instead of fieldName)")]
        [SerializeField] bool usePlantFormula;
        [SerializeField] PlantCombine plantCombine = PlantCombine.WeightedSum; // WeightedSum: weighted average. Multiply: weighted geometric mean
        [SerializeField] string suitabilityField = "suitability";
        [SerializeField, Range(0f, 1f)] float suitabilityWeight = 0.5f;  // weights are relative: only their ratio matters
        [SerializeField, Range(0f, 1f)] float biomassWeight = 0.5f;
        [SerializeField, Range(0f, 1f)] float suitabilityMultiplier = 0.5f; // 0 = off. 1 = tile level scaled fully by suitability
        [SerializeField, Range(0.1f, 1f)] float biomassCurve = 0.4f;  // below 1 lifts small biomass (0.4: a 0.05 seedling reads as 0.3)
        [SerializeField, Range(0f, 1f)] float minPlantLevel = 0.35f;  // any living plant starts this far up the tileset

        [Header("Biomass shading (darker = more biomass)")]
        [SerializeField] bool shadeByBiomass;
        [SerializeField] string biomassField = "biomass";      // also used by the plant formula
        [SerializeField, Range(0f, 1f)] float shadeWeight = 0.3f; // darkening at full biomass (0.3 = 30% darker), seedlings stay full color

        [Header("Water (blue, deeper shade = more water)")]
        [SerializeField] bool showWater;
        [SerializeField] string waterField = "water";
        [SerializeField] float waterThreshold = 1f;            // at or above this the cell draws as water
        [SerializeField] float deepWaterValue = 3f;            // at or above this the cell is fully deepWaterColor
        [SerializeField] Color shallowWaterColor = new Color(0.55f, 0.75f, 1f);
        [SerializeField] Color deepWaterColor = new Color(0.05f, 0.15f, 0.5f);

        [Header("Ice (white, drawn over water)")]
        [SerializeField] bool showIce;
        [SerializeField] string iceField = "ice";
        [SerializeField] float iceThreshold = 0.05f;           // at or above this the cell draws as ice

        [Header("Optional textures (empty = plain colored square)")]
        [SerializeField] TileBase oceanTile;
        [SerializeField] TileBase iceTile;
        [SerializeField, Min(0)] int colorSteps = 16;          // shade bands; fewer = fewer redraws. 0 = smooth

        const int OceanIndex = -2;
        const int IceIndex = -3;
        Color32[] lastColor;    // previously drawn tint per cell
        UnityEngine.Tilemaps.Tile plainTile;      // white square used for water/ice when no texture is set
        WorldGrid lastGrid;
        public string FieldName => fieldName;
        public TileBase[] TileSet => tileSet;
        int[] lastTileIndex; // previously drawn tile indices
        readonly System.Collections.Generic.List<TileChangeData> changes = new System.Collections.Generic.List<TileChangeData>();

        // Shows or hides this view. Hidden: stops drawing and hides its tilemap. Shown: repaints every cell.
        public void SetShown(bool shown)
        {
            enabled = shown;
            var tilemapRenderer = tilemap.GetComponent<TilemapRenderer>();
            if (tilemapRenderer != null) tilemapRenderer.enabled = shown;
            if (!shown) return;

            if (lastTileIndex != null)
            {
                for (int i = 0; i < lastTileIndex.Length; i++)
                    lastTileIndex[i] = -1;
            }
            if (lastGrid != null) Draw(lastGrid);
        }

        public void SetField(string newFieldName, TileBase[] newTileSet = null)
        {
            fieldName = newFieldName;
            if (newTileSet != null && newTileSet.Length > 0) tileSet = newTileSet; // empty = keep the current tileset

            // forget what was drawn so every cell repaints with the new field and tileset
            if (lastTileIndex != null)
            {
                for (int i = 0; i < lastTileIndex.Length; i++)
                    lastTileIndex[i] = -1;
            }

            if (lastGrid != null) Draw(lastGrid);
        }
        public void Draw(WorldGrid grid) // public method to update and draw the tilemap using world grid data
        {
            lastGrid = grid;
            if (!enabled) return; // hidden view: don't draw
            float[] values = grid.GetField(fieldName);
            float[] suitability = usePlantFormula ? grid.GetField(suitabilityField) : null;
            float[] biomass = (usePlantFormula || shadeByBiomass) ? grid.GetField(biomassField) : null;
            float[] ice = showIce ? grid.GetField(iceField) : null;
            // HasField: don't create "water" early as zeros, the plant model seeds it with a start value
            float[] water = showWater && grid.HasField(waterField) ? grid.GetField(waterField) : null;

            if (lastColor == null || lastColor.Length != grid.CellCount)
                lastColor = new Color32[grid.CellCount];

            if (lastTileIndex == null)
            {
                lastTileIndex = new int[grid.CellCount];
                for (int i = 0; i < lastTileIndex.Length; i++)
                    lastTileIndex[i] = -1;
            }

            changes.Clear();

            for (int y = 0; y < grid.Height; y++) // loop through the grid
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    int i = grid.Index(x, y); // convert 2D grid to array index
                    int tileIndex;
                    Color32 color;
                    if (ice != null && ice[i] >= iceThreshold)
                    {
                        tileIndex = IceIndex;
                        color = Color.white;
                    }
                    else if (water != null && water[i] >= waterThreshold)
                    {
                        tileIndex = OceanIndex;
                        color = Color.Lerp(shallowWaterColor, deepWaterColor, Step(Mathf.InverseLerp(waterThreshold, deepWaterValue, water[i])));
                    }
                    else
                    {
                        tileIndex = usePlantFormula ? PlantTileIndex(suitability[i], biomass[i]) : ValueToTileIndex(values[i]);
                        color = shadeByBiomass ? Shade(biomass[i]) : Color.white;
                    }

                    if (tileIndex == lastTileIndex[i] && color.Equals(lastColor[i])) continue; // if unchanged, skip it

                    TileBase tile = tileIndex == IceIndex ? (iceTile != null ? iceTile : PlainTile())
                                  : tileIndex == OceanIndex ? (oceanTile != null ? oceanTile : PlainTile())
                                  : tileSet[tileIndex];
                    changes.Add(new TileChangeData(new Vector3Int(x, y, 0), tile, color, Matrix4x4.identity)); // queue only the cells that changed
                    lastTileIndex[i] = tileIndex; // updates recorded tile index for the cell position
                    lastColor[i] = color;
                }
            }

            // one batched tilemap update instead of one SetTile call per cell
            if (changes.Count > 0)
            {
                tilemap.SetTiles(changes.ToArray(), true); // true = tint even though tiles have Lock Color
                ReapplyNeighbourTints(grid);
            }
        }

        // The plant tiles are AutoTiles: setting one refreshes its 8 neighbours, and that refresh resets their tint
        // to white (and the water/ice square resets to white too). Re-tint the neighbours of every changed cell.
        void ReapplyNeighbourTints(WorldGrid grid)
        {
            foreach (TileChangeData change in changes)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = change.position.x + dx, y = change.position.y + dy;
                        if (x < 0 || x >= grid.Width || y < 0 || y >= grid.Height) continue;
                        int i = grid.Index(x, y);
                        if (lastTileIndex[i] == -1) continue; // not drawn yet
                        var pos = new Vector3Int(x, y, 0);
                        tilemap.SetTileFlags(pos, TileFlags.None);
                        tilemap.SetColor(pos, lastColor[i]);
                    }
                }
            }
        }
        int ValueToTileIndex(float value) // the field value is the tile index (plantGrowth 0-8 -> tileSet[0-8])
        {
            return Mathf.Clamp(Mathf.FloorToInt(value), 0, tileSet.Length - 1);
        }

        // 0 = no plant (tileSet[0]). Any living plant gets tileSet[1..last], starting at minPlantLevel
        // so a fresh seedling is already a solid, visible tile.
        int PlantTileIndex(float suitability, float biomass)
        {
            if (biomass <= 0f || tileSet.Length < 2) return 0;
            float score = PlantScore(Mathf.Clamp01(suitability), CurvedBiomass(biomass));
            float level = Mathf.Lerp(minPlantLevel, 1f, score);
            level *= Mathf.Lerp(1f, Mathf.Clamp01(suitability), suitabilityMultiplier); // poor land pulls the tile down
            return Mathf.Clamp(1 + Mathf.RoundToInt(level * (tileSet.Length - 2)), 1, tileSet.Length - 1);
        }

        // Combines suitability and biomass into 0-1. Weights are normalized so the result never leaves 0-1.
        float PlantScore(float suitability, float biomass)
        {
            float total = suitabilityWeight + biomassWeight;
            if (total <= 0f) return biomass;
            float ws = suitabilityWeight / total;
            float wb = biomassWeight / total;
            if (plantCombine == PlantCombine.Multiply) // weighted geometric mean: low in either one pulls it down
                return Mathf.Pow(suitability, ws) * Mathf.Pow(biomass, wb);
            return ws * suitability + wb * biomass;      // weighted average
        }

        float CurvedBiomass(float biomass) => Mathf.Pow(Mathf.Clamp01(biomass), biomassCurve);

        Color Shade(float biomass) // more biomass = darker, capped at shadeWeight
        {
            float b = 1f - shadeWeight * Step(CurvedBiomass(biomass));
            return new Color(b, b, b, 1f);
        }

        // snaps a 0-1 blend to colorSteps bands so tiny value drift doesn't repaint the tile
        float Step(float t) => colorSteps > 0 ? Mathf.Round(t * colorSteps) / colorSteps : t;

        TileBase PlainTile()
        {
            if (plainTile == null)
            {
                Texture2D tex = Texture2D.whiteTexture;
                float pixelsPerUnit = tex.width / tilemap.layoutGrid.cellSize.x; // sprite fills one cell
                plainTile = ScriptableObject.CreateInstance<UnityEngine.Tilemaps.Tile>();
                plainTile.sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            }
            return plainTile;
        }
    }
}
