using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

namespace WorldSim
{
    // Lets the player click a tile to plant the first seed. Works with the plant model in PlantModel.cs.
    // Only one plant can be placed; after that it spreads on its own. If the whole colony dies out, the player
    // can plant again (otherwise a dead planet could never be restarted).
    //
    // A click is ignored, with a line in the Console saying why, when it lands on a button or slider, when the
    // evolution menu is open, or when the tile is too harsh for a seedling to survive (that case also shows an
    // on-screen message and does NOT use up the seed).
    public class PlantPlacer : MonoBehaviour
    {
        [SerializeField] TickEngine tickEngine;
        [SerializeField] Tilemap tilemap; // only used to convert a click into a grid cell

        PlantModel model;
        readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        string message;
        float messageUntil;

        public bool HasPlanted { get; private set; } // true once a plant has been placed

        void Update()
        {
            if (!Input.GetMouseButtonDown(0)) return; // return if no mouse input

            WorldGrid grid = tickEngine.Grid;
            if (grid == null) return; // grid not created yet

            if (!EnsureModel())
            {
                Debug.LogWarning("[PlantPlacer] Click ignored: no WorldBootstrap with a plant model was found.");
                return;
            }

            if (PointerBlockedByUI(out string blocker))
            {
                Debug.Log($"[PlantPlacer] Click ignored: UI in the way ({blocker}).");
                return;
            }

            if (HasAnyPlant(grid)) return; // one plant at a time; it spreads on its own

            Vector3 worldPoint = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            Vector3Int cell = tilemap.WorldToCell(worldPoint);
            if (cell.x < 0 || cell.x >= grid.Width || cell.y < 0 || cell.y >= grid.Height)
            {
                Debug.Log($"[PlantPlacer] Click ignored: cell ({cell.x}, {cell.y}) is outside the {grid.Width} x {grid.Height} grid.");
                return;
            }

            int index = grid.Index(cell.x, cell.y);
            float suitability = grid.GetField("suitability")[index];
            if (!model.CanGrowAt(suitability))
            {
                float temperature = grid.GetField("temperature")[index];
                Debug.Log($"[PlantPlacer] Click ignored: ({cell.x}, {cell.y}) is too harsh, suitability {suitability:0.00}, {temperature:0.0} C.");
                Show(temperature < 10f
                    ? "Too cold to grow here. Try a tile nearer the middle of the map."
                    : "Nothing can grow on this tile right now. Try another one.");
                return;
            }

            // SeedTile takes the seedling's carbon from the air so the planet's carbon total stays constant.
            if (model.SeedTile(grid, index))
            {
                HasPlanted = true;
                Debug.Log($"[PlantPlacer] Planted at ({cell.x}, {cell.y}), suitability {suitability:0.00}.");
            }
        }

        bool EnsureModel()
        {
            if (model != null) return true;
            var boot = GetComponent<WorldBootstrap>();
            if (boot == null) boot = FindFirstObjectByType<WorldBootstrap>();
            if (boot != null) model = boot.Model;
            return model != null;
        }

        static bool HasAnyPlant(WorldGrid grid)
        {
            float[] biomass = grid.GetField("biomass");
            for (int i = 0; i < biomass.Length; i++)
                if (biomass[i] > 0f) return true;
            return false;
        }

        // Only things the player can interact with (buttons, sliders) or the open evolution menu swallow a click.
        // Decorative panels and text don't, so an oversized background image can't block planting.
        bool PointerBlockedByUI(out string blocker)
        {
            blocker = null;

            var menu = FindFirstObjectByType<EvolutionMenu>();
            if (menu != null && menu.IsOpen)
            {
                blocker = "evolution menu";
                return true;
            }

            EventSystem es = EventSystem.current;
            if (es == null) return false;

            var data = new PointerEventData(es) { position = Input.mousePosition };
            uiHits.Clear();
            es.RaycastAll(data, uiHits);
            foreach (RaycastResult hit in uiHits)
            {
                if (hit.gameObject.GetComponentInParent<Selectable>() != null)
                {
                    blocker = hit.gameObject.name;
                    return true;
                }
            }
            return false;
        }

        void Show(string text)
        {
            message = text;
            messageUntil = Time.unscaledTime + 3f;
        }

        // Plain on-screen message so no canvas wiring is needed.
        void OnGUI()
        {
            if (string.IsNullOrEmpty(message) || Time.unscaledTime > messageUntil) return;

            var style = new GUIStyle(GUI.skin.box)
            {
                fontSize = 24,
                wordWrap = true,
                alignment = TextAnchor.MiddleCenter,
            };
            float width = Mathf.Min(900f, Screen.width - 40f);
            GUI.Box(new Rect((Screen.width - width) / 2f, Screen.height * 0.30f, width, 70f), message, style);
        }
    }
}